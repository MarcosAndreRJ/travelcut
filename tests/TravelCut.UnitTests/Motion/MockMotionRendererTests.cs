using TravelCut.Application.Motion;
using TravelCut.Contracts.Dtos;
using TravelCut.Infrastructure.Motion;
using Xunit;

namespace TravelCut.Tests.Motion;

public class MockMotionRendererTests : IDisposable
{
    private readonly TempMotionProject _project = new();

    public void Dispose() => _project.Dispose();

    [Fact]
    public async Task DetectCapabilities_IsMockSimulated_WithMockPassTemplates()
    {
        var caps = await new MockMotionRenderer().DetectCapabilitiesAsync();

        Assert.Equal("mock", caps.AdapterMode);
        Assert.Equal(MotionEnvironmentStates.Mock, caps.EnvironmentState);
        Assert.True(caps.IsAvailable);
        Assert.True(caps.IsSimulated);
        Assert.Null(caps.NodeVersion);
        Assert.Null(caps.RendererVersion);
        Assert.Equal(MotionTemplateCatalog.Templates.Count, caps.Templates.Count);
        Assert.All(caps.Templates, t =>
        {
            Assert.True(t.Supported);
            Assert.Equal(CapabilityVerification.MockPass, t.Verification);
        });
    }

    [Fact]
    public async Task Render_IsSimulated_WithoutOutputPath_AndWritesNoVideo()
    {
        var request = TempMotionProject.ValidRequest();
        Assert.True(MotionPaths.TryCreateContext(_project.Root, MotionPaths.RenderIdFor(request.ProjectId, request.IdempotencyKey), out var ctx));
        Directory.CreateDirectory(ctx!.OutputDirAbsolute);

        var result = await new MockMotionRenderer().RenderAsync(request, ctx);

        Assert.Equal(MotionRenderStatuses.Simulated, result.Status);
        Assert.True(result.IsSimulated);
        Assert.Null(result.OutputRelativePath);
        Assert.Null(result.ErrorCode);
        Assert.Equal("DEMONSTRAÇÃO: nenhum vídeo foi renderizado.", result.ErrorMessage);
        Assert.False(File.Exists(ctx.OutputFileAbsolute));
        Assert.Empty(Directory.GetFiles(ctx.OutputDirAbsolute));
        Assert.Equal(MotionPaths.ManifestRelative(ctx.RenderId), result.ManifestRelativePath);
    }

    [Fact]
    public async Task Render_ThroughUseCases_ReturnsSimulatedError_ForInvalidRequest_AndIsSimulatedFlagged()
    {
        var useCases = new MotionUseCases(new MockMotionRenderer(), MotionTestLocks.Shared);

        var result = await useCases.RenderAsync(TempMotionProject.ValidRequest(templateId: "x"), _project.Root);

        Assert.Equal(MotionRenderStatuses.Error, result.Status);
        Assert.True(result.IsSimulated);
    }
}
