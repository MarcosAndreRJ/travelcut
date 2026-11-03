using System.Text.Json;
using TravelCut.Application.Editor;
using TravelCut.Application.Planning;
using TravelCut.Application.Ports;
using TravelCut.Contracts.Dtos;
using TravelCut.UnitTests.Planning;
using Xunit;

namespace TravelCut.UnitTests.Editor;

public class EditorUseCasesTests : IDisposable
{
    private readonly EditorFixture _f = new();

    public void Dispose() => _f.Dispose();

    /// <summary>Aplicação com o fingerprint do dry-run informado.</summary>
    private static ApplyEditorPlanRequest ApplyRequest(EditorApplyPreviewDto preview, string key, bool confirm = false) =>
        new(TravelFixture.ProjectId, preview.Revision, preview.AssemblyFingerprint, confirm, key);

    private async Task<EditorApplyPreviewDto> PrepareAsync(int? revision = null)
    {
        var r = await _f.Editor.PrepareApplyAsync(new PrepareEditorApplyRequest(TravelFixture.ProjectId, revision));
        Assert.True(r.Success, r.ErrorMessage);
        return r.Value!;
    }

    [Fact]
    public async Task PrepareApply_WithoutPlan_ReturnsNoPlan()
    {
        var r = await _f.Editor.PrepareApplyAsync(new PrepareEditorApplyRequest(TravelFixture.ProjectId, null));

        Assert.False(r.Success);
        Assert.Equal("NO_PLAN", r.ErrorCode);
        Assert.Equal(0, _f.Adapter.ApplyCalls);
    }

    [Fact]
    public async Task PrepareApply_UnknownProject_ReturnsProjectNotFound()
    {
        var r = await _f.Editor.PrepareApplyAsync(new PrepareEditorApplyRequest(Guid.NewGuid(), null));

        Assert.Equal("PROJECT_NOT_FOUND", r.ErrorCode);
    }

    [Fact]
    public async Task PrepareApply_MockWithPlan_WritesManifest_CanApply_AndNoAbsolutePathInPreview()
    {
        await _f.GeneratePlanAsync();

        var p = await PrepareAsync();

        Assert.Equal("DRY_RUN", p.Status);
        Assert.True(p.CanApply, string.Join(",", p.BlockingReasons));
        Assert.Empty(p.BlockingReasons);
        Assert.False(p.RequiresProjectSwitchConfirmation);
        Assert.True(p.Capabilities.IsSimulated);
        Assert.NotEmpty(p.Clips);
        Assert.StartsWith("3-Trabalho/editor/", p.ManifestRelativePath);
        Assert.True(File.Exists(Path.Combine(_f.Root, p.ManifestRelativePath.Replace('/', Path.DirectorySeparatorChar))));
        Assert.Matches("^TravelCut_Viagem_Teste_r0001_\\d{8}T\\d{6}$", p.EditorProjectName);
        Assert.Equal("TravelCut r0001", p.TimelineName);

        var json = JsonSerializer.Serialize(p);
        Assert.DoesNotContain(_f.Root.Replace('\\', '/'), json);
        Assert.DoesNotContain(_f.Root, json);
    }

    [Fact]
    public async Task PrepareApply_OutdatedPlan_IsBlocked()
    {
        await _f.GeneratePlanAsync();
        var assets = await _f.Media.ListByProjectAsync(TravelFixture.ProjectId);
        var asset = assets.First();
        asset.Fingerprint = "fp-alterado";
        await _f.Media.SaveAsync(asset);

        var p = await PrepareAsync();

        Assert.False(p.CanApply);
        Assert.Contains("PLAN_OUTDATED", p.BlockingReasons);
    }

