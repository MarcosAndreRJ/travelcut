using TravelCut.Application.Motion;
using TravelCut.Contracts.Dtos;
using TravelCut.Infrastructure.Motion;
using Xunit;

namespace TravelCut.Tests.Motion;

/// <summary>
/// ALTO-1: link ou junção em qualquer segmento entre a raiz do projeto e o asset ou a pasta de saída bloqueia o pedido
/// (PERMISSION_DENIED) antes de qualquer escrita. Usa junções reais (mklink /J, sem elevação) via <see cref="TestJunction"/>.
/// </summary>
public class MotionReparsePointTests : IDisposable
{
    private readonly TempMotionProject _project = new();
    private readonly string _external = Path.Combine(Path.GetTempPath(), "tc-motion-ext-" + Guid.NewGuid().ToString("N"));
    private readonly List<string> _junctions = new();

    public MotionReparsePointTests()
    {
        Directory.CreateDirectory(_external);
    }

    public void Dispose()
    {
        // Junções primeiro: remove só o link, sem tocar no conteúdo externo.
        foreach (string link in _junctions)
            TestJunction.Remove(link);

        _project.Dispose();

        if (Directory.Exists(_external))
            Directory.Delete(_external, recursive: true);
    }

    private void Junction(string link, string target)
    {
        TestJunction.Create(link, target);
        _junctions.Add(link);
    }

    private void ExternalPng(string name) =>
        File.WriteAllBytes(Path.Combine(_external, name), new byte[] { 0x89, 0x50, 0x4E, 0x47 });

    private bool ExternalIsEmpty() => Directory.GetFileSystemEntries(_external).Length == 0;

    private static MotionRenderRequestDto WithAsset(string asset, string idempotencyKey = "key-0001-abc") =>
        TempMotionProject.ValidRequest(
            parameters: new Dictionary<string, string> { ["title"] = "Veneza", ["backgroundAsset"] = asset },
            idempotencyKey: idempotencyKey);

    [Fact]
    public void Asset_InRealFolder_IsAccepted_ControlCase()
    {
        _project.AddAsset("marca/fundo.png");

        var validation = MotionRequestValidator.Validate(WithAsset("marca/fundo.png"), _project.Root);

        Assert.True(validation.IsValid, validation.Message);
        Assert.True(MotionPaths.TryResolveAsset(_project.Root, "marca/fundo.png", out _));
    }

    [Fact]
    public void Asset_ThroughJunctionDirectlyInAssets_IsPermissionDenied()
    {
        ExternalPng("foto.png");
        Junction(Path.Combine(_project.AssetsRoot, "linkext"), _external);

        var validation = MotionRequestValidator.Validate(WithAsset("linkext/foto.png"), _project.Root);

        Assert.False(validation.IsValid);
        Assert.Equal(MotionRequestValidator.PermissionDeniedCode, validation.Code);
        Assert.DoesNotContain(_external, validation.Message);
        Assert.DoesNotContain(_project.Root, validation.Message);
        Assert.False(MotionPaths.TryResolveAsset(_project.Root, "linkext/foto.png", out _));
    }

    [Fact]
    public void Asset_ThroughJunctionInMiddleOfPath_IsPermissionDenied()
    {
        _project.AddAsset("real/outro.png");
        ExternalPng("foto.png");
        Junction(Path.Combine(_project.AssetsRoot, "real", "linkext"), _external);

        var validation = MotionRequestValidator.Validate(WithAsset("real/linkext/foto.png"), _project.Root);

        Assert.Equal(MotionRequestValidator.PermissionDeniedCode, validation.Code);
        Assert.False(MotionPaths.TryResolveAsset(_project.Root, "real/linkext/foto.png", out _));
    }

    [Fact]
    public async Task Asset_ThroughJunction_RenderIsRefused_WithoutCreatingAnything()
    {
        ExternalPng("foto.png");
        Junction(Path.Combine(_project.AssetsRoot, "linkext"), _external);
        var renderer = new CountingMotionRenderer("mock", (_, _) => throw new InvalidOperationException("não deveria renderizar"));
        var useCases = new MotionUseCases(renderer, MotionTestLocks.Shared);

        var result = await useCases.RenderAsync(WithAsset("linkext/foto.png"), _project.Root);

        Assert.Equal(MotionRenderStatuses.Error, result.Status);
        Assert.Equal(MotionRequestValidator.PermissionDeniedCode, result.ErrorCode);
        Assert.Equal(string.Empty, result.ManifestRelativePath);
        Assert.Equal(0, renderer.RenderCount);
        Assert.False(Directory.Exists(_project.WorkRoot));
        Assert.Equal(new[] { Path.Combine(_external, "foto.png") }, Directory.GetFiles(_external));
    }

