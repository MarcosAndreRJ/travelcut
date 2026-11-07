using TravelCut.Application.UseCases;
using TravelCut.Contracts.Dtos;
using TravelCut.Domain.Entities;
using TravelCut.Domain.Enums;
using Xunit;

namespace TravelCut.UnitTests.Preview;

public class PlaybackSourceTests
{
    [Fact]
    public async Task UnsafeRelativePath_IsUnavailable()
    {
        using var h = new Harness();
        var media = h.AddMedia("../fora.mp4", writeFile: false);

        var result = await h.UseCases.GetPlaybackSourceAsync(new PlaybackSourceRequest(h.Project.ProjectId, media.MediaId));

        Assert.True(result.Ok);
        Assert.Equal("unavailable", result.Value!.Status);
        Assert.Null(result.Value.Url);
        Assert.Equal(PreviewPaths.ReasonUnsafePath, result.Value.Reason);
    }

    [Fact]
    public async Task MissingFile_IsUnavailableWithReason()
    {
        using var h = new Harness();
        var media = h.AddMedia("1-Bruto/sumiu.mp4", writeFile: false);

        var result = await h.UseCases.GetPlaybackSourceAsync(new PlaybackSourceRequest(h.Project.ProjectId, media.MediaId));

        Assert.True(result.Ok);
        Assert.Equal("unavailable", result.Value!.Status);
        Assert.Null(result.Value.Url);
        Assert.Equal("arquivo não encontrado", result.Value.Reason);
        Assert.Null(result.Value.LikelyPlayable);
    }

    [Fact]
    public async Task OriginalWithKnownH264_IsOriginalWithEscapedUrl()
    {
        using var h = new Harness();
        h.Ffmpeg.Path = @"C:\ffmpeg\bin\ffmpeg.exe";
        var id = Guid.NewGuid();
        h.AddMedia("1-Bruto/Viagem Açores/cena 1.mp4", metadata: Harness.OkMetadata(id, "h264"), mediaId: id);

        var result = await h.UseCases.GetPlaybackSourceAsync(new PlaybackSourceRequest(h.Project.ProjectId, id));

        Assert.True(result.Ok);
        var dto = result.Value!;
        Assert.Equal("original", dto.Status);
        Assert.Equal("https://media.travelcut.studio/1-Bruto/Viagem%20A%C3%A7ores/cena%201.mp4", dto.Url);
        Assert.Equal("video/mp4", dto.MimeType);
        Assert.Equal("h264", dto.Codec);
        Assert.True(dto.LikelyPlayable);
        Assert.Equal("none", dto.ProxyStatus);
        Assert.Null(dto.Reason);
    }

    [Fact]
    public async Task NoFfmpegAndNoProxy_ProxyStatusIsFfmpegUnavailable()
    {
        using var h = new Harness();
        var id = Guid.NewGuid();
        h.AddMedia("1-Bruto/a.mp4", mediaId: id);

        var result = await h.UseCases.GetPlaybackSourceAsync(new PlaybackSourceRequest(h.Project.ProjectId, id));

        Assert.Equal("original", result.Value!.Status);
        Assert.Equal("ffmpeg_unavailable", result.Value.ProxyStatus);
        // Sem metadado "ok": nada de codec nem de playabilidade.
        Assert.Null(result.Value.Codec);
        Assert.Null(result.Value.LikelyPlayable);
    }

    [Fact]
    public async Task MetadataNotOk_DoesNotDeclareCodecOrPlayability()
    {
        using var h = new Harness();
        var id = Guid.NewGuid();
        var md = Harness.OkMetadata(id, "h264");
        md.MetadataStatus = MediaMetadataStatus.Failed;
        h.AddMedia("1-Bruto/a.mp4", metadata: md, mediaId: id);

        var result = await h.UseCases.GetPlaybackSourceAsync(new PlaybackSourceRequest(h.Project.ProjectId, id));

        Assert.Equal("original", result.Value!.Status);
        Assert.Null(result.Value.Codec);
        Assert.Null(result.Value.LikelyPlayable);
    }

    [Fact]
    public async Task Hevc_LikelyPlayableIsUnknown()
    {
        using var h = new Harness();
        var id = Guid.NewGuid();
        h.AddMedia("1-Bruto/a.mp4", metadata: Harness.OkMetadata(id, "hevc"), mediaId: id);

        var result = await h.UseCases.GetPlaybackSourceAsync(new PlaybackSourceRequest(h.Project.ProjectId, id));

        Assert.Equal("hevc", result.Value!.Codec);
        Assert.Null(result.Value.LikelyPlayable);
    }

    [Fact]
    public async Task Prores_LikelyPlayableIsFalse()
    {
        using var h = new Harness();
        var id = Guid.NewGuid();
        h.AddMedia("1-Bruto/a.mov", metadata: Harness.OkMetadata(id, "prores"), mediaId: id);

        var result = await h.UseCases.GetPlaybackSourceAsync(new PlaybackSourceRequest(h.Project.ProjectId, id));

        Assert.False(result.Value!.LikelyPlayable);
    }

    [Fact]
    public async Task ReadyProxy_TakesPriorityAndUsesProxyUrl()
    {
        using var h = new Harness();
        h.Ffmpeg.Path = @"C:\ffmpeg\bin\ffmpeg.exe";
        var id = Guid.NewGuid();
        h.AddMedia("1-Bruto/orig.mp4", fingerprint: "fp-proxy", metadata: Harness.OkMetadata(id, "hevc"), mediaId: id);

        Directory.CreateDirectory(h.ProxiesFolder);
        string proxyName = PreviewPaths.ProxyFileName(id, "fp-proxy");
        var mockMp4 = new byte[1024]; mockMp4[3] = 32; mockMp4[4] = 0x66; mockMp4[5] = 0x74; mockMp4[6] = 0x79; mockMp4[7] = 0x70;
        File.WriteAllBytes(Path.Combine(h.ProxiesFolder, proxyName), mockMp4);

        var result = await h.UseCases.GetPlaybackSourceAsync(new PlaybackSourceRequest(h.Project.ProjectId, id));

        var dto = result.Value!;
        Assert.Equal("proxy", dto.Status);
        Assert.Equal("https://media.travelcut.studio/3-Trabalho/cache/proxies/" + proxyName, dto.Url);
        Assert.Equal("ready", dto.ProxyStatus);
        Assert.Equal("video/mp4", dto.MimeType);
    }

    [Fact]
    public async Task MediaFromAnotherProject_IsNotFound()
    {
        using var h = new Harness();
        var media = h.AddMedia("1-Bruto/a.mp4");
        var otherProject = Guid.NewGuid();
        h.Projects.Items[otherProject] = new Project { ProjectId = otherProject, RootPath = h.Root, Name = "outro" };

        var result = await h.UseCases.GetPlaybackSourceAsync(new PlaybackSourceRequest(otherProject, media.MediaId));

        Assert.False(result.Ok);
        Assert.Equal("MEDIA_NOT_FOUND", result.ErrorCode);
    }

    [Fact]
    public async Task UnknownProject_IsNotFound()
    {
        using var h = new Harness();
        var media = h.AddMedia("1-Bruto/a.mp4");

        var result = await h.UseCases.GetPlaybackSourceAsync(new PlaybackSourceRequest(Guid.NewGuid(), media.MediaId));

        Assert.False(result.Ok);
        Assert.Equal("PROJECT_NOT_FOUND", result.ErrorCode);
    }
}
