using System.Globalization;
using Microsoft.Extensions.Logging;
using TravelCut.Application.Ports;
using TravelCut.Contracts.Dtos;
using TravelCut.Domain.Entities;
using TravelCut.Domain.Enums;

namespace TravelCut.Application.UseCases;

/// <summary>Resultado de caso de uso: Ok com valor, ou erro com código/mensagem do contrato IPC.</summary>
public sealed record PreviewResult<T>(bool Ok, T? Value, string? ErrorCode, string? ErrorMessage)
{
    public static PreviewResult<T> Success(T value) => new(true, value, null, null);
    public static PreviewResult<T> Fail(string code, string message) => new(false, default, code, message);
}

/// <summary>Pedido de estado de job de proxy (contrato: media.proxy.status { projectId, jobId }).</summary>
public sealed record ProxyStatusRequest(Guid ProjectId, Guid JobId);

/// <summary>
/// Prévia de mídia (F07): fonte de reprodução, forma de onda e proxies. Falhas previsíveis viram status
/// honestos no DTO (unavailable, ffmpeg_unavailable, no_audio, failed); nada é estimado.
/// </summary>
public sealed class PreviewUseCases
{
    public static readonly TimeSpan WaveformTimeout = TimeSpan.FromSeconds(60);

    // Tolerância de comparação com a duração do metadado (arredondamento de ffprobe).
    private const double DurationToleranceSeconds = 0.001;

    /// <summary>Fração mínima de amostras esperadas para a janela; abaixo disso a forma de onda é falha, não zeros.</summary>
    public const double MinDecodedSampleFraction = 0.9;

    /// <summary>Motivo (waveform.reason, status failed) quando o decoder entrega menos amostras que a janela.</summary>
    public const string IncompleteDecodeReason = "áudio decodificado incompleto";

    private readonly IProjectRepository _projectRepo;
    private readonly IMediaRepository _mediaRepo;
    private readonly IFfmpegPathProvider _ffmpegPath;
    private readonly IBinaryStdoutProcessRunner _binaryRunner;
    private readonly IWaveformCache _waveformCache;
    private readonly IProxyJobService _proxyJobs;
    private readonly ILogger<PreviewUseCases> _logger;

    public PreviewUseCases(
        IProjectRepository projectRepo,
        IMediaRepository mediaRepo,
        IFfmpegPathProvider ffmpegPath,
        IBinaryStdoutProcessRunner binaryRunner,
        IWaveformCache waveformCache,
        IProxyJobService proxyJobs,
        ILogger<PreviewUseCases> logger)
    {
        _projectRepo = projectRepo;
        _mediaRepo = mediaRepo;
        _ffmpegPath = ffmpegPath;
        _binaryRunner = binaryRunner;
        _waveformCache = waveformCache;
        _proxyJobs = proxyJobs;
        _logger = logger;
    }

    // ---------------------------------------------------------------- media.getPlaybackSource

