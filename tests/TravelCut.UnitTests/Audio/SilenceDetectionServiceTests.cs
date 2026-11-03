using System.Diagnostics;
using System.Globalization;
using Microsoft.Extensions.Logging.Abstractions;
using TravelCut.Application.Audio;
using TravelCut.Application.Ports;
using TravelCut.Contracts.Dtos;
using TravelCut.Infrastructure.Audio;
using Xunit;

namespace TravelCut.UnitTests.Audio;

public class SilenceDetectionStderrParseTests
{
    private const double Noise = -30.0;

    [Fact]
    public void Basic_Fixture_ParsesAllClosedSilences_WithInvariantNumbersAndBands()
    {
        var stderr = FixtureFiles.Read("silencedetect_basic.stderr.txt");

        var parsed = SilenceDetectionService.ParseSilencedetectStderr(stderr, Noise);

        Assert.Equal(0, parsed.DiscardedOpenSilences);
        Assert.Equal(60.04, parsed.MediaDurationSeconds!.Value, 3);
        Assert.Equal(3, parsed.Segments.Count);

        Assert.Equal(0.0, parsed.Segments[0].StartSeconds);
        Assert.Equal(1.234, parsed.Segments[0].EndSeconds);
        Assert.Equal("medium", parsed.Segments[0].Confidence);

        Assert.Equal(12.5, parsed.Segments[1].StartSeconds);
        Assert.Equal(14.75, parsed.Segments[1].EndSeconds);
        Assert.Equal(2.25, parsed.Segments[1].DurationSeconds);
        Assert.Equal("high", parsed.Segments[1].Confidence);

        Assert.Equal(30.125, parsed.Segments[2].StartSeconds);
        Assert.Equal("low", parsed.Segments[2].Confidence);
        Assert.All(parsed.Segments, s => Assert.Equal(Noise, s.NoiseDb));
    }

    [Fact]
    public void Parsing_IsIndependentOfCurrentCulture()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("pt-BR"); // vírgula decimal
            var stderr = FixtureFiles.Read("silencedetect_basic.stderr.txt");

            var parsed = SilenceDetectionService.ParseSilencedetectStderr(stderr, Noise);

            Assert.Equal(3, parsed.Segments.Count);
            Assert.Equal(12.5, parsed.Segments[1].StartSeconds);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public void OpenSilenceAtEnd_ClosesAtMediaDuration_WhenDurationIsReported()
    {
        var stderr = FixtureFiles.Read("silencedetect_open_tail_with_duration.stderr.txt");

        var parsed = SilenceDetectionService.ParseSilencedetectStderr(stderr, Noise);

        Assert.Equal(0, parsed.DiscardedOpenSilences);
        Assert.Equal(2, parsed.Segments.Count);
        var tail = parsed.Segments[1];
        Assert.Equal(58.75, tail.StartSeconds);
        Assert.Equal(60.04, tail.EndSeconds);
        Assert.Equal(1.29, tail.DurationSeconds);
        Assert.Equal("medium", tail.Confidence);
    }

    [Fact]
    public void OpenSilenceAtEnd_IsDiscardedAndCounted_WhenDurationIsNotReported()
    {
        var stderr = FixtureFiles.Read("silencedetect_open_tail_no_duration.stderr.txt");

        var parsed = SilenceDetectionService.ParseSilencedetectStderr(stderr, Noise);

        Assert.Null(parsed.MediaDurationSeconds);
        Assert.Equal(1, parsed.DiscardedOpenSilences);
        Assert.Single(parsed.Segments); // apenas o silêncio fechado
        Assert.Equal(5.5, parsed.Segments[0].StartSeconds);
    }

    [Theory]
    [InlineData(2.0, "high")]
    [InlineData(2.5, "high")]
    [InlineData(1.0, "medium")]
    [InlineData(1.99, "medium")]
    [InlineData(0.99, "low")]
    [InlineData(0.6, "low")]
    public void ConfidenceBand_FollowsDurationThresholds(double duration, string expected)
    {
        Assert.Equal(expected, SilenceDetectionService.ConfidenceBandForDuration(duration));
    }

