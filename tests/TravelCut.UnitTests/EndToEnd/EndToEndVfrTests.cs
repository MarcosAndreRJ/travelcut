using Xunit;
using static TravelCut.UnitTests.EndToEnd.E2EScenarioData;

namespace TravelCut.UnitTests.EndToEnd;

/// <summary>
/// FIXTURES DE PORTA (ffprobe com metadados VFR sintéticos): não são prova de FFprobe nem de conformação no Resolve.
/// Cenário 7: uma mídia VFR (taxa média 29,97) e uma CFR 29,97 no mesmo projeto.
/// Esperado: plano com base de tempo 29,97 (1001/30000) e editor.prepareApply com aviso VFR_SOURCE só no clipe VFR.
/// Metadados por fixture (ver E2EPortFixtures.Vfr2997); não valida conformação real pelo Resolve.
/// </summary>
public sealed class EndToEndVfrTests : IDisposable
{
    private readonly E2EWorkspace _ws = new();

    public void Dispose() => _ws.Dispose();

    [Fact]
    public async Task MidiaVfrECfrMesmaTaxa_PlanoEm2997_EPrepareApplyAvisaVfrSourceSoNoClipeVfr()
    {
        var root = _ws.ProjectRoot("Viagem VFR");
        using var host = E2EHost.Start(_ws.DatabasePath, Vfr());

        var projectId = await E2ESteps.CreateProjectAsync(host, root, "Viagem VFR");
        E2ESteps.PlaceMedia(root, VfrFiles);
        var scan = await E2ESteps.ScanAsync(host, projectId);
        Assert.Equal(2, scan.GetProperty("newAssetsAdded").GetInt32());
        Assert.Equal("completed", (await E2ESteps.AnalyzeAsync(host, projectId)).GetProperty("state").GetString());

        var generated = await E2ESteps.GeneratePlanAsync(host, projectId);
        var timeBase = generated.GetProperty("timeBase");
        Assert.Equal(1001, timeBase.GetProperty("frameDurationNum").GetInt64());
        Assert.Equal(30000, timeBase.GetProperty("frameDurationDen").GetInt64());

        // Cada mídia com áudio gera um clipe V1 (vídeo) e um A1 (áudio vinculado): 2 mídias → 2 V1 + 2 A1.
        var plan = await E2ESteps.LatestPlanAsync(host, projectId);
        var planClips = plan.GetProperty("clips").EnumerateArray().ToList();
        Assert.Equal(2, planClips.Count(c => c.GetProperty("trackId").GetString() == "V1"));
        Assert.Equal(2, planClips.Count(c => c.GetProperty("trackId").GetString() == "A1"));

        var preview = await E2ESteps.PrepareApplyAsync(host, projectId);
        Assert.Equal("29.97", preview.GetProperty("timelineFrameRate").GetString());

        var clips = preview.GetProperty("clips").EnumerateArray().ToList();
        var vfrClipIds = clips
            .Where(c => c.GetProperty("relativePath").GetString()!.EndsWith(VfrClip, StringComparison.Ordinal))
            .Select(c => c.GetProperty("clipId").GetGuid())
            .ToHashSet();
        var cfrClipIds = clips
            .Where(c => c.GetProperty("relativePath").GetString()!.EndsWith(CfrClip, StringComparison.Ordinal))
            .Select(c => c.GetProperty("clipId").GetGuid())
            .ToHashSet();
        Assert.Single(vfrClipIds);
        Assert.Single(cfrClipIds);

        var vfrWarnings = preview.GetProperty("warnings").EnumerateArray()
            .Where(w => w.GetProperty("code").GetString() == "VFR_SOURCE")
            .ToList();
        var warning = Assert.Single(vfrWarnings);
        Assert.Equal(vfrClipIds.Single(), warning.GetProperty("clipId").GetGuid());
        Assert.DoesNotContain(cfrClipIds, id => preview.GetProperty("warnings").EnumerateArray()
            .Any(w => w.GetProperty("code").GetString() == "VFR_SOURCE" && w.GetProperty("clipId").GetGuid() == id));
    }
}