    [Fact]
    public async Task PrepareApply_EditorUnavailable_IsBlocked_ButManifestIsWritten()
    {
        await _f.GeneratePlanAsync();
        _f.Adapter.Available = false;

        var p = await PrepareAsync();

        Assert.False(p.CanApply);
        Assert.Contains("EDITOR_UNAVAILABLE", p.BlockingReasons);
        Assert.True(File.Exists(Path.Combine(_f.Root, p.ManifestRelativePath.Replace('/', Path.DirectorySeparatorChar))));

        var apply = await _f.Editor.ApplyAsync(ApplyRequest(p, "key-1"));
        Assert.False(apply.Success);
        Assert.Equal("EDITOR_UNAVAILABLE", apply.ErrorCode);
        Assert.Equal(0, _f.Adapter.ApplyCalls);
    }

    [Fact]
    public async Task PrepareApply_MediaOutsideSourceFolders_IsRejectedAsMediaNotFound()
    {
        await _f.GeneratePlanAsync();
        foreach (var asset in await _f.Media.ListByProjectAsync(TravelFixture.ProjectId))
        {
            asset.RelativePath = "../fora/" + Path.GetFileName(asset.RelativePath);
            await _f.Media.SaveAsync(asset);
        }

        var p = await PrepareAsync();

        Assert.Empty(p.Clips);
        Assert.Contains(p.Rejected, r => r.Code == "MEDIA_NOT_FOUND");
        Assert.DoesNotContain(p.Rejected, r => r.Message.Contains(_f.Root, StringComparison.OrdinalIgnoreCase));
        Assert.False(p.CanApply);
        Assert.Contains("NO_CLIPS", p.BlockingReasons);
    }

    [Fact]
    public async Task Apply_VersionConflict_WhenFingerprintDiffers()
    {
        await _f.GeneratePlanAsync();
        var p = await PrepareAsync();

        var r = await _f.Editor.ApplyAsync(new ApplyEditorPlanRequest(TravelFixture.ProjectId, p.Revision, "fingerprint-antigo", true, "key-1"));

        Assert.False(r.Success);
        Assert.Equal("PROJECT_VERSION_CONFLICT", r.ErrorCode);
        Assert.Equal(0, _f.Adapter.ApplyCalls);
    }

    [Theory]
    [InlineData("chave com espaço!")]
    [InlineData("abc\n")]
    [InlineData("")]
    public async Task Apply_InvalidIdempotencyKey_IsRejected(string key)
    {
        await _f.GeneratePlanAsync();
        var p = await PrepareAsync();

        var r = await _f.Editor.ApplyAsync(ApplyRequest(p, key));

        Assert.Equal("INVALID_IDEMPOTENCY_KEY", r.ErrorCode);
        Assert.Equal(0, _f.Adapter.ApplyCalls);
    }

    [Fact]
    public async Task Apply_IdempotencyKeyLimit_Is100Characters()
    {
        await _f.GeneratePlanAsync();
        var p = await PrepareAsync();

        var tooLong = await _f.Editor.ApplyAsync(ApplyRequest(p, new string('k', 101)));
        Assert.Equal("INVALID_IDEMPOTENCY_KEY", tooLong.ErrorCode);
        Assert.Equal(0, _f.Adapter.ApplyCalls);

        var max = await _f.Editor.ApplyAsync(ApplyRequest(p, new string('k', 100)));
        Assert.True(max.Success, max.ErrorMessage);
        Assert.Equal(1, _f.Adapter.ApplyCalls);
    }

    [Fact]
    public async Task Apply_DavinciMode_RequiresProjectSwitchConfirmation()
    {
        await _f.GeneratePlanAsync();
        _f.Adapter.Mode = "davinci";
        var p = await PrepareAsync();
        Assert.True(p.RequiresProjectSwitchConfirmation);

        var blocked = await _f.Editor.ApplyAsync(ApplyRequest(p, "key-1", confirm: false));

        Assert.Equal("CONFIRMATION_REQUIRED", blocked.ErrorCode);
        Assert.Equal(0, _f.Adapter.ApplyCalls);
    }

