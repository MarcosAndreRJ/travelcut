using System.Text.Json;
using TravelCut.Application.Services;
using TravelCut.Domain.Entities;
using TravelCut.Domain.ValueObjects;
using Xunit;

namespace TravelCut.UnitTests.Application;

public class FfprobeMetadataExtractorTests : IDisposable
{
    private readonly string _tempDir;
    private readonly FfprobeMetadataExtractor _extractor;

    public FfprobeMetadataExtractorTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "TravelCutFfprobeTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _extractor = new FfprobeMetadataExtractor("ffprobe_non_existent_binary");
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, recursive: true);
        }
        catch
        {
            // Ignore cleanup errors
        }
    }

    [Fact]
    public void ParseFfprobeJson_ValidVideoAndAudio_ParsesAllMetadataCorrectly()
    {
        string sampleJson = """
        {
          "streams": [
            {
              "codec_type": "video",
              "codec_name": "hevc",
              "width": 3840,
              "height": 2160,
              "r_frame_rate": "60/1",
              "avg_frame_rate": "60/1",
              "time_base": "1/60000",
              "duration": "12.500",
              "tags": {
                "creation_time": "2026-05-15T09:30:00.000000Z",
                "model": "Sony A7S III"
              },
              "side_data_list": [
                {
                  "rotation": 0
                }
              ]
            },
            {
              "codec_type": "audio",
              "codec_name": "aac",
              "channels": 2,
              "sample_rate": "48000"
            }
          ],
          "format": {
            "duration": "12.500",
            "tags": {
              "com.apple.quicktime.creationdate": "2026-05-15T18:30:00+09:00",
              "location": "+35.6586+139.7454/"
            }
          }
        }
        """;

        var info = _extractor.ParseFfprobeJson(sampleJson, "C:/fake/clip.mov");

        Assert.Equal("hevc", info.Codec);
        Assert.Equal(3840, info.Width);
        Assert.Equal(2160, info.Height);
        Assert.Equal(60.0, info.FrameRate);
        Assert.False(info.IsVariableFrameRate);
        Assert.Equal(12.5, info.DurationSeconds);
        Assert.Equal(2, info.AudioChannels);
        Assert.Equal(48000, info.AudioSampleRate);
        Assert.Equal("Sony A7S III", info.CameraModel);
        Assert.Equal("+35.6586+139.7454/", info.GpsLocation);
        Assert.NotNull(info.CreationTimeUtc);
        Assert.Equal("quicktime_creationdate_tag", info.SourceDateEvidence);
        Assert.Equal(2026, info.CreationTimeUtc.Value.Year);
        Assert.Equal(5, info.CreationTimeUtc.Value.Month);
    }

    [Fact]
    public void ParseFfprobeJson_DetectsVariableFrameRate()
    {
        string vfrJson = """
        {
          "streams": [
            {
              "codec_type": "video",
              "codec_name": "h264",
              "width": 1920,
              "height": 1080,
              "r_frame_rate": "30/1",
              "avg_frame_rate": "29.23/1",
              "duration": "10.0"
            }
          ],
          "format": {
            "duration": "10.0"
          }
        }
        """;

        var info = _extractor.ParseFfprobeJson(vfrJson, "C:/fake/phone_clip.mp4");

        Assert.True(info.IsVariableFrameRate);
        Assert.Equal(1920, info.Width);
        Assert.Equal(1080, info.Height);
    }

    [Fact]
    public void ParseFfprobeJson_VariableFrameRate_ReportsAverageRateForConformation()
    {
        // r_frame_rate 60/1 (nominal) e avg 30000/1001 (média real): a taxa reportada é a média, VFR marcado.
        string vfrJson = """
        {
          "streams": [
            {
              "codec_type": "video",
              "codec_name": "h264",
              "width": 1920,
              "height": 1080,
              "r_frame_rate": "60/1",
              "avg_frame_rate": "30000/1001",
              "duration": "10.0"
            }
          ],
          "format": {
            "duration": "10.0"
          }
        }
        """;

        var info = _extractor.ParseFfprobeJson(vfrJson, "C:/fake/vfr_avg.mp4");

        Assert.True(info.IsVariableFrameRate);
        Assert.Equal(29.97, info.FrameRate);
    }

    [Fact]
    public void ParseFfprobeJson_VariableFrameRate_WithoutTimeBase_TimeBaseMatchesReportedAverage()
    {
        // N3: r_frame_rate 60/1 nominal, avg 30000/1001 (VFR), sem time_base: o TimeBase segue a média gravada em FrameRate.
        string vfrJson = """
        {
          "streams": [
            {
              "codec_type": "video",
              "codec_name": "h264",
              "width": 1920,
              "height": 1080,
              "r_frame_rate": "60/1",
              "avg_frame_rate": "30000/1001",
              "duration": "10.0"
            }
          ],
          "format": { "duration": "10.0" }
        }
        """;

        var info = _extractor.ParseFfprobeJson(vfrJson, "C:/fake/vfr_timebase.mp4");

        Assert.True(info.IsVariableFrameRate);
        Assert.Equal(29.97, info.FrameRate);
        Assert.Equal(RationalTimeBase.Fps2997, info.TimeBase);
        double tbFps = info.TimeBase.Denominator / (double)info.TimeBase.Numerator;
        Assert.True(Math.Abs(tbFps - info.FrameRate) < 0.01, $"TimeBase={tbFps} FrameRate={info.FrameRate}");
    }

    [Fact]
    public void ParseFfprobeJson_ConstantFrameRate_TimeBaseFollowsNominalRate()
    {
        // CFR de 60 fps (sem VFR): o fallback continua usando r_frame_rate, que é também a taxa gravada.
        string cfrJson = """
        {
          "streams": [
            {
              "codec_type": "video",
              "codec_name": "h264",
              "width": 1920,
              "height": 1080,
              "r_frame_rate": "60/1",
              "avg_frame_rate": "60/1",
              "duration": "10.0"
            }
          ],
          "format": { "duration": "10.0" }
        }
        """;

        var info = _extractor.ParseFfprobeJson(cfrJson, "C:/fake/cfr60.mp4");

        Assert.False(info.IsVariableFrameRate);
        Assert.Equal(60.0, info.FrameRate);
        Assert.Equal(RationalTimeBase.Fps60, info.TimeBase);
    }

    [Fact]
    public void ParseFfprobeJson_AudioOnlyFile_ExtractsAudioAttributes()
    {
        string audioJson = """
        {
          "streams": [
            {
              "codec_type": "audio",
              "codec_name": "pcm_s24le",
              "channels": 2,
              "sample_rate": "48000"
            }
          ],
          "format": {
            "duration": "120.0"
          }
        }
        """;

        var info = _extractor.ParseFfprobeJson(audioJson, "C:/fake/guide.wav");

        Assert.Equal("pcm_s24le", info.Codec);
        Assert.Equal(2, info.AudioChannels);
        Assert.Equal(48000, info.AudioSampleRate);
        Assert.Equal(120.0, info.DurationSeconds);
    }

    [Fact]
    public async Task ExtractMetadataAsync_WithNonExistentFfprobe_ReturnsHonestFallbackWithoutInventedValues()
    {
        string testFile = Path.Combine(_tempDir, "fallback_test.mp4");
        File.WriteAllText(testFile, "dummy content");

        var info = await _extractor.ExtractMetadataAsync(testFile);

        Assert.NotNull(info);
        Assert.Equal(MediaMetadataStatus.FfprobeUnavailable, info.MetadataStatus);
        Assert.Equal("unknown", info.Codec);
        Assert.Equal(0, info.Width);
        Assert.Equal(0, info.Height);
        Assert.Equal(0.0, info.FrameRate);
        Assert.Equal(0.0, info.DurationSeconds);
        Assert.Equal(0L, info.DurationTicks);
        Assert.Equal(0, info.AudioChannels);
        Assert.Equal(0, info.AudioSampleRate);
        Assert.Null(info.CameraModel);
        Assert.Equal("file_system_last_write_fallback", info.SourceDateEvidence);
    }

    [Fact]
    public void ExtractFallback_NeverGuessesResolutionDurationOrFpsFromFileSize()
    {
        string big = Path.Combine(_tempDir, "grande.mov");
        File.WriteAllBytes(big, new byte[5 * 1024 * 1024]);

        var info = _extractor.ExtractFallback(big, MediaMetadataStatus.Failed);

        Assert.Equal(MediaMetadataStatus.Failed, info.MetadataStatus);
        Assert.Equal(0, info.Width);
        Assert.Equal(0, info.Height);
        Assert.Equal(0.0, info.DurationSeconds);
        Assert.Equal(0.0, info.FrameRate);
        Assert.Equal("unknown", info.Codec);
    }

    [Fact]
    public void ParseFfprobeJson_MissingVideoDimensions_DoesNotInventDefaults()
    {
        string json = """
        {
          "streams": [
            { "codec_type": "video", "codec_name": "vp9" }
          ],
          "format": { }
        }
        """;

        var info = _extractor.ParseFfprobeJson(json, "C:/fake/sem_dimensao.webm");

        Assert.Equal(MediaMetadataStatus.Ok, info.MetadataStatus);
        Assert.Equal("vp9", info.Codec);
        Assert.Equal(0, info.Width);
        Assert.Equal(0, info.Height);
        Assert.Equal(0.0, info.FrameRate);
        Assert.Equal(0.0, info.DurationSeconds);
        Assert.Equal(0, info.AudioChannels);
        Assert.Equal(0, info.AudioSampleRate);
    }

    [Fact]
    public void ParseFfprobeJson_NoMediaStreams_ReportsFailed()
    {
        string json = """{ "streams": [], "format": { "duration": "10.0" } }""";

        var info = _extractor.ParseFfprobeJson(json, "C:/fake/vazio.bin");

        Assert.Equal(MediaMetadataStatus.Failed, info.MetadataStatus);
        Assert.Equal(0, info.Width);
        Assert.Equal(0.0, info.DurationSeconds);
    }

    [Fact]
    public void ParseFfprobeJson_ValidVideo_ReportsOkStatus()
    {
        string json = """
        {
          "streams": [
            { "codec_type": "video", "codec_name": "h264", "width": 1280, "height": 720, "r_frame_rate": "25/1", "duration": "4.0" }
          ],
          "format": { "duration": "4.0" }
        }
        """;

        var info = _extractor.ParseFfprobeJson(json, "C:/fake/ok.mp4");

        Assert.Equal(MediaMetadataStatus.Ok, info.MetadataStatus);
        Assert.Equal(1280, info.Width);
        Assert.Equal(720, info.Height);
        Assert.Equal(4.0, info.DurationSeconds);
    }
}
