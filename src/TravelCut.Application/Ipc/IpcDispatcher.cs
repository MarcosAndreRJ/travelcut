using System.Text.Json;
using Microsoft.Extensions.Logging;
using TravelCut.Application.Audio;
using TravelCut.Application.Editor;
using TravelCut.Application.Planning;
using TravelCut.Application.Ports;
using TravelCut.Application.UseCases;
using TravelCut.Contracts.Dtos;
using TravelCut.Contracts.Ipc;
using TravelCut.Domain.Enums;

namespace TravelCut.Application.Ipc;

public class IpcDispatcher
{
    /// <summary>Limite de ids em suggestions.batchDecide por chamada.</summary>
    public const int MaxBatchSuggestionIds = 500;

    private readonly PlanningUseCases _planning;
    private readonly PreviewIpcHandler? _preview;
    private readonly EditorIpcHandler? _editor;
    private readonly TravelCut.Application.Motion.MotionIpcHandler? _motion;
    private readonly OpenOrCreateProjectUseCase _openProjectUseCase;
    private readonly ScanMediaLibraryUseCase _scanMediaUseCase;
    private readonly ListMediaAssetsUseCase _listMediaUseCase;
    private readonly GetMediaDetailsUseCase _getMediaDetailsUseCase;
    private readonly GetMediaChronologyUseCase _getChronologyUseCase;
    private readonly IEditorAdapter _editorAdapter;
    private readonly IPipelineJobManager _pipelineJobs;
    private readonly IProjectRepository _projectRepo;
    private readonly IMediaRepository _mediaRepo;
    private readonly IEditPlanRepository _planRepo;
    private readonly ListDirectoryUseCase _listDirectoryUseCase;
    private readonly InspectCandidatesUseCase _inspectCandidatesUseCase;
    private readonly GetThumbnailUseCase _getThumbnailUseCase;
    private readonly IMediaImportService _importService;
    private readonly OpenFolderUseCase _openFolderUseCase;
    private readonly ILogger<IpcDispatcher> _logger;
    private readonly TravelCut.Application.PostAnalysis.PipelineResumeService? _resume;
    private readonly IEnvironmentAssistantService? _envAssistant;
    private readonly ITravelCutSettingsService? _settingsService;
    private readonly TravelCut.Application.Editorial.EditorialIpcHandler? _editorial;

    public IpcDispatcher(
        OpenOrCreateProjectUseCase openProjectUseCase,
        ScanMediaLibraryUseCase scanMediaUseCase,
        ListMediaAssetsUseCase listMediaUseCase,
        GetMediaDetailsUseCase getMediaDetailsUseCase,
        GetMediaChronologyUseCase getChronologyUseCase,
        IEditorAdapter editorAdapter,
        IPipelineJobManager pipelineJobs,
        IProjectRepository projectRepo,
        IMediaRepository mediaRepo,
        IEditPlanRepository planRepo,
        ListDirectoryUseCase listDirectoryUseCase,
        InspectCandidatesUseCase inspectCandidatesUseCase,
        GetThumbnailUseCase getThumbnailUseCase,
        IMediaImportService importService,
        OpenFolderUseCase openFolderUseCase,
        PlanningUseCases planning,
        ILogger<IpcDispatcher> logger,
        PreviewIpcHandler? preview = null,
        EditorIpcHandler? editor = null,
        TravelCut.Application.Motion.MotionIpcHandler? motion = null,
        TravelCut.Application.Editorial.EditorialIpcHandler? editorial = null,
        IEnvironmentAssistantService? envAssistant = null,
        ITravelCutSettingsService? settingsService = null,
        TravelCut.Application.PostAnalysis.PipelineResumeService? resume = null)
    {
        _resume = resume;
        _motion = motion;
        _planning = planning;
        _preview = preview;
        _editor = editor;
        _openFolderUseCase = openFolderUseCase;
        _openProjectUseCase = openProjectUseCase;
        _scanMediaUseCase = scanMediaUseCase;
        _listMediaUseCase = listMediaUseCase;
        _getMediaDetailsUseCase = getMediaDetailsUseCase;
        _getChronologyUseCase = getChronologyUseCase;
        _editorAdapter = editorAdapter;
        _pipelineJobs = pipelineJobs;
        _projectRepo = projectRepo;
        _mediaRepo = mediaRepo;
        _planRepo = planRepo;
        _listDirectoryUseCase = listDirectoryUseCase;
        _inspectCandidatesUseCase = inspectCandidatesUseCase;
        _getThumbnailUseCase = getThumbnailUseCase;
        _importService = importService;
        _logger = logger;
        _editorial = editorial;
        _envAssistant = envAssistant;
        _settingsService = settingsService;
    }