    [Fact]
    public void Arguments_UseSpecifiedSilencedetectFilter_AndSingleItemPaths()
    {
        var args = SilenceDetectionService.BuildSilenceDetectArguments(
            @"C:\Videos\Clip ""aspas"" e espaço.mp4",
            new SilenceDetectionOptions(MinSilenceDurationSeconds: 0.6, NoiseToleranceDb: -30.0));

        Assert.Equal(new[]
        {
            "-hide_banner", "-nostats", "-i", @"C:\Videos\Clip ""aspas"" e espaço.mp4",
            "-vn", "-af", "silencedetect=noise=-30dB:d=0.6", "-f", "null", "-"
        }, args);
    }
}

public class SilenceDetectionServiceTests
{
    private static readonly string FfmpegVersionLine = "ffmpeg version 6.1-test Copyright (c) 2000-2024 the FFmpeg developers";

    private static SilenceDetectionService CreateService(FakeProcessRunner runner, TimeSpan? timeout = null) =>
        new(NullLogger<SilenceDetectionService>.Instance, runner, ffmpegPath: "ffmpeg-explicit-test", silenceTimeout: timeout);

    private static FakeProcessRunner FfmpegPresent(string stderr = "", ProcessRunStatus? silenceStatus = null, int exitCode = 0)
    {
        return new FakeProcessRunner(r =>
        {
            if (r.Arguments.Count == 1 && r.Arguments[0] == "-version")
                return FakeProcessRunner.Completed(0, FfmpegVersionLine + "\n");
            if (silenceStatus is { } status)
                return FakeProcessRunner.Status(status);
            return FakeProcessRunner.Completed(exitCode, string.Empty, stderr);
        });
    }

