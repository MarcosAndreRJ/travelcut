using System.Text.Json.Serialization;

namespace TravelCut.Contracts.Dtos;

// Contrato editor/1 (F08). Regras (docs/05, docs/09, docs/10):
// - Mock NUNCA é apresentado como integração real: isSimulated=true e resultados SIMULATED; não gera .drp.
// - Indisponibilidade é estado de primeira classe (environmentState + reasonCode), sem fallback silencioso ao mock.
// - Capabilities por operação, com o nível de verificação (documented | mock_pass | host_pending | host_pass | unsupported).
// - O TravelCut só cria projetos NOVOS no DaVinci (nome com prefixo "TravelCut_"); nunca abre/altera projetos existentes.
// - Aplicar exige dry-run prévio (assemblyFingerprint igual) e confirmação explícita de troca do projeto aberto no Resolve.
// - Tempo: manifesto em frames inteiros; fonte com fim EXCLUSIVO [start,end); a bridge converte para a convenção da API.
// Métodos IPC: editor.capabilities, editor.prepareApply, editor.apply, editor.applications.

public static class EditorEnvironmentStates
{
    public const string NotInstalled = "NOT_INSTALLED";
    public const string NotRunning = "NOT_RUNNING";
    public const string ScriptingDisabled = "SCRIPTING_DISABLED";   // Resolve aberto, scripting externo desligado/inacessível
    public const string PythonUnavailable = "PYTHON_UNAVAILABLE";   // sem Python compatível para a bridge
    public const string ReadyPartial = "READY_PARTIAL";             // conectado, mas parte das operações indisponível
    public const string Ready = "READY";
    public const string Mock = "MOCK";
    public const string Error = "ERROR";
}

public static class EditorOperations
{
    public const string DetectCapabilities = "detect_capabilities";
    public const string CreateProject = "create_project";
    public const string ImportMedia = "import_media";
    public const string CreateTimeline = "create_timeline";
    public const string ApplyEditPlan = "apply_edit_plan";
    public const string QueryTimeline = "query_timeline";
    public const string ExportPreview = "export_preview";
    public const string ExportProject = "export_project";
    public const string CreateSnapshot = "create_snapshot";
}

public static class CapabilityVerification
{
    public const string Documented = "documented";
    public const string MockPass = "mock_pass";
    public const string HostPending = "host_pending";
    public const string HostPass = "host_pass";
    public const string Unsupported = "unsupported";
}

public static class EditorResultStatus
{
    public const string Simulated = "SIMULATED";
    public const string DryRun = "DRY_RUN";
    public const string Unavailable = "UNAVAILABLE";
    public const string ReadyReal = "READY_REAL";       // todas as operações reais concluídas e conferidas
    public const string PartialReal = "PARTIAL_REAL";   // real, com operações rejeitadas/divergências
    public const string Error = "ERROR";
}

public record EditorOperationCapabilityDto(
    [property: JsonPropertyName("operation")] string Operation,       // EditorOperations
    [property: JsonPropertyName("supported")] bool Supported,         // pode ser chamada agora neste ambiente
    [property: JsonPropertyName("verification")] string Verification, // CapabilityVerification
    [property: JsonPropertyName("note")] string? Note
);

public record EditorCapabilitiesDto(
    [property: JsonPropertyName("schemaVersion")] string SchemaVersion,       // "editor/1"
    [property: JsonPropertyName("adapterMode")] string AdapterMode,           // "mock" | "davinci"
    [property: JsonPropertyName("environmentState")] string EnvironmentState, // EditorEnvironmentStates
    [property: JsonPropertyName("isAvailable")] bool IsAvailable,
    [property: JsonPropertyName("isSimulated")] bool IsSimulated,
    [property: JsonPropertyName("productName")] string? ProductName,          // ex.: "DaVinci Resolve Studio"; null se desconhecido
    [property: JsonPropertyName("version")] string? Version,                  // versão lida da API; null se não lida (nunca inventar)
    [property: JsonPropertyName("statusMessage")] string StatusMessage,
    [property: JsonPropertyName("reasonCode")] string? ReasonCode,            // EDITOR_UNAVAILABLE | PYTHON_UNAVAILABLE | SCRIPTING_DISABLED | ...
    [property: JsonPropertyName("operations")] List<EditorOperationCapabilityDto> Operations,
    [property: JsonPropertyName("checkedAtUtc")] DateTime CheckedAtUtc,
    // Legado (derivados de Operations; mantidos para telas antigas):
    [property: JsonPropertyName("canCreateProject")] bool CanCreateProject,
    [property: JsonPropertyName("canImportMedia")] bool CanImportMedia,
    [property: JsonPropertyName("canCreateTimeline")] bool CanCreateTimeline,
    [property: JsonPropertyName("canApplyEditPlan")] bool CanApplyEditPlan,
    [property: JsonPropertyName("canExportPreview")] bool CanExportPreview
);

