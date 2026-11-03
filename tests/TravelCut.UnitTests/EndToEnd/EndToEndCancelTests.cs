using TravelCut.Contracts.Dtos;
using Xunit;
using static TravelCut.UnitTests.EndToEnd.E2EScenarioData;

namespace TravelCut.UnitTests.EndToEnd;

/// <summary>
/// FIXTURES DE PORTA (ffprobe, Whisper, silencedetect): não são prova de mídia real (ver E2EPortFixtures).
/// Cenário 3: análise cancelada no meio (silêncio "lento" até o cancelamento). Esperado: estado canceled,
/// nenhuma sugestão parcial, trava de análise ocupada enquanto roda e liberada depois, e nova análise funcionando.
/// </summary>
public sealed class EndToEndCancelTests : IDisposable
{
    private readonly E2EWorkspace _ws = new();

    public void Dispose() => _ws.Dispose();

    [Fact]
    public async Task Cancelamento_NoMeioDaAnalise_FicaCanceladoSemSugestoesParciais_ENovaAnaliseFunciona()
    {
        var root = _ws.ProjectRoot("Viagem Cancelada");
        var fixtures = Happy();
        fixtures.HoldSilence = true;
        using var host = E2EHost.Start(_ws.DatabasePath, fixtures);

        var projectId = await E2ESteps.CreateProjectAsync(host, root, "Viagem Cancelada");
        E2ESteps.PlaceMedia(root, HappyFiles);
        await E2ESteps.ScanAsync(host, projectId);

        var started = await host.Ok("pipeline.start", new StartPipelineRequest(projectId, null, null, null, false));
        var jobId = started.GetProperty("jobId").GetGuid();

        // A análise está no meio: a transcrição da primeira mídia concluiu e o silêncio está preso até o cancelamento.
        await fixtures.SilenceEntered.WaitAsync(TimeSpan.FromSeconds(30));

        // Trava de análise ocupada: outra análise do mesmo projeto é recusada enquanto esta roda.
        var busy = await host.Fail("pipeline.start", new StartPipelineRequest(projectId, null, null, null, false));
        Assert.Equal("PIPELINE_IN_PROGRESS", busy);

        var cancel = await host.Ok("pipeline.cancel", new PipelineJobRequest(jobId));
        Assert.True(cancel.GetProperty("canceled").GetBoolean());

        var status = await E2ESteps.WaitTerminalAsync(host, projectId);
        Assert.Equal("canceled", status.GetProperty("state").GetString());

        // Sem sugestões parciais: a etapa de candidatos nunca rodou, então nada foi gravado.
        Assert.Empty(await E2ESteps.SuggestionsAsync(host, projectId));

        // Nova análise (silêncio normal) conclui; a trava foi liberada.
        fixtures.HoldSilence = false;
        var second = await E2ESteps.AnalyzeAsync(host, projectId);
        Assert.Equal("completed", second.GetProperty("state").GetString());

        // Exatamente as 3 sugestões esperadas: a análise cancelada não deixou duplicatas nem restos.
        Assert.Equal(3, (await E2ESteps.SuggestionsAsync(host, projectId)).Count);

        // A transcrição já concluída antes do cancelamento foi reaproveitada: uma chamada por mídia.
        Assert.Equal(1, fixtures.WhisperCalls(HappyClipA));
        Assert.Equal(1, fixtures.WhisperCalls(HappyClipB));
    }
}
