using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TravelCut.Application.Audio;
using TravelCut.Application.Ports;
using TravelCut.Application.UseCases;
using TravelCut.Contracts.Dtos;
using TravelCut.Domain.Entities;
using TravelCut.Domain.Enums;
using TravelCut.Infrastructure.Analysis;
using TravelCut.Infrastructure.Persistence;

namespace TravelCut.Infrastructure.Audio;

/// <summary>
/// Orquestra a análise audiovisual (F05) por projeto, com um job ativo por vez.
/// - Etapas por mídia na ordem transcription → silence → hesitation → scene → candidates.
/// - Serviços de Whisper/FFmpeg são chamados via interfaces; sem ferramenta o resultado é "unavailable", nunca estimado.
/// - Cache em 3-Trabalho/cache/analysis por (fingerprint+mídia, toolId, perfil); só resultados "completed" são cacheados.
/// - Escopo de DI (DbContext) por mídia; falha de persistência limpa o ChangeTracker. A transcrição e as sugestões são persistidas em transação.
/// </summary>
public class PipelineJobManager : IPipelineJobManager
{
    public const string CacheCategory = "analysis";
    public const string ProfileVersion = "analysis-v1";
    public const string WhisperModel = "large-v3";
    public const string MediaAnalysisToolId = "travelcut-media-analysis";
    public const string DefaultLanguage = "pt";

    /// <summary>Motivo registrado quando os metadados provam que a mídia não tem trilha de áudio (P08).</summary>
    public const string NoAudioReason = "sem áudio";

    /// <summary>Metadados ausentes ou não confiáveis = desconhecido (não pula); só AudioChannels==0 medido com sucesso pelo ffprobe conta como sem áudio.</summary>
    internal static bool HasNoAudioTrack(MediaAsset asset) =>
        asset.Metadata is { MetadataStatus: MediaMetadataStatus.Ok, AudioChannels: 0 };

    private static readonly TimeSpan EnvironmentCacheTtl = TimeSpan.FromSeconds(60);
    private static readonly SilenceDetectionOptions SilenceOptions = new();
    private static readonly Regex LanguagePattern = new(@"^[a-zA-Z]{2,3}(-[a-zA-Z0-9]{2,8})?$", RegexOptions.Compiled);

    private readonly ILogger<PipelineJobManager> _logger;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IWhisperTranscriptionService _whisper;
    private readonly ISilenceDetectionService _silence;
    private readonly ISceneClassifierService _scene;
    private readonly ICutCandidateService _candidates;
    private readonly IProjectOperationLock _projectLocks;

    /// <summary>Operação entre processos da análise (<c>3-Trabalho/.locks/analysis.lock</c>), mantida até o fim do job.</summary>
    public const string AnalysisLockOperation = "analysis";
    /// <summary>Espera curta pela trava: ocupada → PIPELINE_IN_PROGRESS.</summary>
    public static readonly TimeSpan AnalysisLockWait = TimeSpan.FromSeconds(1);

    private readonly object _registry = new();
    private readonly Dictionary<Guid, PipelineJobRun> _jobs = new();
    private readonly Dictionary<Guid, Guid> _activeByProject = new();
    private readonly Dictionary<Guid, Guid> _latestByProject = new();
    // Coalescência (F14.8/P02): lotes pendentes por projeto (em memória), iniciados ao liberar o slot. FIFO; lotes só se fundem com mesma configuração.
    private readonly Dictionary<Guid, List<PendingBatch>> _pending = new();

    private sealed class PendingBatch
    {
        public required Guid ProjectId { get; init; }
        public required List<string> Stages { get; init; }
        public required string Language { get; init; }
        public required bool Force { get; init; }
        /// <summary>null = todas as mídias do projeto.</summary>
        public List<Guid>? MediaIds { get; set; }

        public StartPipelineRequest ToRequest() => new(ProjectId, MediaIds, Stages, Language, Force);
    }

    /// <summary>F14.8/P01: processamento automático (plano + proxies) acionado só em completed/completed_with_issues.</summary>
    private readonly IPostAnalysisOrchestrator? _postAnalysis;

    private readonly SemaphoreSlim _environmentLock = new(1, 1);
    private AnalysisEnvironmentDto? _environment;
    private DateTime _environmentAtUtc = DateTime.MinValue;
    private long _environmentGeneration = -1;

    public PipelineJobManager(
        ILogger<PipelineJobManager> logger,
        IServiceScopeFactory scopeFactory,
        IWhisperTranscriptionService whisper,
        ISilenceDetectionService silence,
        ISceneClassifierService scene,
        ICutCandidateService candidates,
        IProjectOperationLock projectLocks,
        IPostAnalysisOrchestrator? postAnalysis = null)
    {
        _postAnalysis = postAnalysis;
        _logger = logger;
        _scopeFactory = scopeFactory;
        _whisper = whisper;
        _silence = silence;
        _scene = scene;
        _candidates = candidates;
        _projectLocks = projectLocks;
    }

    // ---------------------------------------------------------------- IPipelineJobManager

    public async Task<PipelineEnqueueResult> EnqueueAsync(StartPipelineRequest request, CancellationToken ct = default)
    {
        // Valida cedo (INVALID_STAGE / idioma): nada inválido entra na fila.
        var stages = NormalizeStages(request.Stages);
        var language = NormalizeLanguage(request.Language);

        if (TryMergeIntoPending(request, stages, language) is { } merged) return merged;

        try
        {
            return new PipelineEnqueueResult(PipelineEnqueueResult.Started, await StartAsync(request, ct), 0);
        }
        catch (InvalidOperationException ex) when (ex.Message == "PIPELINE_IN_PROGRESS")
        {
            // Corrida: outro start ocupou o slot entre a checagem e a reserva. Trava entre processos (sem job local): não há aviso de liberação → propaga.
            if (TryMergeIntoPending(request, stages, language) is { } raced) return raced;
            throw;
        }
    }

