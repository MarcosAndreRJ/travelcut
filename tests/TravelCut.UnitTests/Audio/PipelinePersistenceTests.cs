using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using TravelCut.Domain.Entities;
using TravelCut.Infrastructure.Persistence;
using TravelCut.Infrastructure.Repositories;
using Xunit;

namespace TravelCut.UnitTests.Audio;

public class PipelinePersistenceTests : IDisposable
{
    private readonly List<IDisposable> _disposables = new();
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), "TravelCutPersist_" + Guid.NewGuid().ToString("N"));

    public PipelinePersistenceTests() => Directory.CreateDirectory(_tempDir);

    public void Dispose()
    {
        foreach (var d in _disposables) d.Dispose();
        try
        {
            if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true);
        }
        catch (IOException)
        {
            // Limpeza best-effort.
        }
    }

    private TravelCutDbContext NewMemoryDb(out SqliteConnection connection)
    {
        connection = new SqliteConnection("Filename=:memory:");
        connection.Open();
        _disposables.Add(connection);
        var db = new TravelCutDbContext(new DbContextOptionsBuilder<TravelCutDbContext>().UseSqlite(connection).Options);
        _disposables.Add(db);
        db.Database.EnsureCreated();
        return db;
    }

    private static Transcript BuildTranscript(Guid mediaId, int words, string text) => new()
    {
        TranscriptId = Guid.NewGuid(),
        MediaId = mediaId,
        ModelId = "large-v3",
        Language = "pt",
        AudioDurationSeconds = words,
        FullText = text,
        Words = Enumerable.Range(0, words).Select(i => new TranscriptWord
        {
            Seq = i,
            StartSeconds = i,
            EndSeconds = i + 0.5,
            Text = $"p{i}",
            Confidence = 0.9
        }).ToList()
    };

    [Fact]
    public async Task TranscriptsOfDifferentMedia_Coexist_AndReplacingOneKeepsTheOther()
    {
        var db = NewMemoryDb(out _);
        var repo = new SqliteTranscriptRepository(db);
        var mediaA = Guid.NewGuid();
        var mediaB = Guid.NewGuid();

        await repo.SaveAsync(BuildTranscript(mediaA, 2, "a"));
        await repo.SaveAsync(BuildTranscript(mediaB, 2, "b"));

        Assert.Equal(2, db.Transcripts.Count());
        Assert.Equal(4, db.TranscriptWords.Count());

        await repo.SaveAsync(BuildTranscript(mediaA, 3, "a2"));

        Assert.Equal(2, db.Transcripts.Count());
        Assert.Equal(5, db.TranscriptWords.Count());
        var reloaded = await repo.GetByMediaIdAsync(mediaA);
        Assert.NotNull(reloaded);
        Assert.Equal("a2", reloaded!.FullText);
        Assert.Equal(3, reloaded.Words.Count);
    }

    [Fact]
    public void LegacyDuplicateDecisionVersions_InFileDatabase_AreRenumbered_AfterBackup()
    {
        // Banco anterior à F06: EditDecisions já tinha Version (sempre 1) e não tinha índice único.
        string path = Path.Combine(_tempDir, "decisions.db");
        var suggestionId = Guid.NewGuid();
        using (var seed = new TravelCutDbContext(new DbContextOptionsBuilder<TravelCutDbContext>()
            .UseSqlite($"Data Source={path};Pooling=False").Options))
        {
            seed.EnsureSchemaUpdated();
            seed.Database.ExecuteSqlRaw($"DROP INDEX {TravelCutDbContext.EditDecisionVersionIndex};");
            seed.EditDecisions.AddRange(
                new EditDecision { SuggestionId = suggestionId, Action = "approve", Version = 1, TimestampUtc = new DateTime(2026, 10, 14, 9, 0, 0, DateTimeKind.Utc) },
                new EditDecision { SuggestionId = suggestionId, Action = "reject", Version = 1, TimestampUtc = new DateTime(2026, 10, 14, 11, 0, 0, DateTimeKind.Utc) });
            seed.SaveChanges();
        }
        Assert.Empty(Directory.GetFiles(_tempDir, "decisions.db.bak-*"));

        using (var db = new TravelCutDbContext(new DbContextOptionsBuilder<TravelCutDbContext>()
            .UseSqlite($"Data Source={path};Pooling=False").Options))
        {
            db.EnsureSchemaUpdated();

            var rows = db.EditDecisions.AsNoTracking().Where(d => d.SuggestionId == suggestionId)
                .OrderBy(d => d.Version).Select(d => new { d.Version, d.Action }).ToList();
            Assert.Equal(new[] { 1, 2 }, rows.Select(r => r.Version));
            Assert.Equal(new[] { "approve", "reject" }, rows.Select(r => r.Action));
        }

        // Backup feito antes de reescrever, com as duas decisões originais (ambas Version 1).
        string backup = Assert.Single(Directory.GetFiles(_tempDir, "decisions.db.bak-*"));
        using var check = new SqliteConnection($"Data Source={backup};Pooling=False");
        check.Open();
        using var cmd = check.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM EditDecisions WHERE Version = 1;";
        Assert.Equal(2L, Convert.ToInt64(cmd.ExecuteScalar()));
    }

    [Fact]
    public void LegacyTranscriptWordsPrimaryKeyOnSeq_IsMigrated_WithoutLosingRows()
    {
        string path = Path.Combine(_tempDir, "legacy.db");
        var transcriptId = Guid.NewGuid().ToString();
        using (var conn = new SqliteConnection($"Data Source={path}"))
        {
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText =
                "CREATE TABLE TranscriptWords (Seq INTEGER NOT NULL PRIMARY KEY, StartSeconds REAL NOT NULL, EndSeconds REAL NOT NULL, Text TEXT NULL, Confidence REAL NOT NULL, TranscriptId TEXT NOT NULL);" +
                $"INSERT INTO TranscriptWords VALUES (0, 0.0, 0.5, 'oi', 0.9, '{transcriptId}'), (1, 0.5, 1.0, 'tudo', 0.8, '{transcriptId}');";
            cmd.ExecuteNonQuery();
        }

        var db = new TravelCutDbContext(new DbContextOptionsBuilder<TravelCutDbContext>()
            .UseSqlite($"Data Source={path}").Options);
        _disposables.Add(db);

        db.EnsureSchemaUpdated();

        var connection = db.Database.GetDbConnection();
        var pk = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = "PRAGMA table_info(TranscriptWords);";
            using var reader = cmd.ExecuteReader();
            while (reader.Read()) pk[reader["name"].ToString()!] = Convert.ToInt32(reader["pk"]);
        }
        Assert.Equal(1, pk["TranscriptId"]);
        Assert.Equal(2, pk["Seq"]);

        long rows;
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = "SELECT COUNT(*) FROM TranscriptWords;";
            rows = Convert.ToInt64(cmd.ExecuteScalar());
        }
        Assert.Equal(2, rows);

        // Com a PK composta, outra transcrição pode reutilizar Seq 0.
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = $"INSERT INTO TranscriptWords VALUES (0, 0.0, 0.5, 'outra', 0.9, '{Guid.NewGuid()}');";
            cmd.ExecuteNonQuery();
        }
    }
}
