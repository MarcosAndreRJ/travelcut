using TravelCut.Mcp.Tests.Support;
using Xunit;

namespace TravelCut.Mcp.Tests;

/// <summary>Ferramentas somente leitura: dispatcher real, banco SQLite temporário, autorização e formato de saída.</summary>
public sealed class McpReadToolTests : IDisposable
{
    private readonly McpTestHost _host;

    public McpReadToolTests()
    {
        _host = McpTestHost.Create(allowMutations: false, McpTestHost.ProjectA);
        _host.SeedAsync().GetAwaiter().GetResult();
    }

    public void Dispose() => _host.Dispose();

    [Fact]
    public async Task ListProjects_SemProjetosAutorizados_RetornaListaVazia()
    {
        using var host = McpTestHost.Create(allowMutations: false);
        await host.SeedAsync();

        var result = await host.Read.ListProjects();

        Assert.False(ToolJson.IsError(result));
        Assert.Equal("{\"projects\":[]}", ToolJson.Text(result));
    }

    [Fact]
    public async Task ListProjects_FiltraPelaListaAutorizadaESemCaminhoAbsoluto()
    {
        var result = await _host.Read.ListProjects();

        Assert.False(ToolJson.IsError(result));
        var json = ToolJson.Text(result);
        Assert.Contains(McpTestHost.ProjectA.ToString(), json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(McpTestHost.ProjectB.ToString(), json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("rootPath", json);
        Assert.DoesNotContain(_host.Root, json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetInventory_ProjetoNaoAutorizado_RetornaProjectNotAuthorized()
    {
        var result = await _host.Read.GetInventory(McpTestHost.ProjectB.ToString());

        Assert.True(ToolJson.IsError(result));
        var error = ToolJson.Parse(result);
        Assert.Equal("PROJECT_NOT_AUTHORIZED", error["code"]!.GetValue<string>());
        Assert.False(string.IsNullOrWhiteSpace(error["message"]!.GetValue<string>()));
    }

    [Theory]
    [InlineData("nao-e-guid")]
    [InlineData("")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public async Task GetInventory_ProjectIdInvalido_RetornaInvalidArgument(string projectId)
    {
        var result = await _host.Read.GetInventory(projectId);

        Assert.True(ToolJson.IsError(result));
        Assert.Equal("INVALID_ARGUMENT", ToolJson.Parse(result)["code"]!.GetValue<string>());
    }

    [Fact]
    public async Task GetInventory_ProjetoAutorizado_RetornaCaminhosRelativosApenas()
    {
        var result = await _host.Read.GetInventory(McpTestHost.ProjectA.ToString());

        Assert.False(ToolJson.IsError(result));
        var json = ToolJson.Parse(result);
        var media = json["media"]!.AsArray();
        Assert.Equal(2, media.Count);
        Assert.Contains(media, m => m!["relativePath"]!.GetValue<string>() == "1-Bruto/clip1.mp4");
        var text = ToolJson.Text(result);
        Assert.DoesNotContain("fullPath", text);
        Assert.DoesNotContain(_host.Root, text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetSuggestions_StatusInvalido_RetornaInvalidArgument()
    {
        var result = await _host.Read.GetSuggestions(McpTestHost.ProjectA.ToString(), status: "Qualquer");

        Assert.True(ToolJson.IsError(result));
        Assert.Equal("INVALID_ARGUMENT", ToolJson.Parse(result)["code"]!.GetValue<string>());
    }

    [Fact]
    public async Task GetSuggestions_SemFiltro_RetornaSugestoesDoProjetoSomente()
    {
        var result = await _host.Read.GetSuggestions(McpTestHost.ProjectA.ToString());

        Assert.False(ToolJson.IsError(result));
        var suggestions = ToolJson.Parse(result)["suggestions"]!.AsArray();
        Assert.Equal(2, suggestions.Count);
        var ids = suggestions.Select(s => s!["suggestionId"]!.GetValue<string>()).ToList();
        Assert.DoesNotContain(McpTestHost.SuggestionB.ToString(), ids, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetSuggestions_FiltroDeStatusSemDiferencaDeCaixa_AplicaFiltro()
    {
        var proposed = await _host.Read.GetSuggestions(McpTestHost.ProjectA.ToString(), status: "proposed");
        var approved = await _host.Read.GetSuggestions(McpTestHost.ProjectA.ToString(), status: "Approved");

        Assert.Equal(2, ToolJson.Parse(proposed)["suggestions"]!.AsArray().Count);
        Assert.Empty(ToolJson.Parse(approved)["suggestions"]!.AsArray());
    }

    [Fact]
    public async Task GetTranscript_MidiaSemAnalise_RetornaNotRunSemErro()
    {
        var result = await _host.Read.GetTranscript(McpTestHost.ProjectA.ToString(), McpTestHost.MediaA.ToString());

        Assert.False(ToolJson.IsError(result));
        var json = ToolJson.Parse(result);
        Assert.Equal("not_run", json["transcriptionStatus"]!.GetValue<string>());
        Assert.Null(json["transcript"]);
    }

    [Fact]
    public async Task GetTranscript_MediaIdInvalido_RetornaInvalidArgument()
    {
        var result = await _host.Read.GetTranscript(McpTestHost.ProjectA.ToString(), "midia-x");

        Assert.True(ToolJson.IsError(result));
        Assert.Equal("INVALID_ARGUMENT", ToolJson.Parse(result)["code"]!.GetValue<string>());
    }

    [Fact]
    public async Task GetEditPlan_RevisaoInvalida_RetornaInvalidArgument()
    {
        var result = await _host.Read.GetEditPlan(McpTestHost.ProjectA.ToString(), revision: 0);

        Assert.True(ToolJson.IsError(result));
        Assert.Equal("INVALID_ARGUMENT", ToolJson.Parse(result)["code"]!.GetValue<string>());
    }

    [Fact]
    public async Task GetEditPlan_SemPlano_RetornaNullSemErro_ComoNaUi()
    {
        // Contrato do use case: sem plano gerado, Ok(null). Não é erro para a UI nem para o MCP.
        var result = await _host.Read.GetEditPlan(McpTestHost.ProjectA.ToString());

        Assert.False(ToolJson.IsError(result));
        Assert.Equal("null", ToolJson.Text(result));
    }

    [Fact]
    public async Task JobStatus_JobIdDiferenteDaAnaliseAtual_RetornaJobNotFound()
    {
        var other = Guid.NewGuid().ToString();

        var result = await _host.Read.JobStatus(McpTestHost.ProjectA.ToString(), other);

        Assert.True(ToolJson.IsError(result));
        Assert.Equal("JOB_NOT_FOUND", ToolJson.Parse(result)["code"]!.GetValue<string>());
    }

    [Fact]
    public async Task JobStatus_SemJobId_RetornaEstadoDoProjeto()
    {
        var result = await _host.Read.JobStatus(McpTestHost.ProjectA.ToString());

        Assert.False(ToolJson.IsError(result));
        var json = ToolJson.Parse(result);
        Assert.Equal("idle", json["state"]!.GetValue<string>());
        Assert.Equal(_host.Pipeline.CurrentJobId.ToString(), json["jobId"]!.GetValue<string>());
    }

    [Fact]
    public async Task GetEditorCapabilities_ModoMock_InformaSimulacaoSemCriarProjeto()
    {
        var result = await _host.Read.GetEditorCapabilities();

        Assert.False(ToolJson.IsError(result));
        var json = ToolJson.Parse(result);
        Assert.Equal("editor/1", json["schemaVersion"]!.GetValue<string>());
        Assert.True(json["isSimulated"]!.GetValue<bool>());
        Assert.Equal("mock", json["adapterMode"]!.GetValue<string>());
    }

    [Fact]
    public async Task GetMotionCapabilities_SemCliHyperFrames_DeclaraIndisponivelSemSimular()
    {
        var result = await _host.Read.GetMotionCapabilities();

        Assert.False(ToolJson.IsError(result));
        var json = ToolJson.Parse(result);
        Assert.Equal("motion/1", json["schemaVersion"]!.GetValue<string>());
        if (Environment.GetEnvironmentVariable("TRAVELCUT_HYPERFRAMES_CLI") is null
            && !string.Equals(Environment.GetEnvironmentVariable("TRAVELCUT_MOTION_ADAPTER"), "mock", StringComparison.OrdinalIgnoreCase))
        {
            Assert.False(json["isSimulated"]!.GetValue<bool>());
            Assert.False(json["isAvailable"]!.GetValue<bool>());
            Assert.Contains(json["environmentState"]!.GetValue<string>(), new[] { "NOT_INSTALLED", "NODE_UNAVAILABLE" });
        }
    }
}
