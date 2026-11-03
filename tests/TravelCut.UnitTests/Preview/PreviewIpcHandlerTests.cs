using Microsoft.Extensions.Logging.Abstractions;
using TravelCut.Application.UseCases;
using TravelCut.Contracts.Ipc;
using TravelCut.Domain.Entities;
using Xunit;

namespace TravelCut.UnitTests.Preview;

public class PreviewIpcHandlerTests
{
    private static PreviewIpcHandler CreateHandler(Harness h) =>
        new(h.UseCases, NullLogger<PreviewIpcHandler>.Instance);

    [Theory]
    [InlineData("system.ping")]
    [InlineData("project.open")]
    [InlineData("media.getThumbnail")]
    [InlineData("media.proxy.unknown")]
    [InlineData("")]
    public async Task UnknownMethod_ReturnsNullForDispatcherFallthrough(string type)
    {
        using var h = new Harness();

        var response = await CreateHandler(h).TryHandleAsync(new IpcRequest { RequestId = "r1", Type = type, Payload = null });

        Assert.Null(response);
    }

    [Fact]
    public async Task PlaybackSource_AcceptsDictionaryPayloadAndReturnsDto()
    {
        using var h = new Harness();
        var media = h.AddMedia("1-Bruto/a.mp4", metadata: Harness.OkMetadata(Guid.NewGuid(), "h264"));
        var payload = new { projectId = h.Project.ProjectId, mediaId = media.MediaId };

        var response = await CreateHandler(h).TryHandleAsync(new IpcRequest
        {
            RequestId = "req-1",
            Type = "media.getPlaybackSource",
            Payload = payload
        });

        Assert.NotNull(response);
        Assert.True(response!.Ok);
        Assert.Equal("req-1", response.RequestId);
        Assert.NotNull(response.Payload);
    }

    [Fact]
    public async Task Waveform_WithoutPayload_IsInvalidArgument()
    {
        using var h = new Harness();

        var response = await CreateHandler(h).TryHandleAsync(new IpcRequest
        {
            RequestId = "req-2",
            Type = "media.getWaveform",
            Payload = null
        });

        Assert.NotNull(response);
        Assert.False(response!.Ok);
        Assert.Equal("INVALID_ARGUMENT", response.ErrorCode);
        Assert.Empty(h.Binary.Calls);
    }

    [Fact]
    public async Task ProxyStatus_UnknownJob_IsNotFound()
    {
        using var h = new Harness();
        var payload = new { projectId = h.Project.ProjectId, jobId = Guid.NewGuid() };

        var response = await CreateHandler(h).TryHandleAsync(new IpcRequest
        {
            RequestId = "req-3",
            Type = "media.proxy.status",
            Payload = payload
        });

        Assert.NotNull(response);
        Assert.False(response!.Ok);
        Assert.Equal("PROXY_JOB_NOT_FOUND", response.ErrorCode);
    }

    [Fact]
    public async Task ProxyStart_EmptyProjectId_IsInvalidArgument()
    {
        using var h = new Harness();
        var payload = new { projectId = Guid.Empty, mediaIds = new[] { Guid.NewGuid() } };

        var response = await CreateHandler(h).TryHandleAsync(new IpcRequest
        {
            RequestId = "req-4",
            Type = "media.proxy.start",
            Payload = payload
        });

        Assert.NotNull(response);
        Assert.Equal("INVALID_ARGUMENT", response!.ErrorCode);
    }
}
