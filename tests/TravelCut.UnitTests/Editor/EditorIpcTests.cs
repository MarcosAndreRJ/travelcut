using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using TravelCut.Application.Editor;
using TravelCut.Application.Ipc;
using TravelCut.Contracts.Dtos;
using TravelCut.Contracts.Ipc;
using TravelCut.UnitTests.Planning;
using Xunit;

namespace TravelCut.UnitTests.Editor;

/// <summary>Handler IPC do editor (payload JSON real, como chega do WebView2).</summary>
public class EditorIpcHandlerTests : IDisposable
{
    private readonly EditorFixture _f = new();
    private readonly EditorIpcHandler _handler;

    public EditorIpcHandlerTests()
    {
        _handler = new EditorIpcHandler(_f.Editor);
    }

    public void Dispose() => _f.Dispose();

    private static JsonElement Json(string raw) => JsonSerializer.Deserialize<JsonElement>(raw);

    private static string ProjectJson(Guid id) => $"{{\"projectId\":\"{id:D}\"}}";

    [Fact]
    public async Task Capabilities_ReturnsMockCapabilities()
    {
        var response = await _handler.TryHandleAsync(new IpcRequest { RequestId = "r1", Type = "editor.capabilities" });

        Assert.NotNull(response);
        Assert.True(response!.Ok);
        var caps = JsonSerializer.Deserialize<EditorCapabilitiesDto>(JsonSerializer.Serialize(response.Payload));
        Assert.Equal("mock", caps!.AdapterMode);
        Assert.True(caps.IsSimulated);
    }

    [Fact]
    public async Task PrepareApply_WithoutPlan_FailsWithNoPlan()
    {
        var response = await _handler.TryHandleAsync(new IpcRequest
        {
            RequestId = "r2",
            Type = "editor.prepareApply",
            Payload = Json(ProjectJson(TravelFixture.ProjectId)),
        });

        Assert.NotNull(response);
        Assert.False(response!.Ok);
        Assert.Equal("NO_PLAN", response.ErrorCode);
    }

    [Fact]
    public async Task PrepareApply_WithPlan_ReturnsDryRunPayload()
    {
        await _f.GeneratePlanAsync();

        var response = await _handler.TryHandleAsync(new IpcRequest
        {
            RequestId = "r3",
            Type = "editor.prepareApply",
            Payload = Json(ProjectJson(TravelFixture.ProjectId)),
        });

        Assert.True(response!.Ok, response.ErrorMessage);
        var preview = JsonSerializer.Deserialize<EditorApplyPreviewDto>(JsonSerializer.Serialize(response.Payload));
        Assert.Equal("DRY_RUN", preview!.Status);
        Assert.True(preview.CanApply);
    }

    [Fact]
    public async Task PrepareApply_EmptyProject_IsInvalidArgument()
    {
        var response = await _handler.TryHandleAsync(new IpcRequest
        {
            RequestId = "r4",
            Type = "editor.prepareApply",
            Payload = Json(ProjectJson(Guid.Empty)),
        });

        Assert.Equal("INVALID_ARGUMENT", response!.ErrorCode);
    }

    [Fact]
    public async Task Apply_WithTypedPayload_ReturnsRecord()
    {
        await _f.GeneratePlanAsync();
        var prep = await _f.Editor.PrepareApplyAsync(new PrepareEditorApplyRequest(TravelFixture.ProjectId, null));
        var request = new ApplyEditorPlanRequest(TravelFixture.ProjectId, prep.Value!.Revision,
            prep.Value.AssemblyFingerprint, ConfirmProjectSwitch: false, IdempotencyKey: "ipc-1");

        var response = await _handler.TryHandleAsync(new IpcRequest { RequestId = "r5", Type = "editor.apply", Payload = request });

        Assert.True(response!.Ok, response.ErrorMessage);
        var record = JsonSerializer.Deserialize<EditorApplyResultDto>(JsonSerializer.Serialize(response.Payload));
        Assert.Equal("ipc-1", record!.IdempotencyKey);
        Assert.Equal("editor-apply/1", record.SchemaVersion);
    }

    [Fact]
    public async Task Applications_ReturnsEmptyList_WhenNothingApplied()
    {
        var response = await _handler.TryHandleAsync(new IpcRequest
        {
            RequestId = "r6",
            Type = "editor.applications",
            Payload = Json(ProjectJson(TravelFixture.ProjectId)),
        });

        Assert.True(response!.Ok);
        var list = JsonSerializer.Deserialize<EditorApplicationsResponse>(JsonSerializer.Serialize(response.Payload));
        Assert.Empty(list!.Applications);
    }

    [Fact]
    public async Task UnknownType_ReturnsNull_SoDispatcherContinues()
    {
        var response = await _handler.TryHandleAsync(new IpcRequest { RequestId = "r7", Type = "media.list" });

        Assert.Null(response);
    }
}

/// <summary>Roteamento editor.* pelo IpcDispatcher. Dependências que as rotas editor.* não usam recebem null.</summary>
public class EditorDispatcherRoutingTests : IDisposable
{
    private readonly EditorFixture _f = new();

    public void Dispose() => _f.Dispose();

    // Ordem posicional do construtor de IpcDispatcher (open, scan, list, details, chronology, editorAdapter, pipeline,
    // projectRepo, mediaRepo, planRepo, listDirectory, inspect, thumbnail, import, openFolder, planning, logger, preview, editor).
    private IpcDispatcher Dispatcher(EditorIpcHandler? editor) => new(
        null!, null!, null!, null!, null!,
        _f.Adapter,
        null!, null!, null!, null!, null!, null!, null!, null!, null!,
        _f.Planning,
        NullLogger<IpcDispatcher>.Instance,
        null,
        editor);

    [Fact]
    public async Task EditorApplications_IsRoutedToHandler()
    {
        var dispatcher = Dispatcher(new EditorIpcHandler(_f.Editor));
        var payload = JsonSerializer.Deserialize<JsonElement>($"{{\"projectId\":\"{TravelFixture.ProjectId:D}\"}}");

        var response = await dispatcher.DispatchAsync(new IpcRequest { RequestId = "d1", Type = "editor.applications", Payload = payload });

        Assert.True(response.Ok, response.ErrorMessage);
    }

    [Fact]
    public async Task EditorCapabilities_WithoutHandler_UsesFallback()
    {
        var dispatcher = Dispatcher(null);

        var response = await dispatcher.DispatchAsync(new IpcRequest { RequestId = "d2", Type = "editor.capabilities" });

        Assert.True(response.Ok);
    }

    [Fact]
    public async Task EditorApplyType_WithoutHandler_IsUnknownMethod()
    {
        var dispatcher = Dispatcher(null);

        var response = await dispatcher.DispatchAsync(new IpcRequest { RequestId = "d3", Type = "editor.apply", Payload = null });

        Assert.Equal("UNKNOWN_METHOD", response.ErrorCode);
    }
}
