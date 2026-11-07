using Microsoft.Extensions.Logging;
using TravelCut.Application.Ports;
using TravelCut.Application.UseCases;

namespace TravelCut.Infrastructure.Media.Preview;

/// <summary>
/// Argumentos do FFmpeg para proxy de PRÉVIA (não é mídia final nem substitui o original).
/// -n: nunca sobrescreve; a saída é um arquivo parcial renomeado sem sobrescrita ao final.
/// -fps_mode passthrough: preserva os timestamps da origem (VFR não é convertido em CFR; sem -r).
/// </summary>
public static class ProxyFfmpegArguments
{
    public const string VideoCodec = "libx264";
    public const string Preset = "veryfast";
    public const string Crf = "23";
    public const string VideoFilter = "scale=-2:720";
    public const string PixelFormat = "yuv420p";
    public const string AudioCodec = "aac";
    public const string AudioBitrate = "128k";

    public static IReadOnlyList<string> Build(string sourcePath, string partialOutputPath) => new[]
    {
        "-hide_banner", "-nostats", "-loglevel", "error",
        "-n",
        "-i", sourcePath,
        "-c:v", VideoCodec, "-preset", Preset, "-crf", Crf,
        "-vf", VideoFilter,
        "-profile:v", "main", "-level", "3.1",
        "-pix_fmt", PixelFormat,
        "-fps_mode", "passthrough",
        "-c:a", AudioCodec, "-ac", "2", "-b:a", AudioBitrate,
        "-movflags", "+faststart",
        partialOutputPath
    };
}

/// <summary>
/// Fila de proxies de prévia: um job ativo por projeto (PROXY_IN_PROGRESS), execução em background,
/// no máximo um FFmpeg por vez no app inteiro. Estado em memória (thread-safe por lock).
/// Regras: origem somente leitura; saída só em 3-Trabalho/cache/proxies; proxy é descartável e
/// regenerável a partir do original. Sem FFmpeg: job "ffmpeg_unavailable" imediato, nada gerado.
/// Singleton: Dispose cancela o job e mata o processo do FFmpeg ao encerrar o app.
/// </summary>
public sealed class ProxyJobService : IProxyJobService, IDisposable
{
    /// <summary>Tempo máximo por arquivo (encode 720p de clipes longos pode levar bastante).</summary>
    public static readonly TimeSpan ItemTimeout = TimeSpan.FromMinutes(60);

    private const int MaxRetainedJobs = 50;
    private static readonly TimeSpan DisposeWait = TimeSpan.FromSeconds(5);

    private readonly IProcessRunner _runner;
    private readonly IFfmpegPathProvider _ffmpeg;
    private readonly ILogger<ProxyJobService> _logger;
    private readonly object _gate = new();
    private readonly Dictionary<Guid, JobState> _jobs = new();
    private readonly List<Guid> _jobOrder = new();
    private readonly Dictionary<Guid, Guid> _activeByProject = new();
    private readonly Dictionary<(Guid ProjectId, Guid MediaId), ItemState> _latestItems = new();
    private readonly List<Task> _running = new();
    private readonly SemaphoreSlim _encodeSlot = new(1, 1);
    private readonly CancellationTokenSource _shutdown = new();
    private bool _disposed;

    public ProxyJobService(IProcessRunner runner, IFfmpegPathProvider ffmpeg, ILogger<ProxyJobService> logger)
    {
        _runner = runner;
        _ffmpeg = ffmpeg;
        _logger = logger;
    }

