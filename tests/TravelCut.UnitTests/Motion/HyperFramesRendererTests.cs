using TravelCut.Application.Motion;
using TravelCut.Application.Ports;
using TravelCut.Contracts.Dtos;
using TravelCut.Infrastructure.Motion;
using Xunit;

namespace TravelCut.Tests.Motion;

/// <summary>Detecção do HyperFrames com runner falso: nenhum node, CLI ou HyperFrames real é executado.</summary>
public class HyperFramesDetectionTests : IDisposable
{
    private readonly TempMotionProject _project = new();

    public void Dispose() => _project.Dispose();

    // Node sempre configurado com caminho absoluto fictício: o teste nunca depende do Node instalado na máquina.
    private HyperFramesOptions Options(string? cli) =>
        new(cli, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5), _project.AddFakeNode());

    [Fact]
    public async Task NodeMissing_ReturnsNodeUnavailable_AndDoesNotProbeCli()
    {
        var runner = new ScriptedProcessRunner { Node = _ => ScriptedProcessRunner.Status(ProcessRunStatus.NotStarted) };
        var renderer = new HyperFramesRenderer(runner, Options(_project.AddFakeCli()));

        var caps = await renderer.DetectCapabilitiesAsync();

        Assert.Equal(MotionEnvironmentStates.NodeUnavailable, caps.EnvironmentState);
        Assert.Equal("NODE_NOT_FOUND", caps.ReasonCode);
        Assert.False(caps.IsAvailable);
        Assert.False(caps.IsSimulated);
        Assert.Null(caps.NodeVersion);
        Assert.Null(caps.RendererVersion);
        Assert.Single(runner.Calls);
        Assert.All(caps.Templates, t => Assert.False(t.Supported));
    }

    [Fact]
    public async Task NodeCheckExitNonZero_ReturnsNodeUnavailable()
    {
        var runner = new ScriptedProcessRunner { Node = _ => ScriptedProcessRunner.Ok("", "erro", exitCode: 1) };
        var caps = await new HyperFramesRenderer(runner, Options(_project.AddFakeCli())).DetectCapabilitiesAsync();

        Assert.Equal(MotionEnvironmentStates.NodeUnavailable, caps.EnvironmentState);
        Assert.Equal("NODE_CHECK_FAILED", caps.ReasonCode);
    }

    [Fact]
    public async Task NodeVersionUnparseable_ReturnsNodeUnavailable()
    {
        var runner = new ScriptedProcessRunner { Node = _ => ScriptedProcessRunner.Ok("node sem versao clara") };
        var caps = await new HyperFramesRenderer(runner, Options(_project.AddFakeCli())).DetectCapabilitiesAsync();

        Assert.Equal("NODE_VERSION_UNPARSEABLE", caps.ReasonCode);
    }

    [Fact]
    public async Task CliNotConfigured_ReturnsNotInstalled_WithNodeVersionRead()
    {
        var runner = new ScriptedProcessRunner();
        var caps = await new HyperFramesRenderer(runner, Options(null)).DetectCapabilitiesAsync();

        Assert.Equal(MotionEnvironmentStates.NotInstalled, caps.EnvironmentState);
        Assert.Equal("CLI_NOT_CONFIGURED", caps.ReasonCode);
        Assert.Equal("24.11.0", caps.NodeVersion);
        Assert.Null(caps.RendererVersion);
        Assert.Single(runner.Calls);
        Assert.Contains(HyperFramesOptions.CliEnvironmentVariable, caps.StatusMessage);
    }

    [Fact]
    public async Task CliPathMissingOnDisk_ReturnsNotInstalled_WithoutAbsolutePathInMessage()
    {
        string missing = Path.Combine(_project.Root, "nao-existe", "hf.exe");
        var runner = new ScriptedProcessRunner();
        var caps = await new HyperFramesRenderer(runner, Options(missing)).DetectCapabilitiesAsync();

        Assert.Equal(MotionEnvironmentStates.NotInstalled, caps.EnvironmentState);
        Assert.Equal("CLI_PATH_NOT_FOUND", caps.ReasonCode);
        Assert.DoesNotContain(_project.Root, caps.StatusMessage);
        Assert.Single(runner.Calls);
    }

    [Fact]
    public async Task RelativeCliPath_IsNotAccepted()
    {
        var runner = new ScriptedProcessRunner();
        var caps = await new HyperFramesRenderer(runner, Options("hyperframes.exe")).DetectCapabilitiesAsync();

        Assert.Equal("CLI_PATH_NOT_FOUND", caps.ReasonCode);
        Assert.Single(runner.Calls);
    }

    [Fact]
    public async Task CliPresent_VersionRead_ReturnsReady_WithHostPendingTemplates()
    {
        string cli = _project.AddFakeCli();
        var runner = new ScriptedProcessRunner();
        var caps = await new HyperFramesRenderer(runner, Options(cli)).DetectCapabilitiesAsync();

        Assert.Equal(MotionSchemaVersions.Capabilities, caps.SchemaVersion);
        Assert.Equal("hyperframes", caps.AdapterMode);
        Assert.Equal(MotionEnvironmentStates.Ready, caps.EnvironmentState);
        Assert.True(caps.IsAvailable);
        Assert.False(caps.IsSimulated);
        Assert.Equal("24.11.0", caps.NodeVersion);
        Assert.Equal("1.4.2", caps.RendererVersion);
        Assert.Null(caps.ReasonCode);

        var versionCall = runner.Calls.Single(c => c.Arguments.Contains("--version") && c.FileName == cli);
        Assert.Equal(new[] { "--version" }, versionCall.Arguments);
        Assert.True(versionCall.Timeout > TimeSpan.Zero);

        Assert.Equal(MotionTemplateCatalog.Templates.Count, caps.Templates.Count);
        Assert.All(caps.Templates, t =>
        {
            Assert.True(t.Supported);
            Assert.Equal(CapabilityVerification.HostPending, t.Verification);
        });
    }

    [Fact]
    public async Task CliVersionExitNonZero_ReturnsError()
    {
        var runner = new ScriptedProcessRunner { CliVersion = _ => ScriptedProcessRunner.Ok("", "falha", exitCode: 2) };
        var caps = await new HyperFramesRenderer(runner, Options(_project.AddFakeCli())).DetectCapabilitiesAsync();

        Assert.Equal(MotionEnvironmentStates.Error, caps.EnvironmentState);
        Assert.Equal("CLI_VERSION_FAILED", caps.ReasonCode);
        Assert.False(caps.IsAvailable);
        Assert.Null(caps.RendererVersion);
    }

    [Fact]
    public async Task CliVersionUnparseable_ReturnsErrorAndDoesNotInventVersion()
    {
        var runner = new ScriptedProcessRunner { CliVersion = _ => ScriptedProcessRunner.Ok("bem-vindo ao CLI\nsem versao") };
        var caps = await new HyperFramesRenderer(runner, Options(_project.AddFakeCli())).DetectCapabilitiesAsync();

        Assert.Equal(MotionEnvironmentStates.Error, caps.EnvironmentState);
        Assert.Equal("CLI_VERSION_UNPARSEABLE", caps.ReasonCode);
        Assert.Null(caps.RendererVersion);
    }

    [Fact]
    public async Task CliVersionTimeout_ReturnsError()
    {
        var runner = new ScriptedProcessRunner { CliVersion = _ => ScriptedProcessRunner.Status(ProcessRunStatus.TimedOut) };
        var caps = await new HyperFramesRenderer(runner, Options(_project.AddFakeCli())).DetectCapabilitiesAsync();

        Assert.Equal("CLI_VERSION_TIMEOUT", caps.ReasonCode);
        Assert.Equal(MotionEnvironmentStates.Error, caps.EnvironmentState);
    }

    [Fact]
    public async Task ScriptCli_RunsThroughNode_WithoutShell()
    {
        string script = Path.Combine(_project.Root, "tools", "hf.mjs");
        Directory.CreateDirectory(Path.GetDirectoryName(script)!);
        File.WriteAllText(script, "// fake");
        var runner = new ScriptedProcessRunner();

        var caps = await new HyperFramesRenderer(runner, Options(script)).DetectCapabilitiesAsync();

        Assert.Equal(MotionEnvironmentStates.Ready, caps.EnvironmentState);
        var call = runner.Calls.Last();
        Assert.Equal(_project.AddFakeNode(), call.FileName);
        Assert.True(Path.IsPathFullyQualified(call.FileName));
        Assert.Equal(new[] { script, "--version" }, call.Arguments);
    }

    [Fact]
    public async Task NodePathConfiguredButMissing_ReturnsNodePathNotFound_WithoutRunning()
    {
        string missing = Path.Combine(_project.Root, "nao-existe", "node.exe");
        var runner = new ScriptedProcessRunner();
        var options = new HyperFramesOptions(_project.AddFakeCli(), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5), missing);

        var caps = await new HyperFramesRenderer(runner, options).DetectCapabilitiesAsync();

        Assert.Equal(MotionEnvironmentStates.NodeUnavailable, caps.EnvironmentState);
        Assert.Equal(HyperFramesRenderer.NodePathNotFoundCode, caps.ReasonCode);
        Assert.Empty(runner.Calls);
        Assert.DoesNotContain(_project.Root, caps.StatusMessage);
    }

    [Fact]
    public async Task NodePathRelative_IsNotAccepted_WithoutRunning()
    {
        var runner = new ScriptedProcessRunner();
        var options = new HyperFramesOptions(_project.AddFakeCli(), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5), "node.exe");

        var caps = await new HyperFramesRenderer(runner, options).DetectCapabilitiesAsync();

        Assert.Equal(HyperFramesRenderer.NodePathNotFoundCode, caps.ReasonCode);
        Assert.Empty(runner.Calls);
    }

    [Fact]
    public async Task NodeWithAmpersandInPath_IsRunnedWithExactPath_WithoutShell()
    {
        string node = _project.AddFakeNode("node & dir (x86)");
        var runner = new ScriptedProcessRunner();
        var options = new HyperFramesOptions(_project.AddFakeCli(), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5), node);

        var caps = await new HyperFramesRenderer(runner, options).DetectCapabilitiesAsync();

        Assert.Equal(MotionEnvironmentStates.Ready, caps.EnvironmentState);
        Assert.Equal(node, runner.Calls.First().FileName);
        Assert.Equal(new[] { "--version" }, runner.Calls.First().Arguments);
    }

    [Fact]
    public async Task CliWithAmpersandInPath_IsPassedIntact_WithoutShell()
    {
        string cli = _project.AddFakeCli("hf.exe", "tools & extras");
        var runner = new ScriptedProcessRunner();

        var caps = await new HyperFramesRenderer(runner, Options(cli)).DetectCapabilitiesAsync();

        Assert.Equal(MotionEnvironmentStates.Ready, caps.EnvironmentState);
        var versionCall = runner.Calls.Single(c => c.FileName == cli);
        Assert.Equal(new[] { "--version" }, versionCall.Arguments);
    }

    [Fact]
    public async Task ScriptCliWithAmpersandInPath_RunsThroughNodeWithExactPaths()
    {
        string script = _project.AddFakeCli("hf.mjs", "scripts & more");
        var runner = new ScriptedProcessRunner();

        var caps = await new HyperFramesRenderer(runner, Options(script)).DetectCapabilitiesAsync();

        Assert.Equal(MotionEnvironmentStates.Ready, caps.EnvironmentState);
        var versionCall = runner.Calls.Last();
        Assert.Equal(_project.AddFakeNode(), versionCall.FileName);
        Assert.Equal(new[] { script, "--version" }, versionCall.Arguments);
    }

    [Theory]
    [InlineData("hf.cmd")]
    [InlineData("hf.bat")]
    [InlineData("hf.PS1")]
    public async Task ShellWrapperCli_IsRefused_WithUnsupportedType(string name)
    {
        string wrapper = _project.AddFakeCli(name);
        var runner = new ScriptedProcessRunner();

        var caps = await new HyperFramesRenderer(runner, Options(wrapper)).DetectCapabilitiesAsync();

        Assert.Equal(MotionEnvironmentStates.Error, caps.EnvironmentState);
        Assert.Equal("CLI_UNSUPPORTED_TYPE", caps.ReasonCode);
        Assert.False(caps.IsAvailable);
        Assert.Null(caps.RendererVersion);
        Assert.Contains("Aponte", caps.StatusMessage);
        Assert.Contains(HyperFramesOptions.CliEnvironmentVariable, caps.StatusMessage);
        Assert.DoesNotContain(_project.Root, caps.StatusMessage);
        // Só o node --version roda; o script de shell nunca é executado.
        Assert.Single(runner.Calls);
        Assert.DoesNotContain(runner.Calls, c => c.FileName == wrapper);
    }

    [Fact]
    public async Task ShellWrapperCli_IsRefusedForRender_BeforeAnyProcess()
    {
        string wrapper = _project.AddFakeCli("hf.cmd");
        var runner = new ScriptedProcessRunner();
        var renderer = new HyperFramesRenderer(runner, Options(wrapper));
        var request = TempMotionProject.ValidRequest();
        Assert.True(MotionPaths.TryCreateContext(_project.Root, MotionPaths.RenderIdFor(request.ProjectId, request.IdempotencyKey), out var ctx));
        Directory.CreateDirectory(ctx!.OutputDirAbsolute);
        File.WriteAllText(ctx.ManifestAbsolutePath, "{}");

        var result = await renderer.RenderAsync(request, ctx);

        Assert.Equal("CLI_UNSUPPORTED_TYPE", result.ErrorCode);
        Assert.Empty(runner.RenderCalls);
        Assert.False(File.Exists(ctx.OutputFileAbsolute));
    }

    [Fact]
    public void ResolveNodePath_IgnoresRelativeEntries_AndCurrentDirectory()
    {
        string dir = Path.Combine(_project.Root, "node & dir");
        string pathValue = string.Join(Path.PathSeparator, ".", "", "relativo", dir);

        // Qualquer arquivo "existe": a busca precisa pular "." e "relativo" e parar em dir absoluto.
        string? found = HyperFramesRenderer.ResolveNodePath(null, pathValue, _ => true);

        Assert.Equal(Path.Combine(dir, HyperFramesRenderer.NodeFileName), found);
        Assert.True(Path.IsPathFullyQualified(found!));
    }

    [Fact]
    public void ResolveNodePath_WithOnlyRelativeEntries_ReturnsNull()
    {
        string? found = HyperFramesRenderer.ResolveNodePath(null, string.Join(Path.PathSeparator, ".", "bin", ""), _ => true);

        Assert.Null(found);
    }

    [Fact]
    public void ResolveNodePath_ConfiguredPath_MustBeAbsoluteExistingFile()
    {
        string absolute = Path.Combine(_project.Root, "node.exe");

        Assert.Equal(absolute, HyperFramesRenderer.ResolveNodePath(absolute, "", p => p == absolute));
        Assert.Null(HyperFramesRenderer.ResolveNodePath(absolute, "", _ => false));
        Assert.Null(HyperFramesRenderer.ResolveNodePath("node.exe", Path.GetTempPath(), _ => true));
    }

    [Fact]
    public void FindExecutableOnPath_NullOrEmptyPath_ReturnsNull()
    {
        Assert.Null(HyperFramesRenderer.FindExecutableOnPath("node.exe", null, _ => true));
        Assert.Null(HyperFramesRenderer.FindExecutableOnPath("node.exe", "   ", _ => true));
    }

    [Fact]
    public async Task DetectionMessages_NeverContainAbsolutePaths()
    {
        string cli = _project.AddFakeCli();
        var runner = new ScriptedProcessRunner { CliVersion = _ => ScriptedProcessRunner.Ok("", $"erro em {cli}", exitCode: 3) };
        var caps = await new HyperFramesRenderer(runner, Options(cli)).DetectCapabilitiesAsync();

        Assert.DoesNotContain(_project.Root, caps.StatusMessage);
        Assert.DoesNotContain(cli, caps.StatusMessage);
    }

    [Fact]
    public async Task Cancellation_PropagatesAsOperationCanceled()
    {
        var runner = new ScriptedProcessRunner();
        var renderer = new HyperFramesRenderer(runner, Options(_project.AddFakeCli()));
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => renderer.DetectCapabilitiesAsync(cts.Token));
    }
}

