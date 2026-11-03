using TravelCut.Mcp.Tests.Support;
using Xunit;

namespace TravelCut.Mcp.Tests;

/// <summary>
/// Guardas das mutações: modo habilitado, projeto autorizado, chave de idempotência, reuso, concorrência,
/// pertença da sugestão ao projeto e confirmações. Chamadas reais ao dispatcher e ao banco temporário.
/// </summary>
public sealed class McpMutationGuardTests : IDisposable
{
    private readonly McpTestHost _host;
    private readonly string _projectA = McpTestHost.ProjectA.ToString();

    public McpMutationGuardTests()
    {
        _host = McpTestHost.Create(allowMutations: true, McpTestHost.ProjectA);
        _host.SeedAsync().GetAwaiter().GetResult();
    }

    public void Dispose() => _host.Dispose();

    // ---- Modo de mutação ----

    [Fact]
    public async Task Mutacao_ComMutacoesDesligadas_RetornaMutationsDisabledSemExecutar()
    {
        using var host = McpTestHost.Create(allowMutations: false, McpTestHost.ProjectA);
        await host.SeedAsync();

        var result = await host.Mutations.QueueAnalysis(
            projectId: McpTestHost.ProjectA.ToString(), idempotencyKey: "chave-1");

        Assert.True(ToolJson.IsError(result));
        Assert.Equal("MUTATIONS_DISABLED", ToolJson.Parse(result)["code"]!.GetValue<string>());
        Assert.Equal(0, host.Pipeline.StartCalls);
    }

    // ---- Projeto autorizado ----

    [Fact]
    public async Task Mutacao_ProjetoNaoAutorizado_RetornaProjectNotAuthorized()
    {
        var result = await _host.Mutations.QueueAnalysis(
            projectId: McpTestHost.ProjectB.ToString(), idempotencyKey: "chave-b");

        Assert.Equal("PROJECT_NOT_AUTHORIZED", ToolJson.Parse(result)["code"]!.GetValue<string>());
        Assert.Equal(0, _host.Pipeline.StartCalls);
    }

    [Fact]
    public async Task Mutacao_SemListaDeProjetos_RetornaProjectNotAuthorizedParaTodos()
    {
        using var host = McpTestHost.Create(allowMutations: true);
        await host.SeedAsync();

        var result = await host.Mutations.QueueAnalysis(
            projectId: McpTestHost.ProjectA.ToString(), idempotencyKey: "chave-sem-lista");

        Assert.Equal("PROJECT_NOT_AUTHORIZED", ToolJson.Parse(result)["code"]!.GetValue<string>());
        Assert.Equal(0, host.Pipeline.StartCalls);
    }

    // ---- Chave de idempotência ----

    [Theory]
    [InlineData("")]
    [InlineData("chave com espaco")]
    [InlineData("chave!")]
    [InlineData("acento-ção")]
    [InlineData("linha\n")]
    public async Task Mutacao_ChaveInvalida_RetornaInvalidArgument(string key)
    {
        var result = await _host.Mutations.QueueAnalysis(projectId: _projectA, idempotencyKey: key);

        Assert.Equal("INVALID_ARGUMENT", ToolJson.Parse(result)["code"]!.GetValue<string>());
        Assert.Equal(0, _host.Pipeline.StartCalls);
    }

    [Fact]
    public async Task Mutacao_ChaveDeCemCaracteresAceita_CentoEUmRecusada()
    {
        var ok = await _host.Mutations.QueueAnalysis(projectId: _projectA, idempotencyKey: new string('a', 100));
        var tooLong = await _host.Mutations.QueueAnalysis(projectId: _projectA, idempotencyKey: new string('a', 101));

        Assert.False(ToolJson.IsError(ok));
        Assert.Equal("INVALID_ARGUMENT", ToolJson.Parse(tooLong)["code"]!.GetValue<string>());
    }

    // ---- Reuso e cache ----

    [Fact]
    public async Task QueueAnalysis_MesmaChaveEMesmosArgumentos_ExecutaUmaVezERepeteResultado()
    {
        var first = await _host.Mutations.QueueAnalysis(projectId: _projectA, idempotencyKey: "analise-1");
        var second = await _host.Mutations.QueueAnalysis(projectId: _projectA, idempotencyKey: "analise-1");

        Assert.False(ToolJson.IsError(first));
        Assert.Equal(ToolJson.Text(first), ToolJson.Text(second));
        Assert.Equal(1, _host.Pipeline.StartCalls);
    }

    [Fact]
    public async Task QueueAnalysis_MesmaChaveComArgumentosDiferentes_RetornaIdempotencyKeyReused()
    {
        var first = await _host.Mutations.QueueAnalysis(projectId: _projectA, idempotencyKey: "analise-2");
        var other = await _host.Mutations.QueueAnalysis(
            projectId: _projectA,
            idempotencyKey: "analise-2",
            mediaIds: new[] { McpTestHost.MediaA.ToString() });

        Assert.False(ToolJson.IsError(first));
        Assert.True(ToolJson.IsError(other));
        Assert.Equal("IDEMPOTENCY_KEY_REUSED", ToolJson.Parse(other)["code"]!.GetValue<string>());
        Assert.Equal(1, _host.Pipeline.StartCalls);
    }

