using System.Text.Json;
using Microsoft.Extensions.Logging;
using TravelCut.Application.Audio;
using TravelCut.Application.Ports;
using TravelCut.Contracts.Dtos;

namespace TravelCut.Infrastructure.Audio;

public sealed record WhisperServiceOptions(
    string? PythonPath = null,          // padrão: variável TRAVELCUT_PYTHON_PATH
    string? WorkerScriptPath = null,    // padrão: procura workers/whisper_worker.py subindo a partir de AppContext.BaseDirectory
    TimeSpan? ProbeTimeout = null,      // padrão: 120 s (import de torch pode ser lento)
    TimeSpan? TranscribeTimeout = null, // padrão: 4 h (large-v3 em CPU é lento)
    TimeSpan? EnvironmentRetryTtl = null, // padrão: 60 s (ambiente incompleto é reconsultado após esse prazo)
    TimeProvider? Clock = null            // padrão: TimeProvider.System (gancho de teste)
);

// Serviço Whisper via worker Python isolado (protocolo "travelcut.whisper" v1, stdin/stdout NDJSON).
// Regras:
// - Nunca sintetiza transcrição. Sem Python/worker/openai-whisper -> "unavailable" com motivo.
// - Modelo fixo "large-v3": o worker não baixa nem troca modelo; ausência no cache -> "unavailable".
// - Falha de processo ou resposta inválida -> "failed"; cancelamento -> "canceled".
// - Transcrição vazia com áudio válido é "completed" com 0 palavras.
public class WhisperTranscriptionService : IWhisperTranscriptionService
{
    public const string DefaultModel = "large-v3";
    public const string PythonEnvVar = "TRAVELCUT_PYTHON_PATH";

    private const string PythonCheckCode =
        "import sys; print('TRAVELCUT_PY=' + '.'.join(str(v) for v in sys.version_info[:3]))";
    private const string PythonMarker = "TRAVELCUT_PY=";
    private const string ExitLine = "{\"command\":\"exit\"}\n";

    private const string PythonMissingReason =
        "Python 3 não encontrado. Defina TRAVELCUT_PYTHON_PATH, crie workers/.venv ou instale o Python (ver workers/README.md). Nenhuma transcrição foi estimada.";

    private static readonly TimeSpan PythonCheckTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan DefaultProbeTimeout = TimeSpan.FromSeconds(120);
    private static readonly TimeSpan DefaultTranscribeTimeout = TimeSpan.FromHours(4);
    private static readonly TimeSpan DefaultEnvironmentRetryTtl = TimeSpan.FromSeconds(60);

    private static readonly IReadOnlyDictionary<string, string> PythonEnvironment =
        new Dictionary<string, string>
        {
            ["PYTHONUTF8"] = "1",
            ["PYTHONIOENCODING"] = "utf-8"
        };

    private readonly ILogger<WhisperTranscriptionService> _logger;
    private readonly IProcessRunner _runner;
    private readonly WhisperServiceOptions _options;
    private readonly SemaphoreSlim _envGate = new(1, 1);
    private WorkerEnvironment? _env;
    private long _envGeneration;

    private sealed record PythonRuntime(string FileName, IReadOnlyList<string> PrefixArgs, string Version);

    private sealed record WorkerEnvironment(
        PythonRuntime? Python,
        string? WorkerScript,
        WhisperProbeResult Probe,
        DateTime ResolvedAtUtc);

    public WhisperTranscriptionService(
        ILogger<WhisperTranscriptionService> logger,
        IProcessRunner? processRunner = null,
        WhisperServiceOptions? options = null)
    {
        _logger = logger;
        _runner = processRunner ?? new ProcessRunner();
        _options = options ?? new WhisperServiceOptions();
    }

    public async Task<WhisperProbeResult> ProbeEnvironmentAsync(CancellationToken ct = default)
    {
        var env = await GetEnvironmentAsync(ct).ConfigureAwait(false);
        return env.Probe;
    }

    public async Task<string?> FindPythonExecutableAsync(CancellationToken ct = default)
    {
        var env = await GetEnvironmentAsync(ct).ConfigureAwait(false);
        return env.Python?.FileName;
    }

    public Task<TranscriptionOutcome> TranscribeAsync(
        string mediaPath,
        Guid mediaId,
        string model = DefaultModel,
        string language = "pt",
        CancellationToken ct = default,
        IProgress<TranscriptionProgressUpdate>? progress = null,
        IWhisperExecutionObserver? execution = null)
        => TranscribeCoreAsync(mediaPath, mediaId, model, language, null, ct, progress, execution);