/// <summary>Render real do HyperFrames com runner falso: sucesso só com arquivo existente, erros explícitos.</summary>
public class HyperFramesRenderTests : IDisposable
{
    private readonly TempMotionProject _project = new();

    public void Dispose() => _project.Dispose();

    private HyperFramesOptions Options(string cli) =>
        new(cli, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(30), _project.AddFakeNode());

    private async Task<(MotionRenderResultDto Result, MotionRenderContext Context, ScriptedProcessRunner Runner)> RunAsync(
        Func<ProcessRunRequest, ProcessRunResult> render,
        bool writeManifest = true,
        string? cli = null)
    {
        string cliPath = cli ?? _project.AddFakeCli();
        var runner = new ScriptedProcessRunner { Render = render };
        var renderer = new HyperFramesRenderer(runner, Options(cliPath));
        var request = TempMotionProject.ValidRequest();

        Assert.True(MotionPaths.TryCreateContext(_project.Root, MotionPaths.RenderIdFor(request.ProjectId, request.IdempotencyKey), out var ctx));
        Directory.CreateDirectory(ctx!.OutputDirAbsolute);
        if (writeManifest)
            File.WriteAllText(ctx.ManifestAbsolutePath, "{}");

        var result = await renderer.RenderAsync(request, ctx);
        return (result, ctx, runner);
    }

