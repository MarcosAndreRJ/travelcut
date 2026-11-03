using TravelCut.Mcp.Tests.Support;
using Xunit;

namespace TravelCut.Mcp.Tests;

/// <summary>
/// Validação do corpo antes do cache (erro não consome a chave), normalização do hash e isolamento entre projetos
/// (mídia de outro projeto em queue_analysis e get_transcript). Dispatcher real, banco temporário.
/// </summary>
public sealed class McpMutationValidationTests : IDisposable
{
    private readonly McpTestHost _host;
    private readonly string _projectA = McpTestHost.ProjectA.ToString();

    public McpMutationValidationTests()
    {
        _host = McpTestHost.Create(allowMutations: true, McpTestHost.ProjectA);
        _host.SeedAsync().GetAwaiter().GetResult();
    }

    public void Dispose() => _host.Dispose();

    [Fact]
    public async Task ErroDeValidacao_NaoConsomeAChave_CorrecaoComAMesmaChaveExecuta()
    {
        var invalido = await _host.Mutations.SubmitReview(
            projectId: _projectA, suggestionId: McpTestHost.SuggestionA.ToString(),
            action: "approve", reviewer: "   ", idempotencyKey: "valida-1");

        Assert.True(ToolJson.IsError(invalido));
        Assert.Equal("INVALID_ARGUMENT", ToolJson.Parse(invalido)["code"]!.GetValue<string>());
        Assert.Empty(await _host.DecisionsAsync(McpTestHost.ProjectA));

        var corrigido = await _host.Mutations.SubmitReview(
            projectId: _projectA, suggestionId: McpTestHost.SuggestionA.ToString(),
            action: "approve", reviewer: "Revisor", idempotencyKey: "valida-1");

        Assert.False(ToolJson.IsError(corrigido));
        Assert.Single(await _host.DecisionsAsync(McpTestHost.ProjectA));
    }

    [Fact]
    public async Task ErroDeValidacaoDeConfirmacao_NaoConsomeAChave()
    {
        var semConfirmacao = await _host.Mutations.ApplyPlan(
            projectId: _projectA, idempotencyKey: "conf-corrigida", revision: 1, dryRun: false,
            assemblyFingerprint: "abc", confirmProjectSwitch: false);
        Assert.Equal("CONFIRMATION_REQUIRED", ToolJson.Parse(semConfirmacao)["code"]!.GetValue<string>());

        var comConfirmacao = await _host.Mutations.ApplyPlan(
            projectId: _projectA, idempotencyKey: "conf-corrigida", revision: 1, dryRun: false,
            assemblyFingerprint: "abc", confirmProjectSwitch: true);

        // A chave foi usada pela execução real (sem plano, o editor responde erro próprio), não por IDEMPOTENCY_KEY_REUSED.
        var code = ToolJson.Parse(comConfirmacao)["code"]?.GetValue<string>();
        Assert.NotEqual("CONFIRMATION_REQUIRED", code);
        Assert.NotEqual("IDEMPOTENCY_KEY_REUSED", code);
    }

    [Fact]
    public async Task ReviewerComEspacos_EhNormalizadoNoHash_MesmaChaveReaproveitaSemDuplicar()
    {
        var first = await _host.Mutations.SubmitReview(
            projectId: _projectA, suggestionId: McpTestHost.SuggestionA.ToString(),
            action: "approve", reviewer: "  Revisor  ", idempotencyKey: "trim-1");
        var second = await _host.Mutations.SubmitReview(
            projectId: _projectA, suggestionId: McpTestHost.SuggestionA.ToString(),
            action: "approve", reviewer: "Revisor", idempotencyKey: "trim-1");

        Assert.False(ToolJson.IsError(first));
        Assert.False(ToolJson.IsError(second));
        Assert.Equal(ToolJson.Text(first), ToolJson.Text(second));
        Assert.Single(await _host.DecisionsAsync(McpTestHost.ProjectA));
    }

    [Fact]
    public async Task QueueAnalysis_MediaDeOutroProjeto_RetornaMediaNotFoundSemIniciarAnalise()
    {
        var result = await _host.Mutations.QueueAnalysis(
            projectId: _projectA, idempotencyKey: "outro-proj-1",
            mediaIds: new[] { McpTestHost.MediaB.ToString() });

        Assert.True(ToolJson.IsError(result));
        Assert.Equal("MEDIA_NOT_FOUND", ToolJson.Parse(result)["code"]!.GetValue<string>());
        Assert.Equal(0, _host.Pipeline.StartCalls);
    }

    [Fact]
    public async Task QueueAnalysis_MediaDoProprioProjeto_Inicia()
    {
        var result = await _host.Mutations.QueueAnalysis(
            projectId: _projectA, idempotencyKey: "proprio-1",
            mediaIds: new[] { McpTestHost.MediaA.ToString() });

        Assert.False(ToolJson.IsError(result));
        Assert.Equal(1, _host.Pipeline.StartCalls);
    }

    [Fact]
    public async Task QueueAnalysis_MediaDeOutroProjetoMisturadaComAPropria_EhRecusadaInteira()
    {
        var result = await _host.Mutations.QueueAnalysis(
            projectId: _projectA, idempotencyKey: "misto-1",
            mediaIds: new[] { McpTestHost.MediaA.ToString(), McpTestHost.MediaB.ToString() });

        Assert.Equal("MEDIA_NOT_FOUND", ToolJson.Parse(result)["code"]!.GetValue<string>());
        Assert.Equal(0, _host.Pipeline.StartCalls);
    }

    [Fact]
    public async Task PrimeiroChamadorCancela_MutacaoContinua_SegundoRecebeOResultado()
    {
        _host.Pipeline.StartDelay = TimeSpan.FromMilliseconds(300);
        using var cancelado = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            _host.Mutations.QueueAnalysis(projectId: _projectA, idempotencyKey: "cancel-e2e-1", cancellationToken: cancelado.Token));

        var segundo = await _host.Mutations.QueueAnalysis(projectId: _projectA, idempotencyKey: "cancel-e2e-1");

        Assert.False(ToolJson.IsError(segundo));
        Assert.Equal(1, _host.Pipeline.StartCalls);
    }

    [Fact]
    public async Task GetTranscript_MediaDeOutroProjeto_RetornaMediaNotFound()
    {
        var result = await _host.Read.GetTranscript(projectId: _projectA, mediaId: McpTestHost.MediaB.ToString());

        Assert.True(ToolJson.IsError(result));
        Assert.Equal("MEDIA_NOT_FOUND", ToolJson.Parse(result)["code"]!.GetValue<string>());
        Assert.DoesNotContain("outro.mp4", ToolJson.Text(result));
    }

    [Fact]
    public async Task GetTranscript_MediaDoProprioProjeto_RetornaAnaliseSemErro()
    {
        var result = await _host.Read.GetTranscript(projectId: _projectA, mediaId: McpTestHost.MediaA.ToString());

        Assert.False(ToolJson.IsError(result));
        Assert.Equal(McpTestHost.MediaA.ToString(), ToolJson.Parse(result)["mediaId"]!.GetValue<string>());
    }
}
