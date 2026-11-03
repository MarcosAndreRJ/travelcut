using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TravelCut.Domain.Entities;
using TravelCut.Domain.Enums;
using TravelCut.Infrastructure.Persistence;
using Xunit;

namespace TravelCut.UnitTests.Infrastructure;

/// <summary>
/// Migração legada de TranscriptWords (PK só em Seq, TranscriptId anulável) com palavras órfãs:
/// TranscriptId NULL ou apontando para Transcript inexistente. A migração não pode abortar; as órfãs
/// vão para TranscriptWords_orphans (mesma transação), as válidas para a tabela nova, o backup é feito
/// e a contagem é registrada em Warning.
/// </summary>
public sealed class LegacyTranscriptWordOrphanMigrationTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "tc_orphan_" + Guid.NewGuid().ToString("N"));
    private string DbPath => Path.Combine(_dir, "travelcut.db");
    private string ConnectionString() => new SqliteConnectionStringBuilder { DataSource = DbPath }.ToString();

    public LegacyTranscriptWordOrphanMigrationTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try
        {
            if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
            // Melhor esforço: conexões de teste podem estar liberando o arquivo.
        }
    }

    [Fact]
    public void PalavraSemTranscriptId_NaoAbortaMigracao_PreservaEmOrphans()
    {
        var transcriptId = Guid.NewGuid();
        BuildLegacyDatabase(transcriptId, orphanTranscriptId: null);

        var logger = new CapturingLogger();
        MigrateWith(logger);

        AssertMigratedWithOneOrphan(transcriptId, orphanTranscriptIdIsNull: true, logger);
    }

    [Fact]
    public void PalavraComTranscriptInexistente_NaoAbortaMigracao_PreservaEmOrphans()
    {
        var transcriptId = Guid.NewGuid();
        BuildLegacyDatabase(transcriptId, orphanTranscriptId: Guid.NewGuid());

        var logger = new CapturingLogger();
        MigrateWith(logger);

        AssertMigratedWithOneOrphan(transcriptId, orphanTranscriptIdIsNull: false, logger);
    }

    [Fact]
    public void PalavraComConfiancaNula_NaoAbortaMigracao_PreservaEmOrphans()
    {
        var transcriptId = Guid.NewGuid();
        BuildLegacyDatabase(transcriptId, orphanTranscriptId: transcriptId, orphanHasNullConfidence: true);

        var logger = new CapturingLogger();
        MigrateWith(logger);

        AssertMigratedWithOneOrphan(transcriptId, orphanTranscriptIdIsNull: false, logger);
        Assert.Equal(1L, CountScalar(DbPath, "SELECT COUNT(*) FROM TranscriptWords_orphans WHERE Confidence IS NULL"));
    }

    private void AssertMigratedWithOneOrphan(Guid transcriptId, bool orphanTranscriptIdIsNull, CapturingLogger logger)
    {
        // Backup feito antes da migração (contém o legado com as 3 palavras).
        var backup = Assert.Single(Directory.GetFiles(_dir, "travelcut.db.bak-*"));
        Assert.Equal(3L, CountScalar(backup, "SELECT COUNT(*) FROM TranscriptWords"));

        // Migração concluiu: marcador e inspeção prontos.
        Assert.True(TravelCutSchemaInspector.Inspect(DbPath).IsReady);
        Assert.Equal(1L, CountScalar(DbPath, "SELECT COUNT(*) FROM SchemaMarkers WHERE Name = 'schema/1'"));

        // Tabela nova: PK composta, só as 2 palavras válidas, ligadas ao Transcript.
        Assert.Equal(2L, CountScalar(DbPath, "SELECT COUNT(*) FROM PRAGMA_TABLE_INFO('TranscriptWords') WHERE pk > 0"));
        Assert.Equal(2L, CountScalar(DbPath,
            "SELECT COUNT(*) FROM TranscriptWords w JOIN Transcripts t ON w.TranscriptId = t.TranscriptId"));
        Assert.Equal(2L, CountScalar(DbPath, "SELECT COUNT(*) FROM TranscriptWords"));
        Assert.Equal("Olá|pessoal", ReadText(DbPath, "SELECT group_concat(Text, '|') FROM (SELECT Text FROM TranscriptWords ORDER BY Seq)"));

        // Órfã preservada em TranscriptWords_orphans, com os dados originais.
        Assert.Equal(1L, CountScalar(DbPath, "SELECT COUNT(*) FROM TranscriptWords_orphans"));
        Assert.Equal("órfã", ReadText(DbPath, "SELECT Text FROM TranscriptWords_orphans"));
        Assert.Equal(3L, CountScalar(DbPath, "SELECT Seq FROM TranscriptWords_orphans"));
        if (orphanTranscriptIdIsNull)
        {
            Assert.Equal(1L, CountScalar(DbPath, "SELECT COUNT(*) FROM TranscriptWords_orphans WHERE TranscriptId IS NULL"));
        }
        else
        {
            Assert.Equal(1L, CountScalar(DbPath, "SELECT COUNT(*) FROM TranscriptWords_orphans WHERE TranscriptId IS NOT NULL"));
        }

        // Aviso com a contagem de órfãs.
        Assert.Contains(logger.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains("1 palavra(s)", StringComparison.Ordinal));
        Assert.Equal(transcriptId, ReadGuid(DbPath, "SELECT TranscriptId FROM TranscriptWords ORDER BY Seq LIMIT 1"));
    }

    private void MigrateWith(ILogger logger)
    {
        using var db = new TravelCutDbContext(new DbContextOptionsBuilder<TravelCutDbContext>()
            .UseSqlite(ConnectionString()).Options);
        db.EnsureSchemaUpdated(logger);
    }

    /// <summary>
    /// Monta o legado: tabelas pelo modelo atual, depois rebaixa TranscriptWords para PK só em Seq com TranscriptId anulável
    /// e acrescenta uma palavra órfã (Seq 3).
    /// </summary>
    private void BuildLegacyDatabase(Guid transcriptId, Guid? orphanTranscriptId, bool orphanHasNullConfidence = false)
    {
        var options = new DbContextOptionsBuilder<TravelCutDbContext>().UseSqlite(ConnectionString()).Options;
        var projectId = Guid.NewGuid();
        var mediaId = Guid.NewGuid();

        using (var db = new TravelCutDbContext(options))
        {
            db.Database.EnsureCreated();
            db.Projects.Add(new Project { ProjectId = projectId, Name = "Órfãs", RootPath = Path.Combine(_dir, "root") });
            db.MediaAssets.Add(new MediaAsset
            {
                MediaId = mediaId,
                ProjectId = projectId,
                RelativePath = "1-Bruto/clip.mp4",
                ByteLength = 4096,
                LastWriteUtc = new DateTime(2026, 10, 1, 10, 0, 0, DateTimeKind.Utc),
                Fingerprint = "fp-orfa",
                Kind = MediaKind.Video,
            });
            db.Transcripts.Add(new Transcript
            {
                TranscriptId = transcriptId,
                MediaId = mediaId,
                ModelId = "large-v3",
                Language = "pt",
                AudioDurationSeconds = 3,
                FullText = "Olá pessoal",
                Words =
                [
                    new TranscriptWord { Seq = 1, StartSeconds = 0.0, EndSeconds = 0.5, Text = "Olá", Confidence = 0.9 },
                    new TranscriptWord { Seq = 2, StartSeconds = 0.5, EndSeconds = 1.0, Text = "pessoal", Confidence = 0.9 },
                ],
            });
            db.SaveChanges();
        }

        using var connection = new SqliteConnection(ConnectionString());
        connection.Open();
        Exec(connection, "DROP TABLE IF EXISTS PlanRevisions;");
        Exec(connection, "DROP TABLE IF EXISTS SchemaMarkers;");
        Exec(connection, "ALTER TABLE TranscriptWords RENAME TO TranscriptWords_atual;");
        Exec(connection,
            "CREATE TABLE TranscriptWords (Seq INTEGER NOT NULL PRIMARY KEY, StartSeconds REAL NULL, " +
            "EndSeconds REAL NULL, Text TEXT NULL, Confidence REAL NULL, TranscriptId TEXT NULL);");
        Exec(connection,
            "INSERT INTO TranscriptWords (Seq, StartSeconds, EndSeconds, Text, Confidence, TranscriptId) " +
            "SELECT Seq, StartSeconds, EndSeconds, Text, Confidence, TranscriptId FROM TranscriptWords_atual;");
        Exec(connection, "DROP TABLE TranscriptWords_atual;");

        using (var insert = connection.CreateCommand())
        {
            insert.CommandText =
                "INSERT INTO TranscriptWords (Seq, StartSeconds, EndSeconds, Text, Confidence, TranscriptId) " +
                (orphanHasNullConfidence ? "VALUES (3, 1.0, 1.5, 'órfã', NULL, $tid);" : "VALUES (3, 1.0, 1.5, 'órfã', 0.4, $tid);");
            var tid = insert.CreateParameter();
            tid.ParameterName = "$tid";
            tid.Value = orphanTranscriptId is Guid g ? g.ToString() : DBNull.Value;
            insert.Parameters.Add(tid);
            insert.ExecuteNonQuery();
        }
    }

    private static void Exec(SqliteConnection connection, string sql)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private static long CountScalar(string dbPath, string sql)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = dbPath }.ToString());
        connection.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        return Convert.ToInt64(cmd.ExecuteScalar());
    }

    private static string ReadText(string dbPath, string sql)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = dbPath }.ToString());
        connection.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        return cmd.ExecuteScalar()?.ToString() ?? string.Empty;
    }

    private static Guid ReadGuid(string dbPath, string sql)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = dbPath }.ToString());
        connection.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        return Guid.Parse(cmd.ExecuteScalar()!.ToString()!);
    }

    private sealed class CapturingLogger : ILogger
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
            => Entries.Add((logLevel, formatter(state, exception)));
    }
}