    public async Task<IpcResponse> DispatchAsync(IpcRequest request, CancellationToken ct = default)
    {
        _logger.LogInformation("Despachando requisição IPC: {Type} [{RequestId}]", request.Type, request.RequestId);

        try
        {
            switch (request.Type)
            {
                case "system.ping":
                    return IpcResponse.Success(request.RequestId, new { timestampUtc = DateTime.UtcNow, status = "ok" });

                case "project.create":
                {
                    var req = DeserializePayload<CreateProjectRequest>(request.Payload);
                    if (req == null || string.IsNullOrWhiteSpace(req.RootPath))
                    {
                        return IpcResponse.Fail(request.RequestId, "INVALID_ARGUMENT", "Caminho da raiz do projeto não fornecido.");
                    }

                    var outcome = await _openProjectUseCase.ExecuteAsync(
                        req.RootPath,
                        autoCreateFolders: req.CreateFoldersIfNotExists,
                        projectName: req.Name,
                        ct);

                    if (!outcome.Success || outcome.Summary == null)
                    {
                        return IpcResponse.Fail(request.RequestId, outcome.ErrorCode ?? "CREATE_FAILED", outcome.ErrorMessage ?? "Falha ao criar projeto.");
                    }

                    // status: "created" quando o registro do projeto foi criado agora; "opened_existing" caso contrário.
                    // createdFolders: somente as pastas canônicas efetivamente criadas nesta chamada.
                    string status = outcome.IsNewProject ? "created" : "opened_existing";
                    return IpcResponse.Success(request.RequestId,
                        new CreateProjectResponse(outcome.Summary, outcome.Summary, outcome.CreatedFolders.ToList(), status));
                }

                case "project.open":
                {
                    var req = DeserializePayload<OpenProjectRequest>(request.Payload);
                    if (req == null || string.IsNullOrWhiteSpace(req.RootPath))
                    {
                        return IpcResponse.Fail(request.RequestId, "INVALID_ARGUMENT", "Caminho da raiz do projeto não fornecido.");
                    }

                    var outcome = await _openProjectUseCase.ExecuteAsync(
                        req.RootPath,
                        autoCreateFolders: req.AutoCreateFolders,
                        projectName: null,
                        ct);

                    if (!outcome.Success || outcome.Summary == null)
                    {
                        return IpcResponse.Fail(request.RequestId, outcome.ErrorCode ?? "OPEN_FAILED", outcome.ErrorMessage ?? "Falha ao abrir projeto.");
                    }

                    // P07: retoma pendências (fila persistida, análise faltante, parciais órfãos) sem bloquear a abertura.
                    _resume?.ResumeInBackground(outcome.Summary.ProjectId);
                    return IpcResponse.Success(request.RequestId, new OpenProjectResponse(outcome.Summary, outcome.Summary, "ready"));
                }

                case "project.recent":
                {
                    var recents = await _projectRepo.ListRecentAsync(10, ct);
                    var summaries = new List<ProjectSummaryDto>();
                    foreach (var p in recents)
                    {
                        summaries.Add(await ProjectSummaryBuilder.BuildAsync(p, _mediaRepo, _planRepo, ct));
                    }
                    return IpcResponse.Success(request.RequestId, new RecentProjectsResponse(summaries));
                }

                case "dialog.selectFolder":
                {
                    return IpcResponse.Success(request.RequestId, new SelectFolderResponse(string.Empty, true));
                }

                case "editor.capabilities" when _editor is null:
                {
                    // Fallback sem EditorIpcHandler (composição mínima/testes). Com handler, editor.* é tratado no default.
                    var caps = await _editorAdapter.DetectCapabilitiesAsync(ct);
                    return IpcResponse.Success(request.RequestId, caps);
                }

                case "media.scan":
                {
                    var projectId = ExtractProjectId(request.Payload);
                    if (projectId == Guid.Empty)
                    {
                        return IpcResponse.Fail(request.RequestId, "INVALID_PROJECT_ID", "ID de projeto inválido.");
                    }

                    bool computeFullSha = ExtractBool(request.Payload, "computeFullSha");
                    var (success, result, code, message) = await _scanMediaUseCase.ExecuteAsync(projectId, computeFullSha, ct);
                    if (!success || result == null)
                    {
                        return IpcResponse.Fail(request.RequestId, code ?? "SCAN_FAILED", message ?? "Falha na varredura de mídia.");
                    }

                    return IpcResponse.Success(request.RequestId, result);
                }

                case "media.list":
                {
                    var projectId = ExtractProjectId(request.Payload);
                    var dtos = await _listMediaUseCase.ExecuteAsync(projectId, ct);
                    return IpcResponse.Success(request.RequestId, dtos);
                }

                case "media.getDetails":
                case "media.details":
                {
                    var mediaId = ExtractGuid(request.Payload, "mediaId");
                    if (mediaId == Guid.Empty)
                    {
                        return IpcResponse.Fail(request.RequestId, "INVALID_MEDIA_ID", "ID de mídia inválido.");
                    }

                    var (success, details, code, message) = await _getMediaDetailsUseCase.ExecuteAsync(mediaId, ct);
                    if (!success || details == null)
                    {
                        return IpcResponse.Fail(request.RequestId, code ?? "MEDIA_NOT_FOUND", message ?? "Mídia não encontrada.");
                    }

                    return IpcResponse.Success(request.RequestId, details);
                }

                case "media.getChronology":
                case "media.chronology":
                {
                    var projectId = ExtractProjectId(request.Payload);
                    if (projectId == Guid.Empty)
                    {
                        return IpcResponse.Fail(request.RequestId, "INVALID_PROJECT_ID", "ID de projeto inválido.");
                    }

                    TimeSpan? gap = null;
                    var je = ToJsonElement(request.Payload);
                    if (je.ValueKind == JsonValueKind.Object)
                    {
                        foreach (var prop in je.EnumerateObject())
                        {
                            if (string.Equals(prop.Name, "sessionGapMinutes", StringComparison.OrdinalIgnoreCase) && prop.Value.TryGetDouble(out var gapMin))
                            {
                                gap = TimeSpan.FromMinutes(gapMin);
                                break;
                            }
                            if (string.Equals(prop.Name, "thresholdSeconds", StringComparison.OrdinalIgnoreCase) && prop.Value.TryGetDouble(out var thSec))
                            {
                                gap = TimeSpan.FromSeconds(thSec);
                                break;
                            }
                        }
                    }

                    var report = await _getChronologyUseCase.ExecuteAsync(projectId, gap, ct);
                    return IpcResponse.Success(request.RequestId, report);
                }

                case "suggestions.list":
                {
                    // Lido do repositório pelo projectId real; sem dados de exemplo.
                    var projectId = ExtractProjectId(request.Payload);
                    if (projectId == Guid.Empty)
                    {
                        return IpcResponse.Fail(request.RequestId, "INVALID_PROJECT_ID", "ID de projeto inválido.");
                    }

                    // Estado EFETIVO (última decisão por Version): status, intervalo ajustado, canUndo/canRedo.
                    var suggestions = await _planRepo.ListSuggestionsByProjectAsync(projectId, ct);
                    var decisions = await _planRepo.ListDecisionsByProjectAsync(projectId, ct);
                    var effective = EffectiveSuggestionStatus.ResolveDetailed(suggestions, decisions);
                    var appliedEffects = await _planning.LoadAppliedEffectsAsync(projectId, ct);
                    var batch = await _planning.LoadBatchApprovabilityAsync(
                        projectId, suggestions, effective, decisions.Select(d => d.SuggestionId).ToHashSet(), ct);
                    var dtos = suggestions
                        .Select(s => SuggestionEffectiveMapper.ToDto(s, effective[s.SuggestionId],
                            appliedEffects.TryGetValue(s.SuggestionId, out var fx) ? fx : null,
                            batch.TryGetValue(s.SuggestionId, out var ba) ? ba : null))
                        .ToList();

                    return IpcResponse.Success(request.RequestId, new { suggestions = dtos });
                }

                case "suggestion.createManual":
                {
                    var req = DeserializePayload<CreateManualSuggestionRequest>(request.Payload);
                    if (req == null)
                    {
                        return IpcResponse.Fail(request.RequestId, "INVALID_PAYLOAD", "Dados de pré-corte manual inválidos.");
                    }

                    var outcome = await _planning.CreateManualSuggestionAsync(req, ct);
                    if (!outcome.Success)
                    {
                        return IpcResponse.Fail(request.RequestId, outcome.ErrorCode, outcome.ErrorMessage);
                    }

                    return IpcResponse.Success(request.RequestId, new { suggestion = outcome.Value });
                }

                case "decision.submit":
                {
                    var req = DeserializePayload<SubmitDecisionRequest>(request.Payload);
                    if (req == null || req.SuggestionId == Guid.Empty)
                    {
                        return IpcResponse.Fail(request.RequestId, "INVALID_PAYLOAD", "Dados de decisão inválidos.");
                    }

                    // Validação, gravação, regeneração do plano e resposta (DecisionResultDto) ficam no use case.
                    var outcome = await _planning.SubmitDecisionAsync(req, ct);
                    return PlanResponse(request.RequestId, outcome);
                }

                case "decision.undo":
                {
                    var req = DeserializePayload<UndoDecisionRequest>(request.Payload);
                    if (req == null || req.SuggestionId == Guid.Empty)
                    {
                        return IpcResponse.Fail(request.RequestId, "INVALID_UNDO", "ID de sugestão inválido para desfazer decisão.");
                    }

                    // Append-only: o desfazer é uma nova decisão "undo". Nada é apagado.
                    var outcome = await _planning.UndoDecisionAsync(req, ct);
                    return PlanResponse(request.RequestId, outcome);
                }

                case "decision.redo":
                {
                    var req = DeserializePayload<RedoDecisionRequest>(request.Payload);
                    if (req == null || req.SuggestionId == Guid.Empty)
                    {
                        return IpcResponse.Fail(request.RequestId, "INVALID_REDO", "ID de sugestão inválido para refazer decisão.");
                    }

                    var outcome = await _planning.RedoDecisionAsync(req, ct);
                    return PlanResponse(request.RequestId, outcome);
                }

                case "decision.list":
                {
                    var projectId = ExtractProjectId(request.Payload);
                    if (projectId == Guid.Empty)
                    {
                        return IpcResponse.Fail(request.RequestId, "INVALID_PROJECT_ID", "ID de projeto inválido.");
                    }
                    var decisions = await _planRepo.ListDecisionsByProjectAsync(projectId, ct);
                    var dtos = decisions.Select(d => new EditDecisionDto(
                        d.DecisionId,
                        d.SuggestionId,
                        d.Action,
                        d.Reviewer,
                        d.Notes,
                        d.TimestampUtc,
                        d.Version,
                        d.BaseRevision,
                        d.SourceStartMs is long ss ? SuggestionStorageMapper.ToSeconds(ss) : null,
                        d.SourceEndMs is long se ? SuggestionStorageMapper.ToSeconds(se) : null,
                        DecisionParameters.Parse(d.ParametersJson)
                    )).ToList();
                    return IpcResponse.Success(request.RequestId, new { decisions = dtos });
                }

                case "suggestions.batchDecide":
                {
                    var req = DeserializePayload<BatchDecideRequest>(request.Payload);
                    if (req == null || req.SuggestionIds == null || req.SuggestionIds.Count == 0)
                    {
                        return IpcResponse.Fail(request.RequestId, "INVALID_BATCH", "Nenhuma sugestão informada para decisão em lote.");
                    }

                    // Tudo ou nada em uma transação; modify proibido em lote (validado no use case).
                    var outcome = await _planning.BatchDecideAsync(req, ct);
                    return PlanResponse(request.RequestId, outcome);
                }

                case "system.openFolder":
                {
                    // Única implementação: validação e regras em OpenFolderUseCase (testável); sem shell.
                    string? targetPath = ExtractString(request.Payload, "path") ?? ExtractString(request.Payload, "folderPath");
                    var (ok, outcome, code, message) = await _openFolderUseCase.ExecuteAsync(targetPath, ct);
                    if (!ok || outcome == null)
                    {
                        return IpcResponse.Fail(request.RequestId, code ?? "OPEN_FAILED", message ?? "Não foi possível abrir a pasta.");
                    }

                    return IpcResponse.Success(request.RequestId, new { opened = true, path = outcome.FullPath, selectedFile = outcome.SelectedFile });
                }

                case "analysis.environment":
                {
                    var environment = await _pipelineJobs.GetEnvironmentAsync(ct, ExtractBool(request.Payload, "refresh"));
                    return IpcResponse.Success(request.RequestId, environment);
                }

                case "pipeline.start":
                {
                    var req = DeserializePayload<StartPipelineRequest>(request.Payload);
                    if (req == null || req.ProjectId == Guid.Empty)
                    {
                        return IpcResponse.Fail(request.RequestId, "INVALID_PROJECT_ID", "ID de projeto inválido.");
                    }

                    try
                    {
                        var jobId = await _pipelineJobs.StartAsync(req, ct);
                        _logger.LogInformation("Análise iniciada via IPC: job {JobId}", jobId);
                        return IpcResponse.Success(request.RequestId, new StartPipelineResponse(jobId));
                    }
                    catch (InvalidOperationException ex) when (IsKnownPipelineCode(ex.Message))
                    {
                        return IpcResponse.Fail(request.RequestId, ex.Message, PipelineErrorMessage(ex.Message));
                    }
                }

                case "pipeline.retryFailed":
                {
                    // P12: reenfileira só as mídias com etapa falha/cancelada do último job (etapas concluídas vêm do cache).
                    var req = DeserializePayload<ProjectRequest>(request.Payload);
                    if (req == null || req.ProjectId == Guid.Empty)
                    {
                        return IpcResponse.Fail(request.RequestId, "INVALID_PROJECT_ID", "ID de projeto inválido.");
                    }

                    try
                    {
                        var result = await _pipelineJobs.RetryFailedAsync(req.ProjectId, ct);
                        return IpcResponse.Success(request.RequestId,
                            new RetryFailedPipelineResponse(result.Outcome, result.JobId, result.PendingMediaCount));
                    }
                    catch (InvalidOperationException ex) when (IsKnownPipelineCode(ex.Message))
                    {
                        return IpcResponse.Fail(request.RequestId, ex.Message, PipelineErrorMessage(ex.Message));
                    }
                }

                case "pipeline.diagnostics":
                {
                    var req = DeserializePayload<ProjectRequest>(request.Payload);
                    if (req == null || req.ProjectId == Guid.Empty)
                    {
                        return IpcResponse.Fail(request.RequestId, "INVALID_PROJECT_ID", "ID de projeto inválido.");
                    }
                    return IpcResponse.Success(request.RequestId, _pipelineJobs.GetDiagnostics(req.ProjectId));
                }

                case "pipeline.status":
                {
                    var req = DeserializePayload<ProjectRequest>(request.Payload);
                    if (req == null || req.ProjectId == Guid.Empty)
                    {
                        return IpcResponse.Fail(request.RequestId, "INVALID_PROJECT_ID", "ID de projeto inválido.");
                    }

                    var status = await _pipelineJobs.GetStatusAsync(req.ProjectId, ct);
                    return IpcResponse.Success(request.RequestId, status);
                }

                case "pipeline.cancel":
                {
                    var req = DeserializePayload<PipelineJobRequest>(request.Payload);
                    if (req == null || req.JobId == Guid.Empty)
                    {
                        return IpcResponse.Fail(request.RequestId, "INVALID_JOB_ID", "ID de análise inválido.");
                    }

                    var canceled = _pipelineJobs.Cancel(req.JobId);
                    return IpcResponse.Success(request.RequestId, new CancelPipelineResponse(canceled));
                }

                case "analysis.getMedia":
                {
                    var req = DeserializePayload<MediaAnalysisRequest>(request.Payload);
                    if (req == null || req.ProjectId == Guid.Empty)
                    {
                        return IpcResponse.Fail(request.RequestId, "INVALID_PROJECT_ID", "ID de projeto inválido.");
                    }
                    if (req.MediaId == Guid.Empty)
                    {
                        return IpcResponse.Fail(request.RequestId, "INVALID_MEDIA_ID", "ID de mídia inválido.");
                    }

                    var analysis = await _pipelineJobs.GetMediaAnalysisAsync(req.ProjectId, req.MediaId, ct);
                    // Nunca analisada (ou resultado de outro fingerprint): "not_run", sem dados.
                    analysis ??= new MediaAnalysisDto(
                        MediaId: req.MediaId,
                        TranscriptionStatus: "not_run",
                        TranscriptionReason: null,
                        Transcript: null,
                        SilenceStatus: "not_run",
                        SilenceReason: null,
                        Silences: new List<VadSilenceSegmentDto>(),
                        Hesitations: new List<HesitationCandidateDto>(),
                        Scene: null,
                        CutCandidates: new List<EditSuggestionDto>(),
                        AnalyzedAtUtc: null);
                    return IpcResponse.Success(request.RequestId, analysis);
                }

                case "fs.listDirectory":
                {
                    var req = DeserializePayload<ListDirectoryRequest>(request.Payload);
                    var (ok, listing, code, message) = await _listDirectoryUseCase.ExecuteAsync(req?.Path, ct);
                    if (!ok || listing == null)
                    {
                        return IpcResponse.Fail(request.RequestId, code ?? "LIST_FAILED", message ?? "Falha ao listar pasta.");
                    }

                    return IpcResponse.Success(request.RequestId, listing);
                }

                case "media.inspectCandidates":
                {
                    var req = DeserializePayload<InspectCandidatesRequest>(request.Payload);
                    if (req == null || req.ProjectId == Guid.Empty)
                    {
                        return IpcResponse.Fail(request.RequestId, "INVALID_PROJECT_ID", "ID de projeto inválido.");
                    }

                    var (ok, inspection, code, message) = await _inspectCandidatesUseCase.ExecuteAsync(req, ct);
                    if (!ok || inspection == null)
                    {
                        return IpcResponse.Fail(request.RequestId, code ?? "INSPECT_FAILED", message ?? "Falha ao inspecionar candidatos.");
                    }

                    return IpcResponse.Success(request.RequestId, inspection);
                }

                case "media.getThumbnail":
                {
                    var req = DeserializePayload<GetThumbnailRequest>(request.Payload);
                    if (req == null || req.ProjectId == Guid.Empty)
                    {
                        return IpcResponse.Fail(request.RequestId, "INVALID_PROJECT_ID", "ID de projeto inválido.");
                    }

                    var (ok, thumbnail, code, message) = await _getThumbnailUseCase.ExecuteAsync(req, ct);
                    if (!ok || thumbnail == null)
                    {
                        return IpcResponse.Fail(request.RequestId, code ?? "THUMBNAIL_FAILED", message ?? "Falha ao gerar miniatura.");
                    }

                    return IpcResponse.Success(request.RequestId, thumbnail);
                }

                case "media.import.start":
                {
                    var req = DeserializePayload<StartImportRequest>(request.Payload);
                    if (req == null || req.ProjectId == Guid.Empty)
                    {
                        return IpcResponse.Fail(request.RequestId, "INVALID_PROJECT_ID", "ID de projeto inválido.");
                    }

                    var result = await _importService.StartAsync(req, ct);
                    if (!result.Success || result.JobId == null)
                    {
                        return IpcResponse.Fail(request.RequestId, result.ErrorCode ?? "IMPORT_START_FAILED", result.ErrorMessage ?? "Falha ao iniciar importação.");
                    }

                    return IpcResponse.Success(request.RequestId, new StartImportResponse(result.JobId.Value));
                }

                case "media.import.status":
                {
                    var req = DeserializePayload<ImportJobRequest>(request.Payload);
                    if (req == null || req.JobId == Guid.Empty)
                    {
                        return IpcResponse.Fail(request.RequestId, "INVALID_JOB_ID", "ID de importação inválido.");
                    }

                    var status = _importService.GetStatus(req.JobId);
                    if (status == null)
                    {
                        return IpcResponse.Fail(request.RequestId, "IMPORT_NOT_FOUND", "Importação não encontrada.");
                    }

                    return IpcResponse.Success(request.RequestId, status);
                }

                case "media.import.cancel":
                {
                    var req = DeserializePayload<ImportJobRequest>(request.Payload);
                    if (req == null || req.JobId == Guid.Empty)
                    {
                        return IpcResponse.Fail(request.RequestId, "INVALID_JOB_ID", "ID de importação inválido.");
                    }

                    return IpcResponse.Success(request.RequestId, _importService.Cancel(req.JobId));
                }

                case "profile.get":
                    return IpcResponse.Success(request.RequestId, _planning.GetProfile());

                case "automation.profile.get":
                {
                    var req = DeserializePayload<GetAutomationProfileRequest>(request.Payload);
                    if (req == null || req.ProjectId == Guid.Empty)
                    {
                        return IpcResponse.Fail(request.RequestId, "INVALID_ARGUMENT", "ID de projeto inválido.");
                    }

                    return PlanResponse(request.RequestId, await _planning.GetAutomationProfileAsync(req, ct));
                }

                case "automation.profile.set":
                {
                    var req = DeserializePayload<SetAutomationProfileRequest>(request.Payload);
                    if (req == null || req.ProjectId == Guid.Empty)
                    {
                        return IpcResponse.Fail(request.RequestId, "INVALID_ARGUMENT", "ID de projeto inválido.");
                    }

                    return PlanResponse(request.RequestId, await _planning.SetAutomationProfileAsync(req, ct));
                }

                case "automation.options.get":
                {
                    var req = DeserializePayload<GetAutomationOptionsRequest>(request.Payload);
                    if (req == null || req.ProjectId == Guid.Empty)
                    {
                        return IpcResponse.Fail(request.RequestId, "INVALID_ARGUMENT", "ID de projeto inválido.");
                    }

                    return PlanResponse(request.RequestId, await _planning.GetAutomationOptionsAsync(req, ct));
                }

                case "automation.options.set":
                {
                    var req = DeserializePayload<SetAutomationOptionsRequest>(request.Payload);
                    if (req == null || req.ProjectId == Guid.Empty)
                    {
                        return IpcResponse.Fail(request.RequestId, "INVALID_ARGUMENT", "ID de projeto inválido.");
                    }

                    return PlanResponse(request.RequestId, await _planning.SetAutomationOptionsAsync(req, ct));
                }

                case "plan.generate":
                {
                    var req = DeserializePayload<GeneratePlanRequest>(request.Payload);
                    if (req == null || req.ProjectId == Guid.Empty)
                    {
                        return IpcResponse.Fail(request.RequestId, "INVALID_ARGUMENT", "ID de projeto inválido.");
                    }

                    var outcome = await _planning.GenerateAsync(req, ct);
                    return PlanResponse(request.RequestId, outcome);
                }

                case "plan.get":
                {
                    var req = DeserializePayload<GetPlanRequest>(request.Payload);
                    if (req == null || req.ProjectId == Guid.Empty)
                    {
                        return IpcResponse.Fail(request.RequestId, "INVALID_ARGUMENT", "ID de projeto inválido.");
                    }

                    var outcome = await _planning.GetAsync(req, ct);
                    return PlanResponse(request.RequestId, outcome);
                }

                case "plan.list":
                {
                    var projectId = ExtractProjectId(request.Payload);
                    if (projectId == Guid.Empty)
                    {
                        return IpcResponse.Fail(request.RequestId, "INVALID_ARGUMENT", "ID de projeto inválido.");
                    }

                    var outcome = await _planning.ListAsync(projectId, ct);
                    return PlanResponse(request.RequestId, outcome);
                }

                case "plan.validate":
                {
                    var req = DeserializePayload<ValidatePlanRequest>(request.Payload);
                    if (req == null || req.ProjectId == Guid.Empty)
                    {
                        return IpcResponse.Fail(request.RequestId, "INVALID_ARGUMENT", "ID de projeto inválido.");
                    }

                    var outcome = await _planning.ValidateAsync(req, ct);
                    return PlanResponse(request.RequestId, outcome);
                }

                case "plan.revert":
                {
                    var req = DeserializePayload<RevertPlanRequest>(request.Payload);
                    if (req == null || req.ProjectId == Guid.Empty)
                    {
                        return IpcResponse.Fail(request.RequestId, "INVALID_ARGUMENT", "ID de projeto inválido.");
                    }

                    var outcome = await _planning.RevertAsync(req, ct);
                    return PlanResponse(request.RequestId, outcome);
                }
                case "plan.saveManualRevision":
                {
                    var req = DeserializePayload<SavePlanRevisionRequest>(request.Payload);
                    if (req == null || req.ProjectId == Guid.Empty || req.Plan == null)
                    {
                        return IpcResponse.Fail(request.RequestId, "INVALID_ARGUMENT", "Payload inválido para gravação de plano manual.");
                    }
                    var outcome = await _planning.SaveManualRevisionAsync(req, ct);
                    return PlanResponse(request.RequestId, outcome);
                }
                case "plan.reconcile":
                {
                    var req = DeserializePayload<ReconcilePlanRequest>(request.Payload);
                    if (req == null || req.ProjectId == Guid.Empty)
                    {
                        return IpcResponse.Fail(request.RequestId, "INVALID_ARGUMENT", "Payload inválido para reconciliação de plano.");
                    }
                    var outcome = await _planning.ReconcileAsync(req, ct);
                    return PlanResponse(request.RequestId, outcome);
                }

                // --- Assistente de Ambiente & Instalações Assistidas (F12) ---
                case "environment.diagnose":
                {
                    if (_envAssistant is null)
                        return IpcResponse.Fail(request.RequestId, "ENV_ASSISTANT_UNAVAILABLE", "Assistente de ambiente não disponível.");
                    var diag = await _envAssistant.DiagnoseAsync(ct);
                    return IpcResponse.Success(request.RequestId, diag);
                }

                case "environment.diagnoseTool":
                {
                    if (_envAssistant is null)
                        return IpcResponse.Fail(request.RequestId, "ENV_ASSISTANT_UNAVAILABLE", "Assistente de ambiente não disponível.");
                    var toolKey = ExtractString(request.Payload, "toolKey");
                    if (string.IsNullOrWhiteSpace(toolKey))
                        return IpcResponse.Fail(request.RequestId, "INVALID_TOOL_KEY", "Chave da ferramenta não informada.");
                    var diag = await _envAssistant.DiagnoseToolAsync(toolKey, ct);
                    return IpcResponse.Success(request.RequestId, diag);
                }

                case "environment.getInstallPlan":
                {
                    if (_envAssistant is null)
                        return IpcResponse.Fail(request.RequestId, "ENV_ASSISTANT_UNAVAILABLE", "Assistente de ambiente não disponível.");
                    var toolKey = ExtractString(request.Payload, "toolKey");
                    if (string.IsNullOrWhiteSpace(toolKey))
                        return IpcResponse.Fail(request.RequestId, "INVALID_TOOL_KEY", "Chave da ferramenta não informada.");
                    var plan = await _envAssistant.GetInstallPlanAsync(toolKey, ct);
                    return IpcResponse.Success(request.RequestId, plan);
                }

                case "environment.startInstall":
                {
                    if (_envAssistant is null)
                        return IpcResponse.Fail(request.RequestId, "ENV_ASSISTANT_UNAVAILABLE", "Assistente de ambiente não disponível.");
                    var req = DeserializePayload<StartToolInstallRequest>(request.Payload);
                    if (req is null || string.IsNullOrWhiteSpace(req.ToolKey))
                        return IpcResponse.Fail(request.RequestId, "INVALID_PAYLOAD", "Requisição de instalação inválida.");
                    try
                    {
                        var jobId = await _envAssistant.StartInstallAsync(req, ct);
                        return IpcResponse.Success(request.RequestId, new { jobId });
                    }
                    catch (OperationCanceledException ex)
                    {
                        return IpcResponse.Fail(request.RequestId, "USER_CANCELED", ex.Message);
                    }
                    catch (InvalidOperationException ex)
                    {
                        return IpcResponse.Fail(request.RequestId, "AUTHORIZATION_REQUIRED", ex.Message);
                    }
                    catch (ArgumentException ex)
                    {
                        return IpcResponse.Fail(request.RequestId, "INVALID_ARGUMENT", ex.Message);
                    }
                }

                case "environment.getInstallProgress":
                {
                    if (_envAssistant is null)
                        return IpcResponse.Fail(request.RequestId, "ENV_ASSISTANT_UNAVAILABLE", "Assistente de ambiente não disponível.");
                    var jobId = ExtractGuid(request.Payload, "jobId");
                    if (jobId == Guid.Empty)
                        return IpcResponse.Fail(request.RequestId, "INVALID_JOB_ID", "Identificador do job inválido.");
                    var progress = _envAssistant.GetInstallProgress(jobId);
                    return IpcResponse.Success(request.RequestId, progress);
                }

                case "environment.cancelInstall":
                {
                    if (_envAssistant is null)
                        return IpcResponse.Fail(request.RequestId, "ENV_ASSISTANT_UNAVAILABLE", "Assistente de ambiente não disponível.");
                    var jobId = ExtractGuid(request.Payload, "jobId");
                    if (jobId == Guid.Empty)
                        return IpcResponse.Fail(request.RequestId, "INVALID_JOB_ID", "Identificador do job inválido.");
                    var canceled = _envAssistant.CancelInstall(jobId);
                    return IpcResponse.Success(request.RequestId, new { canceled });
                }

                case "environment.startDownloadModel":
                {
                    if (_envAssistant is null)
                        return IpcResponse.Fail(request.RequestId, "ENV_ASSISTANT_UNAVAILABLE", "Assistente de ambiente não disponível.");
                    var req = DeserializePayload<StartModelDownloadRequest>(request.Payload);
                    if (req is null || string.IsNullOrWhiteSpace(req.ModelName))
                        return IpcResponse.Fail(request.RequestId, "INVALID_PAYLOAD", "Requisição de download inválida.");
                    try
                    {
                        var jobId = await _envAssistant.StartDownloadModelAsync(req, ct);
                        return IpcResponse.Success(request.RequestId, new { jobId });
                    }
                    catch (OperationCanceledException ex)
                    {
                        return IpcResponse.Fail(request.RequestId, "USER_CANCELED", ex.Message);
                    }
                    catch (InvalidOperationException ex)
                    {
                        return IpcResponse.Fail(request.RequestId, "AUTHORIZATION_REQUIRED", ex.Message);
                    }
                    catch (ArgumentException ex)
                    {
                        return IpcResponse.Fail(request.RequestId, "INVALID_ARGUMENT", ex.Message);
                    }
                }

                case "environment.getModelDownloadProgress":
                {
                    if (_envAssistant is null)
                        return IpcResponse.Fail(request.RequestId, "ENV_ASSISTANT_UNAVAILABLE", "Assistente de ambiente não disponível.");
                    var jobId = ExtractGuid(request.Payload, "jobId");
                    if (jobId == Guid.Empty)
                        return IpcResponse.Fail(request.RequestId, "INVALID_JOB_ID", "Identificador do job inválido.");
                    var progress = _envAssistant.GetModelDownloadProgress(jobId);
                    return IpcResponse.Success(request.RequestId, progress);
                }

                case "environment.cancelModelDownload":
                {
                    if (_envAssistant is null)
                        return IpcResponse.Fail(request.RequestId, "ENV_ASSISTANT_UNAVAILABLE", "Assistente de ambiente não disponível.");
                    var jobId = ExtractGuid(request.Payload, "jobId");
                    if (jobId == Guid.Empty)
                        return IpcResponse.Fail(request.RequestId, "INVALID_JOB_ID", "Identificador do job inválido.");
                    var canceled = _envAssistant.CancelModelDownload(jobId);
                    return IpcResponse.Success(request.RequestId, new { canceled });
                }

                case "environment.testTool":
                {
                    if (_envAssistant is null)
                        return IpcResponse.Fail(request.RequestId, "ENV_ASSISTANT_UNAVAILABLE", "Assistente de ambiente não disponível.");
                    var toolKey = ExtractString(request.Payload, "toolKey");
                    if (string.IsNullOrWhiteSpace(toolKey))
                        return IpcResponse.Fail(request.RequestId, "INVALID_TOOL_KEY", "Chave da ferramenta não informada.");
                    var testRes = await _envAssistant.TestToolAsync(toolKey, ct);
                    return IpcResponse.Success(request.RequestId, testRes);
                }

                case "environment.configureToolPath":
                {
                    if (_envAssistant is null)
                        return IpcResponse.Fail(request.RequestId, "ENV_ASSISTANT_UNAVAILABLE", "Assistente de ambiente não disponível.");
                    var req = DeserializePayload<ConfigureToolPathRequest>(request.Payload);
                    if (req is null || string.IsNullOrWhiteSpace(req.ToolKey))
                        return IpcResponse.Fail(request.RequestId, "INVALID_PAYLOAD", "Requisição de configuração de caminho inválida.");
                    var ok = await _envAssistant.ConfigureToolPathAsync(req.ToolKey, req.Path, ct);
                    return IpcResponse.Success(request.RequestId, new { success = ok });
                }

                case "environment.autoLocateTool":
                {
                    if (_envAssistant is null)
                        return IpcResponse.Fail(request.RequestId, "ENV_ASSISTANT_UNAVAILABLE", "Assistente de ambiente não disponível.");
                    var toolKey = ExtractString(request.Payload, "toolKey");
                    if (string.IsNullOrWhiteSpace(toolKey))
                        return IpcResponse.Fail(request.RequestId, "INVALID_TOOL_KEY", "Chave da ferramenta não informada.");
                    var locateRes = await _envAssistant.AutoLocateToolAsync(toolKey, ct);
                    return IpcResponse.Success(request.RequestId, locateRes);
                }

                // --- Configurações Persistentes (F12) ---
                case "settings.get":
                {
                    if (_settingsService is null)
                        return IpcResponse.Fail(request.RequestId, "SETTINGS_SERVICE_UNAVAILABLE", "Serviço de configurações não disponível.");
                    var settings = await _settingsService.GetSettingsAsync(ct);
                    return IpcResponse.Success(request.RequestId, settings);
                }

                case "settings.setToolPath":
                {
                    if (_settingsService is null)
                        return IpcResponse.Fail(request.RequestId, "SETTINGS_SERVICE_UNAVAILABLE", "Serviço de configurações não disponível.");
                    var toolKey = ExtractString(request.Payload, "toolKey");
                    var path = ExtractString(request.Payload, "path");
                    if (string.IsNullOrWhiteSpace(toolKey) || string.IsNullOrWhiteSpace(path))
                        return IpcResponse.Fail(request.RequestId, "INVALID_PAYLOAD", "toolKey e path são obrigatórios.");
                    await _settingsService.SetToolPathAsync(toolKey, path, ct);
                    return IpcResponse.Success(request.RequestId, new { success = true });
                }

                case "settings.setMediaSourceFolders":
                {
                    if (_settingsService is null)
                        return IpcResponse.Fail(request.RequestId, "SETTINGS_SERVICE_UNAVAILABLE", "Serviço de configurações não disponível.");
                    var folders = DeserializePayload<List<string>>(request.Payload)
                        ?? (request.Payload is JsonElement je && je.TryGetProperty("folders", out var fj)
                            ? JsonSerializer.Deserialize<List<string>>(fj.GetRawText())
                            : new List<string>());
                    await _settingsService.SetMediaSourceFoldersAsync(folders ?? new List<string>(), ct);
                    return IpcResponse.Success(request.RequestId, new { success = true });
                }

                case "settings.addMediaSourceFolder":
                {
                    if (_settingsService is null)
                        return IpcResponse.Fail(request.RequestId, "SETTINGS_SERVICE_UNAVAILABLE", "Serviço de configurações não disponível.");
                    var folder = ExtractString(request.Payload, "folder");
                    if (string.IsNullOrWhiteSpace(folder))
                        return IpcResponse.Fail(request.RequestId, "INVALID_PAYLOAD", "Pasta não informada.");
                    await _settingsService.AddMediaSourceFolderAsync(folder, ct);
                    return IpcResponse.Success(request.RequestId, new { success = true });
                }

                case "settings.removeMediaSourceFolder":
                {
                    if (_settingsService is null)
                        return IpcResponse.Fail(request.RequestId, "SETTINGS_SERVICE_UNAVAILABLE", "Serviço de configurações não disponível.");
                    var folder = ExtractString(request.Payload, "folder");
                    if (string.IsNullOrWhiteSpace(folder))
                        return IpcResponse.Fail(request.RequestId, "INVALID_PAYLOAD", "Pasta não informada.");
                    await _settingsService.RemoveMediaSourceFolderAsync(folder, ct);
                    return IpcResponse.Success(request.RequestId, new { success = true });
                }

                default:
                    // Editor (editor.capabilities/prepareApply/apply/applications) vive em handler próprio.
                    if (_editor is not null)
                    {
                        var editorResponse = await _editor.TryHandleAsync(request, ct);
                        if (editorResponse is not null) return editorResponse;
                    }
                    // Animações (motion.capabilities) vivem em handler próprio.
                    if (_motion is not null)
                    {
                        var motionResponse = await _motion.TryHandleAsync(request, ct);
                        if (motionResponse is not null) return motionResponse;
                    }
                    // Prévia (media.getPlaybackSource, media.getWaveform, media.proxy.*) vive em handler próprio.
                    if (_preview is not null)
                    {
                        var previewResponse = await _preview.TryHandleAsync(request, ct);
                        if (previewResponse is not null) return previewResponse;
                    }
                    // Inteligência editorial F14.4 (cenas, transições, zooms) vive em handler próprio.
                    if (_editorial is not null)
                    {
                        var editorialResponse = await _editorial.TryHandleAsync(request, ct);
                        if (editorialResponse is not null) return editorialResponse;
                    }
                    _logger.LogWarning("Tipo de requisição IPC desconhecido: {Type}", request.Type);
                    return IpcResponse.Fail(request.RequestId, "UNKNOWN_METHOD", $"Método IPC '{request.Type}' não é reconhecido.");
            }
        }
        catch (Exception ex)
        {
            // Detalhes (mensagem e stack trace) ficam só no log; o front recebe apenas a referência.
            _logger.LogError(ex, "Erro interno ao processar requisição IPC {Type} [{RequestId}]", request.Type, request.RequestId);
            return IpcResponse.Fail(request.RequestId, "INTERNAL_ERROR", $"Erro interno (ref {request.RequestId})");
        }
    }