// ---- Manifesto de montagem (artefato em 3-Trabalho/editor/<planId>/r<NNNN>.assembly.json; entrada da bridge) ----

public record EditorAssemblyMediaDto(
    [property: JsonPropertyName("mediaId")] Guid MediaId,
    [property: JsonPropertyName("relativePath")] string RelativePath,
    [property: JsonPropertyName("absolutePath")] string AbsolutePath,  // só no manifesto/bridge; nunca enviado à UI
    [property: JsonPropertyName("sourceFpsNum")] long SourceFpsNum,    // frames por segundo = num/den (ex.: 30000/1001)
    [property: JsonPropertyName("sourceFpsDen")] long SourceFpsDen,
    [property: JsonPropertyName("isVariableFrameRate")] bool IsVariableFrameRate,
    [property: JsonPropertyName("durationUs")] long DurationUs
);

public record EditorAssemblyClipDto(
    [property: JsonPropertyName("clipId")] Guid ClipId,
    [property: JsonPropertyName("mediaId")] Guid MediaId,
    [property: JsonPropertyName("trackIndex")] int TrackIndex,                     // 1 = V1 (vídeo + áudio vinculado)
    [property: JsonPropertyName("mediaType")] string MediaType,                    // "video_audio"
    [property: JsonPropertyName("sourceStartFrame")] long SourceStartFrame,        // inclusivo, frames da FONTE
    [property: JsonPropertyName("sourceEndFrameExclusive")] long SourceEndFrameExclusive,
    [property: JsonPropertyName("recordFrame")] long RecordFrame,                  // relativo ao início da timeline (frames da timeline)
    [property: JsonPropertyName("durationFrames")] long DurationFrames,            // frames da timeline
    [property: JsonPropertyName("sourceInUs")] long SourceInUs,
    [property: JsonPropertyName("sourceOutUs")] long SourceOutUs
);

public record EditorRejectedOpDto(
    [property: JsonPropertyName("kind")] string Kind,       // speed | transition | track | clip
    [property: JsonPropertyName("clipId")] Guid? ClipId,
    [property: JsonPropertyName("code")] string Code,       // CAPABILITY_UNSUPPORTED | MEDIA_NOT_FOUND | INVALID_TIMEBASE | ...
    [property: JsonPropertyName("message")] string Message
);

public record EditorWarningDto(
    [property: JsonPropertyName("code")] string Code,       // VFR_SOURCE | MIXED_FRAME_RATE | FRAME_ROUNDING | LINKED_AUDIO_COVERED | ...
    [property: JsonPropertyName("message")] string Message,
    [property: JsonPropertyName("clipId")] Guid? ClipId
);

public record EditorAssemblyDto(
    [property: JsonPropertyName("schemaVersion")] string SchemaVersion,   // "editor-assembly/1"
    [property: JsonPropertyName("notice")] string Notice,                 // "Manifesto de montagem do TravelCut — não é um projeto DaVinci."
    [property: JsonPropertyName("projectId")] Guid ProjectId,
    [property: JsonPropertyName("planId")] Guid PlanId,
    [property: JsonPropertyName("revision")] int Revision,
    [property: JsonPropertyName("planFingerprint")] string PlanFingerprint,
    [property: JsonPropertyName("editorProjectName")] string EditorProjectName, // "TravelCut_<nome>_r<NNNN>_<aaaammddThhmmss>"
    [property: JsonPropertyName("timelineName")] string TimelineName,
    [property: JsonPropertyName("timeBase")] TimelineTimeBaseDto TimeBase,
    [property: JsonPropertyName("timelineFrameRate")] string TimelineFrameRate, // valor do setting "timelineFrameRate" (ex.: "29.97")
    [property: JsonPropertyName("media")] List<EditorAssemblyMediaDto> Media,
    [property: JsonPropertyName("clips")] List<EditorAssemblyClipDto> Clips,
    [property: JsonPropertyName("rejected")] List<EditorRejectedOpDto> Rejected,
    [property: JsonPropertyName("warnings")] List<EditorWarningDto> Warnings,
    [property: JsonPropertyName("assemblyFingerprint")] string AssemblyFingerprint // hash canônico de tudo acima exceto nomes com data
);

