using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using TravelCut.Domain.Entities;
using TravelCut.Domain.Enums;
using TravelCut.Domain.ValueObjects;
using TravelCut.Infrastructure.Persistence;
using TravelCut.Infrastructure.Repositories;
using Xunit;

namespace TravelCut.Tests.Infrastructure;

public class MediaTagsPersistenceTests : IDisposable
{
    private readonly SqliteConnection _connection;

    public MediaTagsPersistenceTests()
    {
        _connection = new SqliteConnection("Filename=:memory:");
        _connection.Open();
    }

    [Fact]
    public async Task TagsAndMetadataStatusRoundTripThroughSqlite()
    {
        using var db = CreateContext();
        db.Database.EnsureCreated();
        var repo = new SqliteMediaRepository(db);
        var projectId = Guid.NewGuid();

        var asset = new MediaAsset
        {
            ProjectId = projectId,
            RelativePath = "1-Bruto/praia.mp4",
            ByteLength = 1024,
            Kind = MediaKind.Video,
            Tags = new List<string> { "viagem", "praia" },
            Metadata = new MediaMetadata
            {
                Codec = "unknown",
                TimeBase = new RationalTimeBase(0, 0),
                MetadataStatus = MediaMetadataStatus.FfprobeUnavailable
            }
        };
        await repo.SaveAsync(asset);

        db.ChangeTracker.Clear();
        var loaded = (await repo.ListByProjectAsync(projectId)).Single();

        Assert.Equal(new[] { "viagem", "praia" }, loaded.Tags);
        Assert.Equal(MediaMetadataStatus.FfprobeUnavailable, loaded.Metadata!.MetadataStatus);
    }

    [Fact]
    public async Task ChangingTagsIsPersisted()
    {
        using var db = CreateContext();
        db.Database.EnsureCreated();
        var repo = new SqliteMediaRepository(db);
        var projectId = Guid.NewGuid();

        var asset = new MediaAsset
        {
            ProjectId = projectId,
            RelativePath = "1-Bruto/a.mp4",
            Tags = new List<string>(),
            Metadata = new MediaMetadata { MetadataStatus = MediaMetadataStatus.NotRequested }
        };
        await repo.SaveAsync(asset);

        asset.Tags = new List<string> { "neve" };
        await repo.SaveAsync(asset);

        db.ChangeTracker.Clear();
        var loaded = (await repo.ListByProjectAsync(projectId)).Single();
        Assert.Equal(new[] { "neve" }, loaded.Tags);
    }

    [Fact]
    public void EnsureSchemaUpdatedAddsTagsAndMetadataStatusToLegacyTables()
    {
        // Esquema anterior: sem TagsJson e sem MetadataStatus.
        using (var legacy = CreateContext())
        {
            legacy.Database.ExecuteSqlRaw(
                "CREATE TABLE MediaAssets (MediaId TEXT NOT NULL PRIMARY KEY, ProjectId TEXT NOT NULL, RelativePath TEXT NOT NULL, " +
                "ByteLength INTEGER NOT NULL, LastWriteUtc TEXT NOT NULL, Fingerprint TEXT NULL, Kind INTEGER NOT NULL, " +
                "IsArchived INTEGER NOT NULL, DiscoveredAtUtc TEXT NOT NULL);");
            legacy.Database.ExecuteSqlRaw(
                "CREATE TABLE MediaMetadata (MediaId TEXT NOT NULL PRIMARY KEY, Codec TEXT NULL, Width INTEGER NOT NULL, " +
                "Height INTEGER NOT NULL, TimeBase TEXT NOT NULL, FrameRate REAL NOT NULL, DurationSeconds REAL NOT NULL, " +
                "DurationTicks INTEGER NOT NULL, AudioChannels INTEGER NOT NULL, AudioSampleRate INTEGER NOT NULL, " +
                "CreationTimeUtc TEXT NULL, SourceDateEvidence TEXT NULL);");
            legacy.Database.ExecuteSqlRaw(
                "INSERT INTO MediaAssets (MediaId, ProjectId, RelativePath, ByteLength, LastWriteUtc, Fingerprint, Kind, IsArchived, DiscoveredAtUtc) " +
                "VALUES ('11111111-1111-1111-1111-111111111111', '22222222-2222-2222-2222-222222222222', '1-Bruto/antigo.mp4', 10, " +
                "'2024-01-01 00:00:00', 'q1', 0, 0, '2024-01-01 00:00:00');");
            legacy.EnsureSchemaUpdated();
        }

        using var db = CreateContext();
        var assetColumns = ReadColumns("MediaAssets");
        var metadataColumns = ReadColumns("MediaMetadata");

        Assert.Contains(TravelCutDbContext.TagsColumnName, assetColumns);
        Assert.Contains("MetadataStatus", metadataColumns);

        // Linha antiga continua legível e recebe tags vazias.
        var repo = new SqliteMediaRepository(db);
        var old = Assert.Single(repo.ListByProjectAsync(Guid.Parse("22222222-2222-2222-2222-222222222222")).Result);
        Assert.Empty(old.Tags);
    }

    [Fact]
    public void EnsureSchemaUpdatedIsIdempotent()
    {
        using var db = CreateContext();
        db.EnsureSchemaUpdated();
        db.EnsureSchemaUpdated();

        Assert.Equal(1, ReadColumns("MediaAssets").Count(c => c == TravelCutDbContext.TagsColumnName));
    }

    private TravelCutDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<TravelCutDbContext>()
            .UseSqlite(_connection)
            .Options;
        return new TravelCutDbContext(options);
    }

    private List<string> ReadColumns(string table)
    {
        var columns = new List<string>();
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info({table});";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            columns.Add(reader["name"].ToString()!);
        }
        return columns;
    }

    public void Dispose() => _connection.Dispose();
}