    [Fact]
    public async Task QueueAnalysis_ChamadasSimultaneasComMesmaChave_ExecutamUmaVez()
    {
        _host.Pipeline.StartDelay = TimeSpan.FromMilliseconds(200);

        var tasks = Enumerable.Range(0, 8)
            .Select(_ => _host.Mutations.QueueAnalysis(projectId: _projectA, idempotencyKey: "concorrente-1"))
            .ToArray();
        var results = await Task.WhenAll(tasks);

        Assert.Equal(1, _host.Pipeline.StartCalls);
        Assert.All(results, r => Assert.False(ToolJson.IsError(r)));
        Assert.Single(results.Select(ToolJson.Text).Distinct());
    }

    [Fact]
    public async Task QueueAnalysis_ErroTransitorio_NaoFicaNoCacheERepeticaExecuta()
    {
        _host.Pipeline.FailOnCall = call => call == 1
            ? new InvalidOperationException("PIPELINE_IN_PROGRESS")
            : null;

        var first = await _host.Mutations.QueueAnalysis(projectId: _projectA, idempotencyKey: "transitorio-1");
        var retry = await _host.Mutations.QueueAnalysis(projectId: _projectA, idempotencyKey: "transitorio-1");

        Assert.True(ToolJson.IsError(first));
        Assert.Equal("PIPELINE_IN_PROGRESS", ToolJson.Parse(first)["code"]!.GetValue<string>());
        Assert.False(ToolJson.IsError(retry));
        Assert.Equal(2, _host.Pipeline.StartCalls);
    }

    [Fact]
    public async Task Cache_ChaveIgualEmFerramentasDiferentes_NaoColide()
    {
        // Cache é por (ferramenta, chave): a mesma chave em outra ferramenta não reaproveita nem conflita.
        var analysis = await _host.Mutations.QueueAnalysis(projectId: _projectA, idempotencyKey: "mesma-chave");
        var review = await _host.Mutations.SubmitReview(
            projectId: _projectA,
            suggestionId: McpTestHost.SuggestionA.ToString(),
            action: "approve",
            reviewer: "Revisor",
            idempotencyKey: "mesma-chave");

        Assert.False(ToolJson.IsError(analysis));
        Assert.False(ToolJson.IsError(review));
        Assert.Equal(1, _host.Pipeline.StartCalls);
    }

    // ---- queue_analysis: entradas ----

    [Fact]
    public async Task QueueAnalysis_SemMediaIds_EnviaSemFiltroDeMidias()
    {
        var result = await _host.Mutations.QueueAnalysis(projectId: _projectA, idempotencyKey: "todas-1");

        Assert.False(ToolJson.IsError(result));
        Assert.Null(Assert.Single(_host.Pipeline.Requests).MediaIds);
    }

    [Fact]
    public async Task QueueAnalysis_MediaIdsRepetidos_RetornaInvalidArgument()
    {
        var id = McpTestHost.MediaA.ToString();

        var result = await _host.Mutations.QueueAnalysis(
            projectId: _projectA, idempotencyKey: "repetidos-1", mediaIds: new[] { id, id });

        Assert.Equal("INVALID_ARGUMENT", ToolJson.Parse(result)["code"]!.GetValue<string>());
        Assert.Equal(0, _host.Pipeline.StartCalls);
    }

    [Fact]
    public async Task QueueAnalysis_ListaVazia_RetornaInvalidArgument()
    {
        var result = await _host.Mutations.QueueAnalysis(
            projectId: _projectA, idempotencyKey: "vazia-1", mediaIds: Array.Empty<string>());

        Assert.Equal("INVALID_ARGUMENT", ToolJson.Parse(result)["code"]!.GetValue<string>());
    }

    // ---- submit_review ----

    [Fact]
    public async Task SubmitReview_SugestaoDeOutroProjeto_RetornaSuggestionNotFoundSemGravar()
    {
        var result = await _host.Mutations.SubmitReview(
            projectId: _projectA,
            suggestionId: McpTestHost.SuggestionB.ToString(),
            action: "reject",
            reviewer: "Revisor",
            idempotencyKey: "rev-outro");

        Assert.True(ToolJson.IsError(result));
        Assert.Equal("SUGGESTION_NOT_FOUND", ToolJson.Parse(result)["code"]!.GetValue<string>());
        Assert.Empty(await _host.DecisionsAsync(McpTestHost.ProjectB));
    }

    [Fact]
    public async Task SubmitReview_Aprovar_GravaDecisaoComoAUI()
    {
        var result = await _host.Mutations.SubmitReview(
            projectId: _projectA,
            suggestionId: McpTestHost.SuggestionA.ToString(),
            action: "approve",
            reviewer: "Revisor Teste",
            idempotencyKey: "rev-approve-1",
            notes: "ok");

        Assert.False(ToolJson.IsError(result));
        var decision = Assert.Single(await _host.DecisionsAsync(McpTestHost.ProjectA));
        Assert.Equal(McpTestHost.SuggestionA, decision.SuggestionId);
        Assert.Equal("approve", decision.Action);
    }