    [Fact]
    public async Task CliWritesOutput_WithExitZero_ReturnsReadyReal()
    {
        var (result, ctx, runner) = await RunAsync(ScriptedProcessRunner.WriteOutputFile);

        Assert.Equal(MotionRenderStatuses.ReadyReal, result.Status);
        Assert.False(result.IsSimulated);
        Assert.Null(result.ErrorCode);
        Assert.Equal(MotionPaths.OutputRelative(ctx.RenderId), result.OutputRelativePath);
        Assert.True(File.Exists(ctx.OutputFileAbsolute));
        Assert.Single(runner.RenderCalls);
    }

    [Fact]
    public async Task ExitZeroWithoutOutputFile_IsError_NotSuccess()
    {
        var (result, ctx, _) = await RunAsync(_ => ScriptedProcessRunner.Ok("feito"));

        Assert.Equal(MotionRenderStatuses.Error, result.Status);
        Assert.Equal("OUTPUT_MISSING", result.ErrorCode);
        Assert.Null(result.OutputRelativePath);
        Assert.False(File.Exists(ctx.OutputFileAbsolute));
    }

    [Fact]
    public async Task ExitZeroWithEmptyOutputFile_IsError()
    {
        var (result, ctx, _) = await RunAsync(r =>
        {
            File.WriteAllBytes(ScriptedProcessRunner.ArgAfter(r, "--output"), Array.Empty<byte>());
            return ScriptedProcessRunner.Ok("");
        });

        Assert.Equal("OUTPUT_MISSING", result.ErrorCode);
        Assert.Null(result.OutputRelativePath);
    }

