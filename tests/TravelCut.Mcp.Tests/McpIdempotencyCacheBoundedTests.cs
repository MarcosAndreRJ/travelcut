using System.Collections;
using System.Reflection;
using TravelCut.Mcp.Facade;
using Xunit;

namespace TravelCut.Mcp.Tests;

/// <summary>
/// M4: a fila de ordem do cache não cresce com erros transitórios repetidos (remoções deixavam itens mortos em _order).
/// M3 (nível do cache): resultado marcado NoCache não é reaproveitado.
/// A fila é lida por reflexão (campo privado <c>_order</c>): o projeto não tem InternalsVisibleTo.
/// </summary>
public sealed class McpIdempotencyCacheBoundedTests
{
    [Fact]
    public async Task ErrosTransitoriosRepetidos_MantemAFilaDeOrdemLimitada()
    {
        const int maxEntries = 3;
        var cache = new McpIdempotencyCache(maxEntries: maxEntries);

        for (int i = 0; i < 5000; i++)
        {
            var r = await cache.RunAsync("ferramenta", $"transitorio-{i}", "h",
                () => Task.FromResult(McpOutcome.Error(McpErrorCodes.InternalError, "falha transitória (teste)")));
            Assert.True(r.Outcome.IsError);
        }

        Assert.Equal(0, cache.Count);
        var fila = QueueCount(cache);
        Assert.True(fila <= 2 * maxEntries + 1, $"fila de ordem com {fila} itens após 5000 erros transitórios");
    }

    [Fact]
    public async Task OutcomeNoCache_NaoEhReaproveitadoNaRepeticao()
    {
        var cache = new McpIdempotencyCache();
        int execucoes = 0;
        Task<McpOutcome> Corpo()
        {
            execucoes++;
            return Task.FromResult(McpOutcome.Success("{\"status\":\"ERROR\"}").AsNotCacheable());
        }

        var primeira = await cache.RunAsync("apply_plan", "nc-1", "h", Corpo);
        var repetida = await cache.RunAsync("apply_plan", "nc-1", "h", Corpo);

        Assert.False(primeira.Reused);
        Assert.False(repetida.Reused);
        Assert.Equal(2, execucoes);
        Assert.Equal(0, cache.Count);
    }

    [Fact]
    public async Task OutcomeCacheavel_ContinuaSendoReaproveitado()
    {
        var cache = new McpIdempotencyCache();
        int execucoes = 0;
        Task<McpOutcome> Corpo()
        {
            execucoes++;
            return Task.FromResult(McpOutcome.Success("{\"status\":\"DRY_RUN\"}"));
        }

        await cache.RunAsync("apply_plan", "cacheavel-1", "h", Corpo);
        var repetida = await cache.RunAsync("apply_plan", "cacheavel-1", "h", Corpo);

        Assert.True(repetida.Reused);
        Assert.Equal(1, execucoes);
    }

    private static int QueueCount(McpIdempotencyCache cache)
    {
        var field = typeof(McpIdempotencyCache).GetField("_order", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException("Campo _order não encontrado.");
        return ((ICollection)field.GetValue(cache)!).Count;
    }
}