    private PipelineEnqueueResult? TryMergeIntoPending(StartPipelineRequest request, List<string> stages, string language)
    {
        lock (_registry)
        {
            if (!_activeByProject.TryGetValue(request.ProjectId, out var activeJobId)) return null;

            if (!_pending.TryGetValue(request.ProjectId, out var batches))
            {
                batches = new List<PendingBatch>();
                _pending[request.ProjectId] = batches;
            }

            var batch = batches.FirstOrDefault(b => b.Force == request.Force && b.Language == language && b.Stages.SequenceEqual(stages));
            if (batch == null)
            {
                batch = new PendingBatch
                {
                    ProjectId = request.ProjectId,
                    Stages = stages,
                    Language = language,
                    Force = request.Force,
                    MediaIds = request.MediaIds == null ? null : new List<Guid>()
                };
                batches.Add(batch);
            }

            if (request.MediaIds == null)
            {
                batch.MediaIds = null; // "todas" absorve qualquer lista
            }
            else if (batch.MediaIds != null)
            {
                foreach (var id in request.MediaIds)
                {
                    if (!batch.MediaIds.Contains(id)) batch.MediaIds.Add(id);
                }
            }

            _logger.LogInformation("Pipeline do projeto {ProjectId} em andamento ({JobId}): mídias mescladas ao lote pendente ({Count})",
                request.ProjectId, activeJobId, batch.MediaIds?.Count ?? -1);
            return new PipelineEnqueueResult(PipelineEnqueueResult.Merged, activeJobId, batch.MediaIds?.Count ?? 0);
        }
    }