    [Fact]
    public async Task NonZeroExit_IsRenderFailed_WithSanitizedStderr()
    {
        var (result, _, _) = await RunAsync(_ => ScriptedProcessRunner.Ok("",
            $"Erro ao ler {_project.Root} e C:\\Users\\usuario\\secreto.png", exitCode: 4));

        Assert.Equal(MotionRenderStatuses.Error, result.Status);
        Assert.Equal("RENDER_FAILED", result.ErrorCode);
        Assert.Contains("código 4", result.ErrorMessage);
        Assert.DoesNotContain("C:\\Users", result.ErrorMessage);
        Assert.DoesNotContain(_project.Root, result.ErrorMessage);
        Assert.Null(result.OutputRelativePath);
    }

    [Fact]
    public async Task RenderTimeout_IsError()
    {
        var (result, _, _) = await RunAsync(_ => ScriptedProcessRunner.Status(ProcessRunStatus.TimedOut));

        Assert.Equal(MotionRenderStatuses.Error, result.Status);
        Assert.Equal("RENDER_TIMEOUT", result.ErrorCode);
        Assert.Null(result.OutputRelativePath);
    }

    [Fact]
    public async Task RenderCanceled_IsError()
    {
        var (result, _, _) = await RunAsync(_ => ScriptedProcessRunner.Status(ProcessRunStatus.Canceled));

        Assert.Equal("RENDER_CANCELED", result.ErrorCode);
    }

