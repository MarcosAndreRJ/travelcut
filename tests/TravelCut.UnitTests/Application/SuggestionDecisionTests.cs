using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TravelCut.Application.Ipc;
using TravelCut.Application.Planning;
using TravelCut.Application.Services;
using TravelCut.Application.UseCases;
using TravelCut.Contracts.Dtos;
using TravelCut.Contracts.Ipc;
using TravelCut.Domain.Entities;
using TravelCut.Domain.Enums;
using TravelCut.Domain.ValueObjects;
using TravelCut.Infrastructure.Persistence;
using TravelCut.Infrastructure.Planning;
using TravelCut.Infrastructure.Repositories;
using TravelCut.UnitTests.Planning;
using Xunit;

namespace TravelCut.UnitTests.Application;

/// <summary>
/// Decisões contra SQLite real (EF + índice único): append-only (A2), Version única (A4), estado efetivo (A5),
/// validação de ação e lote (M10). O dispatcher é montado só com o repositório de decisões; as rotas testadas não usam os demais serviços.
/// </summary>
public class SuggestionDecisionTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly TravelCutDbContext _db;
    private readonly SqliteProjectRepository _projects;
    private readonly SqliteMediaRepository _media;
    private readonly SqliteEditPlanRepository _plan;
    private readonly IpcDispatcher _dispatcher;
    private readonly PlanningUseCases _planning;

    private static readonly Guid Project = TravelFixture.ProjectId;
    private static Guid S(int n) => TravelFixture.S(n);

    public SuggestionDecisionTests()
    {
        _connection = new SqliteConnection("Filename=:memory:");
        _connection.Open();
        _db = new TravelCutDbContext(new DbContextOptionsBuilder<TravelCutDbContext>().UseSqlite(_connection).Options);
        _db.Database.EnsureCreated();

        _projects = new SqliteProjectRepository(_db);
        _media = new SqliteMediaRepository(_db);
        _plan = new SqliteEditPlanRepository(_db);

        _projects.SaveAsync(new Project
        {
            ProjectId = Project,
            Name = "Viagem",
            RootPath = Path.Combine(Path.GetTempPath(), "tc_dec_" + Guid.NewGuid().ToString("N")),
        }).GetAwaiter().GetResult();
        _media.SaveBatchAsync(TravelFixture.Media()).GetAwaiter().GetResult();
        _plan.SaveSuggestionsBatchAsync([Sug(1, 1), Sug(2, 2)]).GetAwaiter().GetResult();

        // Decisões passam pelo use case de planejamento (mesmo repositório SQLite). Sem revisão de plano, nada é regenerado.
        _planning = new PlanningUseCases(
            _projects, _media, _plan, new FakePipelineAnalysis(), new ChronologyService(),
            new SqlitePlanRevisionRepository(_db), new InMemoryPlanArtifactStore(), new FileSystemFileExistence(),
            TimeProvider.System, NullLogger<PlanningUseCases>.Instance);
        _dispatcher = new IpcDispatcher(
            null!, null!, null!, null!, null!, null!, null!, null!, null!, _plan, null!, null!, null!, null!, null!,
            _planning, NullLogger<IpcDispatcher>.Instance);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    private static EditSuggestion Sug(int n, int mediaN) => new()
    {
        SuggestionId = S(n),
        MediaId = TravelFixture.M(mediaN),
        SourceInterval = new TimeRange(1000, 2000),
        EvidenceRefs = "{\"kind\":\"silence\"}",
        ConfidenceBand = "high",
        SuggestedAction = "cut",
        Status = SuggestionStatus.Proposed,
        Author = "test",
        CreatedAtUtc = new DateTime(2026, 10, 14, 9, 0, 0, DateTimeKind.Utc),
    };

    private Task<IpcResponse> Send(string type, object payload) => _dispatcher.DispatchAsync(new IpcRequest
    {
        RequestId = Guid.NewGuid().ToString("N"),
        Type = type,
        Payload = payload,
    });

    private static JsonElement Body(IpcResponse response) => JsonSerializer.SerializeToElement(response.Payload);

    private async Task<string> StatusOfAsync(Guid suggestionId)
    {
        var list = await Send("suggestions.list", new ProjectRequest(Project));
        Assert.True(list.Ok, list.ErrorCode);
        foreach (var s in Body(list).GetProperty("suggestions").EnumerateArray())
        {
            if (s.GetProperty("suggestionId").GetGuid() == suggestionId) return s.GetProperty("status").GetString()!;
        }
        throw new InvalidOperationException("sugestão ausente na listagem");
    }

    private async Task<int> ApprovedCountAsync()
    {
        var project = (await _projects.GetByIdAsync(Project))!;
        return (await ProjectSummaryBuilder.BuildAsync(project, _media, _plan)).ApprovedCutsCount;
    }

    [Fact]
    public async Task ApproveThenReject_ListShowsRejected_AndApprovedCountIsZero()
    {
        Assert.True((await Send("decision.submit", new SubmitDecisionRequest(S(1), "approve", "user", null))).Ok);
        Assert.Equal("Approved", await StatusOfAsync(S(1)));
        Assert.Equal(1, await ApprovedCountAsync());

        Assert.True((await Send("decision.submit", new SubmitDecisionRequest(S(1), "reject", "user", null))).Ok);

        Assert.Equal("Rejected", await StatusOfAsync(S(1)));
        Assert.Equal(0, await ApprovedCountAsync());
    }

    [Fact]
    public async Task ApproveTwice_CountsOneApprovedCut()
    {
        await Send("decision.submit", new SubmitDecisionRequest(S(1), "approve", "user", null));
        await Send("decision.submit", new SubmitDecisionRequest(S(1), "approve", "user", null));

        Assert.Equal("Approved", await StatusOfAsync(S(1)));
        Assert.Equal(1, await ApprovedCountAsync());
    }

    [Fact]
    public async Task Undo_IsAppendOnly_KeepsHistory_AndRestoresProposed()
    {
        await Send("decision.submit", new SubmitDecisionRequest(S(1), "approve", "user", null));

        var undo = await Send("decision.undo", new UndoDecisionRequest(S(1)));

        Assert.True(undo.Ok);
        // Resposta DecisionResultDto: uma decisão gravada e o estado efetivo da sugestão.
        Assert.Equal(1, Body(undo).GetProperty("recordedCount").GetInt32());
        Assert.Equal("Proposed", Body(undo).GetProperty("suggestions")[0].GetProperty("status").GetString());
        Assert.Equal("Proposed", await StatusOfAsync(S(1)));
        Assert.Equal(0, await ApprovedCountAsync());

        // Histórico preservado: nada foi apagado.
        var decisions = Body(await Send("decision.list", new ProjectRequest(Project))).GetProperty("decisions").EnumerateArray()
            .Where(d => d.GetProperty("suggestionId").GetGuid() == S(1))
            .Select(d => (d.GetProperty("version").GetInt32(), d.GetProperty("action").GetString()!))
            .ToList();
        Assert.Equal(new[] { (1, "approve"), (2, "undo") }, decisions);

        // Nova aprovação após o desfazer vira a versão 3 e volta a valer.
        var again = await Send("decision.submit", new SubmitDecisionRequest(S(1), "approve", "user", null));
        Assert.True(again.Ok, again.ErrorCode);
        Assert.Equal("Approved", await StatusOfAsync(S(1)));
    }

    [Fact]
    public async Task Undo_WithoutActiveDecision_IsRefused_AndDoesNotStack()
    {
        var none = await Send("decision.undo", new UndoDecisionRequest(S(2)));
        Assert.Equal("NOTHING_TO_UNDO", none.ErrorCode);

        await Send("decision.submit", new SubmitDecisionRequest(S(2), "defer", "user", null));
        Assert.True((await Send("decision.undo", new UndoDecisionRequest(S(2)))).Ok);
        Assert.Equal("NOTHING_TO_UNDO", (await Send("decision.undo", new UndoDecisionRequest(S(2)))).ErrorCode);
    }

    [Fact]
    public async Task SameTimestamp_EffectiveStateFollowsVersion_NotTimestampOrId()
    {
        // Duas decisões no mesmo instante. A segunda tem id que ordena ANTES da primeira:
        // ordenar por timestamp+GUID daria "approve"; a versão 2 é "reject" e deve prevalecer.
        var at = new DateTime(2026, 10, 14, 10, 0, 0, DateTimeKind.Utc);
        await _plan.SaveDecisionAsync(new EditDecision
        {
            DecisionId = Guid.Parse("ffffffff-ffff-4fff-8fff-ffffffffffff"),
            SuggestionId = S(1), Action = "approve", Reviewer = "user", TimestampUtc = at,
        });
        await _plan.SaveDecisionAsync(new EditDecision
        {
            DecisionId = Guid.Parse("00000000-0000-4000-8000-000000000000"),
            SuggestionId = S(1), Action = "reject", Reviewer = "user", TimestampUtc = at,
        });

        var versions = (await _plan.ListDecisionsByProjectAsync(Project))
            .Where(d => d.SuggestionId == S(1)).OrderBy(d => d.Version).Select(d => (d.Version, d.Action)).ToList();
        Assert.Equal(new[] { (1, "approve"), (2, "reject") }, versions);
        Assert.Equal("Rejected", await StatusOfAsync(S(1)));
        Assert.Equal("reject", (await _plan.GetLatestDecisionBySuggestionAsync(S(1)))!.Action);
    }

    [Fact]
    public async Task DuplicateVersionForSameSuggestion_IsRejectedByUniqueIndex()
    {
        await _plan.SaveDecisionAsync(new EditDecision { SuggestionId = S(1), Action = "approve", TimestampUtc = DateTime.UtcNow });

        _db.EditDecisions.Add(new EditDecision { SuggestionId = S(1), Action = "reject", Version = 1, TimestampUtc = DateTime.UtcNow });
        await Assert.ThrowsAsync<DbUpdateException>(() => _db.SaveChangesAsync());
        _db.ChangeTracker.Clear();
    }

    [Fact]
    public async Task SaveDecision_AssignsVersionInsideTransaction_SequentialPerSuggestion()
    {
        var first = new EditDecision { SuggestionId = S(1), Action = "defer", TimestampUtc = DateTime.UtcNow };
        var second = new EditDecision { SuggestionId = S(1), Action = "approve", TimestampUtc = DateTime.UtcNow, Version = 99 };
        var other = new EditDecision { SuggestionId = S(2), Action = "approve", TimestampUtc = DateTime.UtcNow };

        await _plan.SaveDecisionAsync(first);
        await _plan.SaveDecisionAsync(second);
        await _plan.SaveDecisionAsync(other);

        Assert.Equal(1, first.Version);
        Assert.Equal(2, second.Version); // o valor 99 informado pelo chamador é substituído
        Assert.Equal(1, other.Version);
    }

    [Fact]
    public async Task LegacyDuplicateVersions_AreRenumberedByTimestamp_BeforeUniqueIndexIsCreated()
    {
        // Banco legado: sem índice único e com Version repetida para a mesma sugestão.
        _db.Database.ExecuteSqlRaw($"DROP INDEX {TravelCutDbContext.EditDecisionVersionIndex};");
        var early = new EditDecision { DecisionId = Guid.Parse("ffffffff-0000-4000-8000-000000000001"), SuggestionId = S(1), Action = "approve", Version = 1, TimestampUtc = new DateTime(2026, 10, 14, 9, 0, 0, DateTimeKind.Utc) };
        var late = new EditDecision { DecisionId = Guid.Parse("00000000-0000-4000-8000-000000000002"), SuggestionId = S(1), Action = "reject", Version = 1, TimestampUtc = new DateTime(2026, 10, 14, 11, 0, 0, DateTimeKind.Utc) };
        _db.EditDecisions.AddRange(late, early);
        _db.SaveChanges();
        _db.ChangeTracker.Clear();

        _db.EnsureSchemaUpdated();
        _db.EnsureSchemaUpdated(); // idempotente

        var rows = _db.EditDecisions.AsNoTracking().Where(d => d.SuggestionId == S(1))
            .OrderBy(d => d.Version).Select(d => new { d.Version, d.Action }).ToList();
        Assert.Equal(new[] { 1, 2 }, rows.Select(r => r.Version));
        Assert.Equal(new[] { "approve", "reject" }, rows.Select(r => r.Action));

        _db.EditDecisions.Add(new EditDecision { SuggestionId = S(1), Action = "defer", Version = 2, TimestampUtc = DateTime.UtcNow });
        await Assert.ThrowsAsync<DbUpdateException>(() => _db.SaveChangesAsync());
        _db.ChangeTracker.Clear();
    }

    [Fact]
    public async Task Submit_InvalidAction_IsRejected_AndNothingIsRecorded()
    {
        foreach (var action in new[] { "approved", "undo", "delete", "" })
        {
            var r = await Send("decision.submit", new SubmitDecisionRequest(S(1), action, "user", null));
            Assert.Equal("INVALID_ACTION", r.ErrorCode);
        }
        Assert.Empty(await _plan.ListDecisionsByProjectAsync(Project));
    }

    [Fact]
    public async Task Submit_UnknownSuggestion_IsSuggestionNotFound()
    {
        var r = await Send("decision.submit", new SubmitDecisionRequest(Guid.NewGuid(), "approve", "user", null));

        Assert.Equal("SUGGESTION_NOT_FOUND", r.ErrorCode);
        Assert.Empty(await _plan.ListDecisionsByProjectAsync(Project));
    }

    [Fact]
    public async Task BatchDecide_IgnoresMissingIds_ReportsThem_AndCountsOnlyRecorded()
    {
        var randomId = Guid.NewGuid();
        var r = await Send("suggestions.batchDecide", new BatchDecideRequest(
            [S(1), randomId, S(2), Guid.Empty, S(1)], "reject", "user", null));

        Assert.True(r.Ok, r.ErrorCode);
        var body = Body(r);
        Assert.Equal(2, body.GetProperty("recordedCount").GetInt32());
        var missing = body.GetProperty("missingSuggestionIds").EnumerateArray().Select(e => e.GetGuid()).ToList();
        Assert.Equal(new[] { randomId, Guid.Empty }.OrderBy(g => g).ToList(), missing.OrderBy(g => g).ToList());
        Assert.Equal("Rejected", await StatusOfAsync(S(1)));
        Assert.Equal("Rejected", await StatusOfAsync(S(2)));
    }

    [Fact]
    public async Task BatchDecide_InvalidActionOrOver500Ids_AreRefusedBeforeWriting()
    {
        var bad = await Send("suggestions.batchDecide", new BatchDecideRequest([S(1)], "approved", "user", null));
        Assert.Equal("INVALID_ACTION", bad.ErrorCode);

        var tooMany = Enumerable.Range(0, 501).Select(_ => Guid.NewGuid()).ToList();
        var over = await Send("suggestions.batchDecide", new BatchDecideRequest(tooMany, "approve", "user", null));
        Assert.Equal("INVALID_ARGUMENT", over.ErrorCode);

        Assert.Empty(await _plan.ListDecisionsByProjectAsync(Project));
    }

    [Fact]
    public async Task BatchDecide_Exactly500Ids_IsAccepted()
    {
        var ids = Enumerable.Range(0, 500).Select(_ => Guid.NewGuid()).ToList();

        var r = await Send("suggestions.batchDecide", new BatchDecideRequest(ids, "defer", "user", null));

        Assert.True(r.Ok, r.ErrorCode);
        Assert.Equal(0, Body(r).GetProperty("recordedCount").GetInt32());
        Assert.Equal(500, Body(r).GetProperty("missingSuggestionIds").GetArrayLength());
    }
}