    [Fact]
    public async Task WorkFolder_MotionAsJunction_IsRefused_BeforeAnyWrite()
    {
        Junction(_project.WorkRoot, _external);
        var useCases = new MotionUseCases(new MockMotionRenderer(), MotionTestLocks.Shared);

        var result = await useCases.RenderAsync(TempMotionProject.ValidRequest(), _project.Root);

        Assert.Equal(MotionRenderStatuses.Error, result.Status);
        Assert.Equal(MotionRequestValidator.PermissionDeniedCode, result.ErrorCode);
        Assert.Equal(string.Empty, result.ManifestRelativePath);
        Assert.True(ExternalIsEmpty(), "nenhum manifesto ou pasta pode ser criado através da junção");
    }

    [Fact]
    public async Task RenderFolder_AsJunction_IsRefused_BeforeAnyWrite()
    {
        var request = TempMotionProject.ValidRequest();
        string renderId = MotionPaths.RenderIdFor(request.ProjectId, request.IdempotencyKey);
        Directory.CreateDirectory(_project.WorkRoot);
        Junction(Path.Combine(_project.WorkRoot, renderId), _external);
        var useCases = new MotionUseCases(new MockMotionRenderer(), MotionTestLocks.Shared);

        var result = await useCases.RenderAsync(request, _project.Root);

        Assert.Equal(MotionRequestValidator.PermissionDeniedCode, result.ErrorCode);
        Assert.True(ExternalIsEmpty());
    }

    [Fact]
    public void HasReparsePointBetween_PlainTree_IsFalse_ControlCase()
    {
        string renderId = MotionPaths.RenderIdFor(Guid.NewGuid(), "chave-123456");
        Directory.CreateDirectory(Path.Combine(_project.WorkRoot, renderId));

        Assert.False(MotionPaths.HasReparsePointBetween(_project.Root,
            Path.Combine(_project.WorkRoot, renderId, MotionPaths.OutputFileName)));
    }

    [Fact]
    public void HasReparsePointBetween_DetectsJunctionInWorkFolder_EvenWithMissingTarget()
    {
        Junction(_project.WorkRoot, _external);

        Assert.True(MotionPaths.HasReparsePointBetween(_project.Root,
            Path.Combine(_project.WorkRoot, "mr-00000000000000000000000000000000", MotionPaths.OutputFileName)));
    }

    [Fact]
    public async Task Renderer_OutputFolderThroughJunction_RefusesBeforeCreatingOrDeleting()
    {
        var request = TempMotionProject.ValidRequest();
        string renderId = MotionPaths.RenderIdFor(request.ProjectId, request.IdempotencyKey);
        Directory.CreateDirectory(_project.WorkRoot);
        Junction(Path.Combine(_project.WorkRoot, renderId), _external);
        // Um output.mp4 antigo do lado de fora NÃO pode ser apagado pelo renderer.
        string outsideOutput = Path.Combine(_external, MotionPaths.OutputFileName);
        File.WriteAllBytes(outsideOutput, new byte[] { 9, 9, 9 });

        Assert.True(MotionPaths.TryCreateContext(_project.Root, renderId, out var ctx));
        var runner = new ScriptedProcessRunner { Render = ScriptedProcessRunner.WriteOutputFile };
        var renderer = new HyperFramesRenderer(runner, new HyperFramesOptions(
            _project.AddFakeCli(), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5), _project.AddFakeNode()));

        var result = await renderer.RenderAsync(request, ctx!);

        Assert.Equal(MotionRenderStatuses.Error, result.Status);
        Assert.Equal(MotionRequestValidator.PermissionDeniedCode, result.ErrorCode);
        Assert.Empty(runner.RenderCalls);
        Assert.True(File.Exists(outsideOutput), "o arquivo externo não pode ser apagado");
        Assert.Equal(new byte[] { 9, 9, 9 }, File.ReadAllBytes(outsideOutput));
        Assert.Equal(new[] { outsideOutput }, Directory.GetFiles(_external));
    }
}
