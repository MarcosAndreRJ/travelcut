using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using TravelCut.Infrastructure.Persistence;
using Xunit;

namespace TravelCut.UnitTests.Planning;

/// <summary>
/// Migração aditiva (ADD COLUMN) de bancos antigos: EditDecisions sem BaseRevision/SourceStartMs/SourceEndMs e
/// PlanRevisions sem InputsFingerprint. Linhas existentes são preservadas; a execução é idempotente.
/// </summary>
public sealed class DecisionSchemaMigrationTests : IDisposable
{
    private readonly SqliteConnection _connection = new("Filename=:memory:");

    public DecisionSchemaMigrationTests() => _connection.Open();

    public void Dispose() => _connection.Dispose();

    private TravelCutDbContext Create() =>
        new(new DbContextOptionsBuilder<TravelCutDbContext>().UseSqlite(_connection).Options);

    private void Exec(string sql)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private List<string> Columns(string table)
    {
        var names = new List<string>();
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info({table});";
        using var reader = cmd.ExecuteReader();
        while (reader.Read()) names.Add(reader["name"].ToString()!);
        return names;
    }

    [Fact]
    public void EnsureSchemaUpdated_AddsDecisionAndPlanColumns_ToLegacyTables_KeepingRows_Idempotently()
    {
        using (var db = Create()) db.Database.EnsureCreated();

        // Esquema anterior à F07.2: sem as colunas novas.
        Exec("DROP TABLE EditDecisions;");
        Exec("DROP TABLE PlanRevisions;");
        Exec("CREATE TABLE EditDecisions (DecisionId TEXT NOT NULL PRIMARY KEY, SuggestionId TEXT NOT NULL, " +
             "Action TEXT NOT NULL, Reviewer TEXT NOT NULL, Notes TEXT NOT NULL, TimestampUtc TEXT NOT NULL, " +
             "Version INTEGER NOT NULL DEFAULT 1);");
        Exec("INSERT INTO EditDecisions (DecisionId, SuggestionId, Action, Reviewer, Notes, TimestampUtc, Version) " +
             "VALUES ('11111111-1111-4111-8111-111111111111', '22222222-2222-4222-8222-222222222222', " +
             "'approve', 'user', '', '2026-10-14 10:00:00', 1);");
        Exec("CREATE TABLE PlanRevisions (PlanId TEXT NOT NULL, Revision INTEGER NOT NULL, ProjectId TEXT NOT NULL, " +
             "BaseRevision INTEGER NULL, CreatedAtUtc TEXT NOT NULL, Author TEXT NOT NULL, PlanFingerprint TEXT NOT NULL, " +
             "IsValid INTEGER NOT NULL, ClipCount INTEGER NOT NULL, TimelineDurationFrames INTEGER NOT NULL, " +
             "RelativeJsonPath TEXT NOT NULL, PRIMARY KEY (PlanId, Revision));");

        using (var db = Create())
        {
            db.EnsureSchemaUpdated();
            db.EnsureSchemaUpdated(); // segunda execução: nada muda
        }

        var decisionColumns = Columns("EditDecisions");
        Assert.Contains("BaseRevision", decisionColumns);
        Assert.Contains("SourceStartMs", decisionColumns);
        Assert.Contains("SourceEndMs", decisionColumns);
        Assert.Contains("InputsFingerprint", Columns("PlanRevisions"));

        using var check = Create();
        var legacy = Assert.Single(check.EditDecisions.AsNoTracking().ToList());
        Assert.Equal("approve", legacy.Action);
        Assert.Equal(1, legacy.Version);
        Assert.Null(legacy.BaseRevision);
        Assert.Null(legacy.SourceStartMs);
        Assert.Null(legacy.SourceEndMs);
    }
}
