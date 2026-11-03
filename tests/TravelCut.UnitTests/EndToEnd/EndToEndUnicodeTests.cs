using TravelCut.Contracts.Dtos;
using Xunit;
using static TravelCut.UnitTests.EndToEnd.E2EScenarioData;

namespace TravelCut.UnitTests.EndToEnd;

/// <summary>
/// FIXTURES DE PORTA (ffprobe, Whisper, silencedetect): não são prova de mídia real (ver E2EPortFixtures).
/// Cenário 6: projeto com acentos, emoji e espaços no caminho ("Viagem São Tomé 🌴/1-Bruto/clipe ação 01.mp4"),
/// percorrendo o fluxo até o plano. Fixtures de porta como nos demais cenários.
/// </summary>
public sealed class EndToEndUnicodeTests : IDisposable
{
    private readonly E2EWorkspace _ws = new();

    public void Dispose() => _ws.Dispose();

    [Fact]
    public async Task ProjetoEArquivoComAcentoEmojiEEspacos_PercorreFluxoAtePlano_OrigemIntacta()
    {
        const string projectName = "Viagem São Tomé 🌴";
        var root = _ws.ProjectRoot(projectName);
        using var host = E2EHost.Start(_ws.DatabasePath, Unicode());

        var projectId = await E2ESteps.CreateProjectAsync(host, root, projectName);
        E2ESteps.PlaceMedia(root, UnicodeFiles);
        Assert.True(File.Exists(Path.Combine(root, "1-Bruto", UnicodeClipA)));
        var originBefore = E2EWorkspace.Tree(Path.Combine(root, "1-Bruto"));

        var scan = await E2ESteps.ScanAsync(host, projectId);
        Assert.Equal(2, scan.GetProperty("newAssetsAdded").GetInt32());
        var names = await E2ESteps.MediaNamesAsync(host, projectId);
        Assert.Contains(names.Values, n => n.EndsWith(UnicodeClipA, StringComparison.Ordinal));
        Assert.Contains(names.Values, n => n.EndsWith(UnicodeClipB, StringComparison.Ordinal));

        Assert.Equal("completed", (await E2ESteps.AnalyzeAsync(host, projectId)).GetProperty("state").GetString());

        var suggestions = await E2ESteps.SuggestionsAsync(host, projectId);
        Assert.Equal(3, suggestions.Count);
        var silenceA = E2ESteps.Find(suggestions, names, UnicodeClipA, 1.0);
        var silenceB = E2ESteps.Find(suggestions, names, UnicodeClipA, 6.0);
        await E2ESteps.DecideAsync(host, silenceA.GetProperty("suggestionId").GetGuid(), "approve");
        await E2ESteps.DecideAsync(host, silenceB.GetProperty("suggestionId").GetGuid(), "reject");

        var generated = await E2ESteps.GeneratePlanAsync(host, projectId);
        Assert.Equal(1, generated.GetProperty("revision").GetInt32());

        var plan = await E2ESteps.LatestPlanAsync(host, projectId);
        Assert.Equal(1, plan.GetProperty("summary").GetProperty("approvedCutsApplied").GetInt32());
        var appliedCut = Assert.Single(plan.GetProperty("appliedCuts").EnumerateArray().ToList());
        Assert.Equal(silenceA.GetProperty("suggestionId").GetGuid(), appliedCut.GetProperty("suggestionId").GetGuid());
        Assert.True(plan.GetProperty("validation").GetProperty("isValid").GetBoolean());

        // Os clipes do plano carregam o nome com acento e espaço exatamente como foi registrado.
        var clipPaths = plan.GetProperty("clips").EnumerateArray()
            .Select(c => c.GetProperty("relativePath").GetString()!)
            .ToList();
        Assert.Contains(clipPaths, p => p.EndsWith(UnicodeClipA, StringComparison.Ordinal));
        Assert.Contains(clipPaths, p => p.EndsWith(UnicodeClipB, StringComparison.Ordinal));

        Assert.Equal(originBefore, E2EWorkspace.Tree(Path.Combine(root, "1-Bruto")));
    }
}