    private static string CreateMediaFile()
    {
        var dir = Path.Combine(Path.GetTempPath(), "TravelCut Tests " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var file = Path.Combine(dir, "clip 'a' & b.mp4");
        File.WriteAllText(file, "fixture");
        return file;
    }

    [Fact]
    public async Task FfmpegMissing_ReturnsUnavailable_WithoutSegments()
    {
        var runner = new FakeProcessRunner(_ => FakeProcessRunner.NotStarted());
        var service = CreateService(runner);

        var outcome = await service.DetectSilenceAsync(CreateMediaFile());

        Assert.Equal("unavailable", outcome.Status);
        Assert.Empty(outcome.Segments);
        Assert.Contains("FFmpeg", outcome.Reason);
        Assert.False(service.IsFfmpegAvailable());
        Assert.Null(service.GetFfmpegVersion());
        // Nenhum silêncio é executado sem FFmpeg.
        Assert.DoesNotContain(runner.Requests, r => r.Arguments.Contains("-af"));
    }

    [Fact]
    public async Task FfmpegPresent_ParsesFixture_AndUsesArgumentListWithFullPath()
    {
        var media = CreateMediaFile();
        var runner = FfmpegPresent(FixtureFiles.Read("silencedetect_basic.stderr.txt"));
        var service = CreateService(runner);

        var outcome = await service.DetectSilenceAsync(media);

        Assert.Equal("completed", outcome.Status);
        Assert.Null(outcome.Reason);
        Assert.Equal(3, outcome.Segments.Count);
        Assert.Equal("ffmpeg version 6.1-test Copyright (c) 2000-2024 the FFmpeg developers", service.GetFfmpegVersion());
        Assert.True(service.IsFfmpegAvailable());

        var silence = runner.Requests.Single(r => r.Arguments.Contains("-af"));
        Assert.Equal("ffmpeg-explicit-test", silence.FileName);
        var iIndex = silence.Arguments.ToList().IndexOf("-i");
        Assert.Equal(Path.GetFullPath(media), silence.Arguments[iIndex + 1]);
        Assert.Contains("silencedetect=noise=-30dB:d=0.6", silence.Arguments);
    }

    [Fact]
    public async Task FfmpegExitCodeNonZero_IsFailed()
    {
        var runner = FfmpegPresent(exitCode: 1);
        var service = CreateService(runner);

        var outcome = await service.DetectSilenceAsync(CreateMediaFile());

        Assert.Equal("failed", outcome.Status);
        Assert.Empty(outcome.Segments);
    }

    [Fact]
    public async Task SilenceProcessTimeout_IsFailed()
    {
        var runner = FfmpegPresent(silenceStatus: ProcessRunStatus.TimedOut);
        var service = CreateService(runner, TimeSpan.FromSeconds(1));

        var outcome = await service.DetectSilenceAsync(CreateMediaFile());

        Assert.Equal("failed", outcome.Status);
        Assert.Contains("Tempo limite", outcome.Reason);
        Assert.Empty(outcome.Segments);
        Assert.Equal(TimeSpan.FromSeconds(1), runner.Requests.Single(r => r.Arguments.Contains("-af")).Timeout);
    }

    [Fact]
    public async Task SilenceProcessCanceled_IsCanceled()
    {
        var runner = FfmpegPresent(silenceStatus: ProcessRunStatus.Canceled);
        var service = CreateService(runner);

        var outcome = await service.DetectSilenceAsync(CreateMediaFile());

        Assert.Equal("canceled", outcome.Status);
        Assert.Empty(outcome.Segments);
    }

    [Fact]
    public async Task AlreadyCanceledToken_IsCanceled()
    {
        var runner = FfmpegPresent(FixtureFiles.Read("silencedetect_basic.stderr.txt"));
        var service = CreateService(runner);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var outcome = await service.DetectSilenceAsync(CreateMediaFile(), ct: cts.Token);

        Assert.Equal("canceled", outcome.Status);
    }

    [Fact]
    public async Task MediaFileMissing_IsFailed()
    {
        var runner = FfmpegPresent(FixtureFiles.Read("silencedetect_basic.stderr.txt"));
        var service = CreateService(runner);

        var outcome = await service.DetectSilenceAsync(Path.Combine(Path.GetTempPath(), "nao-existe-" + Guid.NewGuid().ToString("N") + ".mp4"));

        Assert.Equal("failed", outcome.Status);
        Assert.Empty(outcome.Segments);
    }

    [Fact]
    public async Task InvalidOptions_AreFailed()
    {
        var service = CreateService(FfmpegPresent());

        var outcome = await service.DetectSilenceAsync(CreateMediaFile(),
            new SilenceDetectionOptions(MinSilenceDurationSeconds: 0.0, NoiseToleranceDb: -30));

        Assert.Equal("failed", outcome.Status);
    }

    [Fact]
    public async Task ProcessRunner_RealTimeout_KillsProcessAndReportsTimedOut()
    {
        // Subprocesso real de Windows (ping) apenas para validar timeout/kill; não usa FFmpeg/Python.
        var runner = new ProcessRunner();
        var sw = Stopwatch.StartNew();

        var result = await runner.RunAsync(new ProcessRunRequest(
            "ping", new[] { "-n", "60", "127.0.0.1" }, TimeSpan.FromMilliseconds(400)));

        Assert.Equal(ProcessRunStatus.TimedOut, result.Status);
        Assert.Null(result.ExitCode);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(30));
    }