    public async Task<PreviewResult<PlaybackSourceDto>> GetPlaybackSourceAsync(PlaybackSourceRequest request, CancellationToken ct = default)
    {
        var project = await _projectRepo.GetByIdAsync(request.ProjectId, ct);
        if (project == null)
            return PreviewResult<PlaybackSourceDto>.Fail("PROJECT_NOT_FOUND", "Projeto não encontrado.");

        var media = await _mediaRepo.GetByIdAsync(request.MediaId, ct);
        if (media == null || media.ProjectId != request.ProjectId)
            return PreviewResult<PlaybackSourceDto>.Fail("MEDIA_NOT_FOUND", "Mídia não encontrada neste projeto.");

        string extension = Path.GetExtension(media.RelativePath ?? string.Empty);
        string? codec = KnownCodec(media);
        string mime = PreviewPaths.MimeTypeFor(extension);
        string proxyStatus = ComputeProxyStatus(request.ProjectId, media);

        // Proxy pronto (arquivo existente em 3-Trabalho/cache/proxies) tem prioridade sobre o original.
        string? proxyRelative = ProxyRelativePathIfReady(project.RootPath, media);
        if (proxyRelative != null)
        {
            // Codec/contêiner do proxy vêm da configuração do encoder (libx264 + aac em mp4), não de ffprobe.
            return PreviewResult<PlaybackSourceDto>.Success(new PlaybackSourceDto(
                request.MediaId,
                PreviewPaths.PlaybackStatusProxy,
                PreviewPaths.BuildMediaUrl(proxyRelative),
                "video/mp4",
                "h264",
                true,
                PreviewPaths.ProxyStatusReady,
                null));
        }

        if (!PreviewPaths.TryResolveInsideRoot(project.RootPath, media.RelativePath, out string fullPath))
        {
            return PreviewResult<PlaybackSourceDto>.Success(Unavailable(request.MediaId, codec, proxyStatus, PreviewPaths.ReasonUnsafePath, mime));
        }

        if (!File.Exists(fullPath))
        {
            return PreviewResult<PlaybackSourceDto>.Success(Unavailable(request.MediaId, codec, proxyStatus, PreviewPaths.ReasonFileNotFound, mime));
        }

        string url = PreviewPaths.BuildMediaUrl((media.RelativePath ?? string.Empty).Trim().Replace('\\', '/'));
        return PreviewResult<PlaybackSourceDto>.Success(new PlaybackSourceDto(
            request.MediaId,
            PreviewPaths.PlaybackStatusOriginal,
            url,
            mime,
            codec,
            PreviewPaths.ClassifyPlayable(codec, extension),
            proxyStatus,
            null));
    }

    private static PlaybackSourceDto Unavailable(Guid mediaId, string? codec, string proxyStatus, string reason, string mime) =>
        new(mediaId, PreviewPaths.PlaybackStatusUnavailable, null, mime, codec, null, proxyStatus, reason);

    /// <summary>Codec só quando o metadado é "ok" (senão não há base para afirmar nada).</summary>
    private static string? KnownCodec(MediaAsset media) =>
        media.Metadata is { MetadataStatus: MediaMetadataStatus.Ok } md && !string.IsNullOrWhiteSpace(md.Codec)
            ? md.Codec.Trim()
            : null;

    private string? ProxyRelativePathIfReady(string projectRoot, MediaAsset media)
    {
        if (string.IsNullOrWhiteSpace(media.Fingerprint) || string.IsNullOrWhiteSpace(projectRoot))
            return null;

        string fileName = PreviewPaths.ProxyFileName(media.MediaId, media.Fingerprint);
        string relative = $"{PreviewPaths.ProxyFolderRelative}/{fileName}";
        string full = Path.Combine(Path.GetFullPath(projectRoot), PreviewPaths.ProxyFolderRelative.Replace('/', Path.DirectorySeparatorChar), fileName);
        return File.Exists(full) ? relative : null;
    }

    /// <summary>ready | generating | failed | none | ffmpeg_unavailable (sem ffmpeg e sem proxy).</summary>
    private string ComputeProxyStatus(Guid projectId, MediaAsset media)
    {
        var item = _proxyJobs.GetLatestItem(projectId, media.MediaId);
        if (item != null)
        {
            switch (item.State)
            {
                case "pending":
                case "running":
                    return PreviewPaths.ProxyStatusGenerating;
                case "failed":
                    return PreviewPaths.ProxyStatusFailed;
                // "ready" do job não basta: o arquivo é verificado em ProxyRelativePathIfReady.
            }
        }

        return _ffmpegPath.GetFfmpegPath() == null
            ? PreviewPaths.ProxyStatusFfmpegUnavailable
            : PreviewPaths.ProxyStatusNone;
    }

    // ---------------------------------------------------------------- media.getWaveform