    [Fact]
    public async Task CliNotStartable_DuringRender_IsError()
    {
        var (result, _, _) = await RunAsync(_ => ScriptedProcessRunner.Status(ProcessRunStatus.NotStarted));

        Assert.Equal("CLI_NOT_EXECUTABLE", result.ErrorCode);
    }

    [Fact]
    public async Task StaleOutputFromEarlierRun_IsRemoved_AndNotReportedAsSuccess()
    {
        string cli = _project.AddFakeCli();
        var runner = new ScriptedProcessRunner { Render = _ => ScriptedProcessRunner.Ok("", "falha", exitCode: 1) };
        var renderer = new HyperFramesRenderer(runner, Options(cli));
        var request = TempMotionProject.ValidRequest();
        Assert.True(MotionPaths.TryCreateContext(_project.Root, MotionPaths.RenderIdFor(request.ProjectId, request.IdempotencyKey), out var ctx));
        Directory.CreateDirectory(ctx!.OutputDirAbsolute);
        File.WriteAllText(ctx.ManifestAbsolutePath, "{}");
        File.WriteAllBytes(ctx.OutputFileAbsolute, new byte[] { 9, 9, 9 });

        var result = await renderer.RenderAsync(request, ctx);

        Assert.Equal(MotionRenderStatuses.Error, result.Status);
        Assert.False(File.Exists(ctx.OutputFileAbsolute));
    }