    [Fact]
    public async Task ProcessRunner_Cancellation_ReportsCanceled()
    {
        var runner = new ProcessRunner();
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));

        var result = await runner.RunAsync(new ProcessRunRequest(
            "ping", new[] { "-n", "60", "127.0.0.1" }, TimeSpan.FromMinutes(1)), cts.Token);

        Assert.Equal(ProcessRunStatus.Canceled, result.Status);
    }

    [Fact]
    public async Task ProcessRunner_MissingExecutable_IsNotStarted_WithoutPathInMessage()
    {
        var runner = new ProcessRunner();
        var missing = @"K:\nao-existe\sub\binario-inexistente.exe";

        var result = await runner.RunAsync(new ProcessRunRequest(missing, new[] { "x" }, TimeSpan.FromSeconds(5)));

        Assert.Equal(ProcessRunStatus.NotStarted, result.Status);
        Assert.NotNull(result.StartError);
        Assert.DoesNotContain("nao-existe", result.StartError);
    }
}

public class HesitationDetectionTests
{
    private static SilenceDetectionService Service() =>
        new(NullLogger<SilenceDetectionService>.Instance, new FakeProcessRunner(_ => FakeProcessRunner.NotStarted()), ffmpegPath: "x");

    private static TranscriptDto Transcript(double duration, params (string Text, double Start, double End)[] words) =>
        new(
            TranscriptId: Guid.NewGuid(),
            MediaId: Guid.NewGuid(),
            ModelId: "large-v3",
            Language: "pt",
            FullText: string.Join(' ', words.Select(w => w.Text)),
            AudioDurationSeconds: duration,
            Words: words.Select((w, i) => new TranscriptWordDto(i + 1, w.Start, w.End, w.Text, 0.9)).ToList());

    private static List<HesitationCandidateDto> Detect(TranscriptDto t, params VadSilenceSegmentDto[] silences) =>
        Service().DetectHesitationsAndFillers(t, silences);

    [Fact]
    public void Filler_SurroundedByPause_IsHigh_AndDoesNotNeedReview()
    {
        var result = Detect(Transcript(10,
            ("eu", 0.0, 0.5), ("hum", 1.0, 1.4), ("vou", 1.8, 2.2)));

        var h = Assert.Single(result);
        Assert.Equal("filler", h.Type);
        Assert.Equal("hum", h.DetectedWord);
        Assert.Equal("high", h.ConfidenceBand);
        Assert.False(h.NeedsHumanReview);
        Assert.Equal(1.0, h.StartSeconds);
        Assert.Equal(1.4, h.EndSeconds);
    }

    [Fact]
    public void Filler_WithoutPause_IsMedium_AndNeedsReview()
    {
        var result = Detect(Transcript(10,
            ("eu", 0.0, 0.5), ("hum", 0.6, 0.9), ("vou", 1.0, 1.4)));

        var h = Assert.Single(result);
        Assert.Equal("medium", h.ConfidenceBand);
        Assert.True(h.NeedsHumanReview);
    }

    [Fact]
    public void Filler_CoveredBySilenceSegment_IsHigh()
    {
        // Gaps de timestamp de 0,1 s; a pausa vem do silencedetect (0,5 s cobrindo os dois lados).
        var silence = new VadSilenceSegmentDto(0.5, 1.0, 0.5, -30, "high");
        var result = Detect(Transcript(10,
            ("eu", 0.0, 0.5), ("hum", 0.6, 0.9), ("vou", 1.0, 1.4)), silence);

        var h = Assert.Single(result);
        Assert.Equal("high", h.ConfidenceBand);
        Assert.False(h.NeedsHumanReview);
    }

    [Fact]
    public void Tipo_IsNeverHigh_AndNeedsReview()
    {
        var result = Detect(Transcript(10,
            ("vamos", 0.0, 0.5), ("tipo", 1.0, 1.3), ("andar", 1.7, 2.0)));

        var h = Assert.Single(result);
        Assert.Equal("filler", h.Type);
        Assert.Equal("tipo", h.DetectedWord);
        Assert.NotEqual("high", h.ConfidenceBand);
        Assert.True(h.NeedsHumanReview);
    }