    [Fact]
    public async Task SubmitReview_RepetirChaveComMesmosArgumentos_NaoGravaDuasVezes()
    {
        await _host.Mutations.SubmitReview(
            projectId: _projectA, suggestionId: McpTestHost.SuggestionA.ToString(),
            action: "approve", reviewer: "Revisor", idempotencyKey: "rev-dup-1");
        var again = await _host.Mutations.SubmitReview(
            projectId: _projectA, suggestionId: McpTestHost.SuggestionA.ToString(),
            action: "approve", reviewer: "Revisor", idempotencyKey: "rev-dup-1");

        Assert.False(ToolJson.IsError(again));
        Assert.Single(await _host.DecisionsAsync(McpTestHost.ProjectA));
    }

    [Theory]
    [InlineData("aprovar", null, null, "Revisor")]
    [InlineData("modify", null, null, "Revisor")]
    [InlineData("modify", 5.0, 5.0, "Revisor")]
    [InlineData("approve", 1.0, 2.0, "Revisor")]
    [InlineData("approve", null, null, "")]
    public async Task SubmitReview_EntradaInvalida_RetornaInvalidArgumentSemGravar(
        string action, double? start, double? end, string reviewer)
    {
        var result = await _host.Mutations.SubmitReview(
            projectId: _projectA,
            suggestionId: McpTestHost.SuggestionA.ToString(),
            action: action,
            reviewer: reviewer,
            idempotencyKey: "rev-inv-" + Guid.NewGuid().ToString("N"),
            modifiedStartSeconds: start,
            modifiedEndSeconds: end);

        Assert.True(ToolJson.IsError(result));
        Assert.Equal("INVALID_ARGUMENT", ToolJson.Parse(result)["code"]!.GetValue<string>());
        Assert.Empty(await _host.DecisionsAsync(McpTestHost.ProjectA));
    }

    // ---- apply_plan ----

    [Fact]
    public async Task ApplyPlan_AplicarSemRevisao_RetornaInvalidArgument()
    {
        var result = await _host.Mutations.ApplyPlan(
            projectId: _projectA, idempotencyKey: "apl-sem-rev", revision: null, dryRun: false,
            assemblyFingerprint: "abc", confirmProjectSwitch: true);

        Assert.Equal("INVALID_ARGUMENT", ToolJson.Parse(result)["code"]!.GetValue<string>());
    }

    [Fact]
    public async Task ApplyPlan_AplicarSemConfirmacao_RetornaConfirmationRequired()
    {
        var result = await _host.Mutations.ApplyPlan(
            projectId: _projectA, idempotencyKey: "apl-sem-conf", revision: 1, dryRun: false,
            assemblyFingerprint: "abc", confirmProjectSwitch: false);

        Assert.True(ToolJson.IsError(result));
        Assert.Equal("CONFIRMATION_REQUIRED", ToolJson.Parse(result)["code"]!.GetValue<string>());
    }

    [Fact]
    public async Task ApplyPlan_AplicarSemFingerprint_RetornaInvalidArgument()
    {
        var result = await _host.Mutations.ApplyPlan(
            projectId: _projectA, idempotencyKey: "apl-sem-fp", revision: 1, dryRun: false,
            assemblyFingerprint: "  ", confirmProjectSwitch: true);

        Assert.Equal("INVALID_ARGUMENT", ToolJson.Parse(result)["code"]!.GetValue<string>());
    }

    [Fact]
    public async Task ApplyPlan_DryRunSemPlano_RetornaErroEstruturadoSemDetalheInterno()
    {
        var result = await _host.Mutations.ApplyPlan(
            projectId: _projectA, idempotencyKey: "dry-sem-plano", revision: null, dryRun: true);

        Assert.True(ToolJson.IsError(result));
        var error = ToolJson.Parse(result);
        var code = error["code"]!.GetValue<string>();
        Assert.False(string.IsNullOrWhiteSpace(code));
        Assert.NotEqual("INTERNAL_ERROR", code);
        Assert.DoesNotContain("   at ", ToolJson.Text(result));
    }

    [Fact]
    public async Task ApplyPlan_MesmaChaveParaDryRunEAplicacao_RetornaIdempotencyKeyReused()
    {
        // Argumentos diferentes com a mesma chave: o cliente deve usar chaves distintas para preparar e aplicar.
        _ = await _host.Mutations.ApplyPlan(projectId: _projectA, idempotencyKey: "dup-apl", dryRun: true);
        var apply = await _host.Mutations.ApplyPlan(
            projectId: _projectA, idempotencyKey: "dup-apl", revision: 1, dryRun: false,
            assemblyFingerprint: "abc", confirmProjectSwitch: true);

        Assert.Equal("IDEMPOTENCY_KEY_REUSED", ToolJson.Parse(apply)["code"]!.GetValue<string>());
    }
}
