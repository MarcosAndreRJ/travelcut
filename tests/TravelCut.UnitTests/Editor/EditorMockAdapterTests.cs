using Microsoft.Extensions.Logging.Abstractions;
using TravelCut.Application.Editor;
using TravelCut.Application.Ports;
using TravelCut.Contracts.Dtos;
using TravelCut.ResolveBridge;
using Xunit;
using static TravelCut.UnitTests.Editor.EditorTestData;

namespace TravelCut.UnitTests.Editor;

/// <summary>Simulador: resultados SIMULATED, nunca .drp, nunca render.</summary>
public class EditorMockAdapterTests
{
    private static MockResolveAdapter Create() => new(NullLogger<MockResolveAdapter>.Instance);

    private static EditorAssemblyDto Assembly()
    {
        var plan = Plan(Timeline2997,
        [
            Clip(1, "V1", 1, 1_000_000, 2_000_000, start: 0, duration: 30),
            Clip(2, "V1", 1, 2_000_000, 3_000_000, start: 30, duration: 30),
        ]);
        return EditorAssemblyBuilder.Build(plan, Sources(Src(1)), "Viagem", NowUtc).Assembly!;
    }

    [Fact]
    public async Task Apply_ReturnsSimulated_WithManifestValues_AndNullMatches()
    {
        var adapter = Create();
        var asm = Assembly();

        var result = await adapter.ApplyAssemblyAsync(asm, new EditorApplyOptions(null, TimeSpan.FromMinutes(10)));

        Assert.Equal(EditorResultStatus.Simulated, result.Status);
        Assert.Null(result.SnapshotAbsolutePath);
        Assert.Null(result.ErrorCode);
        Assert.All(result.Steps, s => Assert.Equal("simulated", s.Status));
        Assert.Equal(asm.Clips.Count, result.ClipMappings.Count);
        foreach (var (clip, mapping) in asm.Clips.Zip(result.ClipMappings))
        {
            Assert.Equal(clip.ClipId, mapping.ClipId);
            Assert.Equal($"sim-{clip.ClipId:N}", mapping.EditorItemId);
            Assert.Equal(clip.RecordFrame, mapping.ObservedRecordFrame);
            Assert.Equal(clip.DurationFrames, mapping.ObservedDurationFrames);
            Assert.Null(mapping.Matches);
        }
    }

    [Fact]
    public async Task Apply_NeverProducesSnapshot_EvenWhenRequested()
    {
        var adapter = Create();
        var requested = Path.Combine(Path.GetTempPath(), "tc_mock_" + Guid.NewGuid().ToString("N") + ".drp");

        var result = await adapter.ApplyAssemblyAsync(Assembly(), new EditorApplyOptions(requested, TimeSpan.FromMinutes(10)));

        Assert.Null(result.SnapshotAbsolutePath);
        Assert.False(File.Exists(requested));
    }

    [Fact]
    public async Task Apply_UnknownSchemaVersion_IsRefused()
    {
        var adapter = Create();
        var asm = Assembly() with { SchemaVersion = "editor-assembly/99" };

        var result = await adapter.ApplyAssemblyAsync(asm, new EditorApplyOptions(null, TimeSpan.FromMinutes(1)));

        Assert.Equal(EditorResultStatus.Error, result.Status);
        Assert.Equal("CONTRACT_VERSION_MISMATCH", result.ErrorCode);
        Assert.Empty(result.ClipMappings);
    }

    [Fact]
    public async Task QueryTimeline_AfterApply_ReturnsSimulatedItems()
    {
        var adapter = Create();
        var asm = Assembly();
        await adapter.ApplyAssemblyAsync(asm, new EditorApplyOptions(null, TimeSpan.FromMinutes(1)));

        var query = await adapter.QueryTimelineAsync(asm.EditorProjectName, asm.TimelineName);

        Assert.Equal(EditorResultStatus.Simulated, query.Status);
        Assert.Equal(2, query.Items.Count);
        Assert.Equal(new long[] { 0, 30 }, query.Items.Select(i => i.RecordFrame).ToArray());
        Assert.All(query.Items, i => Assert.StartsWith("sim-", i.EditorItemId));
    }

    [Fact]
    public async Task QueryTimeline_Unknown_ReturnsError()
    {
        var query = await Create().QueryTimelineAsync("TravelCut_Inexistente_r0001_20261102T120000", "TravelCut r0001");

        Assert.Equal(EditorResultStatus.Error, query.Status);
        Assert.Equal("TIMELINE_NOT_FOUND", query.ErrorCode);
    }

    [Fact]
    public async Task Export_IsUnavailable_AndNeverWritesFiles()
    {
        var adapter = Create();
        var target = Path.Combine(Path.GetTempPath(), "tc_mock_export_" + Guid.NewGuid().ToString("N") + ".drp");

        var project = await adapter.ExportProjectAsync("TravelCut_X_r0001_20261102T120000", target);
        var preview = await adapter.ExportPreviewAsync("TravelCut_X_r0001_20261102T120000", "TravelCut r0001", Path.GetTempPath());

        Assert.Equal(EditorResultStatus.Unavailable, project.Status);
        Assert.Equal("CAPABILITY_UNSUPPORTED", project.ErrorCode);
        Assert.Null(project.OutputAbsolutePath);
        Assert.Equal(EditorResultStatus.Unavailable, preview.Status);
        Assert.Null(preview.OutputAbsolutePath);
        Assert.False(File.Exists(target));
    }
}