// ---- IPC ----

public record PrepareEditorApplyRequest(
    [property: JsonPropertyName("projectId")] Guid ProjectId,
    [property: JsonPropertyName("revision")] int? Revision   // null = última
);

/// <summary>Clipe do dry-run para a UI (sem caminho absoluto).</summary>
public record EditorClipPreviewDto(
    [property: JsonPropertyName("clipId")] Guid ClipId,
    [property: JsonPropertyName("relativePath")] string RelativePath,
    [property: JsonPropertyName("sourceStartFrame")] long SourceStartFrame,
    [property: JsonPropertyName("sourceEndFrameExclusive")] long SourceEndFrameExclusive,
    [property: JsonPropertyName("recordFrame")] long RecordFrame,
    [property: JsonPropertyName("durationFrames")] long DurationFrames
);

public record EditorApplyPreviewDto(
    [property: JsonPropertyName("status")] string Status,                    // DRY_RUN
    [property: JsonPropertyName("planId")] Guid PlanId,
    [property: JsonPropertyName("revision")] int Revision,
    [property: JsonPropertyName("planFingerprint")] string PlanFingerprint,
    [property: JsonPropertyName("assemblyFingerprint")] string AssemblyFingerprint,
    [property: JsonPropertyName("editorProjectName")] string EditorProjectName,
    [property: JsonPropertyName("timelineName")] string TimelineName,
    [property: JsonPropertyName("timelineFrameRate")] string TimelineFrameRate,
    [property: JsonPropertyName("clips")] List<EditorClipPreviewDto> Clips,
    [property: JsonPropertyName("rejected")] List<EditorRejectedOpDto> Rejected,
    [property: JsonPropertyName("warnings")] List<EditorWarningDto> Warnings,
    [property: JsonPropertyName("manifestRelativePath")] string ManifestRelativePath, // fallback seguro: manifesto para aplicação manual no host
    [property: JsonPropertyName("capabilities")] EditorCapabilitiesDto Capabilities,
    [property: JsonPropertyName("canApply")] bool CanApply,
    [property: JsonPropertyName("blockingReasons")] List<string> BlockingReasons,      // códigos: EDITOR_UNAVAILABLE | PLAN_OUTDATED | NO_CLIPS | ...
    [property: JsonPropertyName("requiresProjectSwitchConfirmation")] bool RequiresProjectSwitchConfirmation
);

public record ApplyEditorPlanRequest(
    [property: JsonPropertyName("projectId")] Guid ProjectId,
    [property: JsonPropertyName("revision")] int Revision,
    [property: JsonPropertyName("assemblyFingerprint")] string AssemblyFingerprint, // do dry-run; divergente → PROJECT_VERSION_CONFLICT
    [property: JsonPropertyName("confirmProjectSwitch")] bool ConfirmProjectSwitch, // obrigatório true no adaptador real
    [property: JsonPropertyName("idempotencyKey")] string IdempotencyKey            // mesma chave + mesmo fingerprint → devolve o registro anterior
);

public record EditorStepResultDto(
    [property: JsonPropertyName("operation")] string Operation,   // EditorOperations
    [property: JsonPropertyName("status")] string Status,         // ok | failed | skipped | simulated
    [property: JsonPropertyName("message")] string Message,
    [property: JsonPropertyName("durationMs")] long? DurationMs
);

public record EditorClipMappingDto(
    [property: JsonPropertyName("clipId")] Guid ClipId,
    [property: JsonPropertyName("editorItemId")] string? EditorItemId,         // id do TimelineItem quando a API expõe; null se não
    [property: JsonPropertyName("observedRecordFrame")] long? ObservedRecordFrame, // lido de volta da timeline (relativo ao início)
    [property: JsonPropertyName("observedDurationFrames")] long? ObservedDurationFrames,
    [property: JsonPropertyName("matches")] bool? Matches                      // null quando não observado (mock ou consulta indisponível)
);

