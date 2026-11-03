using TravelCut.Application.Editor;
using TravelCut.Application.Ports;
using TravelCut.Contracts.Dtos;

namespace TravelCut.UnitTests.Editor;

/// <summary>Relógio que avança um minuto a cada leitura: ordem de aplicações determinística.</summary>
public sealed class SteppingTimeProvider : TimeProvider
{
    private DateTimeOffset _now = new(2026, 11, 2, 12, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow()
    {
        _now = _now.AddMinutes(1);
        return _now;
    }
}

/// <summary>
/// Adaptador de teste. Modo "davinci" simula a escrita de snapshot (arquivo real) para testar o caminho relativo;
/// o comportamento é controlado pelo teste (Available, ThrowOnApply, OnApply).
/// </summary>
public sealed class FakeEditorAdapter : IEditorAdapter
{
    public string Mode { get; set; } = "mock";
    public bool Available { get; set; } = true;
    public int ApplyCalls { get; private set; }
    public EditorApplyOptions? LastOptions { get; private set; }
    public Exception? ThrowOnApply { get; set; }
    public Func<EditorAssemblyDto, EditorAdapterApplyResult>? OnApply { get; set; }

    public Task<EditorCapabilitiesDto> DetectCapabilitiesAsync(CancellationToken ct = default)
    {
        var simulated = Mode == "mock";
        var operations = new List<EditorOperationCapabilityDto>
        {
            new(EditorOperations.CreateProject, Available, CapabilityVerification.MockPass, null),
            new(EditorOperations.ApplyEditPlan, Available, CapabilityVerification.MockPass, null),
            new(EditorOperations.ExportPreview, false, CapabilityVerification.Unsupported, null),
        };
        var caps = new EditorCapabilitiesDto(
            SchemaVersion: "editor/1",
            AdapterMode: Mode,
            EnvironmentState: Available ? (simulated ? EditorEnvironmentStates.Mock : EditorEnvironmentStates.Ready) : EditorEnvironmentStates.NotInstalled,
            IsAvailable: Available,
            IsSimulated: simulated,
            ProductName: Available ? "Fake" : null,
            Version: null,
            StatusMessage: Available ? "Fake disponível." : "Fake indisponível.",
            ReasonCode: Available ? null : "EDITOR_UNAVAILABLE",
            Operations: operations,
            CheckedAtUtc: DateTime.UtcNow,
            CanCreateProject: Available,
            CanImportMedia: Available,
            CanCreateTimeline: Available,
            CanApplyEditPlan: Available,
            CanExportPreview: false);
        return Task.FromResult(caps);
    }

    public Task<EditorAdapterApplyResult> ApplyAssemblyAsync(EditorAssemblyDto assembly, EditorApplyOptions options, CancellationToken ct = default)
    {
        ApplyCalls++;
        LastOptions = options;
        if (ThrowOnApply is not null) throw ThrowOnApply;
        if (OnApply is not null) return Task.FromResult(OnApply(assembly));

        string? snapshot = null;
        if (options.SnapshotAbsolutePath is { } path && Mode == "davinci")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, new byte[] { 1, 2, 3 });
            snapshot = path;
        }

        var status = Mode == "mock" ? EditorResultStatus.Simulated : EditorResultStatus.ReadyReal;
        var mappings = assembly.Clips
            .Select(c => new EditorClipMappingDto(c.ClipId, $"fake-{c.ClipId:N}", c.RecordFrame, c.DurationFrames, null))
            .ToList();
        return Task.FromResult(new EditorAdapterApplyResult(
            status,
            EditorVersion: null,
            Steps: [new EditorStepResultDto(EditorOperations.ApplyEditPlan, "ok", "Fake aplicou.", null)],
            ClipMappings: mappings,
            Rejected: [],
            Warnings: [],
            SnapshotAbsolutePath: snapshot,
            ErrorCode: null,
            ErrorMessage: null));
    }

    public Task<EditorTimelineQueryDto> QueryTimelineAsync(string editorProjectName, string timelineName, CancellationToken ct = default) =>
        Task.FromResult(new EditorTimelineQueryDto(EditorResultStatus.Simulated, editorProjectName, timelineName, 0, [], null, null));

    public Task<EditorExportResultDto> ExportProjectAsync(string editorProjectName, string outputAbsolutePath, CancellationToken ct = default) =>
        Task.FromResult(new EditorExportResultDto(EditorResultStatus.Unavailable, null, "CAPABILITY_UNSUPPORTED", "Fake não exporta."));

    public Task<EditorExportResultDto> ExportPreviewAsync(string editorProjectName, string timelineName, string outputDirectoryAbsolute, CancellationToken ct = default) =>
        Task.FromResult(new EditorExportResultDto(EditorResultStatus.Unavailable, null, "CAPABILITY_UNSUPPORTED", "Fake não renderiza."));
}
