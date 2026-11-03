using Microsoft.Extensions.Logging.Abstractions;
using TravelCut.Application.Editor;
using TravelCut.Application.Ports;
using TravelCut.Contracts.Dtos;
using TravelCut.Infrastructure.Editor;
using Xunit;
using static TravelCut.UnitTests.Editor.EditorTestData;

namespace TravelCut.UnitTests.Editor;

public class FileEditorArtifactStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "tc_store_" + Guid.NewGuid().ToString("N"));
    private readonly FileEditorArtifactStore _store = new(NullLogger<FileEditorArtifactStore>.Instance);

    public FileEditorArtifactStoreTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // Pasta temporária: limpeza melhor esforço.
        }
    }

    private static EditorAssemblyDto Assembly(int clips = 1)
    {
        var plan = Plan(Timeline2997, Enumerable.Range(1, clips)
            .Select(i => Clip(i, "V1", 1, 1_000_000, 2_000_000, start: (i - 1) * 30L, duration: 30))
            .ToArray());
        return EditorAssemblyBuilder.Build(plan, Sources(Src(1)), "Viagem", NowUtc).Assembly!;
    }

    private static EditorApplyResultDto Application(Guid applicationId, string key = "key-1") => new(
        SchemaVersion: "editor-apply/1",
        ApplicationId: applicationId,
        Status: "SIMULATED",
        AdapterMode: "mock",
        IsSimulated: true,
        ProjectId: ProjectId,
        PlanId: PlanId,
        Revision: 1,
        AssemblyFingerprint: "fp",
        IdempotencyKey: key,
        EditorProjectName: "TravelCut_Viagem_r0001_20261102T120000",
        TimelineName: "TravelCut r0001",
        EditorVersion: null,
        Steps: [],
        ClipMappings: [],
        Rejected: [],
        Warnings: [],
        SnapshotRelativePath: null,
        RecordRelativePath: EditorStoragePaths.RelativeApplicationPath(PlanId, applicationId),
        StartedAtUtc: NowUtc,
        FinishedAtUtc: NowUtc,
        ErrorCode: null,
        ErrorMessage: null,
        ReusedPrevious: false);

    [Fact]
    public async Task WriteAssembly_WritesUnderEditorFolder_WithCamelCaseJson()
    {
        var rel = await _store.WriteAssemblyAsync(_root, Assembly());

        Assert.Equal($"3-Trabalho/editor/{PlanId:D}/r0001.assembly.json", rel);
        var full = Path.Combine(_root, rel.Replace('/', Path.DirectorySeparatorChar));
        var text = await File.ReadAllTextAsync(full);
        Assert.Contains("\"schemaVersion\": \"editor-assembly/1\"", text);
        Assert.Contains("\"assemblyFingerprint\"", text);
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(full)!, "*.tmp"));
    }

    [Fact]
    public async Task WriteAssembly_IsDerived_SoOverwriteIsAllowed()
    {
        await _store.WriteAssemblyAsync(_root, Assembly(clips: 1));
        var rel = await _store.WriteAssemblyAsync(_root, Assembly(clips: 2));

        var text = await File.ReadAllTextAsync(Path.Combine(_root, rel.Replace('/', Path.DirectorySeparatorChar)));
        Assert.Contains("\"sourceStartFrame\"", text);
    }

    [Fact]
    public async Task WriteApplication_NeverOverwrites()
    {
        var id = Guid.NewGuid();
        var rel = await _store.WriteApplicationAsync(_root, Application(id, "primeiro"));
        Assert.Equal(EditorStoragePaths.RelativeApplicationPath(PlanId, id), rel);

        await Assert.ThrowsAsync<EditorArtifactConflictException>(() => _store.WriteApplicationAsync(_root, Application(id, "segundo")));

        var text = await File.ReadAllTextAsync(Path.Combine(_root, rel.Replace('/', Path.DirectorySeparatorChar)));
        Assert.Contains("\"idempotencyKey\": \"primeiro\"", text);
    }

    [Fact]
    public async Task WriteApplication_RejectsInconsistentRecordPath()
    {
        var bad = Application(Guid.NewGuid()) with { RecordRelativePath = "3-Trabalho/editor/../../evil.json" };

        await Assert.ThrowsAsync<ArgumentException>(() => _store.WriteApplicationAsync(_root, bad));
        Assert.False(File.Exists(Path.Combine(_root, "evil.json")));
    }

    [Fact]
    public async Task ListApplications_SkipsCorruptFiles_AndReturnsValidOnes()
    {
        var id = Guid.NewGuid();
        await _store.WriteApplicationAsync(_root, Application(id));
        var dir = Path.Combine(_root, "3-Trabalho", "editor", PlanId.ToString("D"), "applications");
        await File.WriteAllTextAsync(Path.Combine(dir, "corrompido.json"), "{ isto não é json");

        var list = await _store.ListApplicationsAsync(_root);

        var only = Assert.Single(list);
        Assert.Equal(id, only.ApplicationId);
    }

    [Fact]
    public async Task ListApplications_EmptyWhenNothingWritten()
    {
        var list = await _store.ListApplicationsAsync(_root);

        Assert.Empty(list);
    }

    [Fact]
    public void SnapshotAndPreviewPaths_StayInsideEditorFolder()
    {
        var snapshot = _store.GetSnapshotAbsolutePath(_root, PlanId, "TravelCut_Viagem_r0001_20261102T120000");
        var preview = _store.GetPreviewDirectory(_root, PlanId, "TravelCut_Viagem_r0001_20261102T120000");

        var editorBase = Path.Combine(Path.GetFullPath(_root), "3-Trabalho", "editor");
        Assert.StartsWith(editorBase, snapshot, StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith(editorBase, preview, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith(Path.Combine("snapshots", "TravelCut_Viagem_r0001_20261102T120000.drp"), snapshot, StringComparison.OrdinalIgnoreCase);
        Assert.False(Directory.Exists(Path.GetDirectoryName(snapshot)!), "Get não deve criar diretórios.");
    }

    [Theory]
    [InlineData("..\\..\\evil")]
    [InlineData("nome/com/barra")]
    [InlineData("")]
    [InlineData("com espaço")]
    public void SnapshotPath_RejectsUnsafeNames(string name)
    {
        Assert.Throws<ArgumentException>(() => _store.GetSnapshotAbsolutePath(_root, PlanId, name));
    }

    [Fact]
    public void TryGetRelativePath_InsideEditor_ReturnsRelative_OutsideReturnsNull()
    {
        var inside = Path.Combine(_root, "3-Trabalho", "editor", "x", "snap.drp");
        var outside = Path.Combine(_root, "1-Bruto", "video.mp4");

        Assert.Equal("3-Trabalho/editor/x/snap.drp", _store.TryGetRelativePath(_root, inside));
        Assert.Null(_store.TryGetRelativePath(_root, outside));
        Assert.Null(_store.TryGetRelativePath(_root, Path.Combine(_root, "3-Trabalho", "editor-evil", "x.drp")));
    }
}