    /// <summary>Inicia o próximo lote pendente do projeto (se o slot estiver livre). Nunca lança: falhas vão ao log.</summary>
    private async Task DrainPendingAsync(Guid projectId)
    {
        try
        {
            while (true)
            {
                PendingBatch? batch;
                lock (_registry)
                {
                    // Slot ocupado: o job ativo drena ao terminar.
                    if (_activeByProject.ContainsKey(projectId)) return;
                    if (!_pending.TryGetValue(projectId, out var list) || list.Count == 0) { _pending.Remove(projectId); return; }
                    batch = list[0];
                    list.RemoveAt(0);
                    if (list.Count == 0) _pending.Remove(projectId);
                }

                try
                {
                    var jobId = await StartAsync(batch.ToRequest(), CancellationToken.None);
                    _logger.LogInformation("Lote pendente do projeto {ProjectId} iniciado como job {JobId}", projectId, jobId);
                    return; // o próximo lote é drenado quando este job liberar o slot
                }
                catch (InvalidOperationException ex) when (ex.Message == "PIPELINE_IN_PROGRESS")
                {
                    lock (_registry)
                    {
                        if (_activeByProject.ContainsKey(projectId))
                        {
                            // Outro job tomou o slot: devolve o lote ao início da fila; ele drena ao terminar.
                            if (!_pending.TryGetValue(projectId, out var list)) { list = new List<PendingBatch>(); _pending[projectId] = list; }
                            list.Insert(0, batch);
                            return;
                        }
                    }
                    _logger.LogError("Lote pendente do projeto {ProjectId} descartado: a trava de análise entre processos está ocupada.", projectId);
                }
                catch (Exception ex)
                {
                    // Ex.: NO_MEDIA_TO_ANALYZE / PROJECT_NOT_FOUND. Segue para o próximo lote.
                    _logger.LogError(ex, "Falha ao iniciar lote pendente do projeto {ProjectId}; lote descartado.", projectId);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Falha inesperada ao drenar a fila pendente do projeto {ProjectId}", projectId);
        }
    }

    /// <summary>Cancelamento explícito do usuário descarta os lotes pendentes (não reinicia análise sem pedido).</summary>
    private void DropPending(Guid projectId)
    {
        lock (_registry)
        {
            if (_pending.Remove(projectId, out var dropped))
            {
                _logger.LogWarning("Análise cancelada: {Count} lote(s) pendente(s) do projeto {ProjectId} descartado(s).", dropped.Count, projectId);
            }
        }
    }

    public async Task<Guid> StartAsync(StartPipelineRequest request, CancellationToken ct = default)
    {
        var stages = NormalizeStages(request.Stages);
        var language = NormalizeLanguage(request.Language);
        var job = new PipelineJobRun(Guid.NewGuid(), request.ProjectId, stages, language, request.Force);

        // Reserva o slot do projeto antes de qualquer await: impede dois starts concorrentes.
        lock (_registry)
        {
            if (_activeByProject.ContainsKey(request.ProjectId))
            {
                throw new InvalidOperationException("PIPELINE_IN_PROGRESS");
            }
            _activeByProject[request.ProjectId] = job.JobId;
            _jobs[job.JobId] = job;
        }

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var sp = scope.ServiceProvider;

            var project = await sp.GetRequiredService<IProjectRepository>().GetByIdAsync(request.ProjectId, ct)
                ?? throw new InvalidOperationException("PROJECT_NOT_FOUND");
            if (string.IsNullOrWhiteSpace(project.RootPath))
            {
                throw new InvalidOperationException("PROJECT_NOT_FOUND");
            }

            string root = Path.GetFullPath(project.RootPath);
            var all = await sp.GetRequiredService<IMediaRepository>().ListByProjectAsync(request.ProjectId, ct);
            var selected = SelectMedia(all, request.MediaIds);
            if (selected.Count == 0)
            {
                throw new InvalidOperationException("NO_MEDIA_TO_ANALYZE");
            }

            // Trava entre processos: outra instância (app ou MCP) analisando este projeto → PIPELINE_IN_PROGRESS.
            IAsyncDisposable? analysisLock;
            try
            {
                analysisLock = await _projectLocks.TryAcquireAsync(root, AnalysisLockOperation, AnalysisLockWait, ct);
            }
            catch (ProjectLockException ex)
            {
                // Falha estrutural da trava (pasta ausente/inacessível): código estável para o IPC, sem caminho.
                throw new InvalidOperationException(ex.Code, ex);
            }
            if (analysisLock is null)
            {
                throw new InvalidOperationException("PIPELINE_IN_PROGRESS");
            }
            job.SetAnalysisLock(analysisLock);

            job.ProjectRoot = root;
            job.InitializeMedia(selected.Select(a => CreateWork(root, a)));
        }
        catch
        {
            var held = job.TakeAnalysisLock();
            if (held is not null)
            {
                try
                {
                    await held.DisposeAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Falha ao liberar a trava de análise após erro no início do job {JobId}", job.JobId);
                }
            }
            lock (_registry)
            {
                _activeByProject.Remove(request.ProjectId);
                _jobs.Remove(job.JobId);
            }
            // Lotes mesclados durante a breve reserva não podem ficar órfãos.
            _ = Task.Run(() => DrainPendingAsync(request.ProjectId));
            throw;
        }

        lock (_registry)
        {
            _latestByProject[request.ProjectId] = job.JobId;
        }

        job.Completion = Task.Run(() => RunJobAsync(job));
        _logger.LogInformation("Análise {JobId} iniciada no projeto {ProjectId} com {MediaCount} mídia(s) e etapas {Stages}",
            job.JobId, job.ProjectId, job.Media.Count, string.Join(",", stages));

        return job.JobId;
    }

    public async Task<PipelineStatusDto> GetStatusAsync(Guid projectId, CancellationToken ct = default)
    {
        PipelineJobRun? job;
        lock (_registry)
        {
            job = _latestByProject.TryGetValue(projectId, out var jobId) && _jobs.TryGetValue(jobId, out var found) ? found : null;
        }

        var environment = await GetEnvironmentAsync(ct);
        if (job == null)
        {
            return new PipelineStatusDto(
                JobId: null,
                ProjectId: projectId,
                State: "idle",
                Stages: new List<PipelineStageStatusDto>(),
                Media: new List<PipelineMediaStatusDto>(),
                CurrentMedia: null,
                Environment: environment,
                StartedAtUtc: null,
                FinishedAtUtc: null,
                PostAnalysis: _postAnalysis?.GetStatus(projectId));
        }

        return job.Snapshot(environment) with { PostAnalysis = _postAnalysis?.GetStatus(projectId) };
    }

    public PipelineDiagnosticsDto GetDiagnostics(Guid projectId)
    {
        lock (_registry)
        {
            return _latestByProject.TryGetValue(projectId, out var jobId) && _jobs.TryGetValue(jobId, out var job)
                ? job.DiagnosticsSnapshot()
                : new PipelineDiagnosticsDto(null, new List<WhisperExecutionDto>());
        }
    }

    public bool Cancel(Guid jobId)
    {
        lock (_registry)
        {
            if (!_jobs.TryGetValue(jobId, out var job) || job.IsFinished) return false;
            job.Cts.Cancel();
            return true;
        }
    }

    public async Task<AnalysisEnvironmentDto> GetEnvironmentAsync(CancellationToken ct = default, bool forceRefresh = false)
    {
        if (!forceRefresh && PeekEnvironment() is { } cached) return cached;

        await _environmentLock.WaitAsync(ct);
        try
        {
            if (!forceRefresh && PeekEnvironment() is { } again) return again;

            if (forceRefresh) _whisper.InvalidateEnvironment();
            // Geração lida antes do probe: invalidação durante o probe força nova consulta na próxima chamada.
            long generation = _whisper.EnvironmentGeneration;
            _environment = await ProbeEnvironmentAsync(ct);
            _environmentAtUtc = DateTime.UtcNow;
            _environmentGeneration = generation;
            return _environment;
        }
        finally
        {
            _environmentLock.Release();
        }
    }

    /// <summary>
    /// Ambiente em cache (TTL de 60 s, inclusive quando Whisper/modelo estão ausentes) ou null se expirado/invalidado
    /// (instalação via assistente). Nunca dispara probe: seguro para caminhos que não podem bloquear.
    /// </summary>
    public AnalysisEnvironmentDto? PeekEnvironment()
    {
        var cached = _environment;
        return cached != null
            && DateTime.UtcNow - _environmentAtUtc < EnvironmentCacheTtl
            && _environmentGeneration == _whisper.EnvironmentGeneration
                ? cached
                : null;
    }

    public async Task<MediaAnalysisDto?> GetMediaAnalysisAsync(Guid projectId, Guid mediaId, CancellationToken ct = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var sp = scope.ServiceProvider;

        var media = await sp.GetRequiredService<IMediaRepository>().GetByIdAsync(mediaId, ct);
        if (media == null || media.ProjectId != projectId) return null;

        var project = await sp.GetRequiredService<IProjectRepository>().GetByIdAsync(projectId, ct);
        if (project == null || string.IsNullOrWhiteSpace(project.RootPath)) return null;

        var cache = sp.GetRequiredService<IMediaCacheManager>();
        var key = AnalysisCacheKey(cache, media, MediaAnalysisToolId);
        var text = await cache.ReadArtifactTextAsync(Path.GetFullPath(project.RootPath), CacheCategory, key, ct);
        return Deserialize<MediaAnalysisDto>(text);
    }

    // ---------------------------------------------------------------- Execução

    private async Task RunJobAsync(PipelineJobRun job)
    {
        var ct = job.Cts.Token;
        bool canceled = false;
        job.MarkRunning();

        try
        {
            foreach (var media in job.Media)
            {
                ct.ThrowIfCancellationRequested();
                job.SetCurrentMedia(media.RelativePath);

                // Escopo (e DbContext) por mídia: estado rastreado de uma falha não contamina a mídia seguinte.
                using var mediaScope = _scopeFactory.CreateScope();
                try
                {
                    await ProcessMediaAsync(job, media, mediaScope.ServiceProvider, ct);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Falha inesperada ao analisar mídia {MediaId} no job {JobId}", media.MediaId, job.JobId);
                    job.FailNonTerminal("Falha inesperada nesta mídia; detalhes no log.");
                }
            }

            job.SetCurrentMedia(null);
            await ReleaseSlotAsync(job);
            job.Finish(forcedState: null);
            _logger.LogInformation("Análise {JobId} finalizada com estado {State}", job.JobId, job.State);
            TriggerPostAnalysis(job);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            canceled = true;
            job.CancelNonTerminal();
            await ReleaseSlotAsync(job);
            job.Finish(forcedState: "canceled");
            _logger.LogInformation("Análise {JobId} cancelada", job.JobId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Análise {JobId} falhou", job.JobId);
            job.FailNonTerminal("Falha interna da análise; detalhes no log.");
            await ReleaseSlotAsync(job);
            job.Finish(forcedState: "failed");
        }
        finally
        {
            // Redundante por segurança (idempotente): garante liberação mesmo em caminhos não previstos.
            await ReleaseSlotAsync(job);

            // Slot livre e job já finalizado: inicia o lote pendente (mídias importadas durante este pipeline) ou o descarta se cancelado.
            if (canceled) DropPending(job.ProjectId);
            else await DrainPendingAsync(job.ProjectId);
        }
    }

    /// <summary>
    /// Aciona o orquestrador pós-análise em segundo plano (sem bloquear o fim do job). Nunca em failed/canceled.
    /// Falha do orquestrador é registrada e não altera o estado da análise.
    /// </summary>
    private void TriggerPostAnalysis(PipelineJobRun job)
    {
        if (_postAnalysis is null) return;
        if (job.State is not ("completed" or "completed_with_issues")) return;

        try
        {
            _ = _postAnalysis.ScheduleAsync(job.ProjectId, job.JobId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Falha ao acionar o processamento pós-análise do job {JobId}", job.JobId);
        }
    }

    /// <summary>Libera a reserva do projeto somente se ainda pertence a este job (antes de marcar o job como finalizado).</summary>
    private void ReleaseProjectSlot(PipelineJobRun job)
    {
        lock (_registry)
        {
            if (_activeByProject.TryGetValue(job.ProjectId, out var activeJobId) && activeJobId == job.JobId)
            {
                _activeByProject.Remove(job.ProjectId);
            }
        }
    }

    /// <summary>
    /// Libera a reserva em memória e a trava entre processos (antes de o job aparecer como finalizado). Idempotente.
    /// Falha ao liberar a trava é registrada em log e NÃO marca o job como failed: o trabalho da análise já terminou.
    /// </summary>
    private async Task ReleaseSlotAsync(PipelineJobRun job)
    {
        ReleaseProjectSlot(job);
        var held = job.TakeAnalysisLock();
        if (held is null)
        {
            return;
        }

        try
        {
            await held.DisposeAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Falha ao liberar a trava de análise do job {JobId}", job.JobId);
        }
    }

    private async Task ProcessMediaAsync(PipelineJobRun job, PipelineMediaWork media, IServiceProvider sp, CancellationToken ct)
    {
        if (media.AbsolutePath == null)
        {
            job.FailStages(media, media.PathError ?? "Caminho da mídia inválido.", onlyNonTerminal: false);
            return;
        }
        if (!File.Exists(media.AbsolutePath))
        {
            job.FailStages(media, "Arquivo da mídia não encontrado na pasta do projeto.", onlyNonTerminal: false);
            return;
        }

        var cache = sp.GetRequiredService<IMediaCacheManager>();
        var previousJson = await cache.ReadArtifactTextAsync(job.ProjectRoot, CacheCategory,
            AnalysisCacheKey(cache, media.Asset, MediaAnalysisToolId), ct);
        var work = MediaAnalysisWork.From(Deserialize<MediaAnalysisDto>(previousJson));

        // P06: etapas concluídas são gravadas no cache de análise mesmo se o job for cancelado no meio da mídia
        // (a transcrição já persistida no banco não pode ficar como "not_run" em analysis.getMedia).
        bool progressed = false;
        try
        {
        foreach (var stage in job.Stages)
        {
            ct.ThrowIfCancellationRequested();
            job.SetStage(media, stage, StageStates.Running, null);

            try
            {
                switch (stage)
                {
                    case PipelineStages.Transcription:
                        await RunTranscriptionAsync(job, media, work, sp, ct);
                        break;
                    case PipelineStages.Silence:
                        await RunSilenceAsync(job, media, work, sp, ct);
                        break;
                    case PipelineStages.Hesitation:
                        RunHesitation(job, media, work);
                        break;
                    case PipelineStages.Scene:
                        RunScene(job, media, work, sp);
                        break;
                    case PipelineStages.Candidates:
                        await RunCandidatesAsync(job, media, work, sp, ct);
                        break;
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Etapa {Stage} falhou para mídia {MediaId} no job {JobId}", stage, media.MediaId, job.JobId);
                job.SetStage(media, stage, StageStates.Failed, "Falha inesperada nesta etapa; detalhes no log.");
            }
            progressed = true;
        }
        }
        finally
        {
            if (progressed)
            {
                try
                {
                    // Sem o token de cancelamento: o resultado parcial precisa ser gravado justamente quando houve cancelamento.
                    var analysis = work.ToDto(media.MediaId, DateTime.UtcNow);
                    await cache.WriteArtifactTextAsync(job.ProjectRoot, CacheCategory,
                        AnalysisCacheKey(cache, media.Asset, MediaAnalysisToolId), Serialize(analysis), CancellationToken.None);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Falha ao gravar o cache de análise da mídia {MediaId} no job {JobId}", media.MediaId, job.JobId);
                }
            }
        }

        ct.ThrowIfCancellationRequested();
    }

    private async Task RunTranscriptionAsync(PipelineJobRun job, PipelineMediaWork media, MediaAnalysisWork work, IServiceProvider sp, CancellationToken ct)
    {
        // P08: sem trilha de áudio não há o que transcrever; nem inicia o worker Python.
        if (HasNoAudioTrack(media.Asset))
        {
            work.Transcript = null;
            work.TranscriptionStatus = StageStates.Skipped;
            work.TranscriptionReason = NoAudioReason;
            job.SetStage(media, PipelineStages.Transcription, StageStates.Skipped, NoAudioReason);
            return;
        }

        var cache = sp.GetRequiredService<IMediaCacheManager>();
        string toolId = $"whisper:{WhisperModel}:{job.Language}";
        string key = AnalysisCacheKey(cache, media.Asset, toolId);

        TranscriptionOutcome? outcome = null;
        if (!job.Force)
        {
            var cached = Deserialize<TranscriptionOutcome>(await cache.ReadArtifactTextAsync(job.ProjectRoot, CacheCategory, key, ct));
            if (cached is { Status: "completed", Transcript: not null })
            {
                outcome = cached;
                job.MarkFromCache(media);
            }
        }

        if (outcome == null)
        {
            outcome = await _whisper.TranscribeAsync(media.AbsolutePath!, media.MediaId, WhisperModel, job.Language, ct,
                new SynchronousProgress<TranscriptionProgressUpdate>(job.ReportTranscriptionProgress),
                job.BeginWhisperExecution(media));

            // P09: duração do áudio transcrito muito diferente da dos metadados = possível truncamento. Não persiste nem cacheia.
            if (outcome.Status == "completed" && outcome.Transcript != null
                && media.Asset.Metadata is { MetadataStatus: MediaMetadataStatus.Ok } md
                && TranscriptValidator.CheckDurationMismatch(outcome.Transcript.AudioDurationSeconds, md.DurationSeconds) is { } mismatch)
            {
                _logger.LogWarning("Transcrição da mídia {MediaId} descartada do cache: {Mismatch}", media.MediaId, mismatch);
                work.Transcript = null;
                work.TranscriptionStatus = StageStates.CompletedWithIssues;
                work.TranscriptionReason = mismatch;
                job.SetStage(media, PipelineStages.Transcription, StageStates.CompletedWithIssues, mismatch);
                return;
            }

            if (outcome.Status == "completed" && outcome.Transcript != null)
            {
                await cache.WriteArtifactTextAsync(job.ProjectRoot, CacheCategory, key, Serialize(outcome), ct);
            }
        }

        ct.ThrowIfCancellationRequested();

        if (outcome.Status == "completed" && outcome.Transcript != null)
        {
            var transcript = outcome.Transcript with { MediaId = media.MediaId };
            await PersistTranscriptAsync(sp, transcript, ct);

            work.Transcript = transcript;
            work.TranscriptionStatus = "completed";
            work.TranscriptionReason = null;
            job.SetStage(media, PipelineStages.Transcription, StageStates.Completed, null);
            return;
        }

        string state = outcome.Status == "completed" ? StageStates.Failed : StageStates.FromOutcome(outcome.Status);
        string reason = outcome.Reason ?? (outcome.Status == "completed"
            ? "Resposta de transcrição sem conteúdo."
            : "Transcrição indisponível.");

        work.Transcript = null;
        work.TranscriptionStatus = state;
        work.TranscriptionReason = reason;
        job.SetStage(media, PipelineStages.Transcription, state, reason);
    }

    private async Task RunSilenceAsync(PipelineJobRun job, PipelineMediaWork media, MediaAnalysisWork work, IServiceProvider sp, CancellationToken ct)
    {
        if (HasNoAudioTrack(media.Asset))
        {
            work.Silences = new List<VadSilenceSegmentDto>();
            work.SilenceStatus = StageStates.Skipped;
            work.SilenceReason = NoAudioReason;
            job.SetStage(media, PipelineStages.Silence, StageStates.Skipped, NoAudioReason);
            return;
        }

        var cache = sp.GetRequiredService<IMediaCacheManager>();
        string toolId = SilenceToolId(SilenceOptions);
        string key = AnalysisCacheKey(cache, media.Asset, toolId);

        SilenceDetectionOutcome? outcome = null;
        if (!job.Force)
        {
            var cached = Deserialize<SilenceDetectionOutcome>(await cache.ReadArtifactTextAsync(job.ProjectRoot, CacheCategory, key, ct));
            if (cached is { Status: "completed" })
            {
                outcome = cached;
                job.MarkFromCache(media);
            }
        }

        if (outcome == null)
        {
            outcome = await _silence.DetectSilenceAsync(media.AbsolutePath!, SilenceOptions, ct);
            if (outcome.Status == "completed")
            {
                await cache.WriteArtifactTextAsync(job.ProjectRoot, CacheCategory, key, Serialize(outcome), ct);
            }
        }

        ct.ThrowIfCancellationRequested();

        if (outcome.Status == "completed")
        {
            work.Silences = outcome.Segments ?? new List<VadSilenceSegmentDto>();
            work.SilenceStatus = "completed";
            work.SilenceReason = null;
            job.SetStage(media, PipelineStages.Silence, StageStates.Completed, null);
            return;
        }

        string state = StageStates.FromOutcome(outcome.Status);
        string reason = outcome.Reason ?? "Detecção de silêncios indisponível.";
        work.Silences = new List<VadSilenceSegmentDto>();
        work.SilenceStatus = state;
        work.SilenceReason = reason;
        job.SetStage(media, PipelineStages.Silence, state, reason);
    }

    private void RunHesitation(PipelineJobRun job, PipelineMediaWork media, MediaAnalysisWork work)
    {
        if (work.TranscriptionStatus != "completed" || work.Transcript == null)
        {
            work.Hesitations = new List<HesitationCandidateDto>();
            job.SetStage(media, PipelineStages.Hesitation, StageStates.Skipped,
                "Sem transcrição concluída: hesitações não analisadas.");
            return;
        }

        var silences = work.SilenceStatus == "completed" ? work.Silences : new List<VadSilenceSegmentDto>();
        work.Hesitations = _silence.DetectHesitationsAndFillers(work.Transcript, silences);
        job.SetStage(media, PipelineStages.Hesitation, StageStates.Completed, null);
    }

    private void RunScene(PipelineJobRun job, PipelineMediaWork media, MediaAnalysisWork work, IServiceProvider sp)
    {
        var details = BuildMediaDetails(job, media);
        var transcript = work.TranscriptionStatus == "completed" ? work.Transcript : null;
        var silences = work.SilenceStatus == "completed" ? work.Silences : null;

        work.Scene = _scene.ClassifyScene(details, transcript, silences);
        job.SetStage(media, PipelineStages.Scene, StageStates.Completed, null);
    }

    private async Task RunCandidatesAsync(PipelineJobRun job, PipelineMediaWork media, MediaAnalysisWork work, IServiceProvider sp, CancellationToken ct)
    {
        bool hasSilence = work.SilenceStatus == "completed";
        bool hasTranscript = work.TranscriptionStatus == "completed" && work.Transcript != null;

        if (!hasSilence && !hasTranscript)
        {
            job.SetStage(media, PipelineStages.Candidates, StageStates.Skipped,
                "Sem silêncios nem transcrição analisados: nenhum candidato gerado.");
            return;
        }

        var candidates = _candidates.GenerateCutCandidates(
            media.MediaId,
            hasTranscript ? work.Transcript : null,
            hasSilence ? work.Silences : new List<VadSilenceSegmentDto>(),
            hasTranscript ? work.Hesitations : new List<HesitationCandidateDto>());

        await ReplaceProposedSuggestionsAsync(sp, job.ProjectId, media.MediaId, candidates, ct);
        work.CutCandidates = candidates;

        if (hasSilence && hasTranscript)
        {
            job.SetStage(media, PipelineStages.Candidates, StageStates.Completed, null);
        }
        else
        {
            job.SetStage(media, PipelineStages.Candidates, StageStates.CompletedWithIssues,
                hasSilence
                    ? "Transcrição indisponível: candidatos sem contexto de palavras nem checagem de risada."
                    : "Silêncios indisponíveis: candidatos apenas de hesitações.");
        }
    }

    // ---------------------------------------------------------------- Persistência

    private static async Task PersistTranscriptAsync(IServiceProvider sp, TranscriptDto dto, CancellationToken ct)
    {
        var db = sp.GetRequiredService<TravelCutDbContext>();
        var repo = sp.GetRequiredService<ITranscriptRepository>();

        var entity = new Transcript
        {
            TranscriptId = dto.TranscriptId,
            MediaId = dto.MediaId,
            ModelId = dto.ModelId,
            Language = dto.Language,
            AudioDurationSeconds = dto.AudioDurationSeconds,
            FullText = dto.FullText,
            CreatedAtUtc = DateTime.UtcNow,
            Words = dto.Words.Select(w => new TranscriptWord
            {
                Seq = w.Seq,
                StartSeconds = w.StartSeconds,
                EndSeconds = w.EndSeconds,
                Text = w.Text,
                Confidence = w.Confidence
            }).ToList()
        };

        await RunPersistenceAsync(db, async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            await repo.SaveAsync(entity, ct);
            await tx.CommitAsync(ct);
        });
    }

    /// <summary>
    /// Executa uma persistência. Em falha, descarta o estado rastreado pelo DbContext: sem isso, entidades
    /// adicionadas e não gravadas seriam reenviadas pela próxima gravação do mesmo contexto.
    /// </summary>
    internal static async Task RunPersistenceAsync(TravelCutDbContext db, Func<Task> persist)
    {
        try
        {
            await persist();
        }
        catch
        {
            db.ChangeTracker.Clear();
            throw;
        }
    }

    /// <summary>
    /// Substitui os candidatos Proposed da mídia gerados pelos autores de análise; preserva os já decididos
    /// (Approved/Rejected/Deferred/Modified) e não recria um candidato com o mesmo intervalo e kind de uma decisão.
    /// </summary>
    private static async Task ReplaceProposedSuggestionsAsync(
        IServiceProvider sp,
        Guid projectId,
        Guid mediaId,
        List<EditSuggestionDto> candidates,
        CancellationToken ct)
    {
        var db = sp.GetRequiredService<TravelCutDbContext>();
        var planRepo = sp.GetRequiredService<IEditPlanRepository>();

        // O status gravado da sugestão não muda com decisões (estado efetivo vem de EditDecisions),
        // então "decidida" = tem qualquer decisão registrada, inclusive undo. Essas nunca são apagadas.
        var mediaSuggestions = (await planRepo.ListSuggestionsByProjectAsync(projectId, ct))
            .Where(s => s.MediaId == mediaId)
            .ToList();
        var mediaSuggestionIds = mediaSuggestions.Select(s => s.SuggestionId).ToList();
        var withDecision = (await db.EditDecisions
                .Where(d => mediaSuggestionIds.Contains(d.SuggestionId))
                .Select(d => d.SuggestionId)
                .Distinct()
                .ToListAsync(ct))
            .ToHashSet();
        var decided = mediaSuggestions
            .Where(s => s.Status != SuggestionStatus.Proposed || withDecision.Contains(s.SuggestionId))
            .ToList();
        var protectedIds = decided.Select(s => s.SuggestionId).ToList();

        var now = DateTime.UtcNow;
        var toInsert = candidates
            .Where(c => !decided.Any(d => SuggestionStorageMapper.SameSuggestion(d, c)))
            .Select(c => SuggestionStorageMapper.ToEntity(c, now))
            .ToList();

        await RunPersistenceAsync(db, async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            await db.EditSuggestions
                .Where(s => s.MediaId == mediaId
                    && s.Status == SuggestionStatus.Proposed
                    && !protectedIds.Contains(s.SuggestionId)
                    // Reconfere dentro da transação: decisão gravada após a leitura acima também protege.
                    && !db.EditDecisions.Any(d => d.SuggestionId == s.SuggestionId)
                    && (s.Author == CutCandidateService.SilenceAuthor || s.Author == CutCandidateService.HesitationAuthor))
                .ExecuteDeleteAsync(ct);

            if (toInsert.Count > 0)
            {
                await planRepo.SaveSuggestionsBatchAsync(toInsert, ct);
            }
            await tx.CommitAsync(ct);
        });
    }

    // ---------------------------------------------------------------- Cache e chaves

    /// <summary>
    /// Chave do cache: a identidade da mídia inclui o MediaId, então duplicatas de conteúdo não compartilham
    /// metadados (MediaId/caminho) de outra mídia. Sem fingerprint, usa tamanho e data de modificação.
    /// </summary>
    private static string AnalysisCacheKey(IMediaCacheManager cache, MediaAsset media, string toolId)
    {
        string content = string.IsNullOrWhiteSpace(media.Fingerprint)
            ? $"size:{media.ByteLength}:mtime:{media.LastWriteUtc.Ticks}"
            : media.Fingerprint;
        string identity = $"{content}|{media.MediaId:N}";
        return cache.ComputeCacheKey(identity, toolId, ProfileVersion);
    }

    public static string SilenceToolId(SilenceDetectionOptions options) =>
        FormattableString.Invariant($"ffmpeg-silencedetect:{options.NoiseToleranceDb:0}dB:{options.MinSilenceDurationSeconds:0.###}");

    private static T? Deserialize<T>(string? json) where T : class
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            return JsonSerializer.Deserialize<T>(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string Serialize<T>(T value) => JsonSerializer.Serialize(value);

    // ---------------------------------------------------------------- Ambiente

    private async Task<AnalysisEnvironmentDto> ProbeEnvironmentAsync(CancellationToken ct)
    {
        var reasons = new List<string>();

        bool ffmpeg = false;
        string? ffmpegVersion = null;
        try
        {
            // Síncronos e podem bloquear até ~15 s: fora da thread de UI do WebView2.
            (ffmpeg, ffmpegVersion) = await Task.Run(() =>
            {
                bool available = _silence.IsFfmpegAvailable();
                return (available, available ? _silence.GetFfmpegVersion() : null);
            }, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Falha ao verificar FFmpeg");
        }
        if (!ffmpeg)
        {
            reasons.Add("FFmpeg/FFprobe não encontrado: detecção de silêncios indisponível nesta máquina.");
        }

        WhisperProbeResult? probe = null;
        try
        {
            probe = await _whisper.ProbeEnvironmentAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Falha ao sondar Python/Whisper");
        }

        bool python = probe?.HasPython ?? false;
        bool installed = probe?.HasWhisper ?? false;
        // null = desconhecido (probe sem a informação): não bloqueia. O worker real sempre informa os dois campos.
        bool modelCached = probe?.ModelCached != false;
        bool ffmpegOnPath = probe?.FfmpegOnPath != false;
        // Whisper só é "disponível" com TODAS as dependências reais: Python + openai-whisper + modelo em cache + ffmpeg no PATH do worker.
        bool whisper = probe != null && python && installed && modelCached && ffmpegOnPath;

        var whisperReasons = new List<string>();
        if (probe == null)
        {
            whisperReasons.Add("Não foi possível sondar Python/Whisper: transcrição indisponível.");
        }
        else
        {
            if (!python)
            {
                whisperReasons.Add("Python não encontrado: transcrição Whisper indisponível.");
            }
            else if (!installed)
            {
                whisperReasons.Add("Whisper large-v3 não disponível no Python configurado: transcrição indisponível.");
            }
            else
            {
                if (!modelCached)
                {
                    whisperReasons.Add("Modelo Whisper large-v3 não está no cache local (o worker não baixa modelos automaticamente): transcrição indisponível.");
                }
                if (!ffmpegOnPath)
                {
                    whisperReasons.Add("ffmpeg não está no PATH do worker Whisper: transcrição indisponível.");
                }
            }
        }
        reasons.AddRange(whisperReasons);

        bool cpuOnly = probe != null && python
            && (probe.HasCuda == false || string.Equals(probe.Device, "cpu", StringComparison.OrdinalIgnoreCase));
        if (cpuOnly)
        {
            reasons.Add("CUDA não disponível: Whisper large-v3 rodaria em CPU (processamento lento).");
        }

        return new AnalysisEnvironmentDto(
            FfmpegAvailable: ffmpeg,
            FfmpegVersion: ffmpegVersion,
            PythonAvailable: python,
            WhisperAvailable: whisper,
            WhisperEngine: installed ? probe?.Engine : null,
            WhisperModel: WhisperModel,
            CudaAvailable: probe?.HasCuda,
            Reasons: reasons,
            Device: python ? probe?.Device : null,
            ModelCached: python ? probe?.ModelCached : null,
            CpuFallback: whisper && cpuOnly,
            WhisperUnavailableReason: whisper || whisperReasons.Count == 0 ? null : string.Join(" ", whisperReasons));
    }

    // ---------------------------------------------------------------- Helpers de entrada

    private static List<string> NormalizeStages(List<string>? stages)
    {
        if (stages == null || stages.Count == 0)
        {
            return PipelineStages.Ordered.ToList();
        }

        var requested = stages.Select(s => (s ?? string.Empty).Trim().ToLowerInvariant()).ToHashSet(StringComparer.Ordinal);
        if (requested.Any(s => !PipelineStages.Ordered.Contains(s, StringComparer.Ordinal)))
        {
            throw new InvalidOperationException("INVALID_STAGE");
        }

        return PipelineStages.Ordered.Where(requested.Contains).ToList();
    }

    private static string NormalizeLanguage(string? language)
    {
        var value = string.IsNullOrWhiteSpace(language) ? DefaultLanguage : language.Trim();
        if (!LanguagePattern.IsMatch(value))
        {
            throw new InvalidOperationException("INVALID_LANGUAGE");
        }
        return value;
    }

    private static List<MediaAsset> SelectMedia(List<MediaAsset> all, List<Guid>? mediaIds)
    {
        var eligible = all.Where(a => a.Kind is MediaKind.Video or MediaKind.Audio).ToList();
        if (mediaIds == null || mediaIds.Count == 0)
        {
            return eligible;
        }

        var wanted = mediaIds.ToHashSet();
        var selected = eligible.Where(a => wanted.Contains(a.MediaId)).ToList();
        if (selected.Count != wanted.Count)
        {
            throw new InvalidOperationException("MEDIA_NOT_FOUND");
        }
        return selected;
    }

    private static PipelineMediaWork CreateWork(string root, MediaAsset asset)
    {
        string? absolute = null;
        string? error = null;
        try
        {
            string full = Path.GetFullPath(Path.Combine(root, asset.RelativePath));
            string prefix = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                absolute = full;
            }
            else
            {
                error = "Caminho da mídia fora da pasta do projeto.";
            }
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            error = "Caminho da mídia inválido.";
        }

        return new PipelineMediaWork
        {
            MediaId = asset.MediaId,
            RelativePath = asset.RelativePath,
            AbsolutePath = absolute,
            Asset = asset,
            PathError = error
        };
    }

    private static MediaDetailsDto BuildMediaDetails(PipelineJobRun job, PipelineMediaWork media)
    {
        var asset = media.Asset;
        var md = asset.Metadata;
        return new MediaDetailsDto(
            MediaId: asset.MediaId,
            ProjectId: job.ProjectId,
            RelativePath: asset.RelativePath,
            FullPath: media.AbsolutePath ?? string.Empty,
            ByteLength: asset.ByteLength,
            Kind: asset.Kind == MediaKind.Audio ? "audio" : "video",
            Fingerprint: asset.Fingerprint,
            LastWriteUtc: asset.LastWriteUtc,
            Codec: md?.Codec ?? string.Empty,
            Width: md?.Width ?? 0,
            Height: md?.Height ?? 0,
            FrameRate: md?.FrameRate ?? 0.0,
            TimeBase: md?.TimeBase.ToString() ?? string.Empty,
            DurationSeconds: md?.DurationSeconds ?? 0.0,
            DurationTicks: md?.DurationTicks ?? 0,
            AudioChannels: md?.AudioChannels ?? 0,
            AudioSampleRate: md?.AudioSampleRate ?? 0,
            CreationTimeUtc: md?.CreationTimeUtc,
            SourceDateEvidence: md?.SourceDateEvidence ?? string.Empty,
            IsVariableFrameRate: md?.IsVariableFrameRate ?? false,
            CameraModel: md?.CameraModel);
    }
}
