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
    TimeSpan? TranscribeTimeout = null  // padrão: 4 h (large-v3 em CPU é lento)
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
    private static readonly TimeSpan RetryUnavailableAfter = TimeSpan.FromSeconds(30);

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

    public async Task<TranscriptionOutcome> TranscribeAsync(
        string mediaPath,
        Guid mediaId,
        string model = DefaultModel,
        string language = "pt",
        CancellationToken ct = default)
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
                    language = requestedLanguage
                }
            }) + "\n" + ExitLine;

            var run = await _runner.RunAsync(
                new ProcessRunRequest(
                    env.Python.FileName,
                    BuildArgs(env.Python, env.WorkerScript),
                    _options.TranscribeTimeout ?? DefaultTranscribeTimeout,
                    command,
                    PythonEnvironment),
                ct).ConfigureAwait(false);

            return MapTranscribeRun(run, reqId, mediaId, requestedModel, requestedLanguage);
        }
        catch (OperationCanceledException)
        {
            return Canceled(requestedModel);
        }
    }

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

    // Ambiente disponível fica em cache; indisponível é reconsultado após pouco tempo
    // (ex.: usuário acabou de criar workers/.venv).
    private static bool IsStillValid(WorkerEnvironment env) =>
        env.Python is not null || DateTime.UtcNow - env.ResolvedAtUtc < RetryUnavailableAfter;

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
            StatusSummary: summary));
    }

    private static WorkerEnvironment Env(PythonRuntime? python, string? script, WhisperProbeResult probe) =>
        new(python, script, probe, DateTime.UtcNow);

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
                return Failed("Não foi possível iniciar o worker Whisper.", model);
        }

        var response = FindResponse(run.StandardOutput, reqId);
        if (response is not { } root)
        {
            _logger.LogWarning("Worker Whisper sem resposta válida (código {ExitCode}).", run.ExitCode);
            return Failed($"Worker Whisper não retornou resposta válida (código {run.ExitCode}).", model);
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

    // Procura a resposta com o requestId esperado (linhas que não são JSON do protocolo são ignoradas).
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
                    && GetString(root, "requestId") == requestId)
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

    private static TranscriptionOutcome Failed(string reason, string model) =>
        new("failed", null, reason, null, model);

    private static TranscriptionOutcome Canceled(string model) =>
        new("canceled", null, "Transcrição cancelada.", null, model);
}