public record EditorApplyResultDto(
    [property: JsonPropertyName("schemaVersion")] string SchemaVersion,       // "editor-apply/1"
    [property: JsonPropertyName("applicationId")] Guid ApplicationId,
    [property: JsonPropertyName("status")] string Status,                     // EditorResultStatus
    [property: JsonPropertyName("adapterMode")] string AdapterMode,
    [property: JsonPropertyName("isSimulated")] bool IsSimulated,
    [property: JsonPropertyName("projectId")] Guid ProjectId,
    [property: JsonPropertyName("planId")] Guid PlanId,
    [property: JsonPropertyName("revision")] int Revision,
    [property: JsonPropertyName("assemblyFingerprint")] string AssemblyFingerprint,
    [property: JsonPropertyName("idempotencyKey")] string IdempotencyKey,
    [property: JsonPropertyName("editorProjectName")] string? EditorProjectName,
    [property: JsonPropertyName("timelineName")] string? TimelineName,
    [property: JsonPropertyName("editorVersion")] string? EditorVersion,
    [property: JsonPropertyName("steps")] List<EditorStepResultDto> Steps,
    [property: JsonPropertyName("clipMappings")] List<EditorClipMappingDto> ClipMappings,
    [property: JsonPropertyName("rejected")] List<EditorRejectedOpDto> Rejected,
    [property: JsonPropertyName("warnings")] List<EditorWarningDto> Warnings,
    [property: JsonPropertyName("snapshotRelativePath")] string? SnapshotRelativePath, // .drp REAL exportado pelo Resolve; sempre null no mock
    [property: JsonPropertyName("recordRelativePath")] string RecordRelativePath,      // 3-Trabalho/editor/<planId>/applications/<id>.json
    [property: JsonPropertyName("startedAtUtc")] DateTime StartedAtUtc,
    [property: JsonPropertyName("finishedAtUtc")] DateTime FinishedAtUtc,
    [property: JsonPropertyName("errorCode")] string? ErrorCode,
    [property: JsonPropertyName("errorMessage")] string? ErrorMessage,
    [property: JsonPropertyName("reusedPrevious")] bool ReusedPrevious
);

public record ListEditorApplicationsRequest(
    [property: JsonPropertyName("projectId")] Guid ProjectId
);

public record EditorApplicationsResponse(
    [property: JsonPropertyName("applications")] List<EditorApplyResultDto> Applications // mais recente primeiro
);

// ---- Resultados de baixo nível do adaptador (Application ↔ ResolveBridge) ----

public record EditorTimelineItemDto(
    [property: JsonPropertyName("editorItemId")] string? EditorItemId,
    [property: JsonPropertyName("trackIndex")] int TrackIndex,
    [property: JsonPropertyName("recordFrame")] long RecordFrame,       // relativo ao início da timeline
    [property: JsonPropertyName("durationFrames")] long DurationFrames,
    [property: JsonPropertyName("sourceStartFrame")] long? SourceStartFrame,
    [property: JsonPropertyName("mediaName")] string? MediaName
);

public record EditorTimelineQueryDto(
    [property: JsonPropertyName("status")] string Status,               // EditorResultStatus
    [property: JsonPropertyName("editorProjectName")] string EditorProjectName,
    [property: JsonPropertyName("timelineName")] string TimelineName,
    [property: JsonPropertyName("timelineStartFrame")] long? TimelineStartFrame, // frame absoluto do início (timecode inicial)
    [property: JsonPropertyName("items")] List<EditorTimelineItemDto> Items,
    [property: JsonPropertyName("errorCode")] string? ErrorCode,
    [property: JsonPropertyName("errorMessage")] string? ErrorMessage
);

public record EditorExportResultDto(
    [property: JsonPropertyName("status")] string Status,               // EditorResultStatus
    [property: JsonPropertyName("outputAbsolutePath")] string? OutputAbsolutePath,
    [property: JsonPropertyName("errorCode")] string? ErrorCode,
    [property: JsonPropertyName("errorMessage")] string? ErrorMessage
);
