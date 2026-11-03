using System.Text.Json;
using TravelCut.Application.Motion;
using TravelCut.Contracts.Dtos;
using TravelCut.Infrastructure.Motion;
using Xunit;

namespace TravelCut.Tests.Motion;

/// <summary>
/// MÉDIO-3: o manifesto registra adapterMode; resultado só é reaproveitado com o mesmo adaptador; READY_REAL só é
/// reaproveitado se o output.mp4 existir e não estiver vazio. Caso contrário o render é executado de novo.
/// </summary>
public class MotionIdempotencyModeTests : IDisposable
{
    private readonly TempMotionProject _project = new();

    public void Dispose() => _project.Dispose();

    private static MotionRenderResultDto Simulated(MotionRenderContext ctx) => new(
        MotionRenderStatuses.Simulated, true, null, MotionPaths.ManifestRelative(ctx.RenderId),
        null, MockMotionRenderer.NoRenderMessage, DateTime.UtcNow, DateTime.UtcNow);

    private HyperFramesRenderer HyperFrames(ScriptedProcessRunner runner) =>
        new(runner, new HyperFramesOptions(
            _project.AddFakeCli(), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5), _project.AddFakeNode()));

    private string ManifestPath(MotionRenderRequestDto request) =>
        Path.Combine(_project.WorkRoot, MotionPaths.RenderIdFor(request.ProjectId, request.IdempotencyKey), MotionPaths.RequestFileName);

    private string OutputPath(MotionRenderRequestDto request) =>
        Path.Combine(_project.WorkRoot, MotionPaths.RenderIdFor(request.ProjectId, request.IdempotencyKey), MotionPaths.OutputFileName);

    [Fact]
    public async Task ManifestRecordsAdapterMode()
    {
        var request = TempMotionProject.ValidRequest();
        await new MotionUseCases(new MockMotionRenderer(), MotionTestLocks.Shared).RenderAsync(request, _project.Root);

        using var doc = JsonDocument.Parse(File.ReadAllText(ManifestPath(request)));
        Assert.Equal("mock", doc.RootElement.GetProperty("AdapterMode").GetString());
    }

    [Fact]
    public async Task SameKey_DifferentAdapter_DoesNotReplaySimulatedResult()
    {
        var request = TempMotionProject.ValidRequest();
        var mock = new CountingMotionRenderer("mock", (_, ctx) => Task.FromResult(Simulated(ctx)));
        await new MotionUseCases(mock, MotionTestLocks.Shared).RenderAsync(request, _project.Root);

        var runner = new ScriptedProcessRunner { Render = ScriptedProcessRunner.WriteOutputFile };
        var result = await new MotionUseCases(HyperFrames(runner), MotionTestLocks.Shared).RenderAsync(request, _project.Root);

        Assert.Equal(1, mock.RenderCount);
        Assert.Equal(MotionRenderStatuses.ReadyReal, result.Status);
        Assert.False(result.IsSimulated);
        Assert.Single(runner.RenderCalls);

        using var doc = JsonDocument.Parse(File.ReadAllText(ManifestPath(request)));
        Assert.Equal("hyperframes", doc.RootElement.GetProperty("AdapterMode").GetString());
    }

    [Fact]
    public async Task SameKey_SameAdapter_StillReplaysSimulated()
    {
        var request = TempMotionProject.ValidRequest();
        var mock = new CountingMotionRenderer("mock", (_, ctx) => Task.FromResult(Simulated(ctx)));
        var useCases = new MotionUseCases(mock, MotionTestLocks.Shared);

        await useCases.RenderAsync(request, _project.Root);
        var second = await useCases.RenderAsync(request, _project.Root);

        Assert.Equal(1, mock.RenderCount);
        Assert.Equal(MotionRenderStatuses.Simulated, second.Status);
    }

    [Fact]
    public async Task ManifestWithoutAdapterMode_IsNotReplayed()
    {
        var request = TempMotionProject.ValidRequest();
        var mock = new CountingMotionRenderer("mock", (_, ctx) => Task.FromResult(Simulated(ctx)));
        await new MotionUseCases(mock, MotionTestLocks.Shared).RenderAsync(request, _project.Root);

        // Manifesto antigo (sem AdapterMode): não se sabe qual renderer o produziu, então não é reaproveitado.
        var legacy = new MotionStoredRequest(
            MotionSchemaVersions.Request,
            MotionPaths.RenderIdFor(request.ProjectId, request.IdempotencyKey),
            MotionUseCases.ComputeRequestHash(request),
            DateTime.UtcNow,
            request,
            AdapterMode: null);
        File.WriteAllText(ManifestPath(request), JsonSerializer.Serialize(legacy));

        await new MotionUseCases(mock, MotionTestLocks.Shared).RenderAsync(request, _project.Root);

        Assert.Equal(2, mock.RenderCount);
    }

    [Fact]
    public async Task ReadyReal_WithOutputDeleted_RendersAgain()
    {
        var request = TempMotionProject.ValidRequest();
        var runner = new ScriptedProcessRunner { Render = ScriptedProcessRunner.WriteOutputFile };
        var useCases = new MotionUseCases(HyperFrames(runner), MotionTestLocks.Shared);

        var first = await useCases.RenderAsync(request, _project.Root);
        Assert.Equal(MotionRenderStatuses.ReadyReal, first.Status);
        File.Delete(OutputPath(request));

        var second = await useCases.RenderAsync(request, _project.Root);

        Assert.Equal(MotionRenderStatuses.ReadyReal, second.Status);
        Assert.Equal(2, runner.RenderCalls.Count);
        Assert.True(File.Exists(OutputPath(request)));
    }

    [Fact]
    public async Task ReadyReal_WithEmptyOutput_RendersAgain()
    {
        var request = TempMotionProject.ValidRequest();
        var runner = new ScriptedProcessRunner { Render = ScriptedProcessRunner.WriteOutputFile };
        var useCases = new MotionUseCases(HyperFrames(runner), MotionTestLocks.Shared);

        await useCases.RenderAsync(request, _project.Root);
        File.WriteAllBytes(OutputPath(request), Array.Empty<byte>());

        var second = await useCases.RenderAsync(request, _project.Root);

        Assert.Equal(MotionRenderStatuses.ReadyReal, second.Status);
        Assert.Equal(2, runner.RenderCalls.Count);
        Assert.True(new FileInfo(OutputPath(request)).Length > 0);
    }

    [Fact]
    public async Task ReadyReal_WithCompleteOutput_IsReplayed_WithoutRendering()
    {
        var request = TempMotionProject.ValidRequest();
        var runner = new ScriptedProcessRunner { Render = ScriptedProcessRunner.WriteOutputFile };
        var useCases = new MotionUseCases(HyperFrames(runner), MotionTestLocks.Shared);

        await useCases.RenderAsync(request, _project.Root);
        var second = await useCases.RenderAsync(request, _project.Root);

        Assert.Equal(MotionRenderStatuses.ReadyReal, second.Status);
        Assert.Single(runner.RenderCalls);
    }
}