    /// <summary>Converte o resultado de um use case de plano em resposta IPC (código e mensagem genéricos).</summary>
    private static IpcResponse PlanResponse<T>(string requestId, PlanOutcome<T> outcome) =>
        outcome.Success
            ? IpcResponse.Success(requestId, outcome.Value)
            : IpcResponse.Fail(requestId, outcome.ErrorCode!, outcome.ErrorMessage ?? "Falha no plano editorial.",
                outcome.ErrorCode == PlanningUseCases.PlanManualProtectedCode ? "suggestedActions:" + PlanningUseCases.ManualProtectedSuggestedActions : null);

    // Códigos de erro do pipeline lançados pelo IPipelineJobManager (InvalidOperationException.Message).
    private static bool IsKnownPipelineCode(string message) => message is
        "PIPELINE_IN_PROGRESS" or "PROJECT_NOT_FOUND" or "NO_MEDIA_TO_ANALYZE" or "MEDIA_NOT_FOUND" or "INVALID_STAGE" or "INVALID_LANGUAGE"
        or ProjectLockException.ProjectRootMissingCode or ProjectLockException.LocksFolderUnavailableCode
        or ProjectLockException.LockAccessDeniedCode or ProjectLockException.LockIoErrorCode;

    private static string PipelineErrorMessage(string code) => code switch
    {
        "PIPELINE_IN_PROGRESS" => "Já existe uma análise em andamento neste projeto.",
        "PROJECT_NOT_FOUND" => "Projeto não encontrado.",
        "NO_MEDIA_TO_ANALYZE" => "Nenhuma mídia de vídeo ou áudio para analisar.",
        "MEDIA_NOT_FOUND" => "Mídia não encontrada no projeto ou não é vídeo/áudio.",
        "INVALID_STAGE" => "Etapa de análise inválida.",
        "INVALID_LANGUAGE" => "Idioma de transcrição inválido.",
        ProjectLockException.ProjectRootMissingCode => "A pasta do projeto não foi encontrada.",
        ProjectLockException.LockAccessDeniedCode => "Sem permissão para usar a trava de análise do projeto.",
        ProjectLockException.LocksFolderUnavailableCode or ProjectLockException.LockIoErrorCode =>
            "Não foi possível usar a trava de análise do projeto (pasta de travas indisponível).",
        _ => "Falha ao iniciar análise."
    };

