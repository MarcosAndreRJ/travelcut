using System.Text.Json;
using TravelCut.Application.Motion;
using TravelCut.Contracts.Dtos;
using TravelCut.Infrastructure.Motion;
using Xunit;

namespace TravelCut.Tests.Motion;

public class MotionUseCasesTests : IDisposable
{
    private readonly TempMotionProject _project = new();

    public void Dispose() => _project.Dispose();

    private static MotionRenderResultDto Simulated(MotionRenderContext ctx) => new(
        MotionRenderStatuses.Simulated, true, null, MotionPaths.ManifestRelative(ctx.RenderId),
        null, MockMotionRenderer.NoRenderMessage, DateTime.UtcNow, DateTime.UtcNow);

    [Fact]
    public async Task InvalidRequest_ReturnsError_AndCreatesNothing()
    {
        var renderer = new CountingMotionRenderer("mock", (_, ctx) => Task.FromResult(Simulated(ctx)));
        var useCases = new MotionUseCases(renderer, MotionTestLocks.Shared);

        var result = await useCases.RenderAsync(TempMotionProject.ValidRequest(templateId: "nao_existe"), _project.Root);

        Assert.Equal(MotionRenderStatuses.Error, result.Status);
        Assert.Equal(MotionRequestValidator.UnknownTemplateCode, result.ErrorCode);
        Assert.Equal(string.Empty, result.ManifestRelativePath);
        Assert.Equal(0, renderer.RenderCount);
        Assert.False(Directory.Exists(_project.WorkRoot));
    }

    [Fact]
    public async Task MockRender_WritesRequestAndResult_WithoutOutputFile()
    {
        var mock = new MockMotionRenderer();
        var useCases = new MotionUseCases(mock, MotionTestLocks.Shared);
        var request = TempMotionProject.ValidRequest();

        var result = await useCases.RenderAsync(request, _project.Root);

        Assert.Equal(MotionRenderStatuses.Simulated, result.Status);
        Assert.True(result.IsSimulated);
        Assert.Null(result.OutputRelativePath);
        Assert.Contains("DEMONSTRAÇÃO: nenhum vídeo foi renderizado", result.ErrorMessage);

        string renderId = MotionPaths.RenderIdFor(request.ProjectId, request.IdempotencyKey);
        string folder = Path.Combine(_project.WorkRoot, renderId);
        Assert.True(File.Exists(Path.Combine(folder, MotionPaths.RequestFileName)));
        Assert.True(File.Exists(Path.Combine(folder, MotionPaths.ResultFileName)));
        Assert.False(File.Exists(Path.Combine(folder, MotionPaths.OutputFileName)));
        Assert.Empty(Directory.GetFiles(folder, "*.mp4", SearchOption.AllDirectories));
        Assert.Equal(MotionPaths.ManifestRelative(renderId), result.ManifestRelativePath);
    }

    [Fact]
    public async Task SameKeyAndSameRequest_ReplaysStoredResult_WithoutRendering()
    {
        var renderer = new CountingMotionRenderer("mock", (_, ctx) => Task.FromResult(Simulated(ctx)));
        var useCases = new MotionUseCases(renderer, MotionTestLocks.Shared);
        var request = TempMotionProject.ValidRequest();

        var first = await useCases.RenderAsync(request, _project.Root);
        var second = await useCases.RenderAsync(request, _project.Root);

        Assert.Equal(1, renderer.RenderCount);
        Assert.Equal(first.Status, second.Status);
        Assert.Equal(first.ManifestRelativePath, second.ManifestRelativePath);
    }

    [Fact]
    public async Task SameKeyDifferentContent_IsConflict_AndDoesNotRender()
    {
        var renderer = new CountingMotionRenderer("mock", (_, ctx) => Task.FromResult(Simulated(ctx)));
        var useCases = new MotionUseCases(renderer, MotionTestLocks.Shared);

        await useCases.RenderAsync(TempMotionProject.ValidRequest(), _project.Root);
        var conflict = await useCases.RenderAsync(
            TempMotionProject.ValidRequest(parameters: new Dictionary<string, string> { ["title"] = "Outro título" }),
            _project.Root);

        Assert.Equal(MotionRenderStatuses.Error, conflict.Status);
        Assert.Equal(MotionUseCases.IdempotencyKeyReusedCode, conflict.ErrorCode);
        Assert.Equal(1, renderer.RenderCount);
    }

