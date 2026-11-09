using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TravelCut.Application.Ipc;
using TravelCut.Application.Planning;
using TravelCut.Application.Services;
using TravelCut.Contracts.Dtos;
using TravelCut.Contracts.Ipc;
using TravelCut.Domain.Entities;
using TravelCut.Domain.Enums;
using TravelCut.Domain.ValueObjects;
using TravelCut.Infrastructure.Persistence;
using TravelCut.Infrastructure.Planning;
using TravelCut.Infrastructure.Repositories;
using Xunit;

namespace TravelCut.UnitTests.Planning;

/// <summary>
/// F14.8: parâmetros opcionais da decisão (zoom ajustado na revisão) persistidos e usados pelo planejador,
/// e origem (auto|human) + política/razão expostas na sugestão efetiva. Tudo aditivo e compatível com dados antigos.
/// </summary>
public sealed class DecisionParametersTests : IDisposable
{
    private static Guid M(int n) => TravelFixture.M(n);
    private static Guid S(int n) => TravelFixture.S(n);

    private readonly SqliteConnection _connection = new("Filename=:memory:");
    private readonly TravelCutDbContext _db;
    private readonly SqliteProjectRepository _projects;
    private readonly SqliteEditPlanRepository _plan;
    private readonly SqlitePlanRevisionRepository _revisions;
    private readonly InMemoryPlanArtifactStore _artifacts = new();
    private readonly PlanningUseCases _planning;
    private readonly IpcDispatcher _dispatcher;
    private readonly string _root = Path.Combine(Path.GetTempPath(), "tc_f148_" + Guid.NewGuid().ToString("N"));