    public Task<TranscriptionOutcome> TranscribeOnDeviceAsync(
        string mediaPath,
        Guid mediaId,
        string model,
        string language,
        string device,
        CancellationToken ct = default,
        IProgress<TranscriptionProgressUpdate>? progress = null,
        IWhisperExecutionObserver? execution = null)
        => TranscribeCoreAsync(mediaPath, mediaId, model, language, device, ct, progress, execution);

    public IReadOnlyList<Task<TranscriptionOutcome>>? TranscribeBatch(
        IReadOnlyList<TranscriptionBatchItem> items, string model, string language, CancellationToken ct)
    {
        if (items.Count == 0) return null;

        var requestedModel = string.IsNullOrWhiteSpace(model) ? DefaultModel : model.Trim();
        var requestedLanguage = string.IsNullOrWhiteSpace(language) ? "pt" : language.Trim();
        var sources = items
            .Select(_ => new TaskCompletionSource<TranscriptionOutcome>(TaskCreationOptions.RunContinuationsAsynchronously))
            .ToList();

        void CompleteRemaining(TranscriptionOutcome outcome)
        {
            for (int i = 0; i < sources.Count; i++)
            {
                if (sources[i].TrySetResult(outcome))
                {
                    items[i].Execution?.OnFinished(new WhisperExecutionResult(
                        outcome.Status == "canceled" ? "canceled" : "failed", null, outcome.Reason));
                }
            }
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await RunBatchAsync(items, sources, requestedModel, requestedLanguage, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                CompleteRemaining(Canceled(requestedModel));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Falha inesperada no lote de transcrição");
                CompleteRemaining(Failed("Falha inesperada no lote de transcrição; detalhes no log.", requestedModel, TranscriptionFailureCodes.Transient));
            }
            finally
            {
                CompleteRemaining(Failed("Lote de transcrição encerrado sem resposta para esta mídia.", requestedModel, TranscriptionFailureCodes.Transient));
            }
        }, CancellationToken.None);