    [Fact]
    public async Task PreviousErrorResult_IsNotReplayed_AndRenderIsRetried()
    {
        int calls = 0;
        var renderer = new CountingMotionRenderer("hyperframes", (_, ctx) =>
        {
            calls++;
            return Task.FromResult(new MotionRenderResultDto(
                MotionRenderStatuses.Error, false, null, MotionPaths.ManifestRelative(ctx.RenderId),
                "RENDER_TIMEOUT", "Tempo limite do render excedido.", DateTime.UtcNow, DateTime.UtcNow));
        });
        var useCases = new MotionUseCases(renderer, MotionTestLocks.Shared);
        var request = TempMotionProject.ValidRequest();

        var first = await useCases.RenderAsync(request, _project.Root);
        var second = await useCases.RenderAsync(request, _project.Root);

        Assert.Equal("RENDER_TIMEOUT", first.ErrorCode);
        Assert.Equal("RENDER_TIMEOUT", second.ErrorCode);
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task ReadyRealResult_IsReplayed_WithoutSecondRender()
    {
        var runner = new ScriptedProcessRunner { Render = ScriptedProcessRunner.WriteOutputFile };
        var renderer = new HyperFramesRenderer(runner,
            new HyperFramesOptions(_project.AddFakeCli(), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5), _project.AddFakeNode()));
        var useCases = new MotionUseCases(renderer, MotionTestLocks.Shared);
        var request = TempMotionProject.ValidRequest();

        var first = await useCases.RenderAsync(request, _project.Root);
        var second = await useCases.RenderAsync(request, _project.Root);

        Assert.Equal(MotionRenderStatuses.ReadyReal, first.Status);
        Assert.Equal(MotionRenderStatuses.ReadyReal, second.Status);
        Assert.Single(runner.RenderCalls);
    }

    [Fact]
    public async Task ConcurrentSameKey_RendersOnlyOnce()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var renderer = new CountingMotionRenderer("mock", async (_, ctx) =>
        {
            await gate.Task;
            return Simulated(ctx);
        });
        var useCases = new MotionUseCases(renderer, MotionTestLocks.Shared);
        var request = TempMotionProject.ValidRequest();

        var t1 = useCases.RenderAsync(request, _project.Root);
        var t2 = useCases.RenderAsync(request, _project.Root);
        await Task.Delay(50);
        gate.SetResult();

        var results = await Task.WhenAll(t1, t2);

