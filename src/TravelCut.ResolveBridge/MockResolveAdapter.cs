using Microsoft.Extensions.Logging;
using TravelCut.Application.Editor;
using TravelCut.Application.Ports;
using TravelCut.Contracts.Dtos;

namespace TravelCut.ResolveBridge;

/// <summary>
/// Simulador do adaptador de editor, sem DaVinci Resolve. Resultados sempre SIMULATED (isSimulated=true), sem .drp
/// e sem render. Não é apresentado como integração real: o modo "mock" aparece em todas as respostas.
/// Estado (timelines simuladas) só em memória, nesta sessão do processo.
/// </summary>
public sealed class MockResolveAdapter : IEditorAdapter
{
    public const string ModeName = "mock";
    public const string SimulatorProductName = "Simulador TravelCut (sem DaVinci)";
    public const string NoDrpNote = "Simulador não gera .drp nem render.";

    private readonly ILogger<MockResolveAdapter> _logger;
    private readonly TimeProvider _time;
    private readonly object _gate = new();
    private readonly Dictionary<(string Project, string Timeline), EditorAssemblyDto> _timelines = new();

    public MockResolveAdapter(ILogger<MockResolveAdapter> logger, TimeProvider? time = null)
    {
        _logger = logger;
        _time = time ?? TimeProvider.System;
    }

    public string Mode => ModeName;

    public Task<EditorCapabilitiesDto> DetectCapabilitiesAsync(CancellationToken ct = default)
    {
        var operations = new List<EditorOperationCapabilityDto>
        {
            new(EditorOperations.CreateProject, true, CapabilityVerification.MockPass, null),
            new(EditorOperations.ImportMedia, true, CapabilityVerification.MockPass, null),
            new(EditorOperations.CreateTimeline, true, CapabilityVerification.MockPass, null),
            new(EditorOperations.ApplyEditPlan, true, CapabilityVerification.MockPass, null),
            new(EditorOperations.QueryTimeline, true, CapabilityVerification.MockPass, null),
            new(EditorOperations.ExportPreview, false, CapabilityVerification.Unsupported, NoDrpNote),
            new(EditorOperations.ExportProject, false, CapabilityVerification.Unsupported, NoDrpNote),
            new(EditorOperations.CreateSnapshot, false, CapabilityVerification.Unsupported, NoDrpNote),
        };

        var caps = new EditorCapabilitiesDto(
            SchemaVersion: "editor/1",
            AdapterMode: ModeName,
            EnvironmentState: EditorEnvironmentStates.Mock,
            IsAvailable: true,
            IsSimulated: true,
            ProductName: SimulatorProductName,
            Version: null,
            StatusMessage: "Simulador ativo: nenhuma operação é executada no DaVinci Resolve; os resultados são SIMULATED.",
            ReasonCode: null,
            Operations: operations,
            CheckedAtUtc: _time.GetUtcNow().UtcDateTime,
            CanCreateProject: true,
            CanImportMedia: true,
            CanCreateTimeline: true,
            CanApplyEditPlan: true,
            CanExportPreview: false);
        return Task.FromResult(caps);
    }