        return sources.Select(s => s.Task).ToList();
    }

    /// <summary>
    /// Um único processo recebe N comandos "transcribe" (requestIds distintos) seguidos de "exit": o worker atende em sequência e
    /// mantém o modelo carregado (P10). Cada resposta conclui o seu item na hora; o que ficar sem resposta é mapeado pelo resultado do processo.
    /// </summary>
    private async Task RunBatchAsync(
        IReadOnlyList<TranscriptionBatchItem> items,
        List<TaskCompletionSource<TranscriptionOutcome>> sources,
        string model,
        string language,
        CancellationToken ct)
    {
        var env = await GetEnvironmentAsync(ct).ConfigureAwait(false);
        TranscriptionOutcome? notReady =
            env.Python is null ? Unavailable(PythonMissingReason, model)
            : env.WorkerScript is null ? Unavailable("Worker Whisper (workers/whisper_worker.py) não encontrado. Nenhuma transcrição foi estimada.", model)
            : !env.Probe.HasWhisper ? Unavailable("openai-whisper não está instalado no ambiente Python do worker (ver workers/README.md). Nenhuma transcrição foi estimada.", model)
            : null;
        if (notReady is not null)
        {
            foreach (var s in sources) s.TrySetResult(notReady);
            return;
        }

        var reqIds = new string[items.Count];
        var commands = new string[items.Count];
        var active = new List<int>();
        var stdin = new System.Text.StringBuilder();
        for (int i = 0; i < items.Count; i++)
        {
            var path = items[i].MediaPath;
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                sources[i].TrySetResult(Failed("Arquivo de mídia não encontrado para transcrição.", model));
                continue;
            }

            reqIds[i] = Guid.NewGuid().ToString("N");
            commands[i] = JsonSerializer.Serialize(new
            {
                command = "transcribe",
                requestId = reqIds[i],
                payload = new { mediaPath = Path.GetFullPath(path), model, language, device = (string?)null }
            }) + "\n";
            stdin.Append(commands[i]);
            active.Add(i);
        }
        if (active.Count == 0) return;
        stdin.Append(ExitLine);

        var byRequest = active.ToDictionary(i => reqIds[i]);
        var args = BuildArgs(env.Python!, env.WorkerScript!);
        foreach (int i in active)
        {
            items[i].Execution?.OnStarted(new WhisperExecutionInfo(
                RequestId: reqIds[i],
                PythonExecutable: env.Python!.FileName,
                Arguments: args,
                WorkerScript: env.WorkerScript!,
                WorkingDirectory: Environment.CurrentDirectory,
                EnvironmentVariables: PythonEnvironment,
                StandardInput: commands[i] + ExitLine,
                MediaPath: Path.GetFullPath(items[i].MediaPath),
                Model: model,
                Language: language,
                Device: env.Probe.Device));
        }

        void OnLine(string raw)
        {
            var line = raw.Trim();
            if (line.Length == 0 || line[0] != '{') return;

            string? reqId;
            bool isResponse;
            try
            {
                using var doc = JsonDocument.Parse(line);
                if (doc.RootElement.ValueKind != JsonValueKind.Object) return;
                reqId = GetString(doc.RootElement, "requestId");
                isResponse = doc.RootElement.TryGetProperty("ok", out _);
            }
            catch (JsonException)
            {
                return;
            }

            if (reqId is null || !byRequest.TryGetValue(reqId, out int idx)) return;
            var item = items[idx];
            if (!isResponse)
            {
                if (item.Progress is not null) ReportWorkerEvent(line, reqId, item.Progress);
                if (item.Execution is not null && SummarizeStdoutLine(line, reqId) is { } summary) item.Execution.OnLog("stdout", summary);
                return;
            }

            var outcome = MapTranscribeRun(new ProcessRunResult(ProcessRunStatus.Completed, 0, line, string.Empty), reqId, item.MediaId, model, language);
            if (sources[idx].TrySetResult(outcome))
            {
                item.Execution?.OnFinished(new WhisperExecutionResult(ProcessStateOf(ProcessRunStatus.Completed, outcome.Status), 0, outcome.Reason));
            }
        }

        var perItemTimeout = _options.TranscribeTimeout ?? DefaultTranscribeTimeout;
        var run = await _runner.RunAsync(
            new ProcessRunRequest(
                env.Python!.FileName,
                args,
                TimeSpan.FromTicks(perItemTimeout.Ticks * active.Count),
                stdin.ToString(),
                PythonEnvironment,
                StandardOutputLine: OnLine,
                StandardErrorLine: line =>
                {
                    int current = active.FirstOrDefault(i => !sources[i].Task.IsCompleted, -1);
                    if (current >= 0) items[current].Execution?.OnLog("stderr", line);
                }),
            ct).ConfigureAwait(false);

        foreach (int i in active)
        {
            if (sources[i].Task.IsCompleted) continue;
            var outcome = MapTranscribeRun(run, reqIds[i], items[i].MediaId, model, language);
            if (sources[i].TrySetResult(outcome))
            {
                items[i].Execution?.OnFinished(new WhisperExecutionResult(ProcessStateOf(run.Status, outcome.Status), run.ExitCode, outcome.Reason));
            }
        }
    }

    private async Task<TranscriptionOutcome> TranscribeCoreAsync(
        string mediaPath,
        Guid mediaId,
        string model,
        string language,
        string? device,
        CancellationToken ct,
        IProgress<TranscriptionProgressUpdate>? progress,
        IWhisperExecutionObserver? execution)
    {
        var requestedModel = string.IsNullOrWhiteSpace(model) ? DefaultModel : model.Trim();
        var requestedLanguage = string.IsNullOrWhiteSpace(language) ? "pt" : language.Trim();

        if (string.IsNullOrWhiteSpace(mediaPath) || !File.Exists(mediaPath))
        {
            return Failed("Arquivo de mídia não encontrado para transcrição.", requestedModel);
        }

        try
        {
            var env = await GetEnvironmentAsync(ct).ConfigureAwait(false);

            if (env.Python is null)
            {
                return Unavailable(PythonMissingReason, requestedModel);
            }

            if (env.WorkerScript is null)
            {
                return Unavailable("Worker Whisper (workers/whisper_worker.py) não encontrado. Nenhuma transcrição foi estimada.", requestedModel);
            }

            if (!env.Probe.HasWhisper)
            {
                return Unavailable("openai-whisper não está instalado no ambiente Python do worker (ver workers/README.md). Nenhuma transcrição foi estimada.", requestedModel);
            }

            var reqId = Guid.NewGuid().ToString("N");
            var command = JsonSerializer.Serialize(new
            {
                command = "transcribe",
                requestId = reqId,
                payload = new
                {
                    mediaPath = Path.GetFullPath(mediaPath),
                    model = requestedModel,
                    language = requestedLanguage,
                    device = string.Equals(device, "cpu", StringComparison.OrdinalIgnoreCase) ? "cpu" : null
                }
            }) + "\n" + ExitLine;

            var args = BuildArgs(env.Python, env.WorkerScript);
            var fullMediaPath = Path.GetFullPath(mediaPath);
            // Exatamente o que vai ao IProcessRunner; o ProcessRunner herda o diretório do app quando WorkingDirectory é null.
            execution?.OnStarted(new WhisperExecutionInfo(
                RequestId: reqId,
                PythonExecutable: env.Python.FileName,
                Arguments: args,
                WorkerScript: env.WorkerScript,
                WorkingDirectory: Environment.CurrentDirectory,
                EnvironmentVariables: PythonEnvironment,
                StandardInput: command,
                MediaPath: fullMediaPath,
                Model: requestedModel,
                Language: requestedLanguage,
                Device: env.Probe.Device));

            try
            {
                var run = await _runner.RunAsync(
                    new ProcessRunRequest(
                        env.Python.FileName,
                        args,
                        _options.TranscribeTimeout ?? DefaultTranscribeTimeout,
                        command,
                        PythonEnvironment,
                        StandardOutputLine: progress is null && execution is null ? null : line =>
                        {
                            if (progress is not null) ReportWorkerEvent(line, reqId, progress);
                            if (execution is not null && SummarizeStdoutLine(line, reqId) is { } summary) execution.OnLog("stdout", summary);
                        },
                        StandardErrorLine: execution is null ? null : line => execution.OnLog("stderr", line)),
                    ct).ConfigureAwait(false);

                var outcome = MapTranscribeRun(run, reqId, mediaId, requestedModel, requestedLanguage);
                execution?.OnFinished(new WhisperExecutionResult(ProcessStateOf(run.Status, outcome.Status), run.ExitCode, outcome.Reason));
                return outcome;
            }
            catch (OperationCanceledException)
            {
                execution?.OnFinished(new WhisperExecutionResult("canceled", null, "Transcrição cancelada."));
                throw;
            }
        }
        catch (OperationCanceledException)
        {
            return Canceled(requestedModel);
        }
    }

    /// <summary>Sucesso só com resposta válida do worker; processo encerrado sem ela é falha.</summary>
    private static string ProcessStateOf(ProcessRunStatus status, string outcomeStatus) => status switch
    {
        ProcessRunStatus.TimedOut => "timed_out",
        ProcessRunStatus.Canceled => "canceled",
        ProcessRunStatus.NotStarted => "not_started",
        _ => outcomeStatus == "completed" ? "completed" : "failed"
    };

    /// <summary>
    /// Linha de stdout para o log técnico. O protocolo vira um resumo; a resposta final nunca é copiada
    /// (traz o texto transcrito). Heartbeat e percentual repetido não entram, para o log limitado guardar sinal útil.
    /// </summary>
    private static string? SummarizeStdoutLine(string rawLine, string requestId)
    {
        var line = rawLine.Trim();
        if (line.Length == 0) return null;
        if (line[0] != '{') return line;

        try
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object || GetString(root, "protocol") != TranscriptValidator.ExpectedProtocol)
            {
                return line;
            }
            if (GetString(root, "requestId") != requestId) return "mensagem do protocolo de outra requisição (ignorada)";
            if (root.TryGetProperty("ok", out _))
            {
                return GetBool(root, "ok")
                    ? "resposta final ok=true (conteúdo da transcrição omitido do log)"
                    : $"resposta final ok=false, código {GetErrorCode(root)}";
            }
            return GetString(root, "event") == "progress" && GetString(root, "stage") is { } stage
                ? $"evento: etapa {stage}"
                : null;
        }
        catch (JsonException)
        {
            return line;
        }
    }

    public void InvalidateEnvironment()
    {
        _env = null;
        Interlocked.Increment(ref _envGeneration);
    }

    public long EnvironmentGeneration => Interlocked.Read(ref _envGeneration);

    // ---- Ambiente (probe com cache; falha de cancelamento não é cacheada) ----

    private async Task<WorkerEnvironment> GetEnvironmentAsync(CancellationToken ct)
    {
        if (_env is { } cached && IsStillValid(cached))
        {
            return cached;
        }

        await _envGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_env is { } again && IsStillValid(again))
            {
                return again;
            }

            var resolved = await ResolveEnvironmentAsync(ct).ConfigureAwait(false);
            _env = resolved;
            return resolved;
        }
        finally
        {
            _envGate.Release();
        }
    }

    // Ambiente PRONTO (Python, worker, Whisper, modelo em cache e ffmpeg) fica em cache; qualquer ambiente
    // incompleto é reconsultado após o TTL (ex.: usuário acabou de criar workers/.venv, instalar o Whisper
    // ou baixar o modelo). Instalações feitas pelo assistente também chamam InvalidateEnvironment.
    private bool IsStillValid(WorkerEnvironment env) =>
        IsReady(env) || Now() - env.ResolvedAtUtc < (_options.EnvironmentRetryTtl ?? DefaultEnvironmentRetryTtl);

    private static bool IsReady(WorkerEnvironment env) =>
        env.Python is not null
        && env.WorkerScript is not null
        && env.Probe.HasWhisper
        && env.Probe.ModelCached != false
        && env.Probe.FfmpegOnPath != false;

    private DateTime Now() => (_options.Clock ?? TimeProvider.System).GetUtcNow().UtcDateTime;

    private async Task<WorkerEnvironment> ResolveEnvironmentAsync(CancellationToken ct)
    {
        var scriptPath = ResolveWorkerScript();
        var python = await FindPythonAsync(scriptPath, ct).ConfigureAwait(false);

        if (python is null)
        {
            return Env(null, scriptPath, new WhisperProbeResult(
                HasPython: false,
                HasCuda: null,
                Device: null,
                HasWhisper: false,
                Engine: null,
                DefaultModel: DefaultModel,
                StatusSummary: PythonMissingReason));
        }

        if (scriptPath is null)
        {
            return Env(python, null, new WhisperProbeResult(
                HasPython: true,
                HasCuda: null,
                Device: null,
                HasWhisper: false,
                Engine: null,
                DefaultModel: DefaultModel,
                StatusSummary: "Worker workers/whisper_worker.py não encontrado. Nenhuma transcrição foi estimada."));
        }

        var reqId = Guid.NewGuid().ToString("N");
        var stdin = JsonSerializer.Serialize(new { command = "probe", requestId = reqId, payload = new { } })
            + "\n" + ExitLine;

        var run = await _runner.RunAsync(
            new ProcessRunRequest(
                python.FileName,
                BuildArgs(python, scriptPath),
                _options.ProbeTimeout ?? DefaultProbeTimeout,
                stdin,
                PythonEnvironment),
            ct).ConfigureAwait(false);

        if (run.Status == ProcessRunStatus.Canceled)
        {
            throw new OperationCanceledException(ct);
        }

        var response = run.Status == ProcessRunStatus.Completed ? FindResponse(run.StandardOutput, reqId) : null;
        if (response is not { } root || !GetBool(root, "ok") || !root.TryGetProperty("result", out var result))
        {
            return Env(python, scriptPath, new WhisperProbeResult(
                HasPython: true,
                HasCuda: null,
                Device: null,
                HasWhisper: false,
                Engine: null,
                DefaultModel: DefaultModel,
                StatusSummary: $"Worker Python não respondeu ao probe ({DescribeRun(run)})."));
        }

        var installed = GetBool(result, "whisperInstalled");
        var whisperVersion = GetString(result, "whisperVersion");
        var cuda = GetBool(result, "cuda");
        var device = GetString(result, "device") ?? "cpu";
        var modelCached = GetBool(result, "modelCached");
        var ffmpegOnPath = GetBool(result, "ffmpegOnPath");
        var pyVersion = GetString(result, "python") ?? python.Version;

        string? engine = installed
            ? (whisperVersion is null ? "openai-whisper" : $"openai-whisper {whisperVersion}")
            : null;

        var summary = $"Python {pyVersion} | {(installed ? engine : "openai-whisper ausente")} | " +
                      $"dispositivo: {device} | modelo {DefaultModel}: " +
                      $"{(modelCached ? "presente no cache" : "ausente do cache (não baixado automaticamente)")} | " +
                      $"ffmpeg no PATH do worker: {(ffmpegOnPath ? "sim" : "não")}";

        return Env(python, scriptPath, new WhisperProbeResult(
            HasPython: true,
            HasCuda: cuda,
            Device: device,
            HasWhisper: installed,
            Engine: engine,
            DefaultModel: DefaultModel,
            StatusSummary: summary,
            ModelCached: modelCached,
            FfmpegOnPath: ffmpegOnPath));
    }

    private WorkerEnvironment Env(PythonRuntime? python, string? script, WhisperProbeResult probe) =>
        new(python, script, probe, Now());

    private async Task<PythonRuntime?> FindPythonAsync(string? scriptPath, CancellationToken ct)
    {
        var candidates = new List<(string Label, string FileName, string[] Prefix)>();

        var explicitPath = _options.PythonPath ?? Environment.GetEnvironmentVariable(PythonEnvVar);
        if (!string.IsNullOrWhiteSpace(explicitPath))
        {
            candidates.Add(("explicit", explicitPath.Trim().Trim('"'), Array.Empty<string>()));
        }

        if (scriptPath is not null)
        {
            var venvRoot = Path.Combine(Path.GetDirectoryName(scriptPath)!, ".venv");
            var winPython = Path.Combine(venvRoot, "Scripts", "python.exe");
            var posixPython = Path.Combine(venvRoot, "bin", "python");
            if (File.Exists(winPython)) candidates.Add(("venv", winPython, Array.Empty<string>()));
            if (File.Exists(posixPython)) candidates.Add(("venv", posixPython, Array.Empty<string>()));
        }

        candidates.Add(("py-launcher", "py", new[] { "-3" }));
        candidates.Add(("path", "python", Array.Empty<string>()));

        foreach (var extra in Tooling.ToolPathResolver.FindPythonCandidates(null))
        {
            if (!candidates.Any(c => string.Equals(c.FileName, extra, StringComparison.OrdinalIgnoreCase)))
            {
                candidates.Add(("discovered", extra, Array.Empty<string>()));
            }
        }

        foreach (var c in candidates)
        {
            var args = new List<string>(c.Prefix) { "-c", PythonCheckCode };
            var run = await _runner.RunAsync(
                new ProcessRunRequest(c.FileName, args, PythonCheckTimeout),
                ct).ConfigureAwait(false);

            if (run.Status == ProcessRunStatus.Canceled)
            {
                throw new OperationCanceledException(ct);
            }

            // Alias da Microsoft Store (WindowsApps\python.exe) e launcher sem Python saem com
            // código != 0 e sem marcador: tratados como indisponíveis.
            var version = run.Status == ProcessRunStatus.Completed && run.ExitCode == 0
                ? ExtractPythonVersion(run.StandardOutput)
                : null;

            if (version is null)
            {
                _logger.LogDebug("Candidato Python '{Label}' rejeitado ({Status}, código {ExitCode}).",
                    c.Label, run.Status, run.ExitCode);
                continue;
            }

            if (!IsSupportedPython(version))
            {
                _logger.LogWarning("Python {Version} do candidato '{Label}' é anterior a 3.8; ignorado.", version, c.Label);
                continue;
            }

            return new PythonRuntime(c.FileName, c.Prefix, version);
        }

        return null;
    }

    private string? ResolveWorkerScript()
    {
        if (!string.IsNullOrWhiteSpace(_options.WorkerScriptPath))
        {
            return File.Exists(_options.WorkerScriptPath) ? Path.GetFullPath(_options.WorkerScriptPath) : null;
        }

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (var depth = 0; dir is not null && depth < 8; depth++, dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "workers", "whisper_worker.py");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static List<string> BuildArgs(PythonRuntime python, string scriptPath)
    {
        var args = new List<string>(python.PrefixArgs) { scriptPath };
        return args;
    }

    // ---- Resultado da transcrição ----

    private TranscriptionOutcome MapTranscribeRun(
        ProcessRunResult run,
        string reqId,
        Guid mediaId,
        string model,
        string language)
    {
        switch (run.Status)
        {
            case ProcessRunStatus.Canceled:
                return Canceled(model);
            case ProcessRunStatus.TimedOut:
                return Failed("Tempo limite excedido na transcrição (worker Whisper).", model);
            case ProcessRunStatus.NotStarted:
                return Failed("Não foi possível iniciar o worker Whisper.", model, TranscriptionFailureCodes.Transient);
        }

        var response = FindResponse(run.StandardOutput, reqId);
        if (response is not { } root)
        {
            _logger.LogWarning("Worker Whisper sem resposta válida (código {ExitCode}).", run.ExitCode);
            return Failed($"Worker Whisper não retornou resposta válida (código {run.ExitCode}).", model, TranscriptionFailureCodes.Transient);
        }

        var envelopeError = TranscriptValidator.ValidateEnvelope(GetString(root, "protocol"), GetInt(root, "version"));
        if (envelopeError is not null)
        {
            return Failed(envelopeError, model);
        }

        if (!GetBool(root, "ok"))
        {
            var code = GetErrorCode(root);
            return code switch
            {
                "model_not_available" => Unavailable(
                    $"Modelo {model} não está instalado no cache local. O worker não baixa modelos automaticamente nem usa outro modelo. Instale manualmente conforme workers/README.md (requer autorização).",
                    model),
                "ffmpeg_not_found" => Unavailable(
                    "ffmpeg não encontrado no PATH do ambiente do worker (necessário para decodificar o áudio). Nenhuma transcrição foi estimada.",
                    model),
                "whisper_not_installed" => Unavailable(
                    "openai-whisper não está instalado no ambiente Python do worker. Nenhuma transcrição foi estimada.",
                    model),
                "cuda_out_of_memory" => Failed(
                    "Memória da GPU insuficiente para o modelo (CUDA out of memory).",
                    model, TranscriptionFailureCodes.CudaOutOfMemory),
                "transcription_failed" => Failed($"Worker Whisper falhou (código de erro: {code}).", model, TranscriptionFailureCodes.Transient),
                _ => Failed($"Worker Whisper falhou (código de erro: {code}).", model)
            };
        }

        if (run.ExitCode != 0)
        {
            return Failed($"Worker Whisper terminou com código {run.ExitCode}.", model);
        }

        if (!root.TryGetProperty("result", out var result) || result.ValueKind != JsonValueKind.Object)
        {
            return Failed("Resposta do worker Whisper sem resultado.", model);
        }

        var payload = ParsePayload(result, out var parseError);
        if (payload is null)
        {
            return Failed($"Resposta do worker Whisper inválida: {parseError}", model);
        }

        var engine = GetString(result, "engine");
        if (string.IsNullOrWhiteSpace(engine))
        {
            return Failed("Resposta do worker Whisper sem identificação do engine.", model);
        }

        if (!string.Equals(GetString(result, "model"), model, StringComparison.Ordinal))
        {
            return Failed("Worker devolveu modelo diferente do solicitado; resultado descartado (sem substituição de modelo).", model);
        }

        var validation = TranscriptValidator.Validate(payload);
        if (!validation.IsValid)
        {
            return Failed($"Transcrição rejeitada na validação: {validation.Error}", model);
        }

        if (validation.JitterCorrected)
        {
            _logger.LogInformation("Timestamps de palavras com jitter dentro da tolerância foram reordenados.");
        }

        var detectedLanguage = GetString(result, "language");
        var transcript = new TranscriptDto(
            TranscriptId: Guid.NewGuid(),
            MediaId: mediaId,
            ModelId: model,
            Language: string.IsNullOrWhiteSpace(detectedLanguage) ? language : detectedLanguage,
            FullText: payload.Text.Trim(),
            AudioDurationSeconds: payload.AudioDurationSeconds,
            Words: validation.Words.ToList());

        return new TranscriptionOutcome("completed", transcript, null, engine, model);
    }

    private static WhisperTranscriptPayload? ParsePayload(JsonElement result, out string? error)
    {
        error = null;
        var text = GetString(result, "text") ?? string.Empty;

        if (!TryGetDouble(result, "audio_duration_secs", out var duration))
        {
            error = "audio_duration_secs ausente.";
            return null;
        }

        if (!result.TryGetProperty("words", out var wordsEl) || wordsEl.ValueKind != JsonValueKind.Array)
        {
            error = "lista de palavras ausente.";
            return null;
        }

        var words = new List<WhisperRawWord>();
        foreach (var w in wordsEl.EnumerateArray())
        {
            var wText = GetString(w, "text");
            if (wText is null
                || !TryGetDouble(w, "start", out var start)
                || !TryGetDouble(w, "end", out var end)
                || !TryGetDouble(w, "probability", out var probability))
            {
                error = "palavra malformada.";
                return null;
            }

            words.Add(new WhisperRawWord(wText, start, end, probability));
        }

        return new WhisperTranscriptPayload(text, duration, words);
    }

    // ---- Protocolo / JSON ----

    // Evento de telemetria do worker (sem "ok"): linhas fora do protocolo são ignoradas, e um percentual inválido vira null.
    private static void ReportWorkerEvent(string rawLine, string requestId, IProgress<TranscriptionProgressUpdate> progress)
    {
        var line = rawLine.Trim();
        if (line.Length == 0 || line[0] != '{') return;

        try
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || GetString(root, "protocol") != TranscriptValidator.ExpectedProtocol
                || GetString(root, "requestId") != requestId
                || GetString(root, "event") is null
                || GetString(root, "stage") is not { Length: > 0 } stage)
            {
                return;
            }

            double? percent = root.TryGetProperty("percent", out var p)
                && p.ValueKind == JsonValueKind.Number
                && p.TryGetDouble(out var value)
                && value is >= 0 and <= 100
                    ? value
                    : null;

            progress.Report(new TranscriptionProgressUpdate(stage, percent));
        }
        catch (JsonException)
        {
            // linha não JSON: ignorada
        }
    }

    // Procura a resposta com o requestId esperado (linhas que não são JSON do protocolo são ignoradas).
    // Eventos de telemetria têm o mesmo requestId, mas não trazem "ok": não são resposta.
    private static JsonElement? FindResponse(string stdout, string requestId)
    {
        JsonElement? found = null;
        foreach (var raw in stdout.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line[0] != '{')
            {
                continue;
            }

            try
            {
                using var doc = JsonDocument.Parse(line);
                var root = doc.RootElement;
                if (root.ValueKind == JsonValueKind.Object
                    && GetString(root, "protocol") == TranscriptValidator.ExpectedProtocol
                    && GetString(root, "requestId") == requestId
                    && root.TryGetProperty("ok", out _))
                {
                    found = root.Clone();
                }
            }
            catch (JsonException)
            {
                // linha não JSON: ignorada
            }
        }

        return found;
    }

    private static string GetErrorCode(JsonElement root)
    {
        if (root.TryGetProperty("error", out var err) && err.ValueKind == JsonValueKind.Object)
        {
            return GetString(err, "code") ?? "unknown";
        }

        return "unknown";
    }

    private static string? GetString(JsonElement el, string name) =>
        el.ValueKind == JsonValueKind.Object
        && el.TryGetProperty(name, out var p)
        && p.ValueKind == JsonValueKind.String
            ? p.GetString()
            : null;

    private static bool GetBool(JsonElement el, string name) =>
        el.ValueKind == JsonValueKind.Object
        && el.TryGetProperty(name, out var p)
        && p.ValueKind == JsonValueKind.True;

    private static int? GetInt(JsonElement el, string name) =>
        el.ValueKind == JsonValueKind.Object
        && el.TryGetProperty(name, out var p)
        && p.ValueKind == JsonValueKind.Number
        && p.TryGetInt32(out var v)
            ? v
            : null;

    private static bool TryGetDouble(JsonElement el, string name, out double value)
    {
        value = 0;
        return el.ValueKind == JsonValueKind.Object
            && el.TryGetProperty(name, out var p)
            && p.ValueKind == JsonValueKind.Number
            && p.TryGetDouble(out value)
            && double.IsFinite(value);
    }

    private static string? ExtractPythonVersion(string stdout)
    {
        foreach (var raw in stdout.Split('\n'))
        {
            var line = raw.Trim();
            var idx = line.IndexOf(PythonMarker, StringComparison.Ordinal);
            if (idx >= 0)
            {
                var version = line[(idx + PythonMarker.Length)..].Trim();
                var parts = version.Split('.');
                return parts.Length == 3 && parts.All(p => p.Length > 0 && p.All(char.IsAsciiDigit))
                    ? version
                    : null;
            }
        }

        return null;
    }

    private static bool IsSupportedPython(string version)
    {
        var parts = version.Split('.');
        return int.Parse(parts[0]) == 3 && int.Parse(parts[1]) >= 8;
    }

    private static string DescribeRun(ProcessRunResult run) => run.Status switch
    {
        ProcessRunStatus.TimedOut => "tempo limite",
        ProcessRunStatus.NotStarted => "não iniciou",
        _ => $"código {run.ExitCode}"
    };

    private static TranscriptionOutcome Unavailable(string reason, string model) =>
        new("unavailable", null, reason, null, model);

    private static TranscriptionOutcome Failed(string reason, string model, string? failureCode = null) =>
        new("failed", null, reason, null, model, failureCode);

    private static TranscriptionOutcome Canceled(string model) =>
        new("canceled", null, "Transcrição cancelada.", null, model);
}
