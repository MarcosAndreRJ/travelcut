using TravelCut.Application.Ports;
using TravelCut.Application.UseCases;
using TravelCut.Contracts.Dtos;
using TravelCut.Domain.Entities;
using TravelCut.Domain.Enums;
using Xunit;

namespace TravelCut.UnitTests.Preview;

public class WaveformTests
{
    private const double Full = 32768.0;

    // ------------------------------------------------------------ acumulador (bytes s16le conhecidos)

    [Fact]
    public void Accumulator_ParsesKnownSamplesIntoBucketPeaks()
    {
        // 8 Hz e 0,5 s => 4 amostras; 2 buckets => 2 amostras por bucket (sem erro de ponto flutuante).
        var acc = new PreviewWaveformAccumulator(buckets: 2, sampleRate: 8, durationSeconds: 0.5);

        acc.Feed(PcmBytes.S16(0, 16384, -8192, 32767));

        var peaks = acc.ToPeaks();
        Assert.Equal(2, peaks.Count);
        Assert.Equal(0.5, peaks[0], 9);
        Assert.Equal(32767 / Full, peaks[1], 9);
        Assert.Equal(4, acc.SamplesProcessed);
    }

    [Fact]
    public void Accumulator_MinimumValueUsesAbsoluteValueAndClampsToOne()
    {
        var acc = new PreviewWaveformAccumulator(buckets: 1, sampleRate: 8, durationSeconds: 1);

        acc.Feed(PcmBytes.S16(short.MinValue, 100));

        Assert.Equal(1.0, acc.ToPeaks()[0], 9);
    }

    [Fact]
    public void Accumulator_ByteBoundariesDoNotChangeResult()
    {
        byte[] data = PcmBytes.S16(0, 16384, -8192, 32767, -32768, 1000, 2000, 3000);
        var whole = new PreviewWaveformAccumulator(4, 8, 1);
        whole.Feed(data);

        var split = new PreviewWaveformAccumulator(4, 8, 1);
        // Divisões em ímpar: um byte órfão precisa ser guardado para o próximo bloco.
        split.Feed(data.AsSpan(0, 3));
        split.Feed(data.AsSpan(3, 5));
        split.Feed(data.AsSpan(8));

        Assert.Equal(whole.ToPeaks(), split.ToPeaks());
        Assert.Equal(8, split.SamplesProcessed);
    }

    [Fact]
    public void Accumulator_SilenceIsZeroNotMissing()
    {
        var acc = new PreviewWaveformAccumulator(buckets: 2, sampleRate: 8, durationSeconds: 0.5);

        acc.Feed(PcmBytes.S16(0, 0, 0, 0));

        Assert.All(acc.ToPeaks(), p => Assert.Equal(0.0, p));
        Assert.Equal(4, acc.SamplesProcessed);
    }

    [Theory]
    [InlineData(0, 1.0)]
    [InlineData(-1, 1.0)]
    public void Accumulator_RejectsInvalidGeometry(int buckets, double duration)
    {
        Assert.ThrowsAny<ArgumentOutOfRangeException>(() => new PreviewWaveformAccumulator(buckets, 8000, duration));
    }

    // ------------------------------------------------------------ caso de uso

    private static WaveformRequest Request(Harness h, Guid mediaId, double start = 0, double end = 1, int buckets = 16) =>
        new(h.Project.ProjectId, mediaId, start, end, buckets);

    [Fact]
    public async Task ValidRequest_ReturnsPeaksAndRunsFfmpegWithExactArguments()
    {
        using var h = new Harness();
        h.Ffmpeg.Path = @"C:\ffmpeg\bin\ffmpeg.exe";
        var id = Guid.NewGuid();
        h.AddMedia("1-Bruto/voz.mp4", fingerprint: "fp-wave", metadata: Harness.OkMetadata(id, "h264", 10), mediaId: id);

        // 1 s a 8 kHz => 8000 amostras; primeira metade em 0,5, segunda em silêncio (zero, não ausência).
        var samples = new short[8000];
        for (int i = 0; i < 4000; i++) samples[i] = 16384;
        h.Binary.Stdout = PcmBytes.S16(samples);

        var result = await h.UseCases.GetWaveformAsync(Request(h, id));

        Assert.True(result.Ok);
        var dto = result.Value!;
        Assert.Equal("ok", dto.Status);
        Assert.Equal(0.0, dto.StartSeconds);
        Assert.Equal(1.0 / 16, dto.BucketSeconds, 12);
        Assert.Equal(16, dto.Peaks.Count);
        for (int i = 0; i < 8; i++) Assert.Equal(0.5, dto.Peaks[i], 9);
        for (int i = 8; i < 16; i++) Assert.Equal(0.0, dto.Peaks[i], 9);

        var call = Assert.Single(h.Binary.Calls);
        string expectedInput = h.FullPath("1-Bruto/voz.mp4");
        Assert.Equal(
            new[]
            {
                "-hide_banner", "-nostats",
                "-ss", "0.000000",
                "-t", "1.000000",
                "-i", expectedInput,
                "-vn", "-ac", "1", "-ar", "8000",
                "-f", "s16le", "-"
            },
            call.Arguments);
        Assert.Equal(PreviewUseCases.WaveformTimeout, call.Timeout);
    }

