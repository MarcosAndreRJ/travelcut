using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TravelCut.Application.Audio;
using TravelCut.Application.Ports;
using TravelCut.Application.Services;
using TravelCut.Contracts.Dtos;
using TravelCut.Domain.Enums;

namespace TravelCut.Application.PostAnalysis;

/// <summary>Resultado da retomada. Status: queued | merged | skipped. Reason (skipped): código estável.</summary>
public sealed record PipelineResumeOutcome(
    string Status,
    string? Reason,
    int QueuedMedia,
    int RestoredBatches,
    int PartialFilesRemoved,
    bool PostAnalysisResumed);

/// <summary>
/// P07 — retomada ao abrir o projeto. Política explícita:
/// 1) remove <c>*.tcpart</c> órfãos (cópias parciais de importação interrompida) mais antigos que <see cref="PartialFileMinAge"/>;
/// 2) restaura a fila de lotes pendentes persistida e o pós-análise interrompido (ambos são pedidos já feitos pelo usuário/pipeline);
/// 3) calcula mídias de vídeo/áudio cujo arquivo existe e cuja análise é inexistente, stale ou com transcrição/silêncio "not_run"
///    (inclui cancelamento parcial) e as enfileira pelo mesmo gerenciador. NÃO reenfileira falhas/indisponíveis/ignoradas
///    (reanálise de falha é explícita: retryFailed) para não repetir infinitamente a cada abertura;
/// 4) só enfileira se AutoProcess != false, se não há pipeline ativo e se o Whisper está disponível (mesma regra da importação).
/// Nunca toca em mídia original.
/// </summary>
public sealed class PipelineResumeService
{
    public static readonly TimeSpan PartialFileMinAge = TimeSpan.FromMinutes(15);

    public const string ReasonAutoProcessDisabled = "auto_process_disabled";
    public const string ReasonPipelineActive = "pipeline_active";
    public const string ReasonNothingToResume = "nothing_to_resume";
    public const string ReasonWhisperUnavailable = "whisper_unavailable";
    public const string ReasonProjectNotFound = "project_not_found";

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IPipelineJobManager _pipeline;
    private readonly IPostAnalysisOrchestrator? _postAnalysis;
    private readonly TimeProvider _clock;
    private readonly ILogger<PipelineResumeService> _logger;

    public PipelineResumeService(
        IServiceScopeFactory scopeFactory,
        IPipelineJobManager pipeline,
        TimeProvider clock,
        ILogger<PipelineResumeService> logger,
        IPostAnalysisOrchestrator? postAnalysis = null)
    {
        _scopeFactory = scopeFactory;
        _pipeline = pipeline;
        _clock = clock;
        _logger = logger;
        _postAnalysis = postAnalysis;
    }

    /// <summary>Dispara a retomada sem bloquear o IPC; falhas vão ao log.</summary>
    public void ResumeInBackground(Guid projectId)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                var outcome = await ResumeProjectAsync(projectId, CancellationToken.None);
                _logger.LogInformation("Retomada do projeto {ProjectId}: {Status} {Reason} (mídias {Media}, lotes {Batches}, parciais removidos {Partials})",
                    projectId, outcome.Status, outcome.Reason, outcome.QueuedMedia, outcome.RestoredBatches, outcome.PartialFilesRemoved);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Falha ao retomar o pipeline do projeto {ProjectId}", projectId);
            }
        });
    }

    public async Task<PipelineResumeOutcome> ResumeProjectAsync(Guid projectId, CancellationToken ct = default)
    {
        Domain.Entities.Project? project;
        List<Domain.Entities.MediaAsset> media;
        using (var scope = _scopeFactory.CreateScope())
        {
            project = await scope.ServiceProvider.GetRequiredService<IProjectRepository>().GetByIdAsync(projectId, ct);
            if (project is null || string.IsNullOrWhiteSpace(project.RootPath))
            {
                return new PipelineResumeOutcome("skipped", ReasonProjectNotFound, 0, 0, 0, false);
            }
            media = await scope.ServiceProvider.GetRequiredService<IMediaRepository>().ListByProjectAsync(projectId, ct);
        }
        string root = Path.GetFullPath(project.RootPath);

        int removed = CleanOrphanPartials(root, _clock.GetUtcNow().UtcDateTime - PartialFileMinAge);
        bool postResumed = _postAnalysis is not null && await _postAnalysis.RestoreAsync(projectId, ct);
        int restored = await _pipeline.RestorePendingAsync(projectId, ct);

        PipelineResumeOutcome Skip(string reason) => new("skipped", reason, 0, restored, removed, postResumed);

        if (project.AutoProcess == false) return Skip(ReasonAutoProcessDisabled);

        var status = await _pipeline.GetStatusAsync(projectId, ct);
        if (status.State is "running" or "queued") return Skip(ReasonPipelineActive);

        var needing = new List<Guid>();
        foreach (var asset in media)
        {
            ct.ThrowIfCancellationRequested();
            if (asset.Kind is not (MediaKind.Video or MediaKind.Audio) || asset.IsArchived) continue;

            string full = Path.GetFullPath(Path.Combine(root, asset.RelativePath));
            if (!File.Exists(full)) continue;

            var analysis = await _pipeline.GetMediaAnalysisAsync(projectId, asset.MediaId, ct);
            if (NeedsAnalysis(analysis)) needing.Add(asset.MediaId);
        }

        if (needing.Count == 0) return Skip(ReasonNothingToResume);

        var env = await _pipeline.GetEnvironmentAsync(ct);
        if (env is { WhisperAvailable: false }) return Skip(ReasonWhisperUnavailable);

        var result = await _pipeline.EnqueueAsync(new StartPipelineRequest(projectId, needing, null, null, false), ct);
        return new PipelineResumeOutcome(
            result.Outcome == PipelineEnqueueResult.Merged ? "merged" : "queued", null, needing.Count, restored, removed, postResumed);
    }

    /// <summary>Análise ausente, stale ou com etapa de entrada ainda "not_run". Falha/indisponível/ignorada não conta.</summary>
    public static bool NeedsAnalysis(MediaAnalysisDto? analysis) =>
        analysis is null
        || analysis.Stale
        || analysis.TranscriptionStatus == "not_run"
        || analysis.SilenceStatus == "not_run";

    /// <summary>Remove cópias parciais (<c>*.tcpart</c>) de 1-Bruto/2-Assets mais antigas que o corte. Não segue reparse points.</summary>
    public int CleanOrphanPartials(string root, DateTime olderThanUtc)
    {
        int removed = 0;
        foreach (var folder in new[] { "1-Bruto", "2-Assets" })
        {
            string dir = Path.Combine(root, folder);
            if (!Directory.Exists(dir)) continue;

            try
            {
                var options = new EnumerationOptions
                {
                    RecurseSubdirectories = true,
                    IgnoreInaccessible = true,
                    AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.System,
                    MatchCasing = MatchCasing.CaseInsensitive
                };
                foreach (var file in Directory.EnumerateFiles(dir, "*" + MediaFileExpander.PartialFileExtension, options))
                {
                    try
                    {
                        if (File.GetLastWriteTimeUtc(file) > olderThanUtc) continue;
                        File.Delete(file);
                        removed++;
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        _logger.LogWarning(ex, "Não foi possível remover o arquivo parcial órfão {File}", Path.GetFileName(file));
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger.LogWarning(ex, "Falha ao varrer {Folder} por arquivos parciais", folder);
            }
        }
        return removed;
    }
}