    [Fact]
    public async Task Apply_DavinciMode_PassesSnapshotPath_AndRecordsRelativeSnapshot()
    {
        await _f.GeneratePlanAsync();
        _f.Adapter.Mode = "davinci";
        var p = await PrepareAsync();

        var r = await _f.Editor.ApplyAsync(ApplyRequest(p, "key-1", confirm: true));

        Assert.True(r.Success, r.ErrorMessage);
        var record = r.Value!;
        Assert.Equal("READY_REAL", record.Status);
        Assert.False(record.IsSimulated);
        Assert.NotNull(_f.Adapter.LastOptions!.SnapshotAbsolutePath);
        Assert.StartsWith(Path.Combine(_f.Root, "3-Trabalho", "editor"), _f.Adapter.LastOptions.SnapshotAbsolutePath, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(record.SnapshotRelativePath);
        Assert.StartsWith("3-Trabalho/editor/", record.SnapshotRelativePath);
        Assert.True(File.Exists(Path.Combine(_f.Root, record.SnapshotRelativePath!.Replace('/', Path.DirectorySeparatorChar))));
    }

    [Fact]
    public async Task Apply_MockMode_NeverRequestsSnapshot()
    {
        await _f.GeneratePlanAsync();
        var p = await PrepareAsync();

        var r = await _f.Editor.ApplyAsync(ApplyRequest(p, "key-1"));

        Assert.True(r.Success, r.ErrorMessage);
        Assert.Null(_f.Adapter.LastOptions!.SnapshotAbsolutePath);
        Assert.Null(r.Value!.SnapshotRelativePath);
        Assert.True(r.Value.IsSimulated);
        Assert.Equal("editor-apply/1", r.Value.SchemaVersion);
        Assert.Equal(TimeSpan.FromMinutes(10), _f.Adapter.LastOptions.Timeout);
    }

    [Fact]
    public async Task Apply_SameKeySameFingerprint_ReusesPrevious_WithoutCallingAdapter()
    {
        await _f.GeneratePlanAsync();
        var p = await PrepareAsync();
        var first = await _f.Editor.ApplyAsync(ApplyRequest(p, "key-1"));
        Assert.True(first.Success, first.ErrorMessage);

        var second = await _f.Editor.ApplyAsync(ApplyRequest(p, "key-1"));

        Assert.True(second.Success, second.ErrorMessage);
        Assert.True(second.Value!.ReusedPrevious);
        Assert.Equal(first.Value!.ApplicationId, second.Value.ApplicationId);
        Assert.Equal(1, _f.Adapter.ApplyCalls);
        var list = await _f.Editor.ListApplicationsAsync(TravelFixture.ProjectId);
        Assert.Single(list.Value!.Applications);
    }

    [Fact]
    public async Task Apply_KeyReusedWithDifferentFingerprint_IsRejected()
    {
        await _f.GeneratePlanAsync();
        var p = await PrepareAsync();
        var first = await _f.Editor.ApplyAsync(ApplyRequest(p, "key-1"));
        Assert.True(first.Success, first.ErrorMessage);

        // Registro anterior com a mesma chave mas outra montagem (ex.: plano revisado depois).
        var otherId = Guid.NewGuid();
        var other = first.Value! with
        {
            ApplicationId = otherId,
            AssemblyFingerprint = "outra-montagem",
            RecordRelativePath = EditorStoragePaths.RelativeApplicationPath(first.Value.PlanId, otherId),
        };
        await _f.EditorStore.WriteApplicationAsync(_f.Root, other);

        var r = await _f.Editor.ApplyAsync(ApplyRequest(p, "key-1"));

        Assert.False(r.Success);
        Assert.Equal("IDEMPOTENCY_KEY_REUSED", r.ErrorCode);
        Assert.Equal(1, _f.Adapter.ApplyCalls);
    }

    [Fact]
    public async Task Apply_AdapterThrows_RecordsErrorWithoutPaths()
    {
        await _f.GeneratePlanAsync();
        var p = await PrepareAsync();
        _f.Adapter.ThrowOnApply = new InvalidOperationException("falha em " + _f.Root);

        var r = await _f.Editor.ApplyAsync(ApplyRequest(p, "key-1"));

        Assert.True(r.Success);
        var record = r.Value!;
        Assert.Equal("ERROR", record.Status);
        Assert.Equal("EDITOR_BRIDGE_FAILED", record.ErrorCode);
        Assert.DoesNotContain(_f.Root, record.ErrorMessage ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        Assert.True(File.Exists(Path.Combine(_f.Root, record.RecordRelativePath.Replace('/', Path.DirectorySeparatorChar))));

        var list = await _f.Editor.ListApplicationsAsync(TravelFixture.ProjectId);
        var stored = Assert.Single(list.Value!.Applications);
        Assert.Equal("ERROR", stored.Status);
    }

    [Fact]
    public async Task Apply_RecordWriteFails_ReturnsResultWithRecordNotSavedWarning()
    {
        await _f.GeneratePlanAsync();
        var p = await PrepareAsync();
        // Um arquivo no lugar da pasta "applications" impede a gravação do registro.
        var blocker = Path.Combine(_f.Root, "3-Trabalho", "editor", p.PlanId.ToString(), "applications");
        Directory.CreateDirectory(Path.GetDirectoryName(blocker)!);
        File.WriteAllText(blocker, "bloqueio");

        var r = await _f.Editor.ApplyAsync(ApplyRequest(p, "key-1"));

        Assert.True(r.Success);
        Assert.Equal("SIMULATED", r.Value!.Status);
        Assert.Contains(r.Value.Warnings, w => w.Code == "RECORD_NOT_SAVED");
        Assert.Equal(1, _f.Adapter.ApplyCalls);
    }

    [Fact]
    public async Task Apply_AfterError_SameKey_RunsAgain()
    {
        await _f.GeneratePlanAsync();
        var p = await PrepareAsync();
        _f.Adapter.ThrowOnApply = new InvalidOperationException("transitório");
        await _f.Editor.ApplyAsync(ApplyRequest(p, "key-1"));
        _f.Adapter.ThrowOnApply = null;

        var retry = await _f.Editor.ApplyAsync(ApplyRequest(p, "key-1"));

        Assert.True(retry.Success);
        Assert.Equal("SIMULATED", retry.Value!.Status);
        Assert.False(retry.Value.ReusedPrevious);
        Assert.Equal(2, _f.Adapter.ApplyCalls);
    }

    [Fact]
    public async Task ListApplications_NewestFirst()
    {
        await _f.GeneratePlanAsync();
        var p = await PrepareAsync();
        await _f.Editor.ApplyAsync(ApplyRequest(p, "primeira"));
        await _f.Editor.ApplyAsync(ApplyRequest(p, "segunda"));

        var list = await _f.Editor.ListApplicationsAsync(TravelFixture.ProjectId);

        Assert.True(list.Success);
        Assert.Equal(new[] { "segunda", "primeira" }, list.Value!.Applications.Select(a => a.IdempotencyKey).ToArray());
    }

    [Fact]
    public async Task Apply_UnavailableResult_SameKeyRetries_CallingAdapterAgain()
    {
        await _f.GeneratePlanAsync();
        var p = await PrepareAsync();
        _f.Adapter.OnApply = _ => new EditorAdapterApplyResult(EditorResultStatus.Unavailable, null, [], [], [], [], null,
            "EDITOR_UNAVAILABLE", "Editor indisponível (teste).");

        var first = await _f.Editor.ApplyAsync(ApplyRequest(p, "key-1"));
        Assert.True(first.Success);
        Assert.Equal("UNAVAILABLE", first.Value!.Status);

        _f.Adapter.OnApply = null;
        _f.Adapter.Mode = "davinci";
        var retry = await _f.Editor.ApplyAsync(ApplyRequest(p, "key-1", confirm: true));

        Assert.True(retry.Success, retry.ErrorMessage);
        Assert.False(retry.Value!.ReusedPrevious);
        Assert.Equal(2, _f.Adapter.ApplyCalls);
    }

    [Theory]
    [InlineData("PARTIAL_REAL")]
    [InlineData("READY_REAL")]
    public async Task Apply_UsefulRealResult_SameKey_IsReused(string status)
    {
        await _f.GeneratePlanAsync();
        _f.Adapter.Mode = "davinci";
        var p = await PrepareAsync();
        _f.Adapter.OnApply = assembly => new EditorAdapterApplyResult(status, null, [], [], [], [], null, null, null);

        var first = await _f.Editor.ApplyAsync(ApplyRequest(p, "key-1", confirm: true));
        var second = await _f.Editor.ApplyAsync(ApplyRequest(p, "key-1", confirm: true));

        Assert.True(first.Success, first.ErrorMessage);
        Assert.True(second.Success, second.ErrorMessage);
        Assert.True(second.Value!.ReusedPrevious);
        Assert.Equal(1, _f.Adapter.ApplyCalls);
    }

    [Fact]
    public async Task Apply_StaleFingerprint_DoesNotRewriteManifest()
    {
        await _f.GeneratePlanAsync();
        var p = await PrepareAsync();
        var manifestFile = Path.Combine(_f.Root, p.ManifestRelativePath.Replace('/', Path.DirectorySeparatorChar));
        File.WriteAllText(manifestFile, "{\"sentinela\":true}");

        var r = await _f.Editor.ApplyAsync(new ApplyEditorPlanRequest(TravelFixture.ProjectId, p.Revision, "fingerprint-antigo", true, "key-1"));

        Assert.Equal("PROJECT_VERSION_CONFLICT", r.ErrorCode);
        Assert.Equal("{\"sentinela\":true}", File.ReadAllText(manifestFile));
        Assert.Equal(0, _f.Adapter.ApplyCalls);
    }

    [Fact]
    public async Task Apply_PassesManifestAbsolutePath_InsideEditorFolder_WithoutWritingAnotherCopy()
    {
        await _f.GeneratePlanAsync();
        var p = await PrepareAsync();

        var r = await _f.Editor.ApplyAsync(ApplyRequest(p, "key-1"));

        Assert.True(r.Success, r.ErrorMessage);
        var manifest = _f.Adapter.LastOptions!.ManifestAbsolutePath;
        Assert.NotNull(manifest);
        Assert.True(File.Exists(manifest));
        var expected = Path.Combine(_f.Root, p.ManifestRelativePath.Replace('/', Path.DirectorySeparatorChar));
        Assert.Equal(Path.GetFullPath(expected), manifest);
        Assert.StartsWith(Path.Combine(_f.Root, "3-Trabalho", "editor"), manifest, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Apply_IsSimulated_ComesFromRealResultStatus_NotFromDryRun()
    {
        await _f.GeneratePlanAsync();
        _f.Adapter.Mode = "davinci";
        var p = await PrepareAsync();
        Assert.False(p.Capabilities.IsSimulated);
        _f.Adapter.OnApply = _ => new EditorAdapterApplyResult(EditorResultStatus.Simulated, null, [], [], [], [], null, null, null);

        var r = await _f.Editor.ApplyAsync(ApplyRequest(p, "key-1", confirm: true));

        Assert.True(r.Success, r.ErrorMessage);
        Assert.True(r.Value!.IsSimulated);
    }

    [Fact]
    public async Task Capabilities_DelegatesToAdapter()
    {
        var caps = await _f.Editor.CapabilitiesAsync();

        Assert.Equal("mock", caps.AdapterMode);
        Assert.True(caps.IsSimulated);
    }
}
