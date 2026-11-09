using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TravelCut.Application.Planning;
using TravelCut.Application.Services;
using TravelCut.Contracts.Dtos;
using TravelCut.Domain.Entities;
using TravelCut.Infrastructure.Persistence;
using TravelCut.Infrastructure.Planning;
using TravelCut.Infrastructure.Repositories;
using Xunit;

namespace TravelCut.UnitTests.Planning;

/// <summary>F14.8 / I1: perfil de automação persistido por projeto (SQLite real em memória) e usado na geração do plano.</summary>
public class AutomationProfileTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly TravelCutDbContext _db;
    private readonly string _root;
    private readonly SqliteProjectRepository _projects;
    private readonly PlanningUseCases _useCases;

    public AutomationProfileTests()
    {
        _connection = new SqliteConnection("Filename=:memory:");
        _connection.Open();
        _db = new TravelCutDbContext(new DbContextOptionsBuilder<TravelCutDbContext>().UseSqlite(_connection).Options);
        _db.Database.EnsureCreated();

        _root = Path.Combine(Path.GetTempPath(), "tc_autoprof_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);

        _projects = new SqliteProjectRepository(_db);
        var media = new SqliteMediaRepository(_db);
        var suggestions = new SqliteEditPlanRepository(_db);
        var analysis = new FakePipelineAnalysis();

        _projects.SaveAsync(new Project { ProjectId = TravelFixture.ProjectId, Name = "Viagem", RootPath = _root }).GetAwaiter().GetResult();
        media.SaveBatchAsync(TravelFixture.Media()).GetAwaiter().GetResult();
        foreach (var m in TravelFixture.Media())
        {
            var full = Path.Combine(_root, m.RelativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(full, "midia-de-teste");
        }
        foreach (var s in TravelFixture.Suggestions()) suggestions.SaveSuggestionsBatchAsync([s]).GetAwaiter().GetResult();
        foreach (var d in TravelFixture.Decisions()) suggestions.SaveDecisionAsync(d).GetAwaiter().GetResult();
        foreach (var (id, a) in TravelFixture.Analyses()) analysis.Analyses[id] = a;

        _useCases = new PlanningUseCases(
            _projects, media, suggestions, analysis, new ChronologyService(), new SqlitePlanRevisionRepository(_db),
            new PlanArtifactStore(), new FileSystemFileExistence(), TimeProvider.System, NullLogger<PlanningUseCases>.Instance);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
        catch (IOException) { /* limpeza melhor esforço */ }
    }

    [Fact]
    public async Task Get_ProjectNeverConfigured_ReturnsBalancedDefault()
    {
        var result = await _useCases.GetAutomationProfileAsync(new GetAutomationProfileRequest(TravelFixture.ProjectId));

        Assert.True(result.Success);
        Assert.Equal("balanced", result.Value!.Profile);
        Assert.True(result.Value.IsDefault);
    }

    [Theory]
    [InlineData("conservative")]
    [InlineData("aggressive")]
    [InlineData("balanced")]
    public async Task SetThenGet_RoundTripsThroughSqlite(string profile)
    {
        var set = await _useCases.SetAutomationProfileAsync(new SetAutomationProfileRequest(TravelFixture.ProjectId, profile));
        Assert.True(set.Success, set.ErrorMessage);
        Assert.False(set.Value!.IsDefault);

        _db.ChangeTracker.Clear(); // força releitura do banco
        var get = await _useCases.GetAutomationProfileAsync(new GetAutomationProfileRequest(TravelFixture.ProjectId));

        Assert.Equal(profile, get.Value!.Profile);
        Assert.False(get.Value.IsDefault);
    }

    [Fact]
    public async Task Set_NormalizesCase_AndRejectsUnknownValueWithoutChangingStoredProfile()
    {
        await _useCases.SetAutomationProfileAsync(new SetAutomationProfileRequest(TravelFixture.ProjectId, " Aggressive "));
        var bad = await _useCases.SetAutomationProfileAsync(new SetAutomationProfileRequest(TravelFixture.ProjectId, "turbo"));

        Assert.False(bad.Success);
        Assert.Equal("INVALID_ARGUMENT", bad.ErrorCode);
        var get = await _useCases.GetAutomationProfileAsync(new GetAutomationProfileRequest(TravelFixture.ProjectId));
        Assert.Equal("aggressive", get.Value!.Profile);
    }

    [Fact]
    public async Task Set_UnknownProject_ReturnsProjectNotFound()
    {
        var result = await _useCases.SetAutomationProfileAsync(new SetAutomationProfileRequest(Guid.NewGuid(), "balanced"));

        Assert.Equal("PROJECT_NOT_FOUND", result.ErrorCode);
    }

    [Fact]
    public async Task Generate_WithoutProfile_ProjectNeverConfigured_UsesBalanced()
    {
        var result = await _useCases.GenerateAsync(new GeneratePlanRequest(TravelFixture.ProjectId, null, null));

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Equal("balanced", result.Value!.Profile.SilencePolicy);
    }

    [Theory]
    [InlineData("conservative")]
    [InlineData("aggressive")]
    public async Task Generate_WithoutProfile_UsesPersistedProfile(string profile)
    {
        await _useCases.SetAutomationProfileAsync(new SetAutomationProfileRequest(TravelFixture.ProjectId, profile));

        var result = await _useCases.GenerateAsync(new GeneratePlanRequest(TravelFixture.ProjectId, null, null));

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Equal(profile, result.Value!.Profile.SilencePolicy);
    }

    [Fact]
    public async Task Generate_WithExplicitProfile_WinsOverPersisted_AndDoesNotPersistIt()
    {
        await _useCases.SetAutomationProfileAsync(new SetAutomationProfileRequest(TravelFixture.ProjectId, "conservative"));
        var explicitProfile = EditorialProfiles.Default() with { SilencePolicy = "aggressive" };

        var result = await _useCases.GenerateAsync(new GeneratePlanRequest(TravelFixture.ProjectId, explicitProfile, null));

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Equal("aggressive", result.Value!.Profile.SilencePolicy);
        var get = await _useCases.GetAutomationProfileAsync(new GetAutomationProfileRequest(TravelFixture.ProjectId));
        Assert.Equal("conservative", get.Value!.Profile);
    }

    [Fact]
    public async Task ProjectWithUnknownStoredValue_FallsBackToBalanced()
    {
        var project = (await _projects.GetByIdAsync(TravelFixture.ProjectId))!;
        project.AutomationProfile = "valor-corrompido";
        await _projects.SaveAsync(project);

        var get = await _useCases.GetAutomationProfileAsync(new GetAutomationProfileRequest(TravelFixture.ProjectId));
        var generated = await _useCases.GenerateAsync(new GeneratePlanRequest(TravelFixture.ProjectId, null, null));

        Assert.Equal("balanced", get.Value!.Profile);
        Assert.True(get.Value.IsDefault);
        Assert.Equal("balanced", generated.Value!.Profile.SilencePolicy);
    }

    [Fact]
    public async Task LegacyProjectsTableWithoutColumn_IsMigratedAndReadsAsBalancedDefault()
    {
        using var connection = new SqliteConnection("Filename=:memory:");
        connection.Open();
        var options = new DbContextOptionsBuilder<TravelCutDbContext>().UseSqlite(connection).Options;
        var legacyId = Guid.NewGuid();
        using (var legacy = new TravelCutDbContext(options))
        {
            // Esquema anterior ao campo: Projects sem AutomationProfile.
            legacy.Database.ExecuteSqlRaw(
                "CREATE TABLE Projects (ProjectId TEXT NOT NULL PRIMARY KEY, Name TEXT NOT NULL, RootPath TEXT NOT NULL, " +
                "CreatedAtUtc TEXT NOT NULL, LastModifiedAtUtc TEXT NOT NULL, ProfileVersion INTEGER NOT NULL);");
            legacy.Database.ExecuteSqlRaw(
                $"INSERT INTO Projects VALUES ('{legacyId.ToString().ToUpperInvariant()}', 'Antigo', 'C:\\\\antigo', '2024-01-01 00:00:00', '2024-01-01 00:00:00', 1);");
            legacy.EnsureSchemaUpdated();
        }

        using var db = new TravelCutDbContext(options);
        var repo = new SqliteProjectRepository(db);
        var project = (await repo.GetByIdAsync(legacyId))!;

        Assert.Null(project.AutomationProfile);
        var dto = (await new PlanningUseCases(repo, new SqliteMediaRepository(db), new SqliteEditPlanRepository(db), new FakePipelineAnalysis(),
            new ChronologyService(), new SqlitePlanRevisionRepository(db), new PlanArtifactStore(), new FileSystemFileExistence(),
            TimeProvider.System, NullLogger<PlanningUseCases>.Instance)
            .GetAutomationProfileAsync(new GetAutomationProfileRequest(legacyId))).Value!;
        Assert.Equal("balanced", dto.Profile);
        Assert.True(dto.IsDefault);

        // Escrita funciona depois da migração.
        project.AutomationProfile = "aggressive";
        await repo.SaveAsync(project);
        db.ChangeTracker.Clear();
        Assert.Equal("aggressive", (await repo.GetByIdAsync(legacyId))!.AutomationProfile);
    }
}
