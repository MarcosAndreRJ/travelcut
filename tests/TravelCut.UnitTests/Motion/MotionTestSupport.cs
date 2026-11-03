using TravelCut.Application.Motion;
using TravelCut.Application.Ports;
using TravelCut.Contracts.Dtos;
using TravelCut.Infrastructure.Locks;
using TravelCut.Infrastructure.Motion;

namespace TravelCut.Tests.Motion;

/// <summary>Trava real entre processos (arquivo em 3-Trabalho/.locks). Sem estado: o caminho vem do projeto de cada teste.</summary>
internal static class MotionTestLocks
{
    public static IProjectOperationLock Shared { get; } = new FileProjectOperationLock();
}

/// <summary>Runner roteirizado: nunca inicia processo real. Separa node, --version do CLI e render.</summary>
internal sealed class ScriptedProcessRunner : IProcessRunner
{
    public const string NodeOutput = "v24.11.0\n";
    public const string CliVersionOutput = "hyperframes 1.4.2\n";

    public List<ProcessRunRequest> Calls { get; } = new();

    public Func<ProcessRunRequest, ProcessRunResult> Node { get; set; } = _ => Ok(NodeOutput);

    public Func<ProcessRunRequest, ProcessRunResult> CliVersion { get; set; } = _ => Ok(CliVersionOutput);

    public Func<ProcessRunRequest, ProcessRunResult> Render { get; set; } = _ => Ok(string.Empty);

    public IReadOnlyList<ProcessRunRequest> RenderCalls =>
        Calls.Where(IsRender).ToList();

    public Task<ProcessRunResult> RunAsync(ProcessRunRequest request, CancellationToken ct = default)
    {
        Calls.Add(request);
        ct.ThrowIfCancellationRequested();

        ProcessRunResult result;
        if (IsNodeCheck(request))
            result = Node(request);
        else if (IsRender(request))
            result = Render(request);
        else
            result = CliVersion(request);

        return Task.FromResult(result);
    }

    public static ProcessRunResult Ok(string stdout, string stderr = "", int exitCode = 0) =>
        new(ProcessRunStatus.Completed, exitCode, stdout, stderr);

    public static ProcessRunResult Status(ProcessRunStatus status) =>
        new(status, null, string.Empty, string.Empty, status == ProcessRunStatus.NotStarted ? "Não iniciado." : null);

    /// <summary>Node é reconhecido pelo nome do executável: o renderer passa caminho absoluto (ex.: C:\...\node.exe).</summary>
    public static bool IsNodeCheck(ProcessRunRequest r) =>
        string.Equals(Path.GetFileNameWithoutExtension(r.FileName), HyperFramesRendererNames.Node, StringComparison.OrdinalIgnoreCase)
        && r.Arguments.Count == 1 && r.Arguments[0] == "--version";

    public static bool IsRender(ProcessRunRequest r) => r.Arguments.Contains("render");

    /// <summary>Valor do argumento que segue <paramref name="option"/> no render (ex.: --output).</summary>
    public static string ArgAfter(ProcessRunRequest r, string option)
    {
        int i = r.Arguments.ToList().IndexOf(option);
        return i >= 0 && i + 1 < r.Arguments.Count ? r.Arguments[i + 1] : string.Empty;
    }

    /// <summary>Render que "gera" o arquivo de saída indicado por --output (simula um CLI bem-sucedido).</summary>
    public static ProcessRunResult WriteOutputFile(ProcessRunRequest r)
    {
        string output = ArgAfter(r, "--output");
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        File.WriteAllBytes(output, new byte[] { 1, 2, 3, 4 });
        return Ok(string.Empty);
    }
}

internal static class HyperFramesRendererNames
{
    public const string Node = "node";
}

/// <summary>Projeto temporário com 1-Bruto, 2-Assets e 3-Trabalho. Removido no Dispose.</summary>
internal sealed class TempMotionProject : IDisposable
{
    public TempMotionProject()
    {
        Root = Path.Combine(Path.GetTempPath(), "tc-motion-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(Root, "1-Bruto"));
        Directory.CreateDirectory(Path.Combine(Root, "2-Assets"));
        Directory.CreateDirectory(Path.Combine(Root, "3-Trabalho"));
    }

    public string Root { get; }

    public string AssetsRoot => Path.Combine(Root, "2-Assets");

    public string WorkRoot => Path.Combine(Root, "3-Trabalho", "motion");

    /// <summary>Cria um asset em 2-Assets (somente para testes) e devolve o caminho relativo.</summary>
    public string AddAsset(string relative)
    {
        string full = Path.Combine(AssetsRoot, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllBytes(full, new byte[] { 0x89, 0x50, 0x4E, 0x47 });
        return relative;
    }

    /// <summary>Arquivo executável fictício (o runner é falso; só precisa existir para o renderer aceitar o caminho).</summary>
    public string AddFakeCli(string name = "hyperframes-fake.exe", string folder = "tools")
    {
        string full = Path.Combine(Root, folder, name);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, "fake");
        return full;
    }

    /// <summary>Node fictício em caminho absoluto (o runner é falso). Usado como TRAVELCUT_NODE_PATH nos testes.</summary>
    public string AddFakeNode(string folder = "tools") => AddFakeCli(HyperFramesRenderer.NodeFileName, folder);

    public static MotionRenderRequestDto ValidRequest(
        Guid? projectId = null,
        string templateId = MotionTemplateIds.TitleCard,
        Dictionary<string, string>? parameters = null,
        string idempotencyKey = "key-0001-abc")
    {
        return new MotionRenderRequestDto(
            ProjectId: projectId ?? Guid.Parse("11111111-2222-3333-4444-555555555555"),
            TemplateId: templateId,
            Parameters: parameters ?? new Dictionary<string, string> { ["title"] = "Veneza" },
            DurationFrames: 90,
            FrameRateNum: 30,
            FrameRateDen: 1,
            Width: 1920,
            Height: 1080,
            IdempotencyKey: idempotencyKey);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Root))
                Directory.Delete(Root, recursive: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}

/// <summary>Renderer de teste que conta chamadas e devolve um resultado controlado.</summary>
internal sealed class CountingMotionRenderer : IMotionRenderer
{
    private readonly Func<MotionRenderRequestDto, MotionRenderContext, Task<MotionRenderResultDto>> _render;

    public CountingMotionRenderer(string mode, Func<MotionRenderRequestDto, MotionRenderContext, Task<MotionRenderResultDto>> render)
    {
        Mode = mode;
        _render = render;
    }

    public string Mode { get; }

    public int RenderCount { get; private set; }

    public Task<MotionCapabilitiesDto> DetectCapabilitiesAsync(CancellationToken ct = default) =>
        Task.FromResult(new MotionCapabilitiesDto(
            MotionSchemaVersions.Capabilities, Mode, MotionEnvironmentStates.Mock, true, Mode == "mock",
            null, null, "teste", null, new List<MotionTemplateCapabilityDto>(), DateTime.UtcNow));

    public Task<MotionRenderResultDto> RenderAsync(MotionRenderRequestDto request, MotionRenderContext context, CancellationToken ct = default)
    {
        RenderCount++;
        return _render(request, context);
    }
}
