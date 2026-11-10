using TravelCut.Mcp.Tests.Support;
using Xunit;

namespace TravelCut.Mcp.Tests;

/// <summary>Ferramentas get_automation_profile / set_automation_profile: Application Core real (automation.profile.*), SQLite temporário.</summary>
public sealed class McpAutomationProfileTests : IDisposable
{
    private readonly McpTestHost _host;
    private readonly string _projectA = McpTestHost.ProjectA.ToString();

    public McpAutomationProfileTests()
    {
        _host = McpTestHost.Create(allowMutations: true, McpTestHost.ProjectA);
        _host.SeedAsync().GetAwaiter().GetResult();
    }

    public void Dispose() => _host.Dispose();

    [Fact]
    public async Task Get_ProjetoSemEscolha_RetornaBalancedComIsDefault()
    {
        var result = await _host.Read.GetAutomationProfile(_projectA);

        Assert.False(ToolJson.IsError(result));
        var json = ToolJson.Parse(result);
        Assert.Equal("balanced", json["profile"]!.GetValue<string>());
        Assert.True(json["isDefault"]!.GetValue<bool>());
    }

    [Theory]
    [InlineData("conservative")]
    [InlineData("aggressive")]
    [InlineData("balanced")]
    public async Task Set_PerfilValido_PersisteELeituraRetornaOEscolhido(string profile)
    {
        var set = await _host.Mutations.SetAutomationProfile(_projectA, profile, "perfil-" + profile);

        Assert.False(ToolJson.IsError(set));
        Assert.Equal(profile, ToolJson.Parse(set)["profile"]!.GetValue<string>());

        var get = ToolJson.Parse(await _host.Read.GetAutomationProfile(_projectA));
        Assert.Equal(profile, get["profile"]!.GetValue<string>());
        Assert.False(get["isDefault"]!.GetValue<bool>());
    }

    [Fact]
    public async Task Set_NormalizaMaiusculasEEspacos()
    {
        var set = await _host.Mutations.SetAutomationProfile(_projectA, "  Aggressive ", "norm-1");

        Assert.False(ToolJson.IsError(set));
        Assert.Equal("aggressive", ToolJson.Parse(await _host.Read.GetAutomationProfile(_projectA))["profile"]!.GetValue<string>());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("turbo")]
    [InlineData("conservativo")]
    public async Task Set_PerfilInvalido_RetornaInvalidArgumentESemAlterarOPerfil(string profile)
    {
        var set = await _host.Mutations.SetAutomationProfile(_projectA, profile, "invalido-1");

        Assert.True(ToolJson.IsError(set));
        Assert.Equal("INVALID_ARGUMENT", ToolJson.Parse(set)["code"]!.GetValue<string>());
        Assert.True(ToolJson.Parse(await _host.Read.GetAutomationProfile(_projectA))["isDefault"]!.GetValue<bool>());
    }

    [Fact]
    public async Task Set_ErroDeValidacao_NaoConsomeAChave()
    {
        var invalido = await _host.Mutations.SetAutomationProfile(_projectA, "turbo", "chave-corrigida");
        Assert.Equal("INVALID_ARGUMENT", ToolJson.Parse(invalido)["code"]!.GetValue<string>());

        var corrigido = await _host.Mutations.SetAutomationProfile(_projectA, "conservative", "chave-corrigida");

        Assert.False(ToolJson.IsError(corrigido));
        Assert.Equal("conservative", ToolJson.Parse(await _host.Read.GetAutomationProfile(_projectA))["profile"]!.GetValue<string>());
    }

    [Fact]
    public async Task Set_ChaveDeIdempotenciaInvalida_RetornaInvalidArgument()
    {
        var set = await _host.Mutations.SetAutomationProfile(_projectA, "aggressive", "chave inválida!");

        Assert.Equal("INVALID_ARGUMENT", ToolJson.Parse(set)["code"]!.GetValue<string>());
        Assert.True(ToolJson.Parse(await _host.Read.GetAutomationProfile(_projectA))["isDefault"]!.GetValue<bool>());
    }

    [Fact]
    public async Task Set_MesmaChaveEMesmoPerfil_ReaproveitaResultado_MesmaChaveOutroPerfilEhReuso()
    {
        var first = await _host.Mutations.SetAutomationProfile(_projectA, "aggressive", "idem-1");
        var again = await _host.Mutations.SetAutomationProfile(_projectA, "aggressive", "idem-1");
        Assert.Equal(ToolJson.Text(first), ToolJson.Text(again));

        var other = await _host.Mutations.SetAutomationProfile(_projectA, "conservative", "idem-1");
        Assert.Equal("IDEMPOTENCY_KEY_REUSED", ToolJson.Parse(other)["code"]!.GetValue<string>());
        Assert.Equal("aggressive", ToolJson.Parse(await _host.Read.GetAutomationProfile(_projectA))["profile"]!.GetValue<string>());
    }

    [Fact]
    public async Task Set_ProjetoNaoAutorizado_RetornaProjectNotAuthorized_SemTocarNoProjeto()
    {
        var set = await _host.Mutations.SetAutomationProfile(McpTestHost.ProjectB.ToString(), "aggressive", "nao-autorizado-1");
        Assert.Equal("PROJECT_NOT_AUTHORIZED", ToolJson.Parse(set)["code"]!.GetValue<string>());

        var get = await _host.Read.GetAutomationProfile(McpTestHost.ProjectB.ToString());
        Assert.Equal("PROJECT_NOT_AUTHORIZED", ToolJson.Parse(get)["code"]!.GetValue<string>());
    }

    [Fact]
    public async Task Set_ComMutacoesDesligadas_RetornaMutationsDisabled_MasGetFunciona()
    {
        using var readOnly = McpTestHost.Create(allowMutations: false, McpTestHost.ProjectA);
        await readOnly.SeedAsync();

        var set = await readOnly.Mutations.SetAutomationProfile(_projectA, "aggressive", "desligado-1");
        Assert.Equal("MUTATIONS_DISABLED", ToolJson.Parse(set)["code"]!.GetValue<string>());

        var get = await readOnly.Read.GetAutomationProfile(_projectA);
        Assert.False(ToolJson.IsError(get));
        Assert.True(ToolJson.Parse(get)["isDefault"]!.GetValue<bool>());
    }

    [Fact]
    public async Task Get_ProjetoAutorizadoMasInexistente_RetornaErroEstruturado()
    {
        var unknown = Guid.Parse("dddddddd-0000-4000-8000-000000000009");
        using var host = McpTestHost.Create(allowMutations: true, unknown);
        await host.SeedAsync();

        var get = await host.Read.GetAutomationProfile(unknown.ToString());

        Assert.True(ToolJson.IsError(get));
        Assert.Equal("PROJECT_NOT_FOUND", ToolJson.Parse(get)["code"]!.GetValue<string>());
    }
}
