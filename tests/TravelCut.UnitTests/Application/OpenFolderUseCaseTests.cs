using Microsoft.Extensions.Logging.Abstractions;
using TravelCut.Application.Ipc;
using TravelCut.Application.Ports;
using TravelCut.Application.UseCases;
using TravelCut.Domain.Entities;
using Xunit;

namespace TravelCut.Tests.Application;

public class OpenFolderUseCaseTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "tc_open_root_" + Guid.NewGuid().ToString("N"));
    private readonly string _outside = Path.Combine(Path.GetTempPath(), "tc_open_out_" + Guid.NewGuid().ToString("N"));
    private readonly FakeProjects _projects = new();
    private readonly FakeOpener _opener = new();

    public OpenFolderUseCaseTests()
    {
        Directory.CreateDirectory(Path.Combine(_root, "1-Bruto"));
        Directory.CreateDirectory(_outside);
        File.WriteAllText(Path.Combine(_root, "1-Bruto", "clip.mp4"), "x");
        _projects.Items.Add(new Project { ProjectId = Guid.NewGuid(), Name = "P", RootPath = _root });
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
        if (Directory.Exists(_outside)) Directory.Delete(_outside, true);
    }

    private OpenFolderUseCase NewUseCase() => new(_projects, _opener, NullLogger<OpenFolderUseCase>.Instance);

    [Fact]
    public async Task UncPath_IsRejected_WithoutOpening()
    {
        var result = await NewUseCase().ExecuteAsync(@"\\servidor\compartilhado\projeto");

        Assert.False(result.Success);
        Assert.Equal("INVALID_PATH", result.ErrorCode);
        Assert.Empty(_opener.Calls);
    }

    [Fact]
    public async Task ExtendedUncPrefix_IsRejected()
    {
        var result = await NewUseCase().ExecuteAsync(@"\\?\UNC\servidor\share");

        Assert.False(result.Success);
        Assert.Equal("INVALID_PATH", result.ErrorCode);
    }

    [Fact]
    public async Task RelativePath_IsRejected()
    {
        var result = await NewUseCase().ExecuteAsync(@"1-Bruto\clip.mp4");

        Assert.False(result.Success);
        Assert.Equal("INVALID_PATH", result.ErrorCode);
        Assert.Empty(_opener.Calls);
    }

    [Fact]
    public async Task EmptyPath_IsRejected()
    {
        var result = await NewUseCase().ExecuteAsync("   ");

        Assert.False(result.Success);
        Assert.Equal("INVALID_PATH", result.ErrorCode);
    }

    [Fact]
    public async Task FolderOutsideAnyKnownProject_IsRejected()
    {
        var result = await NewUseCase().ExecuteAsync(_outside);

        Assert.False(result.Success);
        Assert.Equal("PATH_NOT_ALLOWED", result.ErrorCode);
        Assert.Empty(_opener.Calls);
    }

    [Fact]
    public async Task ParentTraversalEscapingProjectRoot_IsRejected()
    {
        // Após a normalização o caminho sai da raiz do projeto.
        string escaping = Path.Combine(_root, "1-Bruto", "..", "..", Path.GetFileName(_outside));

        var result = await NewUseCase().ExecuteAsync(escaping);

        Assert.False(result.Success);
        Assert.Equal("PATH_NOT_ALLOWED", result.ErrorCode);
        Assert.Empty(_opener.Calls);
    }

    [Fact]
    public async Task SiblingFolderWithSamePrefixAsRoot_IsRejected()
    {
        // "<root>-outro" começa com o texto da raiz, mas não está dentro dela.
        string sibling = _root + "-irmao";
        Directory.CreateDirectory(sibling);
        try
        {
            var result = await NewUseCase().ExecuteAsync(sibling);

            Assert.Equal("PATH_NOT_ALLOWED", result.ErrorCode);
        }
        finally
        {
            Directory.Delete(sibling, true);
        }
    }

    [Fact]
    public async Task FolderInsideProject_OpensFolderWithoutSelection()
    {
        var result = await NewUseCase().ExecuteAsync(Path.Combine(_root, "1-Bruto"));

        Assert.True(result.Success);
        Assert.Equal(Path.GetFullPath(Path.Combine(_root, "1-Bruto")), result.Outcome!.FullPath);
        Assert.False(result.Outcome.SelectedFile);
        var call = Assert.Single(_opener.Calls);
        Assert.False(call.SelectFile);
    }

    [Fact]
    public async Task ProjectRootItself_IsAllowed()
    {
        var result = await NewUseCase().ExecuteAsync(_root);

        Assert.True(result.Success);
        Assert.Single(_opener.Calls);
    }

    [Fact]
    public async Task FileInsideProject_OpensContainingFolderWithSelection()
    {
        string file = Path.Combine(_root, "1-Bruto", "clip.mp4");

        var result = await NewUseCase().ExecuteAsync(file);

        Assert.True(result.Success);
        Assert.True(result.Outcome!.SelectedFile);
        var call = Assert.Single(_opener.Calls);
        Assert.True(call.SelectFile);
        Assert.Equal(Path.GetFullPath(file), call.Path);
    }

    [Fact]
    public async Task MissingPathInsideProject_ReturnsNotFound()
    {
        var result = await NewUseCase().ExecuteAsync(Path.Combine(_root, "nao-existe"));

        Assert.False(result.Success);
        Assert.Equal("DIRECTORY_NOT_FOUND", result.ErrorCode);
        Assert.Empty(_opener.Calls);
    }

    [Fact]
    public async Task OpenerFailure_ReturnsGenericOpenFailed()
    {
        _opener.Throw = new InvalidOperationException("detalhe interno C:\\privado");

        var result = await NewUseCase().ExecuteAsync(Path.Combine(_root, "1-Bruto"));

        Assert.False(result.Success);
        Assert.Equal("OPEN_FAILED", result.ErrorCode);
        Assert.DoesNotContain("privado", result.ErrorMessage);
    }

    [Fact]
    public void IsSameOrInside_HandlesDriveRootAndSeparators()
    {
        string drive = Path.GetPathRoot(Path.GetFullPath(_root))!;

        Assert.True(OpenFolderUseCase.IsSameOrInside(drive, Path.Combine(drive, "qualquer", "pasta")));
        Assert.True(OpenFolderUseCase.IsSameOrInside(_root + Path.DirectorySeparatorChar, _root));
        Assert.False(OpenFolderUseCase.IsSameOrInside(_root, _root + "-x"));
    }

    private sealed class FakeProjects : IProjectRepository
    {
        public List<Project> Items { get; } = new();

        public Task<Project?> GetByIdAsync(Guid projectId, CancellationToken ct = default) =>
            Task.FromResult(Items.FirstOrDefault(p => p.ProjectId == projectId));

        public Task<Project?> GetByRootPathAsync(string rootPath, CancellationToken ct = default) =>
            Task.FromResult(Items.FirstOrDefault(p => p.RootPath == rootPath));

        public Task<List<Project>> ListRecentAsync(int limit = 10, CancellationToken ct = default) =>
            Task.FromResult(Items.Take(limit).ToList());

        public Task SaveAsync(Project project, CancellationToken ct = default)
        {
            Items.RemoveAll(p => p.ProjectId == project.ProjectId);
            Items.Add(project);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeOpener : IShellFolderOpener
    {
        public List<(string Path, bool SelectFile)> Calls { get; } = new();
        public Exception? Throw { get; set; }

        public void OpenInExplorer(string fullPath, bool selectFile)
        {
            if (Throw != null) throw Throw;
            Calls.Add((fullPath, selectFile));
        }
    }
}
