using System.Globalization;
using Xunit.Abstractions;

namespace TravelCut.UnitTests.EndToEnd;

/// <summary>
/// Métricas reais do cenário de aceite, lidas só pelo IPC (suggestions.list e plan.get) e impressas no log do teste.
/// Também são acrescentadas em acceptance-metrics.tsv ao lado do binário de teste. Sem taxa-alvo artificial.
/// Fixtures de porta: não são mídia real (integration_pass, não host_pass).
/// </summary>
public static class AcceptanceMetrics
{
    private static readonly object Gate = new();

    public static async Task RecordAsync(ITestOutputHelper output, E2EHost host, Guid projectId, string scenario, int restored)
    {
        var suggestions = await E2ESteps.SuggestionsAsync(host, projectId);
        var plan = await E2ESteps.LatestPlanAsync(host, projectId);
        var summary = plan.GetProperty("summary");
        var effects = plan.TryGetProperty("appliedEffects", out var fx) && fx.ValueKind == System.Text.Json.JsonValueKind.Array
            ? fx.EnumerateArray().Select(e => e.GetProperty("kind").GetString() + ":" + e.GetProperty("origin").GetString()).OrderBy(s => s, StringComparer.Ordinal).ToList()
            : [];
        int issues = plan.GetProperty("validation").GetProperty("issues").GetArrayLength();
        var inv = CultureInfo.InvariantCulture;

        string line = string.Join('\t',
            scenario,
            "candidatos=" + suggestions.Count,
            "aplicadosAuto=" + summary.GetProperty("autoCutsApplied").GetInt32(),
            "aprovadosHumano=" + summary.GetProperty("approvedCutsApplied").GetInt32(),
            "pendentes=" + summary.GetProperty("pendingSuggestions").GetInt32(),
            "restaurados=" + restored,
            "removidoS=" + (summary.GetProperty("removedSourceUs").GetInt64() / 1_000_000d).ToString("0.###", inv),
            "efeitos=[" + string.Join(",", effects) + "]",
            "conflitosValidacao=" + issues,
            "revisao=" + plan.GetProperty("revision").GetInt32());
        output.WriteLine("METRICAS " + line);
        lock (Gate)
        {
            File.AppendAllText(Path.Combine(AppContext.BaseDirectory, "acceptance-metrics.tsv"), line + Environment.NewLine);
        }
    }
}
