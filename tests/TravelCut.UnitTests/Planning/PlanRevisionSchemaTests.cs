using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using TravelCut.Application.Ports;
using TravelCut.Infrastructure.Persistence;
using TravelCut.Infrastructure.Repositories;
using Xunit;

namespace TravelCut.UnitTests.Planning;

/// <summary>Tabela PlanRevisions: criação em banco novo (EnsureCreated) e em banco já existente (EnsureSchemaUpdated).</summary>
public class PlanRevisionSchemaTests : IDisposable
{
    private readonly SqliteConnection _connection;

    public PlanRevisionSchemaTests()
    {
        _connection = new SqliteConnection("Filename=:memory:");
        _connection.Open();
    }

    public void Dispose() => _connection.Dispose();

    private TravelCutDbContext Create() =>
        new(new DbContextOptionsBuilder<TravelCutDbContext>().UseSqlite(_connection).Options);

    private static PlanRevisionIndexEntry Entry(Guid planId, int revision) => new(
        ProjectId: TravelFixture.ProjectId,
        PlanId: planId,
        Revision: revision,
        BaseRevision: revision > 1 ? revision - 1 : null,
        CreatedAtUtc: new DateTime(2026, 11, 2, 12, 0, 0, DateTimeKind.Utc),
        Author: "planner-rules/1",
        PlanFingerprint: new string('a', 64),
        IsValid: true,
        ClipCount: 18,
        TimelineDurationFrames: 6057,
        RelativeJsonPath: $"3-Trabalho/plans/{planId:D}/r{revision:D4}.json");

    [Fact]
    public async Task EnsureSchemaUpdated_CreatesMissingPlanRevisionsTable_OnExistingDatabase_Idempotently()
    {
        using (var db = Create())
        {
            db.Database.EnsureCreated();
        }
        // Banco de versão anterior: já tem tabelas, mas não a do plano.
        using (var drop = _connection.CreateCommand())
        {
            drop.CommandText = "DROP TABLE PlanRevisions;";
            drop.ExecuteNonQuery();
        }

        using (var db = Create())
        {
            db.EnsureSchemaUpdated();
            db.EnsureSchemaUpdated(); // segunda execução: nada muda
        }

        var planId = Guid.NewGuid();
        using var check = Create();
        var repo = new SqlitePlanRevisionRepository(check);
        await repo.AddAsync(Entry(planId, 1));
        await repo.AddAsync(Entry(planId, 2));

        var latest = await repo.GetLatestAsync(TravelFixture.ProjectId);
        Assert.Equal(2, latest!.Revision);
        Assert.Equal(new DateTime(2026, 11, 2, 12, 0, 0, DateTimeKind.Utc), latest.CreatedAtUtc);
        Assert.Equal(2, (await repo.ListAsync(TravelFixture.ProjectId)).Count);
    }

    [Fact]
    public async Task Index_RejectsDuplicateRevisionForSameProject()
    {
        using var db = Create();
        db.Database.EnsureCreated();
        var repo = new SqlitePlanRevisionRepository(db);
        var planId = Guid.NewGuid();
        await repo.AddAsync(Entry(planId, 1));

        await Assert.ThrowsAnyAsync<Exception>(() => repo.AddAsync(Entry(planId, 1)));
        Assert.Single(await repo.ListAsync(TravelFixture.ProjectId));
    }
}