    public async Task<PreviewResult<WaveformDto>> GetWaveformAsync(WaveformRequest request, CancellationToken ct = default)
    {
        if (request.Buckets < PreviewPaths.MinBuckets || request.Buckets > PreviewPaths.MaxBuckets)
            return PreviewResult<WaveformDto>.Fail("INVALID_ARGUMENT", $"buckets deve estar entre {PreviewPaths.MinBuckets} e {PreviewPaths.MaxBuckets}.");

        double start = request.StartSeconds;
        double end = request.EndSeconds;
        if (!double.IsFinite(start) || !double.IsFinite(end) || start < 0 || end <= start)
            return PreviewResult<WaveformDto>.Fail("INVALID_ARGUMENT", "Intervalo inválido: use início >= 0 e fim > início.");

        var project = await _projectRepo.GetByIdAsync(request.ProjectId, ct);
        if (project == null)
            return PreviewResult<WaveformDto>.Fail("PROJECT_NOT_FOUND", "Projeto não encontrado.");

        var media = await _mediaRepo.GetByIdAsync(request.MediaId, ct);
        if (media == null || media.ProjectId != request.ProjectId)
            return PreviewResult<WaveformDto>.Fail("MEDIA_NOT_FOUND", "Mídia não encontrada neste projeto.");

        var md = media.Metadata;
        bool metadataOk = md is { MetadataStatus: MediaMetadataStatus.Ok };
        if (!metadataOk || md!.DurationSeconds <= 0 || !double.IsFinite(md.DurationSeconds))
            return PreviewResult<WaveformDto>.Fail("INVALID_ARGUMENT", "Duração da mídia desconhecida: forma de onda indisponível.");

        if (end > md.DurationSeconds + DurationToleranceSeconds)
            return PreviewResult<WaveformDto>.Fail("INVALID_ARGUMENT", "Intervalo fora da duração conhecida da mídia.");

        int buckets = request.Buckets;
        double bucketSeconds = (end - start) / buckets;

        if (md.AudioChannels == 0)
        {
            return PreviewResult<WaveformDto>.Success(new WaveformDto(
                "no_audio", start, bucketSeconds, new List<double>(), "Mídia sem trilha de áudio (metadado)."));
        }

        if (!PreviewPaths.TryResolveInsideRoot(project.RootPath, media.RelativePath, out string fullPath))
            return PreviewResult<WaveformDto>.Success(Failed(start, bucketSeconds, PreviewPaths.ReasonUnsafePath));

        if (!File.Exists(fullPath))
            return PreviewResult<WaveformDto>.Success(Failed(start, bucketSeconds, PreviewPaths.ReasonFileNotFound));

        if (string.IsNullOrWhiteSpace(media.Fingerprint))
        {
            // Sem fingerprint a chave de cache não distingue mídias: não há forma de onda confiável.
            return PreviewResult<WaveformDto>.Success(Failed(start, bucketSeconds, "Mídia sem fingerprint: cache de forma de onda não identificável."));
        }

        string cacheKey = PreviewPaths.WaveformCacheKey(media.Fingerprint, start, end, buckets);
        var cached = await _waveformCache.ReadAsync(project.RootPath, cacheKey, ct);
        if (cached != null)
        {
            return PreviewResult<WaveformDto>.Success(cached);
        }

        string? ffmpeg = _ffmpegPath.GetFfmpegPath();
        if (ffmpeg == null)
        {
            return PreviewResult<WaveformDto>.Success(new WaveformDto(
                "ffmpeg_unavailable", start, bucketSeconds, new List<double>(),
                "FFmpeg não encontrado: forma de onda indisponível."));
        }

        var accumulator = new PreviewWaveformAccumulator(buckets, PreviewPaths.WaveformSampleRate, end - start);
        var processRequest = new ProcessRunRequest(
            ffmpeg,
            new[]
            {
                "-hide_banner", "-nostats",
                "-ss", start.ToString("F6", CultureInfo.InvariantCulture),
                "-t", (end - start).ToString("F6", CultureInfo.InvariantCulture),
                "-i", fullPath,
                "-vn", "-ac", "1", "-ar", PreviewPaths.WaveformSampleRate.ToString(CultureInfo.InvariantCulture),
                "-f", "s16le", "-"
            },
            WaveformTimeout);

        BinaryStdoutRunResult run;
        try
        {
            run = await _binaryRunner.RunAsync(
                processRequest,
                (stream, token) => accumulator.ConsumeAsync(stream, token),
                ct);
        }
        catch (IOException ex)
        {
            _logger.LogWarning(ex, "Falha de I/O ao ler stdout do FFmpeg para forma de onda");
            return PreviewResult<WaveformDto>.Success(Failed(start, bucketSeconds, "Falha ao ler o áudio decodificado."));
        }

        switch (run.Status)
        {
            case ProcessRunStatus.NotStarted:
                return PreviewResult<WaveformDto>.Success(new WaveformDto(
                    "ffmpeg_unavailable", start, bucketSeconds, new List<double>(),
                    "Não foi possível iniciar o FFmpeg: forma de onda indisponível."));

            case ProcessRunStatus.TimedOut:
                return PreviewResult<WaveformDto>.Success(Failed(start, bucketSeconds,
                    $"Tempo limite de {WaveformTimeout.TotalSeconds:0} s excedido ao gerar forma de onda."));

            case ProcessRunStatus.Canceled:
                ct.ThrowIfCancellationRequested();
                return PreviewResult<WaveformDto>.Success(Failed(start, bucketSeconds, "Geração de forma de onda cancelada."));
        }

        if (run.ExitCode != 0)
        {
            _logger.LogWarning("FFmpeg retornou {Code} ao gerar forma de onda", run.ExitCode);
            return PreviewResult<WaveformDto>.Success(Failed(start, bucketSeconds,
                $"FFmpeg falhou ao decodificar o áudio (código {run.ExitCode})."));
        }

        if (accumulator.SamplesProcessed == 0)
        {
            // Sem amostras decodificadas: distinto de silêncio (que produz amostras zeradas).
            return PreviewResult<WaveformDto>.Success(new WaveformDto(
                "no_audio", start, bucketSeconds, new List<double>(),
                "FFmpeg não decodificou amostras de áudio neste intervalo."));
        }

        // Decodificação parcial (ex.: stream cortado) produziria buckets finais zerados que pareceriam silêncio:
        // falha honesta, sem gravar no cache.
        double expectedSamples = (end - start) * PreviewPaths.WaveformSampleRate;
        if (accumulator.SamplesProcessed < expectedSamples * MinDecodedSampleFraction)
        {
            _logger.LogWarning("Forma de onda incompleta: {Decoded} de {Expected} amostras esperadas",
                accumulator.SamplesProcessed, Math.Round(expectedSamples));
            return PreviewResult<WaveformDto>.Success(Failed(start, bucketSeconds, IncompleteDecodeReason));
        }

        var result = new WaveformDto("ok", start, bucketSeconds, accumulator.ToPeaks(), null);
        try
        {
            await _waveformCache.WriteAsync(project.RootPath, cacheKey, result, ct);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // O cache é otimização: a forma de onda já foi calculada e segue na resposta.
            _logger.LogWarning(ex, "Forma de onda calculada, mas o cache não pôde ser gravado");
        }
        return PreviewResult<WaveformDto>.Success(result);
    }