    public Task<EditorAdapterApplyResult> ApplyAssemblyAsync(EditorAssemblyDto assembly, EditorApplyOptions options, CancellationToken ct = default)
    {
        if (!string.Equals(assembly.SchemaVersion, EditorAssemblyBuilder.SchemaVersion, StringComparison.Ordinal))
        {
            _logger.LogWarning("[Simulador] Manifesto com versão de contrato desconhecida recusado");
            return Task.FromResult(Error(EditorResultStatus.Error, "CONTRACT_VERSION_MISMATCH",
                "Versão do manifesto não suportada pelo simulador."));
        }

        var steps = new List<EditorStepResultDto>
        {
            new(EditorOperations.CreateProject, "simulated", "Etapa simulada: nenhum projeto foi criado no DaVinci.", null),
            new(EditorOperations.ImportMedia, "simulated", "Etapa simulada: nenhuma mídia foi importada.", null),
            new(EditorOperations.CreateTimeline, "simulated", "Etapa simulada: nenhuma timeline foi criada.", null),
            new(EditorOperations.ApplyEditPlan, "simulated", "Etapa simulada: a montagem não foi aplicada no editor.", null),
            new(EditorOperations.QueryTimeline, "simulated", "Etapa simulada: leitura de volta não observada.", null),
        };

        // Valores "observados" são os do próprio manifesto (não há leitura real); matches fica null.
        var mappings = assembly.Clips
            .Select(c => new EditorClipMappingDto(c.ClipId, SimulatedItemId(c.ClipId), c.RecordFrame, c.DurationFrames, null))
            .ToList();

        lock (_gate)
        {
            _timelines[(assembly.EditorProjectName, assembly.TimelineName)] = assembly;
        }

        _logger.LogInformation("[Simulador] Montagem simulada com {ClipCount} clipe(s)", assembly.Clips.Count);

        // Snapshot nunca é gerado pelo simulador, mesmo que as opções peçam.
        return Task.FromResult(new EditorAdapterApplyResult(
            Status: EditorResultStatus.Simulated,
            EditorVersion: null,
            Steps: steps,
            ClipMappings: mappings,
            Rejected: [],
            Warnings: [],
            SnapshotAbsolutePath: null,
            ErrorCode: null,
            ErrorMessage: null));
    }

    public Task<EditorTimelineQueryDto> QueryTimelineAsync(string editorProjectName, string timelineName, CancellationToken ct = default)
    {
        EditorAssemblyDto? assembly;
        lock (_gate)
        {
            _timelines.TryGetValue((editorProjectName, timelineName), out assembly);
        }

        if (assembly is null)
        {
            return Task.FromResult(new EditorTimelineQueryDto(
                Status: EditorResultStatus.Error,
                EditorProjectName: editorProjectName,
                TimelineName: timelineName,
                TimelineStartFrame: null,
                Items: [],
                ErrorCode: "TIMELINE_NOT_FOUND",
                ErrorMessage: "Timeline simulada não encontrada nesta sessão."));
        }

        var items = assembly.Clips
            .Select(c => new EditorTimelineItemDto(SimulatedItemId(c.ClipId), 1, c.RecordFrame, c.DurationFrames, c.SourceStartFrame, null))
            .ToList();

        return Task.FromResult(new EditorTimelineQueryDto(
            Status: EditorResultStatus.Simulated,
            EditorProjectName: editorProjectName,
            TimelineName: timelineName,
            TimelineStartFrame: 0,
            Items: items,
            ErrorCode: null,
            ErrorMessage: null));
    }

    public Task<EditorExportResultDto> ExportProjectAsync(string editorProjectName, string outputAbsolutePath, CancellationToken ct = default) =>
        Task.FromResult(new EditorExportResultDto(
            Status: EditorResultStatus.Unavailable,
            OutputAbsolutePath: null,
            ErrorCode: "CAPABILITY_UNSUPPORTED",
            ErrorMessage: NoDrpNote));

    public Task<EditorExportResultDto> ExportPreviewAsync(string editorProjectName, string timelineName, string outputDirectoryAbsolute, CancellationToken ct = default) =>
        Task.FromResult(new EditorExportResultDto(
            Status: EditorResultStatus.Unavailable,
            OutputAbsolutePath: null,
            ErrorCode: "CAPABILITY_UNSUPPORTED",
            ErrorMessage: "Simulador não gera render de prévia."));

    private static string SimulatedItemId(Guid clipId) => $"sim-{clipId:N}";

    private static EditorAdapterApplyResult Error(string status, string code, string message) => new(
        Status: status,
        EditorVersion: null,
        Steps: [],
        ClipMappings: [],
        Rejected: [],
        Warnings: [],
        SnapshotAbsolutePath: null,
        ErrorCode: code,
        ErrorMessage: message);
}