    [Fact]
    public void TipoAssim_IsDetectedAsPhrase_WithContext()
    {
        var result = Detect(Transcript(10,
            ("vamos", 0.0, 0.5), ("tipo", 0.6, 0.9), ("assim", 1.0, 1.3), ("andar", 1.4, 1.8)));

        var h = Assert.Single(result);
        Assert.Equal("filler", h.Type);
        Assert.Equal("tipo assim", h.DetectedWord);
        Assert.Equal("vamos", h.ContextBefore);
        Assert.Equal("andar", h.ContextAfter);
        Assert.NotEqual("high", h.ConfidenceBand);
    }

    [Fact]
    public void Ne_IsAtMostMedium_AndNeedsReview()
    {
        var result = Detect(Transcript(10,
            ("é", 0.0, 0.2), ("né", 1.0, 1.3), ("bom", 1.9, 2.2)));

        var h = Assert.Single(result, c => c.DetectedWord == "né");
        Assert.NotEqual("high", h.ConfidenceBand);
        Assert.True(h.NeedsHumanReview);
    }

    [Fact]
    public void Entao_OnlyWhenIsolatedByPause()
    {
        var isolated = Detect(Transcript(10,
            ("isso", 0.0, 0.4), ("então", 1.0, 1.5), ("bom", 2.0, 2.3)));
        var h = Assert.Single(isolated);
        Assert.Equal("filler", h.Type);
        Assert.Equal("medium", h.ConfidenceBand);

        var notIsolated = Detect(Transcript(10,
            ("isso", 0.0, 0.4), ("então", 0.45, 0.9), ("bom", 0.95, 1.2)));
        Assert.Empty(notIsolated);
    }

    [Fact]
    public void Eh_VerbUsage_IsNotReported()
    {
        var result = Detect(Transcript(10,
            ("isso", 0.0, 0.4), ("é", 0.45, 0.6), ("bom", 0.65, 1.0)));

        Assert.Empty(result);
    }

    [Fact]
    public void Eh_IsolatedByPause_IsHigh()
    {
        var result = Detect(Transcript(10,
            ("isso", 0.0, 0.4), ("é", 1.0, 1.2), ("bom", 1.8, 2.1)));

        var h = Assert.Single(result);
        Assert.Equal("high", h.ConfidenceBand);
        Assert.Equal("é", h.DetectedWord);
    }

    [Fact]
    public void Prolongation_IsDetected_AndNeverHigh()
    {
        var result = Detect(Transcript(10, ("booooom", 0.0, 1.5)));

        var h = Assert.Single(result);
        Assert.Equal("prolongation", h.Type);
        Assert.NotEqual("high", h.ConfidenceBand);
        Assert.True(h.NeedsHumanReview);
    }

    [Fact]
    public void Repetition_SingleWord_IsDetected()
    {
        var result = Detect(Transcript(10,
            ("vamos", 0.5, 0.9), ("vamos", 1.0, 1.4), ("lá", 1.5, 1.8)));

        var h = Assert.Single(result);
        Assert.Equal("repetition", h.Type);
        Assert.Equal("vamos vamos", h.DetectedWord);
        Assert.True(h.NeedsHumanReview);
    }

    [Fact]
    public void Repetition_Bigram_IsDetected()
    {
        var result = Detect(Transcript(10,
            ("muito", 0.0, 0.4), ("bom", 0.5, 0.8), ("muito", 0.9, 1.3), ("bom", 1.4, 1.7)));

        var h = Assert.Single(result);
        Assert.Equal("repetition", h.Type);
        Assert.Equal("muito bom muito bom", h.DetectedWord);
    }

    [Fact]
    public void FalseStart_NaoPera_IsDetected()
    {
        var result = Detect(Transcript(10,
            ("não,", 0.0, 0.3), ("pera", 0.4, 0.7), ("vamos", 1.2, 1.5)));

        var h = Assert.Single(result);
        Assert.Equal("false_start", h.Type);
        Assert.Equal("não, pera", h.DetectedWord);
    }