    [Theory]
    [InlineData(15)]
    [InlineData(2001)]
    [InlineData(0)]
    public async Task BucketsOutOfRange_IsInvalidArgumentAndNoProcess(int buckets)
    {
        using var h = new Harness();
        h.Ffmpeg.Path = @"C:\ffmpeg\bin\ffmpeg.exe";
        var id = Guid.NewGuid();
        h.AddMedia("1-Bruto/a.mp4", metadata: Harness.OkMetadata(id, "h264", 10), mediaId: id);

        var result = await h.UseCases.GetWaveformAsync(Request(h, id, buckets: buckets));

        Assert.False(result.Ok);
        Assert.Equal("INVALID_ARGUMENT", result.ErrorCode);
        Assert.Empty(h.Binary.Calls);
    }

    [Theory]
    [InlineData(-1.0, 1.0)]
    [InlineData(2.0, 1.0)]
    [InlineData(0.0, 11.0)]
    public async Task IntervalOutsideKnownDuration_IsInvalidArgument(double start, double end)
    {
        using var h = new Harness();
        h.Ffmpeg.Path = @"C:\ffmpeg\bin\ffmpeg.exe";
        var id = Guid.NewGuid();
        h.AddMedia("1-Bruto/a.mp4", metadata: Harness.OkMetadata(id, "h264", 10), mediaId: id);

        var result = await h.UseCases.GetWaveformAsync(Request(h, id, start, end));

        Assert.False(result.Ok);
        Assert.Equal("INVALID_ARGUMENT", result.ErrorCode);
        Assert.Empty(h.Binary.Calls);
    }

    [Fact]
    public async Task UnknownDuration_IsInvalidArgument()
    {
        using var h = new Harness();
        h.Ffmpeg.Path = @"C:\ffmpeg\bin\ffmpeg.exe";
        var id = Guid.NewGuid();
        var md = Harness.OkMetadata(id, "h264", 10);
        md.MetadataStatus = MediaMetadataStatus.FfprobeUnavailable;
        h.AddMedia("1-Bruto/a.mp4", metadata: md, mediaId: id);

        var result = await h.UseCases.GetWaveformAsync(Request(h, id));

        Assert.False(result.Ok);
        Assert.Equal("INVALID_ARGUMENT", result.ErrorCode);
        Assert.Empty(h.Binary.Calls);
    }

    [Fact]
    public async Task WithoutFfmpeg_IsFfmpegUnavailableWithReason()
    {
        using var h = new Harness();
        var id = Guid.NewGuid();
        h.AddMedia("1-Bruto/a.mp4", metadata: Harness.OkMetadata(id, "h264", 10), mediaId: id);

        var result = await h.UseCases.GetWaveformAsync(Request(h, id));

        Assert.True(result.Ok);
        Assert.Equal("ffmpeg_unavailable", result.Value!.Status);
        Assert.False(string.IsNullOrWhiteSpace(result.Value.Reason));
        Assert.Empty(result.Value.Peaks);
        Assert.Empty(h.Binary.Calls);
    }

    [Fact]
    public async Task MetadataWithoutAudio_IsNoAudioAndNoProcess()
    {
        using var h = new Harness();
        h.Ffmpeg.Path = @"C:\ffmpeg\bin\ffmpeg.exe";
        var id = Guid.NewGuid();
        h.AddMedia("1-Bruto/mudo.mp4", metadata: Harness.OkMetadata(id, "h264", 10, audioChannels: 0), mediaId: id);

        var result = await h.UseCases.GetWaveformAsync(Request(h, id));

        Assert.True(result.Ok);
        Assert.Equal("no_audio", result.Value!.Status);
        Assert.Empty(result.Value.Peaks);
        Assert.Empty(h.Binary.Calls);
    }

    [Fact]
    public async Task FfmpegDecodedNoSamples_IsNoAudioNotSilence()
    {
        using var h = new Harness();
        h.Ffmpeg.Path = @"C:\ffmpeg\bin\ffmpeg.exe";
        var id = Guid.NewGuid();
        h.AddMedia("1-Bruto/a.mp4", metadata: Harness.OkMetadata(id, "h264", 10), mediaId: id);
        h.Binary.Stdout = Array.Empty<byte>();

        var result = await h.UseCases.GetWaveformAsync(Request(h, id));

        Assert.Equal("no_audio", result.Value!.Status);
    }