    public ProxyStartOutcome Start(Guid projectId, string projectRoot, IReadOnlyList<ProxyJobItemPlan> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        if (string.IsNullOrWhiteSpace(projectRoot))
            throw new ArgumentException("Raiz do projeto não informada.", nameof(projectRoot));

        string? ffmpeg = _ffmpeg.GetFfmpegPath();

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (_activeByProject.TryGetValue(projectId, out var activeId) && _jobs.TryGetValue(activeId, out var active) && active.IsActive)
            {
                return new ProxyStartOutcome(true, Snapshot(active));
            }

            var job = new JobState(Guid.NewGuid(), projectId, Path.GetFullPath(projectRoot));
            foreach (var plan in items)
            {
                var item = new ItemState(plan.MediaId, plan.DisplayName, plan.SourcePath, plan.Fingerprint);
                job.Items.Add(item);

                if (plan.SkipReason != null)
                {
                    item.Set("skipped", plan.SkipReason);
                }
                else if (ffmpeg == null)
                {
                    item.Set("skipped", "FFmpeg não encontrado: proxy não gerado.");
                }
                else if (IsValidExistingProxy(ProxyFinalPath(job.RootPath, plan.MediaId, plan.Fingerprint)))
                {
                    item.Set("ready", "Proxy já existente.");
                }
                else
                {
                    item.Set("pending", null);
                }

                _latestItems[(projectId, plan.MediaId)] = item;
            }

            _jobs[job.JobId] = job;
            _jobOrder.Add(job.JobId);

            if (ffmpeg == null)
            {
                job.State = "ffmpeg_unavailable";
                TrimRetainedJobs();
                return new ProxyStartOutcome(false, Snapshot(job));
            }

            if (job.Items.All(i => i.State != "pending"))
            {
                job.State = "completed";
                TrimRetainedJobs();
                return new ProxyStartOutcome(false, Snapshot(job));
            }

            job.State = "queued";
            _activeByProject[projectId] = job.JobId;
            _running.RemoveAll(t => t.IsCompleted);
            var task = Task.Run(() => RunJobAsync(job, ffmpeg, _shutdown.Token));
            _running.Add(task);
            return new ProxyStartOutcome(false, Snapshot(job));
        }
    }

    public ProxyJobSnapshot? GetJob(Guid jobId)
    {
        lock (_gate)
        {
            return _jobs.TryGetValue(jobId, out var job) ? Snapshot(job) : null;
        }
    }

    public ProxyItemSnapshot? GetLatestItem(Guid projectId, Guid mediaId)
    {
        lock (_gate)
        {
            if (!_latestItems.TryGetValue((projectId, mediaId), out var item))
                return null;

            return new ProxyItemSnapshot(item.MediaId, item.State, item.Message);
        }
    }

    private async Task RunJobAsync(JobState job, string ffmpeg, CancellationToken ct)
    {
        try
        {
            lock (_gate)
            {
                job.State = "queued";
            }

            foreach (var item in SnapshotPending(job))
            {
                ct.ThrowIfCancellationRequested();

                await _encodeSlot.WaitAsync(ct).ConfigureAwait(false);
                try
                {
                    lock (_gate)
                    {
                        job.State = "running";
                        job.CurrentMedia = item.DisplayName;
                        item.Set("running", null);
                    }

                    var (state, message) = await EncodeOneAsync(ffmpeg, job.RootPath, item, ct).ConfigureAwait(false);

                    lock (_gate)
                    {
                        item.Set(state, message);
                    }
                }
                finally
                {
                    _encodeSlot.Release();
                }
            }

            lock (_gate)
            {
                job.CurrentMedia = null;
                job.State = FinalState(job);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            lock (_gate)
            {
                foreach (var item in job.Items.Where(i => i.State is "pending" or "running"))
                {
                    item.Set("skipped", "Job cancelado antes da conclusão.");
                }

                job.CurrentMedia = null;
                job.State = "canceled";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Falha inesperada no job de proxy {JobId}", job.JobId);
            lock (_gate)
            {
                foreach (var item in job.Items.Where(i => i.State is "pending" or "running"))
                {
                    item.Set("failed", "Erro interno no job de proxy.");
                }

                job.CurrentMedia = null;
                job.State = "failed";
            }
        }
        finally
        {
            lock (_gate)
            {
                if (_activeByProject.TryGetValue(job.ProjectId, out var activeId) && activeId == job.JobId)
                {
                    _activeByProject.Remove(job.ProjectId);
                }

                TrimRetainedJobs();
            }
        }
    }

    private static bool IsValidExistingProxy(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length < 1024)
            {
                DeleteQuietly(path);
                return false;
            }
            using var stream = File.OpenRead(path);
            Span<byte> header = stackalloc byte[12];
            int read = stream.Read(header);
            if (read < 8 || header[4] != 0x66 || header[5] != 0x74 || header[6] != 0x79 || header[7] != 0x70)
            {
                stream.Dispose();
                DeleteQuietly(path);
                return false;
            }
            return true;
        }
        catch
        {
            return false;
        }
    }
    private async Task<(bool IsValid, string Reason)> ValidateProxyFileAsync(string ffmpeg, string path, CancellationToken ct)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length < 1024)
                return (false, "Arquivo inexistente ou menor que 1 KB.");

            using (var stream = File.OpenRead(path))
            {
                Span<byte> header = stackalloc byte[12];
                int read = stream.Read(header);
                if (read < 8 || header[4] != 0x66 || header[5] != 0x74 || header[6] != 0x79 || header[7] != 0x70)
                    return (false, "Cabeçalho MP4 inválido (box ftyp ausente).");
            }

            var verifyRun = await _runner.RunAsync(
                new ProcessRunRequest(ffmpeg, new[] { "-v", "error", "-i", path, "-vframes", "1", "-f", "null", "-" }, TimeSpan.FromSeconds(15)),
                ct).ConfigureAwait(false);

            if (verifyRun.ExitCode != 0)
            {
                _logger.LogWarning("Validação FFmpeg falhou ao decodificar frame de {Path}: ExitCode={ExitCode}, Error={Error}", path, verifyRun.ExitCode, verifyRun.StandardError);
                return (false, $"FFmpeg não conseguiu decodificar frames do vídeo (código {verifyRun.ExitCode}).");
            }

            string? ffprobe = FfprobeLocator.Locate();
            if (ffprobe != null)
            {
                var probeRun = await _runner.RunAsync(
                    new ProcessRunRequest(ffprobe, new[] { "-v", "error", "-select_streams", "v:0", "-show_entries", "stream=codec_name,pix_fmt", "-show_entries", "format=format_name,duration", "-of", "json", path }, TimeSpan.FromSeconds(10)),
                    ct).ConfigureAwait(false);

                if (probeRun.ExitCode == 0 && !string.IsNullOrWhiteSpace(probeRun.StandardOutput))
                {
                    string stdout = probeRun.StandardOutput.ToLowerInvariant();
                    if (!stdout.Contains("h264"))
                    {
                        _logger.LogWarning("Validação FFprobe reprovou stream de {Path}: codec não é h264 ({Stdout})", path, stdout);
                        return (false, "Stream de vídeo não codificado em H.264.");
                    }
                    if (!stdout.Contains("yuv420p"))
                    {
                        _logger.LogWarning("Validação FFprobe reprovou stream de {Path}: pixel format não é yuv420p ({Stdout})", path, stdout);
                        return (false, "Formato de pixel incompatível (esperado yuv420p).");
                    }
                }
            }

            _logger.LogInformation("Validação técnica de proxy aprovada com sucesso para {Path} (Length={Length} bytes)", path, info.Length);
            return (true, "OK");
        }
        catch (Exception ex)
        {
            return (false, $"Exceção na validação: {ex.Message}");
        }
    }
    private async Task<(string State, string Message)> EncodeOneAsync(string ffmpeg, string rootPath, ItemState item, CancellationToken ct)
    {
        string finalPath = ProxyFinalPath(rootPath, item.MediaId, item.Fingerprint);
        string partialPath = ProxyPartialPath(rootPath, item.MediaId, item.Fingerprint);

        if (item.SourcePath == null)
            return ("failed", "Origem do proxy indisponível.");

        if (File.Exists(finalPath))
        {
            var cachedValidation = await ValidateProxyFileAsync(ffmpeg, finalPath, ct).ConfigureAwait(false);
            if (cachedValidation.IsValid)
                return ("ready", "Proxy já existente e validado em cache.");
            DeleteQuietly(finalPath);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(finalPath)!);

        // Parcial residual de execução anterior (interrompida): removido antes, pois -n não sobrescreve.
        DeleteQuietly(partialPath);

        ProcessRunResult run;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        _logger.LogInformation("Iniciando FFmpeg para proxy: MediaId={MediaId}, Source={Source}, Partial={Partial}, Final={Final}",
            item.MediaId, item.SourcePath, partialPath, finalPath);

        try
        {
            run = await _runner.RunAsync(
                new ProcessRunRequest(ffmpeg, ProxyFfmpegArguments.Build(item.SourcePath, partialPath), ItemTimeout),
                ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            sw.Stop();
            _logger.LogWarning("Geração de proxy cancelada: MediaId={MediaId}, Elapsed={Elapsed}ms", item.MediaId, sw.ElapsedMilliseconds);
            DeleteQuietly(partialPath);
            throw;
        }

        sw.Stop();
        _logger.LogInformation("FFmpeg concluído: MediaId={MediaId}, Status={Status}, ExitCode={ExitCode}, Elapsed={Elapsed}ms",
            item.MediaId, run.Status, run.ExitCode, sw.ElapsedMilliseconds);

        switch (run.Status)
        {
            case ProcessRunStatus.Canceled:
                DeleteQuietly(partialPath);
                throw new OperationCanceledException(ct);

            case ProcessRunStatus.TimedOut:
                DeleteQuietly(partialPath);
                return ("failed", $"Tempo limite de {ItemTimeout.TotalMinutes:0} min excedido ao gerar proxy.");

            case ProcessRunStatus.NotStarted:
                DeleteQuietly(partialPath);
                return ("failed", "Não foi possível iniciar o FFmpeg para gerar o proxy.");
        }

        if (run.ExitCode != 0)
        {
            DeleteQuietly(partialPath);
            return ("failed", $"FFmpeg falhou ao gerar o proxy (código {run.ExitCode}).");
        }

        if (!File.Exists(partialPath) || new FileInfo(partialPath).Length == 0)
        {
            DeleteQuietly(partialPath);
            return ("failed", "FFmpeg não gerou arquivo de proxy.");
        }

        var validation = await ValidateProxyFileAsync(ffmpeg, partialPath, ct).ConfigureAwait(false);
        if (!validation.IsValid)
        {
            DeleteQuietly(partialPath);
            return ("failed", $"Proxy gerado é inválido ({validation.Reason}).");
        }
        try
        {
            File.Move(partialPath, finalPath, overwrite: true);
            return ("ready", "Proxy gerado e validado.");
        }
        catch (IOException)
        {
            DeleteQuietly(partialPath);
            // Outro processo gerou o mesmo proxy entre a checagem e o rename: reaproveita.
            return File.Exists(finalPath)
                ? ("ready", "Proxy já existente.")
                : ("failed", "Falha ao gravar o arquivo de proxy.");
        }
    }

    private static string FinalState(JobState job)
    {
        int ready = job.Items.Count(i => i.State == "ready");
        int failed = job.Items.Count(i => i.State == "failed");

        if (failed == 0) return "completed";
        return ready == 0 ? "failed" : "completed_with_errors";
    }

    private List<ItemState> SnapshotPending(JobState job)
    {
        lock (_gate)
        {
            return job.Items.Where(i => i.State == "pending").ToList();
        }
    }

    private static ProxyJobSnapshot Snapshot(JobState job)
    {
        int processed = job.Items.Count(i => i.State is "ready" or "failed" or "skipped");
        var items = job.Items
            .Select(i => new ProxyItemSnapshot(i.MediaId, i.State, i.Message))
            .ToList();

        return new ProxyJobSnapshot(job.JobId, job.ProjectId, job.State, processed, job.Items.Count, job.CurrentMedia, items);
    }

    private void TrimRetainedJobs()
    {
        // Remove os mais antigos já finalizados, mantendo os ativos.
        while (_jobOrder.Count > MaxRetainedJobs)
        {
            var oldest = _jobOrder.FirstOrDefault(id => _jobs.TryGetValue(id, out var j) && !j.IsActive);
            if (oldest == Guid.Empty) break;

            _jobOrder.Remove(oldest);
            _jobs.Remove(oldest);
        }
    }

    private static string ProxyFolder(string rootPath) =>
        Path.Combine(rootPath, PreviewPaths.ProxyFolderRelative.Replace('/', Path.DirectorySeparatorChar));

    private static string ProxyFinalPath(string rootPath, Guid mediaId, string fingerprint) =>
        Path.Combine(ProxyFolder(rootPath), PreviewPaths.ProxyFileName(mediaId, fingerprint));

    private static string ProxyPartialPath(string rootPath, Guid mediaId, string fingerprint) =>
        Path.Combine(ProxyFolder(rootPath), PreviewPaths.ProxyPartialFileName(mediaId, fingerprint));

    private static void DeleteQuietly(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    public void Dispose()
    {
        List<Task> running;
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            running = _running.ToList();
        }

        _shutdown.Cancel();
        if (!AwaitRunning(running))
        {
            // Tarefas ainda em uso: semáforo e CTS NÃO são liberados (ainda referenciados); o processo encerra com o app.
            _logger.LogWarning("Encerramento do serviço de proxy: {Count} job(s) ainda não terminaram em {Seconds} s",
                running.Count(t => !t.IsCompleted), DisposeWait.TotalSeconds);
            return;
        }

        _shutdown.Dispose();
        _encodeSlot.Dispose();
    }

    /// <summary>
    /// Aguarda as tarefas dos jobs com prazo curto. Exceções das tarefas são observadas e registradas: nunca
    /// propagam para quem encerra o serviço. Retorna true quando todas terminaram.
    /// </summary>
    private bool AwaitRunning(List<Task> running)
    {
        if (running.Count == 0) return true;
        try
        {
            return Task.WaitAll(running.ToArray(), DisposeWait);
        }
        catch (AggregateException ex)
        {
            foreach (var inner in ex.Flatten().InnerExceptions)
            {
                _logger.LogWarning(inner, "Job de proxy terminou com erro durante o encerramento");
            }
            return running.All(t => t.IsCompleted);
        }
    }

    private sealed class JobState
    {
        public JobState(Guid jobId, Guid projectId, string rootPath)
        {
            JobId = jobId;
            ProjectId = projectId;
            RootPath = rootPath;
        }

        public Guid JobId { get; }
        public Guid ProjectId { get; }
        public string RootPath { get; }
        public string State { get; set; } = "queued";
        public string? CurrentMedia { get; set; }
        public List<ItemState> Items { get; } = new();

        public bool IsActive => State is "queued" or "running";
    }

    private sealed class ItemState
    {
        public ItemState(Guid mediaId, string displayName, string? sourcePath, string fingerprint)
        {
            MediaId = mediaId;
            DisplayName = displayName;
            SourcePath = sourcePath;
            Fingerprint = fingerprint;
        }

        public Guid MediaId { get; }
        public string DisplayName { get; }
        public string? SourcePath { get; }
        public string Fingerprint { get; }
        public string State { get; private set; } = "pending";
        public string? Message { get; private set; }

        public void Set(string state, string? message)
        {
            State = state;
            Message = message;
        }
    }
}