    [Fact]
    public void FalseStart_PeraiAlone_IsDetected()
    {
        var result = Detect(Transcript(10, ("peraí", 0.0, 0.5)));

        var h = Assert.Single(result);
        Assert.Equal("false_start", h.Type);
    }

    [Fact]
    public void FalseStart_DeixaEu_AndVouDeNovo_AreDetected()
    {
        var deixa = Detect(Transcript(10, ("deixa", 0.0, 0.4), ("eu", 0.5, 0.8), ("fazer", 0.9, 1.3)));
        Assert.Equal("false_start", Assert.Single(deixa).Type);
        Assert.Equal("deixa eu", Assert.Single(deixa).DetectedWord);

        var novo = Detect(Transcript(10, ("vou", 0.0, 0.3), ("de", 0.4, 0.5), ("novo", 0.6, 0.9), ("agora", 1.0, 1.3)));
        var h = Assert.Single(novo);
        Assert.Equal("false_start", h.Type);
        Assert.Equal("vou de novo", h.DetectedWord);
        Assert.Equal(0.0, h.StartSeconds);
        Assert.Equal(0.9, h.EndSeconds);
    }

    [Theory]
    [InlineData("kkkkkk")]
    [InlineData("kkk")]
    [InlineData("hahaha")]
    [InlineData("rsrsrs")]
    public void Laughter_IsNeverReported(string laugh)
    {
        var result = Detect(Transcript(10,
            ("ai", 0.0, 0.3), (laugh, 0.5, 1.0), (laugh, 1.2, 1.7), ("que", 2.0, 2.2)));

        Assert.Empty(result);
    }

    [Fact]
    public void Laughter_IsNotProlongation_EvenWithLongDuration()
    {
        var result = Detect(Transcript(10, ("kkkkkkkkkk", 0.0, 2.5)));

        Assert.Empty(result);
    }

    [Fact]
    public void Context_IsLimitedToSixWordsEachSide()
    {
        var words = new (string, double, double)[]
        {
            ("um", 0.0, 0.2), ("dois", 0.3, 0.5), ("três", 0.6, 0.8), ("quatro", 0.9, 1.1),
            ("cinco", 1.2, 1.4), ("seis", 1.5, 1.7), ("sete", 1.8, 2.0),
            ("hum", 2.6, 3.0), // índice 7, pausa dos dois lados
            ("oito", 3.5, 3.7), ("nove", 3.8, 4.0)
        };

        var result = Detect(Transcript(10, words));

        var h = Assert.Single(result);
        Assert.Equal("dois três quatro cinco seis sete", h.ContextBefore);
        Assert.Equal("oito nove", h.ContextAfter);
        Assert.Equal(6, h.ContextBefore.Split(' ').Length);
        Assert.True(h.ContextAfter.Split(' ').Length <= 6);
    }

    [Fact]
    public void EmptyTranscript_ReturnsNoCandidates()
    {
        var empty = new TranscriptDto(Guid.NewGuid(), Guid.NewGuid(), "large-v3", "pt", "", 5.0, new List<TranscriptWordDto>());

        Assert.Empty(Service().DetectHesitationsAndFillers(empty, new List<VadSilenceSegmentDto>()));
    }

    [Fact]
    public void NeedsHumanReview_IsTrue_ExceptForHighFiller()
    {
        var result = Detect(Transcript(20,
            ("eu", 0.0, 0.5), ("hum", 1.0, 1.4), ("vou", 1.8, 2.2),
            ("tipo", 3.5, 3.8), ("andar", 4.5, 4.9),
            ("booooom", 6.0, 7.5),
            ("vamos", 9.0, 9.4), ("vamos", 9.5, 9.9)));

        Assert.NotEmpty(result);
        foreach (var h in result)
        {
            var expectedNeedsReview = !(h.Type == "filler" && h.ConfidenceBand == "high");
            Assert.Equal(expectedNeedsReview, h.NeedsHumanReview);
        }
    }
}
