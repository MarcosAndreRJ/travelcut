using TravelCut.Application.Ipc;
using Xunit;

namespace TravelCut.Tests.Application;

public class IpcOriginPolicyTests
{
    [Theory]
    [InlineData("https://travelcut.studio/index.html")]
    [InlineData("https://travelcut.studio/")]
    [InlineData("https://TRAVELCUT.STUDIO/assets/app.js")]
    public void PublishedOrigin_IsTrustedInReleaseAndDebug(string url)
    {
        Assert.True(IpcOriginPolicy.IsTrustedOrigin(url, allowDevServer: false));
        Assert.True(IpcOriginPolicy.IsTrustedOrigin(url, allowDevServer: true));
    }

    [Theory]
    [InlineData("https://travelcut.studio.evil.com/index.html")]
    [InlineData("https://evil-travelcut.studio/")]
    [InlineData("https://travelcut.studio:8443/")]
    [InlineData("http://travelcut.studio/")]
    [InlineData("https://user:senha@travelcut.studio/")]
    [InlineData("https://google.com/")]
    [InlineData("file:///C:/Users/x/index.html")]
    [InlineData("about:blank")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("não é uma url")]
    public void ForeignOrMalformedOrigin_IsNeverTrusted(string? url)
    {
        Assert.False(IpcOriginPolicy.IsTrustedOrigin(url, allowDevServer: false));
        Assert.False(IpcOriginPolicy.IsTrustedOrigin(url, allowDevServer: true));
    }

    [Fact]
    public void DevServer_IsTrustedOnlyWhenAllowed()
    {
        Assert.True(IpcOriginPolicy.IsTrustedOrigin("http://localhost:5173", allowDevServer: true));
        Assert.True(IpcOriginPolicy.IsTrustedOrigin("http://localhost:5173/", allowDevServer: true));
        Assert.False(IpcOriginPolicy.IsTrustedOrigin("http://localhost:5173", allowDevServer: false));
        Assert.False(IpcOriginPolicy.IsTrustedOrigin("http://localhost:8080", allowDevServer: true));
        Assert.False(IpcOriginPolicy.IsTrustedOrigin("https://localhost:5173", allowDevServer: true));
    }
}