    private static WaveformDto Failed(double start, double bucketSeconds, string reason) =>
        new("failed", start, bucketSeconds, new List<double>(), reason);

    // ---------------------------------------------------------------- media.proxy.start / status

    public async Task<PreviewResult<ProxyJobStatusDto>> StartProxyAsync(StartProxyRequest request, CancellationToken ct = default)
    {
        var project = await _projectRepo.GetByIdAsync(request.ProjectId, ct);
        if (project == null)
            return PreviewResult<ProxyJobStatusDto>.Fail("PROJECT_NOT_FOUND", "Projeto não encontrado.");

        var mediaIds = (request.MediaIds ?? new List<Guid>()).Distinct().ToList();
        if (mediaIds.Count == 0)
            return PreviewResult<ProxyJobStatusDto>.Fail("INVALID_ARGUMENT", "Nenhuma mídia informada para proxy.");

        if (mediaIds.Count > PreviewPaths.MaxProxyMediaPerRequest)
            return PreviewResult<ProxyJobStatusDto>.Fail("INVALID_ARGUMENT", $"Limite de {PreviewPaths.MaxProxyMediaPerRequest} mídias por chamada.");

        var items = new List<ProxyJobItemPlan>(mediaIds.Count);
        foreach (var mediaId in mediaIds)
        {
            items.Add(await PlanProxyItemAsync(project, request.ProjectId, mediaId, ct));
        }

        var outcome = _proxyJobs.Start(request.ProjectId, project.RootPath, items);
        if (outcome.Conflict || outcome.Job == null)
            return PreviewResult<ProxyJobStatusDto>.Fail("PROXY_IN_PROGRESS", "Já existe um job de proxy em andamento para este projeto.");

        return PreviewResult<ProxyJobStatusDto>.Success(ToDto(outcome.Job));
    }

