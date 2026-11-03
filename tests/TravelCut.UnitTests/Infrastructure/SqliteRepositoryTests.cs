using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using TravelCut.Domain.Entities;
using TravelCut.Domain.Enums;
using TravelCut.Domain.ValueObjects;
using TravelCut.Infrastructure.Persistence;
using TravelCut.Infrastructure.Repositories;
using Xunit;

namespace TravelCut.Tests.Infrastructure;

public class SqliteRepositoryTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly TravelCutDbContext _dbContext;

    public SqliteRepositoryTests()
    {
        _connection = new SqliteConnection("Filename=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<TravelCutDbContext>()
            .UseSqlite(_connection)
            .Options;

        _dbContext = new TravelCutDbContext(options);
        _dbContext.Database.EnsureCreated();
    }

    [Fact]
    public async Task ProjectRepository_SaveAndGetByPath_WorksCorrectly()
    {
        var repo = new SqliteProjectRepository(_dbContext);
        var project = new Project
        {
            ProjectId = Guid.NewGuid(),
            Name = "Viagem Japao 2026",
            RootPath = @"D:\Projetos\ViagemJapao",
            CreatedAtUtc = DateTime.UtcNow,
            LastModifiedAtUtc = DateTime.UtcNow
        };

        await repo.SaveAsync(project);

        var retrieved = await repo.GetByRootPathAsync(@"D:\Projetos\ViagemJapao");
        Assert.NotNull(retrieved);
        Assert.Equal(project.ProjectId, retrieved.ProjectId);
        Assert.Equal("Viagem Japao 2026", retrieved.Name);
    }

    [Fact]
    public async Task MediaAndEditPlanRepository_SaveAndRetrieve_WorksCorrectly()
    {
        var projectRepo = new SqliteProjectRepository(_dbContext);
        var mediaRepo = new SqliteMediaRepository(_dbContext);
        var planRepo = new SqliteEditPlanRepository(_dbContext);

        var project = new Project
        {
            ProjectId = Guid.NewGuid(),
            Name = "Viagem Atacama",
            RootPath = @"D:\Projetos\Atacama",
            CreatedAtUtc = DateTime.UtcNow,
            LastModifiedAtUtc = DateTime.UtcNow
        };
        await projectRepo.SaveAsync(project);

        var media = new MediaAsset
        {
            MediaId = Guid.NewGuid(),
            ProjectId = project.ProjectId,
            RelativePath = "1-Bruto/gopro_001.mp4",
            Metadata = new MediaMetadata
            {
                MediaId = Guid.NewGuid(),
                DurationSeconds = 45.0,
                Codec = "h264",
                Width = 3840,
                Height = 2160,
                TimeBase = RationalTimeBase.Fps30
            }
        };
        await mediaRepo.SaveAsync(media);

        var suggestion = new EditSuggestion
        {
            SuggestionId = Guid.NewGuid(),
            MediaId = media.MediaId,
            SourceInterval = new TimeRange(0, 90),
            Kind = SuggestionKind.Silence,
            Rationale = "Silêncio longo inicial",
            SuggestedAction = "cut",
            ConfidenceBand = "high",
            Status = SuggestionStatus.Proposed,
            Author = "vad"
        };
        await planRepo.SaveSuggestionsBatchAsync([suggestion]);

        var decision = new EditDecision
        {
            DecisionId = Guid.NewGuid(),
            SuggestionId = suggestion.SuggestionId,
            Action = "approved",
            Reviewer = "lead_editor",
            TimestampUtc = DateTime.UtcNow
        };
        await planRepo.SaveDecisionAsync(decision);

        var suggestions = await planRepo.ListSuggestionsByProjectAsync(project.ProjectId);
        Assert.Single(suggestions);
        Assert.Equal(suggestion.SuggestionId, suggestions[0].SuggestionId);

        var decisions = await planRepo.ListDecisionsByProjectAsync(project.ProjectId);
        Assert.Single(decisions);
        Assert.Equal("approved", decisions[0].Action);
        Assert.Equal("lead_editor", decisions[0].Reviewer);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
        _connection.Dispose();
    }
}