    public DecisionParametersTests()
    {
        _connection.Open();
        _db = new TravelCutDbContext(new DbContextOptionsBuilder<TravelCutDbContext>().UseSqlite(_connection).Options);
        _db.Database.EnsureCreated();
        _projects = new SqliteProjectRepository(_db);
        var media = new SqliteMediaRepository(_db);
        _plan = new SqliteEditPlanRepository(_db);
        _revisions = new SqlitePlanRevisionRepository(_db);
        _projects.SaveAsync(new Project { ProjectId = TravelFixture.ProjectId, Name = "Viagem", RootPath = _root }).GetAwaiter().GetResult();
        media.SaveBatchAsync(TravelFixture.Media()).GetAwaiter().GetResult();
        _plan.SaveSuggestionsBatchAsync([ZoomSug(1, 12_400, 40_000), CutSug(2)]).GetAwaiter().GetResult();
        _planning = new PlanningUseCases(
            _projects, media, _plan, new FakePipelineAnalysis(), new ChronologyService(),
            _revisions, _artifacts, new FileSystemFileExistence(),
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

    private static EditSuggestion ZoomSug(int n, long startMs, long endMs, double scale = 1.05, bool focalKnown = true)
    {
        var p = new SmartZoomParametersDto(SmartZoomConstants.TypePunchIn, scale, 0.5, 0.5, (endMs - startMs) / 1000.0, 1.0, scale,
            "medium", focalKnown);
        return new EditSuggestion
        {
            SuggestionId = S(100 + n), MediaId = M(1), SourceInterval = new TimeRange(startMs, endMs),
            Kind = SuggestionKind.SubtleZoom, Rationale = "zoom", EvidenceRefs = JsonSerializer.Serialize(p),
            ConfidenceBand = "high", NeedsHumanReview = true, SuggestedAction = "zoom",
            Status = SuggestionStatus.Proposed, Author = "smart-zoom-engine",
            CreatedAtUtc = new DateTime(2026, 10, 14, 9, 30, n, DateTimeKind.Utc),
        };
    }

    private static EditSuggestion CutSug(int n) => new()
    {
        SuggestionId = S(n), MediaId = M(1), SourceInterval = new TimeRange(1000, 2000), EvidenceRefs = "{\"kind\":\"silence\"}",
        ConfidenceBand = "high", SuggestedAction = "cut", Status = SuggestionStatus.Proposed, Author = "test",
        CreatedAtUtc = new DateTime(2026, 10, 14, 9, 0, 0, DateTimeKind.Utc),
    };

    private static DecisionParametersDto Zoom(double scale, double fx = 0.3, double fy = 0.6, double dur = 8) =>
        new(new SmartZoomParametersDto(SmartZoomConstants.TypePunchIn, scale, fx, fy, dur, 1.0, scale, "medium", true));

    private static EditDecision PlannerDecision(string action, int seq, string? parametersJson = null) => new()
    {
        DecisionId = TravelFixture.D(100 + seq), SuggestionId = S(101), Action = action, Reviewer = "user",
        TimestampUtc = new DateTime(2026, 10, 14, 10, 30, seq, DateTimeKind.Utc), Version = seq, ParametersJson = parametersJson,
    };

    private static EditPlanDto ZoomPlan(IEnumerable<EditDecision>? decisions = null, double recommenderScale = 1.05) =>
        TravelFixture.BuildPlan(TravelFixture.Input(
            suggestions: TravelFixture.Suggestions().Concat([ZoomSug(1, 12_400, 40_000, recommenderScale)]),
            decisions: TravelFixture.Decisions().Concat(decisions ?? [])));

    private static PlanClipDto V1Clip(EditPlanDto plan) =>
        plan.Clips.Where(c => c.TrackId == "V1" && c.MediaId == M(1)).OrderBy(c => c.TimelineStartFrame).ElementAt(1);

    private Task<IpcResponse> Send(string type, object payload) => _dispatcher.DispatchAsync(new IpcRequest
    {
        RequestId = Guid.NewGuid().ToString("N"), Type = type, Payload = payload,
    });

    private static JsonElement Body(IpcResponse r) => JsonSerializer.SerializeToElement(r.Payload);

    // ------------------------------------------------------------------ Normalização

    [Fact]
    public void Normalize_ClampsScaleToMax_AndRejectsInvalidValues()
    {
        var (json, error) = DecisionParameters.Normalize(Zoom(1.5), "zoom");
        Assert.Null(error);
        Assert.Equal(1.12, DecisionParameters.Parse(json)!.Zoom!.Scale);

        Assert.NotNull(DecisionParameters.Normalize(Zoom(1.05, fx: 1.5), "zoom").Error);
        Assert.NotNull(DecisionParameters.Normalize(Zoom(double.NaN), "zoom").Error);
        Assert.NotNull(DecisionParameters.Normalize(Zoom(1.05, dur: -1), "zoom").Error);
        Assert.NotNull(DecisionParameters.Normalize(Zoom(1.05), "cut").Error); // só zoom
        Assert.Equal((null, null), DecisionParameters.Normalize(null, "cut"));
        Assert.Null(DecisionParameters.Parse("lixo{"));
    }

    // ------------------------------------------------------------------ Planejador

    [Fact]
    public void Planner_ApprovedZoom_UsesUserParameters_OverRecommender()
    {
        var json = DecisionParameters.Normalize(Zoom(1.10), "zoom").Json;
        var plan = ZoomPlan([PlannerDecision("approve", 1, json)]);

        Assert.Equal(1.10, V1Clip(plan).Zoom);
        var fx = Assert.Single(plan.AppliedEffects!, e => e.Kind == "zoom");
        Assert.Equal("human", fx.Origin);
        Assert.Equal(1.10, fx.Scale);
        Assert.Equal(0.3, fx.FocalPointX);
        Assert.Contains(V1Clip(plan).Evidence, e => e.Contains("parâmetros definidos pelo usuário", StringComparison.Ordinal));
    }

    [Fact]
    public void Planner_UserScaleAboveLimit_IsCappedAt112()
    {
        // Mesmo um JSON gravado fora do limite (ex.: edição manual do banco) nunca passa de 1,12x.
        var raw = JsonSerializer.Serialize(Zoom(1.40));
        var plan = ZoomPlan([PlannerDecision("approve", 1, raw)]);

        Assert.Equal(1.12, V1Clip(plan).Zoom);
    }

    [Fact]
    public void Planner_LegacyDecisionWithoutParameters_KeepsRecommenderScale()
    {
        var plan = ZoomPlan([PlannerDecision("approve", 1)], recommenderScale: 1.07);

        Assert.Equal(1.07, V1Clip(plan).Zoom);
        Assert.Equal("human", Assert.Single(plan.AppliedEffects!, e => e.Kind == "zoom").Origin);
        Assert.DoesNotContain(V1Clip(plan).Evidence, e => e.Contains("parâmetros definidos pelo usuário", StringComparison.Ordinal));
    }

    [Fact]
    public void Planner_AutoAppliedZoom_ExposesPolicyVersionAndReason()
    {
        var plan = ZoomPlan();

        Assert.Equal(1.05, V1Clip(plan).Zoom);
        var fx = Assert.Single(plan.AppliedEffects!, e => e.Kind == "zoom");
        Assert.Equal("auto", fx.Origin);
        Assert.Equal(AutoEditorialPolicy.Version, fx.PolicyVersion);
        Assert.False(string.IsNullOrWhiteSpace(fx.Reason));
        // Corte automático (silêncio trivial da fixture) também registra a origem.
        Assert.Contains(plan.AppliedEffects!, e => e.Kind == "cut" && e.Origin == "auto" && e.PolicyVersion == AutoEditorialPolicy.Version);
        Assert.Contains(plan.AppliedEffects!, e => e.Kind == "cut" && e.Origin == "human");
    }

    [Fact]
    public void PlanFingerprint_IsNotAffectedByAppliedEffects_AndInputsFingerprintChangesWithParameters()
    {
        var plan = ZoomPlan();
        Assert.Equal(plan.PlanFingerprint, ZoomPlan().PlanFingerprint);

        var a = new EffectiveSuggestion(S(101), SuggestionStatus.Approved, 0, 1, false, true, false, null);
        var b = a with { ParametersJson = "{\"zoom\":{}}" };
        string Fp(EffectiveSuggestion e) => PlanInputsFingerprint.Compute("m", plan.Profile, plan.TimeBase, plan.TimeZoneId, [e]);
        Assert.NotEqual(Fp(a), Fp(b));
    }

    // ------------------------------------------------------------------ Round-trip (SQLite real)

    [Fact]
    public async Task Submit_PersistsParameters_AndListsExposeThem_WithHumanOrigin()
    {
        var result = await _planning.SubmitDecisionAsync(
            new SubmitDecisionRequest(S(101), "approve", "user", null, Parameters: Zoom(1.09, 0.25, 0.75, 6)));
        Assert.True(result.Success, result.ErrorMessage);
        var dto = Assert.Single(result.Value!.Suggestions);
        Assert.Equal("human", dto.Origin);
        Assert.Null(dto.PolicyVersion);
        Assert.Equal(1.09, dto.Parameters!.Zoom!.Scale);
        Assert.Equal(0.25, dto.Parameters.Zoom.FocalPointX);

        // Persistido de fato (novo contexto sobre o mesmo banco).
        using (var fresh = new TravelCutDbContext(new DbContextOptionsBuilder<TravelCutDbContext>().UseSqlite(_connection).Options))
        {
            var row = Assert.Single(fresh.EditDecisions.AsNoTracking().ToList());
            Assert.NotNull(row.ParametersJson);
        }

        var decisions = Body(await Send("decision.list", new ProjectRequest(TravelFixture.ProjectId))).GetProperty("decisions");
        Assert.Equal(1.09, decisions[0].GetProperty("parameters").GetProperty("zoom").GetProperty("scale").GetDouble());

        var list = Body(await Send("suggestions.list", new ProjectRequest(TravelFixture.ProjectId))).GetProperty("suggestions");
        var zoom = list.EnumerateArray().Single(s => s.GetProperty("suggestionId").GetGuid() == S(101));
        Assert.Equal("human", zoom.GetProperty("origin").GetString());
        Assert.Equal(0.75, zoom.GetProperty("parameters").GetProperty("zoom").GetProperty("focalPointY").GetDouble());
    }

    [Fact]
    public async Task LaterDecisionsWithoutParameters_InheritThem_AndUndoRedoKeepThem()
    {
        await _planning.SubmitDecisionAsync(new SubmitDecisionRequest(S(101), "approve", "user", null, Parameters: Zoom(1.08)));
        await _planning.SubmitDecisionAsync(new SubmitDecisionRequest(S(101), "defer", "user", null));
        var deferred = (await _planning.BatchDecideAsync(new BatchDecideRequest([S(101)], "approve", "user", null))).Value!.Suggestions[0];
        Assert.Equal(1.08, deferred.Parameters!.Zoom!.Scale);

        await _planning.UndoDecisionAsync(new UndoDecisionRequest(S(101)));
        var redone = (await _planning.RedoDecisionAsync(new RedoDecisionRequest(S(101)))).Value!.Suggestions[0];
        Assert.Equal(1.08, redone.Parameters!.Zoom!.Scale);
    }

    [Fact]
    public async Task Submit_InvalidParameters_AreRejected_WithoutRecording()
    {
        var tooFar = await _planning.SubmitDecisionAsync(new SubmitDecisionRequest(S(101), "approve", "user", null, Parameters: Zoom(1.05, fx: 2)));
        Assert.False(tooFar.Success);
        Assert.Equal("INVALID_ARGUMENT", tooFar.ErrorCode);

        var onCut = await _planning.SubmitDecisionAsync(new SubmitDecisionRequest(S(2), "approve", "user", null, Parameters: Zoom(1.05)));
        Assert.Equal("INVALID_ARGUMENT", onCut.ErrorCode);

        var onReject = await _planning.SubmitDecisionAsync(new SubmitDecisionRequest(S(101), "reject", "user", null, Parameters: Zoom(1.05)));
        Assert.Equal("INVALID_ARGUMENT", onReject.ErrorCode);

        var batchOnCut = await _planning.BatchDecideAsync(new BatchDecideRequest([S(2)], "approve", "user", null, Zoom(1.05)));
        Assert.Equal("INVALID_ARGUMENT", batchOnCut.ErrorCode);

        using var fresh = new TravelCutDbContext(new DbContextOptionsBuilder<TravelCutDbContext>().UseSqlite(_connection).Options);
        Assert.Empty(fresh.EditDecisions.AsNoTracking().ToList());
    }

    [Fact]
    public async Task Batch_WithZoomParameters_AppliesToZoomSuggestions()
    {
        var result = await _planning.BatchDecideAsync(new BatchDecideRequest([S(101)], "approve", "user", null, Zoom(1.11)));
        Assert.True(result.Success, result.ErrorMessage);
        Assert.Equal(1.11, result.Value!.Suggestions[0].Parameters!.Zoom!.Scale);
    }

    [Fact]
    public async Task ProjectWithoutParameters_ShowsRecommenderParametersAndNullOrigin()
    {
        var list = Body(await Send("suggestions.list", new ProjectRequest(TravelFixture.ProjectId))).GetProperty("suggestions");
        var zoom = list.EnumerateArray().Single(s => s.GetProperty("suggestionId").GetGuid() == S(101));
        Assert.Equal(JsonValueKind.Null, zoom.GetProperty("origin").ValueKind);
        Assert.Equal(1.05, zoom.GetProperty("parameters").GetProperty("zoom").GetProperty("scale").GetDouble());

        var cut = list.EnumerateArray().Single(s => s.GetProperty("suggestionId").GetGuid() == S(2));
        Assert.Equal(JsonValueKind.Null, cut.GetProperty("parameters").ValueKind);
    }

    [Fact]
    public async Task Suggestions_ExposeAutoOrigin_PolicyVersionAndReason_FromCurrentPlan()
    {
        var plan = ZoomPlan(); // zoom autoaplicado pela política
        await _artifacts.WriteRevisionAsync(_root, plan.PlanId, plan.Revision, PlanJson.Serialize(plan));
        await _revisions.AddAsync(PlanMapping.ToIndexEntry(plan));

        var list = Body(await Send("suggestions.list", new ProjectRequest(TravelFixture.ProjectId))).GetProperty("suggestions");
        var zoom = list.EnumerateArray().Single(s => s.GetProperty("suggestionId").GetGuid() == S(101));
        Assert.Equal("auto", zoom.GetProperty("origin").GetString());
        Assert.Equal(AutoEditorialPolicy.Version, zoom.GetProperty("policyVersion").GetString());
        Assert.False(string.IsNullOrWhiteSpace(zoom.GetProperty("policyReason").GetString()));
    }

    [Fact]
    public void LegacyDatabase_WithoutParametersColumn_IsMigratedAndRowsKeepNullParameters()
    {
        using var legacy = new SqliteConnection("Filename=:memory:");
        legacy.Open();
        var options = new DbContextOptionsBuilder<TravelCutDbContext>().UseSqlite(legacy).Options;
        using (var db = new TravelCutDbContext(options)) db.Database.EnsureCreated();
        void Exec(string sql) { using var c = legacy.CreateCommand(); c.CommandText = sql; c.ExecuteNonQuery(); }
        Exec("DROP TABLE EditDecisions;");
        Exec("CREATE TABLE EditDecisions (DecisionId TEXT NOT NULL PRIMARY KEY, SuggestionId TEXT NOT NULL, " +
             "Action TEXT NOT NULL, Reviewer TEXT NOT NULL, Notes TEXT NOT NULL, TimestampUtc TEXT NOT NULL, " +
             "Version INTEGER NOT NULL DEFAULT 1);");
        Exec("INSERT INTO EditDecisions (DecisionId, SuggestionId, Action, Reviewer, Notes, TimestampUtc, Version) " +
             "VALUES ('11111111-1111-4111-8111-111111111111', '22222222-2222-4222-8222-222222222222', 'approve', 'user', '', '2026-10-14 10:00:00', 1);");

        using (var db = new TravelCutDbContext(options)) { db.EnsureSchemaUpdated(); db.EnsureSchemaUpdated(); }

        using var check = new TravelCutDbContext(options);
        var row = Assert.Single(check.EditDecisions.AsNoTracking().ToList());
        Assert.Null(row.ParametersJson);
        Assert.Null(DecisionParameters.Parse(row.ParametersJson));
    }
}
