using TravelCut.Mcp.Configuration;
using TravelCut.Mcp.Facade;
using Xunit;

namespace TravelCut.Mcp.Tests;

/// <summary>Parser de linha de comando/ambiente, cache de idempotência e sanitização de mensagens (sem host).</summary>
public sealed class McpConfigurationAndGuardUnitTests
{
    private const string GuidA = "aaaaaaaa-0000-4000-8000-000000000001";
    private const string GuidB = "bbbbbbbb-0000-4000-8000-000000000002";

    private static string? NoEnv(string _) => null;

    private static Func<string, string?> Env(params (string Name, string Value)[] values) =>
        name => values.FirstOrDefault(v => v.Name == name).Value;

    [Fact]
    public void Parse_SemArgumentos_ModoLeituraESemProjetos()
    {
        var result = McpCommandLine.Parse(Array.Empty<string>(), NoEnv);

        Assert.NotNull(result.Configuration);
        Assert.False(result.Configuration!.AllowMutations);
        Assert.Empty(result.Configuration.AuthorizedProjects);
        Assert.Null(result.Configuration.DatabasePath);
        Assert.Null(result.Error);
    }

    [Fact]
    public void Parse_FlagsEProjetosRepetiveisEmDoisFormatos()
    {
        var result = McpCommandLine.Parse(
            new[] { "--allow-mutations", "--project", GuidA, $"--project={GuidB}" }, NoEnv);

        var config = Assert.IsType<McpServerConfiguration>(result.Configuration);
        Assert.True(config.AllowMutations);
        Assert.Equal(new[] { Guid.Parse(GuidA), Guid.Parse(GuidB) }.OrderBy(g => g), config.AuthorizedProjects.OrderBy(g => g));
    }

    [Fact]
    public void Parse_ProjetosDoAmbienteSeparadosPorPontoEVirgula_EUniaoComLinhaDeComando()
    {
        var result = McpCommandLine.Parse(
            new[] { "--project", GuidA },
            Env((McpServerConfiguration.ProjectsEnvVar, $"{GuidB} ; {GuidA};")));

        var config = Assert.IsType<McpServerConfiguration>(result.Configuration);
        Assert.Equal(2, config.AuthorizedProjects.Count);
        Assert.Contains(Guid.Parse(GuidB), config.AuthorizedProjects);
    }

    [Theory]
    [InlineData("--flag-desconhecida")]
    [InlineData("--project")]
    [InlineData("--project=nao-guid")]
    [InlineData("--allow-mutations=sim")]
    public void Parse_ArgumentoInvalido_RetornaErroSemConfiguracao(string arg)
    {
        var result = McpCommandLine.Parse(new[] { arg }, NoEnv);

        Assert.Null(result.Configuration);
        Assert.False(string.IsNullOrWhiteSpace(result.Error));
    }

    [Fact]
    public void Parse_ProjetoDoAmbienteNaoGuid_RetornaErro()
    {
        var result = McpCommandLine.Parse(Array.Empty<string>(),
            Env((McpServerConfiguration.ProjectsEnvVar, "xyz")));

        Assert.Null(result.Configuration);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public void Parse_BancoRelativoRecusado_BancoAbsolutoAceito()
    {
        var relative = McpCommandLine.Parse(Array.Empty<string>(),
            Env((McpServerConfiguration.DatabaseEnvVar, @"dados\travelcut.db")));
        var absolute = McpCommandLine.Parse(Array.Empty<string>(),
            Env((McpServerConfiguration.DatabaseEnvVar, Path.Combine(Path.GetTempPath(), "tc.db"))));

        Assert.Null(relative.Configuration);
        Assert.NotNull(relative.Error);
        Assert.NotNull(absolute.Configuration);
        Assert.Equal(Path.Combine(Path.GetTempPath(), "tc.db"), absolute.Configuration!.DatabasePath);
    }

    [Theory]
    [InlineData("--help")]
    [InlineData("-h")]
    public void Parse_Ajuda_NaoConfiguraNemRetornaErro(string arg)
    {
        var result = McpCommandLine.Parse(new[] { arg }, NoEnv);

        Assert.True(result.ShowHelp);
        Assert.Null(result.Error);
    }

    [Theory]
    [InlineData("aBc_123-x", true)]
    [InlineData("", false)]
    [InlineData("tem espaco", false)]
    [InlineData("ponto.nao", false)]
    [InlineData("quebra\n", false)]
    public void ChaveDeIdempotencia_Formato(string key, bool valid)
    {
        if (valid)
        {
            Assert.Equal(key, TravelCutMcpFacade.RequireIdempotencyKey(key));
        }
        else
        {
            var failure = Assert.Throws<McpFailure>(() => TravelCutMcpFacade.RequireIdempotencyKey(key));
            Assert.Equal(McpErrorCodes.InvalidArgument, failure.Code);
        }
    }

    [Fact]
    public async Task Cache_MesmaChaveComHashDiferente_RetornaReuso()
    {
        var cache = new McpIdempotencyCache();
        int executions = 0;
        Task<McpOutcome> Body() { executions++; return Task.FromResult(McpOutcome.Success("{\"ok\":true}")); }

        var first = await cache.RunAsync("ferramenta", "k1", "hash-1", Body);
        var second = await cache.RunAsync("ferramenta", "k1", "hash-2", Body);

        Assert.False(first.Outcome.IsError);
        Assert.True(second.Outcome.IsError);
        Assert.Equal(McpErrorCodes.IdempotencyKeyReused, second.Outcome.ErrorCode);
        Assert.Equal(1, executions);
    }

    [Fact]
    public async Task Cache_ErroTransitorio_RemovidoEExecutaDeNovo()
    {
        var cache = new McpIdempotencyCache();
        int executions = 0;
        Task<McpOutcome> Body()
        {
            executions++;
            return Task.FromResult(executions == 1
                ? McpOutcome.Error("EDITOR_UNAVAILABLE", "indisponível")
                : McpOutcome.Success("{}"));
        }

        var first = await cache.RunAsync("apply_plan", "k2", "h", Body);
        var second = await cache.RunAsync("apply_plan", "k2", "h", Body);

        Assert.True(first.Outcome.IsError);
        Assert.False(second.Outcome.IsError);
        Assert.Equal(2, executions);
    }

    [Fact]
    public async Task Cache_ExcecaoNoCorpo_PropagaERemoveEntrada()
    {
        var cache = new McpIdempotencyCache();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            cache.RunAsync("ferramenta", "k3", "h", () => throw new InvalidOperationException("falha")));

        Assert.Equal(0, cache.Count);
    }

    [Fact]
    public void Sanitize_CaminhosAbsolutosNaoVazamAsMensagens()
    {
        var sanitized = McpOutcome.Sanitize(@"Arquivo C:\Trabalho\Privado\video.mp4 não encontrado em \\servidor\share\x.");

        Assert.DoesNotContain(@"C:\Trabalho", sanitized);
        Assert.DoesNotContain(@"\\servidor", sanitized);
        Assert.Contains("[caminho]", sanitized);
    }

    [Fact]
    public void Error_SempreTemCodigoEMensagemEmJson()
    {
        var outcome = McpOutcome.Error("INVALID_ARGUMENT", "mensagem");

        Assert.True(outcome.IsError);
        Assert.Equal("{\"code\":\"INVALID_ARGUMENT\",\"message\":\"mensagem\"}", outcome.Text);
    }
}