    [Fact]
    public async Task RenderArguments_UseArgumentListAndConfinedPaths()
    {
        var (_, ctx, runner) = await RunAsync(ScriptedProcessRunner.WriteOutputFile);

        var call = runner.RenderCalls.Single();
        Assert.Equal(new[]
        {
            "render",
            "--manifest", ctx.ManifestAbsolutePath,
            "--assets-root", ctx.AssetsRoot,
            "--output", ctx.OutputFileAbsolute,
        }, call.Arguments);

        Assert.True(MotionPaths.IsInside(_project.WorkRoot, ctx.ManifestAbsolutePath));
        Assert.True(MotionPaths.IsInside(_project.WorkRoot, ctx.OutputFileAbsolute));
        Assert.Equal(_project.AssetsRoot, ctx.AssetsRoot);
    }

    [Fact]
    public async Task ContextOutsideWorkFolder_IsRejected_BeforeAnyProcess()
    {
        string cli = _project.AddFakeCli();
        var runner = new ScriptedProcessRunner();
        var renderer = new HyperFramesRenderer(runner, Options(cli));
        var request = TempMotionProject.ValidRequest();
        var bad = new MotionRenderContext(
            _project.Root,
            "mr-00000000000000000000000000000000",
            Path.Combine(_project.Root, "1-Bruto"),
            _project.AssetsRoot);
        File.WriteAllText(bad.ManifestAbsolutePath, "{}");

        var result = await renderer.RenderAsync(request, bad);

        Assert.Equal("CONTEXT_INVALID", result.ErrorCode);
        Assert.Empty(runner.RenderCalls);
    }

    [Fact]
    public async Task NodeMissing_DuringRender_IsUnavailable_WithoutRendering()
    {
        var runner = new ScriptedProcessRunner { Node = _ => ScriptedProcessRunner.Status(ProcessRunStatus.NotStarted) };
        var renderer = new HyperFramesRenderer(runner, Options(_project.AddFakeCli()));
        var request = TempMotionProject.ValidRequest();
        Assert.True(MotionPaths.TryCreateContext(_project.Root, MotionPaths.RenderIdFor(request.ProjectId, request.IdempotencyKey), out var ctx));
        Directory.CreateDirectory(ctx!.OutputDirAbsolute);
        File.WriteAllText(ctx.ManifestAbsolutePath, "{}");

        var result = await renderer.RenderAsync(request, ctx);

        Assert.Equal(MotionRenderStatuses.Unavailable, result.Status);
        Assert.Equal("NODE_NOT_FOUND", result.ErrorCode);
        Assert.Empty(runner.RenderCalls);
        Assert.Null(result.OutputRelativePath);
    }

    [Fact]
    public async Task RenderErrorMessages_NeverContainProjectRoot()
    {
        var (result, _, _) = await RunAsync(_ => ScriptedProcessRunner.Ok("", $"falha em {_project.Root}\\x", exitCode: 5));

        Assert.DoesNotContain(_project.Root, result.ErrorMessage);
    }

    [Fact]
    public void SanitizeFirstLine_HidesAbsolutePathsAndTruncates()
    {
        string line = HyperFramesRenderer.SanitizeFirstLine("erro em C:\\a\\b.png e /home/u/x/y.mp4\nsegunda linha");

        Assert.DoesNotContain("C:\\", line);
        Assert.DoesNotContain("/home/u", line);
        Assert.Contains("<caminho>", line);
        Assert.DoesNotContain("segunda linha", line);
        Assert.True(HyperFramesRenderer.SanitizeFirstLine(new string('x', 500)).Length <= 160);
    }

    [Theory]
    [InlineData("hyperframes 1.4.2", "1.4.2")]
    [InlineData("v2.0.0-beta.1", "2.0.0-beta.1")]
    [InlineData("0.9.12", "0.9.12")]
    public void ParseCliVersion_AcceptsKnownShapes(string output, string expected)
    {
        Assert.Equal(expected, HyperFramesRenderer.ParseCliVersion(output));
    }

    [Theory]
    [InlineData("")]
    [InlineData("versão desconhecida")]
    [InlineData("1.2")]
    public void ParseCliVersion_RejectsOtherOutputs(string output)
    {
        Assert.Null(HyperFramesRenderer.ParseCliVersion(output));
    }
}
