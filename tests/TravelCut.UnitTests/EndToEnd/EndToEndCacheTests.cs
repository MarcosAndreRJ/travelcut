using TravelCut.Contracts.Dtos;
using Xunit;
using static TravelCut.UnitTests.EndToEnd.E2EScenarioData;

namespace TravelCut.UnitTests.EndToEnd;

/// <summary>
/// FIXTURES DE PORTA (ffprobe, Whisper, silencedetect): não são prova de mídia real (ver E2EPortFixtures).
/// Cenário 4: cache de análise por fingerprint. O pipeline tem cache (3-Trabalho/cache/analysis); só resultados
/// "completed" são reaproveitados. Esperado: segunda análise sem mudanças não chama transcrição nem silêncio;
/// arquivo alterado refaz só a mídia alterada; force ignora o cache.
/// </summary>
public sealed class EndToEndCacheTests : IDisposable
{
    private readonly E2EWorkspace _ws = new();

    public void Dispose() => _ws.Dispose();

    [Fact]
    public async Task Cache_SegundaAnaliseSemMudancas_ReaproveitaResultado_MudancaRefazSoAMidia_ForceIgnora()
    {
        var root = _ws.ProjectRoot("Viagem Cache");
        var fixtures = Happy();
        using var host = E2EHost.Start(_ws.DatabasePath, fixtures);

        var projectId = await E2ESteps.CreateProjectAsync(host, root, "Viagem Cache");
        E2ESteps.PlaceMedia(root, HappyFiles);
        await E2ESteps.ScanAsync(host, projectId);
        var names = await E2ESteps.MediaNamesAsync(host, projectId);
        var mediaA = names.Single(kv => kv.Value.EndsWith(HappyClipA, StringComparison.Ordinal)).Key;

        // 1ª análise: uma chamada de transcrição e de silêncio por mídia.
        Assert.Equal("completed", (await E2ESteps.AnalyzeAsync(host, projectId)).GetProperty("state").GetString());
        Assert.Equal(1, fixtures.WhisperCalls(HappyClipA));
        Assert.Equal(1, fixtures.WhisperCalls(HappyClipB));
        Assert.Equal(1, fixtures.SilenceCalls(HappyClipA));
        Assert.Equal(1, fixtures.SilenceCalls(HappyClipB));
        Assert.Equal(3, (await E2ESteps.SuggestionsAsync(host, projectId)).Count);

        // 2ª análise sem mudanças: nada é recalculado; o resultado vem do cache.
        Assert.Equal("completed", (await E2ESteps.AnalyzeAsync(host, projectId)).GetProperty("state").GetString());
        Assert.Equal(1, fixtures.WhisperCalls(HappyClipA));
        Assert.Equal(1, fixtures.WhisperCalls(HappyClipB));
        Assert.Equal(1, fixtures.SilenceCalls(HappyClipA));
        Assert.Equal(1, fixtures.SilenceCalls(HappyClipB));

        var cached = await host.Ok("analysis.getMedia", new MediaAnalysisRequest(projectId, mediaA));
        Assert.Equal("completed", cached.GetProperty("transcriptionStatus").GetString());
        Assert.Equal(6, cached.GetProperty("transcript").GetProperty("words").GetArrayLength());
        Assert.Equal(3, (await E2ESteps.SuggestionsAsync(host, projectId)).Count);

        // Mudança real na mídia A (novo conteúdo e nova data de escrita): só ela é reanalisada.
        var fileA = Path.Combine(root, "1-Bruto", HappyClipA);
        E2EWorkspace.WriteSyntheticMedia(fileA, seed: 99);
        File.SetLastWriteTimeUtc(fileA, DateTime.UtcNow.AddHours(1));
        var rescan = await E2ESteps.ScanAsync(host, projectId);
        Assert.Equal(1, rescan.GetProperty("updatedAssets").GetInt32());

        Assert.Equal("completed", (await E2ESteps.AnalyzeAsync(host, projectId)).GetProperty("state").GetString());
        Assert.Equal(2, fixtures.WhisperCalls(HappyClipA));
        Assert.Equal(1, fixtures.WhisperCalls(HappyClipB));
        Assert.Equal(2, fixtures.SilenceCalls(HappyClipA));
        Assert.Equal(1, fixtures.SilenceCalls(HappyClipB));
        // Reanálise não duplica sugestões propostas.
        Assert.Equal(3, (await E2ESteps.SuggestionsAsync(host, projectId)).Count);

        // force=true ignora o cache em todas as mídias.
        Assert.Equal("completed", (await E2ESteps.AnalyzeAsync(host, projectId, force: true)).GetProperty("state").GetString());
        Assert.Equal(3, fixtures.WhisperCalls(HappyClipA));
        Assert.Equal(2, fixtures.WhisperCalls(HappyClipB));
    }
}
