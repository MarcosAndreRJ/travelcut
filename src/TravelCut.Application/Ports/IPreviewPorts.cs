using TravelCut.Contracts.Dtos;

namespace TravelCut.Application.Ports;

/// <summary>Localização do executável FFmpeg (null quando ausente: ffmpeg_unavailable, nunca estimativa).</summary>
public interface IFfmpegPathProvider
{
    string? GetFfmpegPath();
}

/// <summary>
/// Resultado de execução com stdout binário lido em stream. O stdout NÃO é materializado em memória:
/// o chamador consome o <see cref="Stream"/> durante a execução.
/// </summary>
public sealed record BinaryStdoutRunResult(
    ProcessRunStatus Status,
    int? ExitCode,
    string StandardError,
    string? StartError = null);

/// <summary>
/// Variante de <see cref="IProcessRunner"/> para stdout binário (ex.: PCM s16le do FFmpeg).
/// Mesmas garantias: ArgumentList sem shell, timeout e cancelamento com Kill(entireProcessTree: true).
/// </summary>
public interface IBinaryStdoutProcessRunner
{
    Task<BinaryStdoutRunResult> RunAsync(
        ProcessRunRequest request,
        Func<Stream, CancellationToken, Task> readStdout,
        CancellationToken ct = default);
}

/// <summary>Cache de formas de onda (JSON versionado) em 3-Trabalho/cache/waveforms.</summary>
public interface IWaveformCache
{
    Task<WaveformDto?> ReadAsync(string projectRoot, string cacheKey, CancellationToken ct = default);
    Task WriteAsync(string projectRoot, string cacheKey, WaveformDto result, CancellationToken ct = default);
}

/// <summary>Item de um job de proxy. <see cref="SkipReason"/> preenchido = item já marcado como skipped (sem processo).</summary>
public sealed record ProxyJobItemPlan(
    Guid MediaId,
    string DisplayName,
    string? SourcePath,
    string Fingerprint,
    string? SkipReason);

public sealed record ProxyItemSnapshot(Guid MediaId, string State, string? Message);

public sealed record ProxyJobSnapshot(
    Guid JobId,
    Guid ProjectId,
    string State,
    int Processed,
    int Total,
    string? CurrentMedia,
    IReadOnlyList<ProxyItemSnapshot> Items);

/// <summary>Resultado de tentativa de início: Conflict = já existe job ativo do projeto (PROXY_IN_PROGRESS).</summary>
public sealed record ProxyStartOutcome(bool Conflict, ProxyJobSnapshot? Job);

/// <summary>Fila de proxies de prévia (um job ativo por projeto). Estado em memória; proxies são arquivos em 3-Trabalho.</summary>
public interface IProxyJobService
{
    ProxyStartOutcome Start(Guid projectId, string projectRoot, IReadOnlyList<ProxyJobItemPlan> items);
    ProxyJobSnapshot? GetJob(Guid jobId);

    /// <summary>Estado do item mais recente do projeto para a mídia (pending/running/ready/failed/skipped) ou null.</summary>
    ProxyItemSnapshot? GetLatestItem(Guid projectId, Guid mediaId);
}
