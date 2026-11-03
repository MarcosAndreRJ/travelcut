using TravelCut.Mcp.Configuration;
using TravelCut.Mcp.Facade;
using Xunit;

namespace TravelCut.Mcp.Tests;

/// <summary>
/// Cache de idempotência com token do servidor (cancelamento do primeiro chamador não cancela a mutação), limite de
/// entradas, transitórios, e parse do <c>--migrate-database</c>. Sem host.
/// </summary>
public sealed class McpCacheResilienceTests
{
    [Fact]
    public async Task PrimeiroChamadorCancela_SegundoRecebeOResultadoDaMesmaExecucao()
    {
        var cache = new McpIdempotencyCache();
        var liberar = new TaskCompletionSource();
        int execucoes = 0;
        async Task<McpOutcome> Corpo(CancellationToken serverToken)
        {
            Interlocked.Increment(ref execucoes);
            await liberar.Task.WaitAsync(serverToken);
            return McpOutcome.Success("{\"ok\":true}");
        }

        using var cancelado = new CancellationTokenSource();
        var primeiro = cache.RunAsync("ferramenta", "cancela-1", "hash", Corpo, cancelado.Token);
        await Task.Delay(20); // deixa o corpo começar
        cancelado.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => primeiro);

        var segundo = cache.RunAsync("ferramenta", "cancela-1", "hash", Corpo, CancellationToken.None);
        liberar.SetResult();
        var resultado = await segundo;

        Assert.False(resultado.Outcome.IsError);
        Assert.Equal("{\"ok\":true}", resultado.Outcome.Text);
        Assert.True(resultado.Reused);
        Assert.Equal(1, execucoes);
    }

    [Fact]
    public async Task TokenDoServidorCancelado_InterrompeAExecucaoERemoveAEntrada()
    {
        using var servidor = new CancellationTokenSource();
        var cache = new McpIdempotencyCache(servidor.Token);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            cache.RunAsync("ferramenta", "desligado-1", "hash", async token =>
            {
                servidor.Cancel();
                await Task.Delay(Timeout.Infinite, token);
                return McpOutcome.Success("{}");
            }, CancellationToken.None));

        Assert.Equal(0, cache.Count);
    }

    [Fact]
    public async Task LimiteDeEntradas_RemoveAsMaisAntigasConcluidas()
    {
        var cache = new McpIdempotencyCache(maxEntries: 3);
        int execucoes = 0;
        Task<McpOutcome> Corpo() { execucoes++; return Task.FromResult(McpOutcome.Success("{}")); }

        for (int i = 1; i <= 5; i++)
        {
            await cache.RunAsync("ferramenta", $"lim-{i}", "h", Corpo);
        }

        Assert.Equal(3, cache.Count);
        Assert.Equal(5, execucoes);

        // A chave mais antiga saiu do cache: a repetição executa de novo (limite documentado).
        var repetida = await cache.RunAsync("ferramenta", "lim-1", "h", Corpo);
        Assert.False(repetida.Reused);
        Assert.Equal(6, execucoes);
    }

    [Fact]
    public async Task LimiteDeEntradas_NaoRemoveChamadasEmVoo()
    {
        var cache = new McpIdempotencyCache(maxEntries: 1);
        var liberar = new TaskCompletionSource();
        var emVoo = cache.RunAsync("ferramenta", "voo-1", "h", async _ => { await liberar.Task; return McpOutcome.Success("{}"); });

        await cache.RunAsync("ferramenta", "voo-2", "h", () => Task.FromResult(McpOutcome.Success("{}")));

        // voo-1 ainda não concluiu: continua no cache mesmo acima do limite.
        Assert.Equal(2, cache.Count);
        liberar.SetResult();
        await emVoo;
    }

    [Fact]
    public async Task PIPELINE_IN_PROGRESS_E_OPERATION_IN_PROGRESS_NaoFicamNoCache()
    {
        var cache = new McpIdempotencyCache();
        int execucoes = 0;
        Task<McpOutcome> Corpo()
        {
            execucoes++;
            return Task.FromResult(execucoes == 1
                ? McpOutcome.Error("OPERATION_IN_PROGRESS", "ocupado")
                : McpOutcome.Success("{}"));
        }

        var primeira = await cache.RunAsync("apply_plan", "ocupado-1", "h", Corpo);
        var repetida = await cache.RunAsync("apply_plan", "ocupado-1", "h", Corpo);

        Assert.True(primeira.Outcome.IsError);
        Assert.False(repetida.Outcome.IsError);
        Assert.Equal(2, execucoes);
    }

    [Theory]
    [InlineData(@"Arquivo K:\Trabalho\Privado\video.mp4 falhou", @"K:\Trabalho")]
    [InlineData("Arquivo K:/Trabalho/Privado/video.mp4 falhou", "K:/Trabalho")]
    [InlineData("Lendo /home/usuario/midia/clip.mp4 falhou", "/home/usuario")]
    [InlineData(@"Servidor \\maquina\compartilhado\x.mp4 falhou", @"\\maquina")]
    public void Sanitize_CaminhosWindowsPosixEUncNaoVazam(string mensagem, string trecho)
    {
        var sanitized = McpOutcome.Sanitize(mensagem);

        Assert.DoesNotContain(trecho, sanitized);
        Assert.Contains("[caminho]", sanitized);
    }

    [Fact]
    public void Sanitize_PreservaReferenciasRelativasEUrls()
    {
        var sanitized = McpOutcome.Sanitize("Mídia 1-Bruto/clip1.mp4 em https://exemplo.com/x falhou");

        Assert.Contains("1-Bruto/clip1.mp4", sanitized);
        Assert.Contains("https://exemplo.com/x", sanitized);
        Assert.DoesNotContain("[caminho]", sanitized);
    }

    [Fact]
    public void Parse_MigrateDatabase_EhOpcionalESemPadraoDeMigracao()
    {
        var padrao = McpCommandLine.Parse(Array.Empty<string>(), _ => null);
        var migra = McpCommandLine.Parse(new[] { "--migrate-database" }, _ => null);

        Assert.False(padrao.Configuration!.MigrateDatabase);
        Assert.True(migra.Configuration!.MigrateDatabase);
    }

    [Fact]
    public void Parse_MigrateDatabaseComValor_EhRecusado()
    {
        var result = McpCommandLine.Parse(new[] { "--migrate-database=sim" }, _ => null);

        Assert.Null(result.Configuration);
        Assert.NotNull(result.Error);
    }
}