    private async Task<ProxyJobItemPlan> PlanProxyItemAsync(Project project, Guid projectId, Guid mediaId, CancellationToken ct)
    {
        var media = await _mediaRepo.GetByIdAsync(mediaId, ct);
        if (media == null || media.ProjectId != projectId)
            return new ProxyJobItemPlan(mediaId, string.Empty, null, string.Empty, "Mídia não encontrada neste projeto.");

        string displayName = Path.GetFileName(media.RelativePath ?? string.Empty);

        if (media.Kind != MediaKind.Video)
            return new ProxyJobItemPlan(mediaId, displayName, null, media.Fingerprint ?? string.Empty, "Somente mídia de vídeo recebe proxy.");

        if (string.IsNullOrWhiteSpace(media.Fingerprint))
            return new ProxyJobItemPlan(mediaId, displayName, null, string.Empty, "Mídia sem fingerprint: proxy não identificável.");

        if (!PreviewPaths.TryResolveInsideRoot(project.RootPath, media.RelativePath, out string fullPath))
            return new ProxyJobItemPlan(mediaId, displayName, null, media.Fingerprint, PreviewPaths.ReasonUnsafePath);

        if (!File.Exists(fullPath))
            return new ProxyJobItemPlan(mediaId, displayName, null, media.Fingerprint, PreviewPaths.ReasonFileNotFound);

        return new ProxyJobItemPlan(mediaId, displayName, fullPath, media.Fingerprint, null);
    }

    /// <summary>
    /// Estado de um job de proxy. O job precisa pertencer ao projeto informado; job de outro projeto (ou inexistente)
    /// responde PROXY_JOB_NOT_FOUND, sem revelar que ele existe.
    /// </summary>
    public PreviewResult<ProxyJobStatusDto> GetProxyStatus(ProxyStatusRequest request)
    {
        var job = _proxyJobs.GetJob(request.JobId);
        if (job == null || job.ProjectId != request.ProjectId)
            return PreviewResult<ProxyJobStatusDto>.Fail("PROXY_JOB_NOT_FOUND", "Job de proxy não encontrado.");

        return PreviewResult<ProxyJobStatusDto>.Success(ToDto(job));
    }

    private static ProxyJobStatusDto ToDto(ProxyJobSnapshot job) => new(
        job.JobId,
        job.State,
        job.Processed,
        job.Total,
        job.CurrentMedia,
        job.Items.Select(i => new ProxyItemStatusDto(i.MediaId, i.State, i.Message)).ToList());
}
