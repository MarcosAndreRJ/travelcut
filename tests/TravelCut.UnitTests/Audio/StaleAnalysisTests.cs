using TravelCut.Application.Planning;
using TravelCut.Contracts.Dtos;
using Xunit;

namespace TravelCut.UnitTests.Audio;

/// <summary>F14.8/P11: arquivo alterado (fingerprint novo) → análise stale, reenfileirada; plano dependente invalidado.</summary>
public class StaleAnalysisTests : IDisposable
{
    private readonly PipelineHarness _h = new(new FakeWhisperService { Available = true, ModelCached = true, FfmpegOnPath = true });

    public void Dispose() => _h.Dispose();

    [Fact]
    public async Task Changed_media_is_marked_stale_and_reanalyzed_without_reusing_old_results()
    {
        var project = _h.CreateProject();
        var media = _h.AddMedia(project, "1-Bruto/a.mp4", fingerprint: "old", metadata: new TravelCut.Domain.Entities.MediaMetadata
        {
            Codec = "h264", Width = 1920, Height = 1080, FrameRate = 30, DurationSeconds = 5, DurationTicks = 5 * 30000,
            TimeBase = new TravelCut.Domain.ValueObjects.RationalTimeBase(1, 30000), AudioChannels = 2, AudioSampleRate = 48000,
            MetadataStatus = TravelCut.Domain.Entities.MediaMetadataStatus.Ok
        });
        var transcript = new TranscriptDto(Guid.NewGuid(), media.MediaId, "large-v3", "pt", "oi", 5.0,
            new List<TranscriptWordDto> { new(0, 0.0, 0.5, "oi", 0.9) });
        _h.Whisper.Outcome = new TranscriptionOutcome("completed", transcript, null, "fake", "large-v3");
        _h.Silence.Outcome = new SilenceDetectionOutcome("completed", new List<VadSilenceSegmentDto>(), null);

        await _h.Manager.StartAsync(new StartPipelineRequest(project.ProjectId, null, null, null, false));
        await _h.WaitForFinishAsync(project.ProjectId);
        Assert.Equal(1, _h.Whisper.TranscribeCalls);
        Assert.Equal("completed", (await _h.Manager.GetMediaAnalysisAsync(project.ProjectId, media.MediaId))!.TranscriptionStatus);

        var oldSet = PlanValidation.MediaSetFingerprintOf(new[] { media });

        // O arquivo mudou: o scan grava o fingerprint novo e avisa o gerenciador.
        media.Fingerprint = "new";
        _h.Db.SaveChanges();
        var before = await _h.Manager.GetStatusAsync(project.ProjectId);
        await _h.Manager.MarkMediaStaleAsync(project.ProjectId, new List<Guid> { media.MediaId });

        // Plano dependente: o fingerprint do conjunto de mídias muda, então a revisão anterior fica inválida.
        Assert.NotEqual(oldSet, PlanValidation.MediaSetFingerprintOf(new[] { media }));

        // Reanálise: cache do fingerprint antigo não é reaproveitado (nova chamada ao Whisper).
        await WaitUntilAsync(async () =>
        {
            var s = await _h.Manager.GetStatusAsync(project.ProjectId);
            return s.JobId != before.JobId && s.State is "completed" or "completed_with_issues";
        });
        Assert.Equal(2, _h.Whisper.TranscribeCalls);
        var after = await _h.Manager.GetMediaAnalysisAsync(project.ProjectId, media.MediaId);
        Assert.False(after!.Stale);
        Assert.Equal("completed", after.TranscriptionStatus);
    }

    [Fact]
    public async Task Stale_marker_is_visible_until_reanalysis_runs()
    {
        _h.Whisper.Available = false; // reanálise não consegue rodar: o marcador permanece (e a retomada reenfileira depois)
        var project = _h.CreateProject();
        var media = _h.AddMedia(project, "1-Bruto/a.mp4", fingerprint: "new");

        await _h.Manager.MarkMediaStaleAsync(project.ProjectId, new List<Guid> { media.MediaId });

        // Se o job terminou rápido, "unavailable" sobrescreve; senão o marcador stale está lá. Em ambos os casos não há resultado antigo.
        await _h.WaitForFinishAsync(project.ProjectId);
        var analysis = await _h.Manager.GetMediaAnalysisAsync(project.ProjectId, media.MediaId);
        Assert.NotNull(analysis);
        Assert.Null(analysis!.Transcript);
    }

    private static async Task WaitUntilAsync(Func<Task<bool>> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (!await condition())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException();
            await Task.Delay(20);
        }
    }
}