    private static T? DeserializePayload<T>(object? payload)
    {
        if (payload == null) return default;
        if (payload is T direct) return direct;
        if (payload is JsonElement json)
        {
            return JsonSerializer.Deserialize<T>(json.GetRawText(), new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
        }
        string raw = JsonSerializer.Serialize(payload);
        return JsonSerializer.Deserialize<T>(raw, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });
    }

    private static JsonElement ToJsonElement(object? payload)
    {
        if (payload == null) return default;
        if (payload is JsonElement je) return je;
        return JsonSerializer.SerializeToElement(payload);
    }

    private static string? ExtractString(object? payload, string propertyName)
    {
        if (payload == null) return null;
        var json = ToJsonElement(payload);
        if (json.ValueKind != JsonValueKind.Object) return null;

        foreach (var prop in json.EnumerateObject())
        {
            if (string.Equals(prop.Name, propertyName, StringComparison.OrdinalIgnoreCase) && prop.Value.ValueKind == JsonValueKind.String)
            {
                return prop.Value.GetString();
            }
        }
        return null;
    }

    private static Guid ExtractProjectId(object? payload)
    {
        return ExtractGuid(payload, "projectId");
    }

    private static Guid ExtractGuid(object? payload, string propertyName)
    {
        if (payload == null) return Guid.Empty;
        var json = ToJsonElement(payload);
        if (json.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in json.EnumerateObject())
            {
                if (string.Equals(prop.Name, propertyName, StringComparison.OrdinalIgnoreCase))
                {
                    if (prop.Value.TryGetGuid(out var g)) return g;
                    if (Guid.TryParse(prop.Value.GetString(), out var parsed)) return parsed;
                }
            }
        }
        return Guid.Empty;
    }

    private static bool ExtractBool(object? payload, string propertyName)
    {
        if (payload == null) return false;
        var json = ToJsonElement(payload);
        if (json.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in json.EnumerateObject())
            {
                if (string.Equals(prop.Name, propertyName, StringComparison.OrdinalIgnoreCase))
                {
                    if (prop.Value.ValueKind == JsonValueKind.True) return true;
                    if (prop.Value.ValueKind == JsonValueKind.False) return false;
                    if (bool.TryParse(prop.Value.GetString(), out var parsed)) return parsed;
                }
            }
        }
        return false;
    }
}
