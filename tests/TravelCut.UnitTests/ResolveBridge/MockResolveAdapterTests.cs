using Microsoft.Extensions.Logging.Abstractions;
using TravelCut.Application.Ports;
using TravelCut.Contracts.Dtos;
using TravelCut.ResolveBridge;
using Xunit;

namespace TravelCut.Tests.ResolveBridge;

public class MockResolveAdapterTests
{
    private static MockResolveAdapter Create() => new(NullLogger<MockResolveAdapter>.Instance);

    [Fact]
    public async Task DetectCapabilitiesAsync_ReturnsSimulatedMockWithMockPassOperations()
    {
        var caps = await Create().DetectCapabilitiesAsync();

        Assert.Equal("mock", caps.AdapterMode);
        Assert.Equal("MOCK", caps.EnvironmentState);
        Assert.True(caps.IsAvailable);
        Assert.True(caps.IsSimulated);
        Assert.Null(caps.Version);
        Assert.Equal("Simulador TravelCut (sem DaVinci)", caps.ProductName);
        Assert.True(caps.CanApplyEditPlan);
        Assert.False(caps.CanExportPreview);

        var apply = caps.Operations.Single(o => o.Operation == EditorOperations.ApplyEditPlan);
        Assert.True(apply.Supported);
        Assert.Equal(CapabilityVerification.MockPass, apply.Verification);
    }

    [Fact]
    public async Task DetectCapabilitiesAsync_ExportOperations_AreUnsupportedWithNote()
    {
        var caps = await Create().DetectCapabilitiesAsync();

        foreach (var op in new[] { EditorOperations.ExportPreview, EditorOperations.ExportProject, EditorOperations.CreateSnapshot })
        {
            var cap = caps.Operations.Single(o => o.Operation == op);
            Assert.False(cap.Supported);
            Assert.Equal(CapabilityVerification.Unsupported, cap.Verification);
            Assert.Equal(MockResolveAdapter.NoDrpNote, cap.Note);
        }
    }

    [Fact]
    public void Mode_IsMock()
    {
        IEditorAdapter adapter = Create();

        Assert.Equal("mock", adapter.Mode);
    }
}
