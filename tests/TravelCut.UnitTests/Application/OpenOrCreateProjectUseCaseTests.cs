using Microsoft.Extensions.Logging.Abstractions;
using TravelCut.Application.Ports;
using TravelCut.Application.UseCases;
using TravelCut.Domain.Entities;
using TravelCut.Domain.ValueObjects;
using TravelCut.Tests;
using Xunit;

namespace TravelCut.Tests.Application;

public class OpenOrCreateProjectUseCaseTests
{
    private class InMemoryProjectRepository : IProjectRepository
    {
        public readonly Dictionary<string, Project> ByPath = [];

        public Task<Project?> GetByIdAsync(Guid projectId, CancellationToken ct = default) =>
            Task.FromResult(ByPath.Values.FirstOrDefault(p => p.ProjectId == projectId));

        public Task<Project?> GetByRootPathAsync(string rootPath, CancellationToken ct = default) =>
            Task.FromResult(ByPath.TryGetValue(rootPath, out var p) ? p : null);

        public Task<List<Project>> ListRecentAsync(int limit = 10, CancellationToken ct = default) =>
            Task.FromResult(ByPath.Values.OrderByDescending(p => p.LastModifiedAtUtc).Take(limit).ToList());

        public Task SaveAsync(Project project, CancellationToken ct = default)
        {
            ByPath[project.RootPath] = project;
            return Task.CompletedTask;
        }
    }

    private class InMemoryMediaRepository : IMediaRepository
    {
        public readonly List<MediaAsset> Saved = [];

        public Task<MediaAsset?> GetByIdAsync(Guid mediaId, CancellationToken ct = default) =>
            Task.FromResult(Saved.FirstOrDefault(a => a.MediaId == mediaId));
        public Task<List<MediaAsset>> ListByProjectAsync(Guid projectId, CancellationToken ct = default) =>
            Task.FromResult(Saved.Where(a => a.ProjectId == projectId).ToList());
        public Task SaveAsync(MediaAsset asset, CancellationToken ct = default)
        {
            Saved.RemoveAll(a => a.MediaId == asset.MediaId);
            Saved.Add(asset);
            return Task.CompletedTask;
        }
        public Task SaveBatchAsync(IEnumerable<MediaAsset> assets, CancellationToken ct = default)
        {
            foreach (var asset in assets) Saved.Add(asset);
            return Task.CompletedTask;
        }
    }