    [Fact]
    public async Task FfmpegNonZeroExit_IsFailed()
    {
        using var h = new Harness();
        h.Ffmpeg.Path = @"C:\ffmpeg\bin\ffmpeg.exe";
        var id = Guid.NewGuid();
        h.AddMedia("1-Bruto/a.mp4", metadata: Harness.OkMetadata(id, "h264", 10), mediaId: id);
        h.Binary.ExitCode = 1;

        var result = await h.UseCases.GetWaveformAsync(Request(h, id));

        Assert.Equal("failed", result.Value!.Status);
        Assert.Contains("código 1", result.Value.Reason);
        Assert.Empty(Directory.Exists(Path.Combine(h.Root, "3-Trabalho", "cache", "waveforms"))
            ? Directory.GetFiles(Path.Combine(h.Root, "3-Trabalho", "cache", "waveforms"))
            : Array.Empty<string>());
    }

    [Fact]
    public async Task Timeout_IsFailedWithReason()
    {
        using var h = new Harness();
        h.Ffmpeg.Path = @"C:\ffmpeg\bin\ffmpeg.exe";
        var id = Guid.NewGuid();
        h.AddMedia("1-Bruto/a.mp4", metadata: Harness.OkMetadata(id, "h264", 10), mediaId: id);
        h.Binary.Status = ProcessRunStatus.TimedOut;

        var result = await h.UseCases.GetWaveformAsync(Request(h, id));

        Assert.Equal("failed", result.Value!.Status);
        Assert.Contains("Tempo limite", result.Value.Reason);
    }

    [Fact]
    public async Task CachedResult_IsReusedWithoutSecondProcess()
    {
        using var h = new Harness();
        h.Ffmpeg.Path = @"C:\ffmpeg\bin\ffmpeg.exe";
        var id = Guid.NewGuid();
        h.AddMedia("1-Bruto/a.mp4", fingerprint: "fp-cache", metadata: Harness.OkMetadata(id, "h264", 10), mediaId: id);
        var samples = new short[8000];
        for (int i = 0; i < 8000; i++) samples[i] = (short)(i % 2 == 0 ? 8192 : -8192);
        h.Binary.Stdout = PcmBytes.S16(samples);

        var first = await h.UseCases.GetWaveformAsync(Request(h, id));
        var second = await h.UseCases.GetWaveformAsync(Request(h, id));

        Assert.Single(h.Binary.Calls);
        Assert.Equal("ok", first.Value!.Status);
        Assert.Equal("ok", second.Value!.Status);
        Assert.Equal(first.Value.Peaks, second.Value.Peaks);
        Assert.True(Directory.Exists(Path.Combine(h.Root, "3-Trabalho", "cache", "waveforms")));
    }

    [Fact]
    public async Task CachedResult_IsNotServedAfterFingerprintChanges()
    {
        using var h = new Harness();
        h.Ffmpeg.Path = @"C:\ffmpeg\bin\ffmpeg.exe";
        var id = Guid.NewGuid();
        var media = h.AddMedia("1-Bruto/a.mp4", fingerprint: "fp-A", metadata: Harness.OkMetadata(id, "h264", 10), mediaId: id);
        h.Binary.Stdout = PcmBytes.S16(new short[8000]);

        await h.UseCases.GetWaveformAsync(Request(h, id));
        media.Fingerprint = "fp-B";
        await h.UseCases.GetWaveformAsync(Request(h, id));

        Assert.Equal(2, h.Binary.Calls.Count);
    }

    [Fact]
    public async Task MissingFingerprint_IsFailedBecauseCacheKeyWouldBeAmbiguous()
    {
        using var h = new Harness();
        h.Ffmpeg.Path = @"C:fmpeginfmpeg.exe";
        var id = Guid.NewGuid();
        h.AddMedia("1-Bruto/a.mp4", fingerprint: "", metadata: Harness.OkMetadata(id, "h264", 10), mediaId: id);

        var result = await h.UseCases.GetWaveformAsync(Request(h, id));

        Assert.Equal("failed", result.Value!.Status);
        Assert.Empty(h.Binary.Calls);
    }

    [Fact]
    public async Task UnknownMedia_IsNotFound()
    {
        using var h = new Harness();
        h.Ffmpeg.Path = @"C:\ffmpeg\bin\ffmpeg.exe";

        var result = await h.UseCases.GetWaveformAsync(Request(h, Guid.NewGuid()));

        Assert.False(result.Ok);
        Assert.Equal("MEDIA_NOT_FOUND", result.ErrorCode);
    }
}