        Assert.All(results, r => Assert.Equal(MotionRenderStatuses.Simulated, r.Status));
        Assert.Equal(1, renderer.RenderCount);
    }

    [Fact]
    public async Task RendererException_IsReportedWithoutPaths()
    {
        var renderer = new CountingMotionRenderer("mock", (_, _) => throw new InvalidOperationException("falha em " + _project.Root));
        var useCases = new MotionUseCases(renderer, MotionTestLocks.Shared);

        var result = await useCases.RenderAsync(TempMotionProject.ValidRequest(), _project.Root);

        Assert.Equal(MotionRenderStatuses.Error, result.Status);
        Assert.Equal(MotionUseCases.RenderInternalErrorCode, result.ErrorCode);
        Assert.DoesNotContain(_project.Root, result.ErrorMessage);
    }

    [Fact]
    public async Task ManifestAndResultJson_ContainOnlyRelativeAssetPaths()
    {
        _project.AddAsset("marca/fundo.png");
        var mock = new MockMotionRenderer();
        var useCases = new MotionUseCases(mock, MotionTestLocks.Shared);
        var request = TempMotionProject.ValidRequest(parameters: new Dictionary<string, string>
        {
            ["title"] = "Veneza",
            ["backgroundAsset"] = "marca/fundo.png",
        });

        var result = await useCases.RenderAsync(request, _project.Root);

        string renderId = MotionPaths.RenderIdFor(request.ProjectId, request.IdempotencyKey);
        string manifest = File.ReadAllText(Path.Combine(_project.WorkRoot, renderId, MotionPaths.RequestFileName));
        string resultJson = File.ReadAllText(Path.Combine(_project.WorkRoot, renderId, MotionPaths.ResultFileName));

        Assert.Contains("marca/fundo.png", manifest);
        Assert.DoesNotContain(_project.Root.Replace('\\', '/'), manifest);
        Assert.DoesNotContain(_project.Root, manifest);
        Assert.DoesNotContain(_project.Root, resultJson);
        Assert.Equal(MotionRenderStatuses.Simulated, result.Status);
    }

    [Fact]
    public async Task StoredManifest_IsValidJson_WithSchemaVersion()
    {
        var useCases = new MotionUseCases(new MockMotionRenderer(), MotionTestLocks.Shared);
        var request = TempMotionProject.ValidRequest();

        await useCases.RenderAsync(request, _project.Root);

        string renderId = MotionPaths.RenderIdFor(request.ProjectId, request.IdempotencyKey);
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(_project.WorkRoot, renderId, MotionPaths.RequestFileName)));
        Assert.Equal(MotionSchemaVersions.Request, doc.RootElement.GetProperty("SchemaVersion").GetString());
        Assert.Equal(renderId, doc.RootElement.GetProperty("RenderId").GetString());
    }

    [Fact]
    public void RenderId_IsDeterministic_AndScopedByProject()
    {
        var p1 = Guid.Parse("11111111-2222-3333-4444-555555555555");
        var p2 = Guid.Parse("66666666-7777-8888-9999-000000000000");

        Assert.Equal(MotionPaths.RenderIdFor(p1, "chave-123456"), MotionPaths.RenderIdFor(p1, "chave-123456"));
        Assert.NotEqual(MotionPaths.RenderIdFor(p1, "chave-123456"), MotionPaths.RenderIdFor(p2, "chave-123456"));
        Assert.True(MotionPaths.IsValidRenderId(MotionPaths.RenderIdFor(p1, "chave-123456")));
    }

    [Fact]
    public void RequestHash_IgnoresParameterOrder_ButDetectsContentChange()
    {
        var a = TempMotionProject.ValidRequest(parameters: new Dictionary<string, string> { ["title"] = "A", ["subtitle"] = "B" });
        var b = TempMotionProject.ValidRequest(parameters: new Dictionary<string, string> { ["subtitle"] = "B", ["title"] = "A" });
        var c = TempMotionProject.ValidRequest(parameters: new Dictionary<string, string> { ["title"] = "A", ["subtitle"] = "C" });

        Assert.Equal(MotionUseCases.ComputeRequestHash(a), MotionUseCases.ComputeRequestHash(b));
        Assert.NotEqual(MotionUseCases.ComputeRequestHash(a), MotionUseCases.ComputeRequestHash(c));
    }

    [Fact]
    public void Context_IsConfinedTo_3TrabalhoMotion()
    {
        string renderId = MotionPaths.RenderIdFor(Guid.NewGuid(), "chave-123456");

        Assert.True(MotionPaths.TryCreateContext(_project.Root, renderId, out var ctx));
        Assert.NotNull(ctx);
        Assert.True(MotionPaths.IsInside(_project.WorkRoot, ctx!.OutputDirAbsolute));
        Assert.Equal(Path.Combine(_project.Root, "2-Assets"), ctx.AssetsRoot);
        Assert.StartsWith(_project.WorkRoot, ctx.ManifestAbsolutePath, StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith(_project.WorkRoot, ctx.OutputFileAbsolute, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("../../etc")]
    [InlineData("mr-../../x")]
    [InlineData("qualquer")]
    public void Context_RejectsInvalidRenderId(string renderId)
    {
        Assert.False(MotionPaths.TryCreateContext(_project.Root, renderId, out _));
    }

    [Theory]
    [InlineData("pasta/fundo.png", true)]
    [InlineData("pasta/../../fora.png", false)]
    [InlineData("..\\1-Bruto\\x.png", false)]
    [InlineData("C:x.png", false)]
    [InlineData("", false)]
    [InlineData("./x.png", false)]
    public void AssetPath_IsConfinedTo_2Assets(string relative, bool expected)
    {
        Assert.Equal(expected, MotionPaths.TryResolveAssetPath(_project.Root, relative, out _));
    }

    [Fact]
    public async Task ConcurrentDifferentKeys_AreSerialized()
    {
        int active = 0;
        int maxActive = 0;
        var renderer = new CountingMotionRenderer("mock", async (_, ctx) =>
        {
            int now = Interlocked.Increment(ref active);
            maxActive = Math.Max(maxActive, now);
            await Task.Delay(20);
            Interlocked.Decrement(ref active);
            return Simulated(ctx);
        });
        var useCases = new MotionUseCases(renderer, MotionTestLocks.Shared);

        var tasks = Enumerable.Range(0, 4)
            .Select(i => useCases.RenderAsync(TempMotionProject.ValidRequest(idempotencyKey: "chave-num-" + i), _project.Root))
            .ToArray();
        var results = await Task.WhenAll(tasks);

        Assert.All(results, r => Assert.Equal(MotionRenderStatuses.Simulated, r.Status));
        Assert.Equal(1, maxActive);
        Assert.Equal(4, renderer.RenderCount);
    }
}