    private class InMemoryEditPlanRepository : IEditPlanRepository
    {
        public Task<EditPlan?> GetLatestByProjectAsync(Guid projectId, CancellationToken ct = default) => Task.FromResult<EditPlan?>(null);
        public Task SavePlanAsync(EditPlan plan, CancellationToken ct = default) => Task.CompletedTask;
        public Task<List<EditSuggestion>> ListSuggestionsByProjectAsync(Guid projectId, CancellationToken ct = default) => Task.FromResult(new List<EditSuggestion>());
        public Task SaveSuggestionsBatchAsync(IEnumerable<EditSuggestion> suggestions, CancellationToken ct = default) => Task.CompletedTask;
        public Task SaveDecisionAsync(EditDecision decision, CancellationToken ct = default) => Task.CompletedTask;
        public Task<EditDecision?> SaveDecisionIfAsync(Guid suggestionId, Func<IReadOnlyList<EditDecision>, EditDecision?> build, CancellationToken ct = default) =>
            Task.FromResult(build(new List<EditDecision>()));
        public Task<List<EditDecision>> ListDecisionsByProjectAsync(Guid projectId, CancellationToken ct = default) => Task.FromResult(new List<EditDecision>());
        public Task<EditDecision?> GetLatestDecisionBySuggestionAsync(Guid suggestionId, CancellationToken ct = default) => Task.FromResult<EditDecision?>(null);
        public Task<List<Guid>> FindExistingSuggestionIdsAsync(IReadOnlyCollection<Guid> suggestionIds, CancellationToken ct = default) => Task.FromResult(new List<Guid>());
        public Task<List<EditDecision>> ListDecisionsBySuggestionAsync(Guid suggestionId, CancellationToken ct = default) => Task.FromResult(new List<EditDecision>());
        public Task SaveDecisionsAtomicAsync(IReadOnlyList<EditDecision> decisions, CancellationToken ct = default) => Task.CompletedTask;
        public Task<List<EditSuggestion>> ListSuggestionsByIdsAsync(IReadOnlyCollection<Guid> suggestionIds, CancellationToken ct = default) => Task.FromResult(new List<EditSuggestion>());
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsFailure_WhenDirectoryDoesNotExist()
    {
        var projectRepo = new InMemoryProjectRepository();
        var mediaRepo = new InMemoryMediaRepository();
        var planRepo = new InMemoryEditPlanRepository();
        var useCase = new OpenOrCreateProjectUseCase(
            projectRepo,
            mediaRepo,
            planRepo,
            NullLogger<OpenOrCreateProjectUseCase>.Instance
        );

        string fakePath = Path.Combine(Path.GetTempPath(), "tc_nonexistent_" + Guid.NewGuid().ToString("N"));

        var response = await useCase.ExecuteAsync(fakePath);

        Assert.False(response.Success);
        Assert.Equal("DIRECTORY_NOT_FOUND", response.ErrorCode);
        Assert.NotNull(response.ErrorMessage);
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsFailure_WhenMissingFourFolders()
    {
        var projectRepo = new InMemoryProjectRepository();
        var mediaRepo = new InMemoryMediaRepository();
        var planRepo = new InMemoryEditPlanRepository();
        var useCase = new OpenOrCreateProjectUseCase(
            projectRepo,
            mediaRepo,
            planRepo,
            NullLogger<OpenOrCreateProjectUseCase>.Instance
        );

        string tempDir = Path.Combine(Path.GetTempPath(), "tc_missing_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            Directory.CreateDirectory(Path.Combine(tempDir, "1-Bruto"));
            // Missing 2-Assets, 3-Trabalho, 4-Final

            var response = await useCase.ExecuteAsync(tempDir);

            Assert.False(response.Success);
            Assert.Equal("INVALID_FOUR_FOLDERS", response.ErrorCode);
            Assert.Contains("2-Assets", response.ErrorMessage);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task ExecuteAsync_CreatesAndPersistsProject_WhenStructureIsValid()
    {
        var projectRepo = new InMemoryProjectRepository();
        var mediaRepo = new InMemoryMediaRepository();
        var planRepo = new InMemoryEditPlanRepository();
        var useCase = new OpenOrCreateProjectUseCase(
            projectRepo,
            mediaRepo,
            planRepo,
            NullLogger<OpenOrCreateProjectUseCase>.Instance
        );

        string tempDir = Path.Combine(Path.GetTempPath(), "tc_valid_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            Directory.CreateDirectory(Path.Combine(tempDir, "1-Bruto"));
            Directory.CreateDirectory(Path.Combine(tempDir, "2-Assets"));
            Directory.CreateDirectory(Path.Combine(tempDir, "3-Trabalho"));
            Directory.CreateDirectory(Path.Combine(tempDir, "4-Final"));

            var response = await useCase.ExecuteAsync(tempDir);

            Assert.True(response.Success);
            Assert.NotNull(response.Summary);
            Assert.Equal(Path.GetFullPath(tempDir), response.Summary.RootPath);
            Assert.True(projectRepo.ByPath.ContainsKey(Path.GetFullPath(tempDir)));
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task ExecuteAsync_AutoCreatesFourFolders_WhenRequested()
    {
        var projectRepo = new InMemoryProjectRepository();
        var mediaRepo = new InMemoryMediaRepository();
        var planRepo = new InMemoryEditPlanRepository();
        var useCase = new OpenOrCreateProjectUseCase(
            projectRepo,
            mediaRepo,
            planRepo,
            NullLogger<OpenOrCreateProjectUseCase>.Instance
        );

        string tempDir = Path.Combine(Path.GetTempPath(), "tc_autocreate_" + Guid.NewGuid().ToString("N"));
        try
        {
            var response = await useCase.ExecuteAsync(tempDir, autoCreateFolders: true, projectName: "Projeto Viagem Japao");

            Assert.True(response.Success);
            Assert.NotNull(response.Summary);
            Assert.Equal("Projeto Viagem Japao", response.Summary.Name);
            Assert.True(Directory.Exists(Path.Combine(tempDir, "1-Bruto")));
            Assert.True(Directory.Exists(Path.Combine(tempDir, "2-Assets")));
            Assert.True(Directory.Exists(Path.Combine(tempDir, "3-Trabalho")));
            Assert.True(Directory.Exists(Path.Combine(tempDir, "4-Final")));
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task ExecuteAsync_ReportsCreatedFoldersAndNewProject_ThenExistingOnSecondOpen()
    {
        var projectRepo = new InMemoryProjectRepository();
        var useCase = new OpenOrCreateProjectUseCase(projectRepo, new InMemoryMediaRepository(), new InMemoryEditPlanRepository(),
            NullLogger<OpenOrCreateProjectUseCase>.Instance);
        string tempDir = Path.Combine(Path.GetTempPath(), "tc_created_" + Guid.NewGuid().ToString("N"));
        try
        {
            var first = await useCase.ExecuteAsync(tempDir, autoCreateFolders: true);

            Assert.True(first.Success);
            Assert.True(first.IsNewProject);
            Assert.Equal(new[] { "1-Bruto", "2-Assets", "3-Trabalho", "4-Final" }, first.CreatedFolders);

            var second = await useCase.ExecuteAsync(tempDir, autoCreateFolders: true);

            Assert.True(second.Success);
            Assert.False(second.IsNewProject);
            Assert.Empty(second.CreatedFolders);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task ExecuteAsync_ExistingFoldersAreNotReportedAsCreated()
    {
        var useCase = new OpenOrCreateProjectUseCase(new InMemoryProjectRepository(), new InMemoryMediaRepository(), new InMemoryEditPlanRepository(),
            NullLogger<OpenOrCreateProjectUseCase>.Instance);
        string tempDir = Path.Combine(Path.GetTempPath(), "tc_partial_" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(tempDir, "1-Bruto"));
            Directory.CreateDirectory(Path.Combine(tempDir, "2-Assets"));

            var response = await useCase.ExecuteAsync(tempDir, autoCreateFolders: true);

            Assert.True(response.Success);
            Assert.Equal(new[] { "3-Trabalho", "4-Final" }, response.CreatedFolders);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task Discovery_DoesNotFollowJunctions_AndRegistersOnlyRealMediaWithRelativePathInsideProject()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "tc_junction_" + Guid.NewGuid().ToString("N"));
        string external = Path.Combine(Path.GetTempPath(), "tc_external_" + Guid.NewGuid().ToString("N"));
        string junction = Path.Combine(tempDir, "1-Bruto", "atalho-externo");
        var mediaRepo = new InMemoryMediaRepository();
        var useCase = new OpenOrCreateProjectUseCase(new InMemoryProjectRepository(), mediaRepo, new InMemoryEditPlanRepository(),
            NullLogger<OpenOrCreateProjectUseCase>.Instance);
        try
        {
            Directory.CreateDirectory(Path.Combine(tempDir, "1-Bruto"));
            Directory.CreateDirectory(Path.Combine(tempDir, "2-Assets"));
            Directory.CreateDirectory(Path.Combine(tempDir, "3-Trabalho"));
            Directory.CreateDirectory(Path.Combine(tempDir, "4-Final"));
            Directory.CreateDirectory(external);
            File.WriteAllBytes(Path.Combine(external, "fora.mp4"), new byte[8]);
            File.WriteAllBytes(Path.Combine(tempDir, "1-Bruto", "dentro.mp4"), new byte[8]);
            TestJunction.Create(junction, external);

            var response = await useCase.ExecuteAsync(tempDir);

            Assert.True(response.Success);
            var asset = Assert.Single(mediaRepo.Saved);
            Assert.Equal("1-Bruto/dentro.mp4", asset.RelativePath);
            Assert.DoesNotContain(mediaRepo.Saved, a => a.RelativePath.Contains("fora.mp4"));
        }
        finally
        {
            TestJunction.Remove(junction);
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
            if (Directory.Exists(external)) Directory.Delete(external, true);
        }
    }
}
