using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TravelCut.Application.Planning;
using TravelCut.Application.Ports;
using TravelCut.Application.Services;
using TravelCut.Contracts.Dtos;
using TravelCut.Domain.Entities;
using TravelCut.Domain.Enums;
using TravelCut.Domain.ValueObjects;
using TravelCut.Infrastructure.Persistence;
using Xunit;

namespace TravelCut.UnitTests.Planning;

/// <summary>
/// Regressões da auditoria QA da F07 (B1–B3, A1, A3, A4, limites de texto e migração do IsRedo).
/// Cada teste cobre um achado; os cenários usam o mesmo ambiente de <see cref="DecisionRig"/> (SQLite em memória).
/// </summary>
public sealed class F07AuditRegressionTests : IDisposable
{
    private readonly DecisionRig _rig = new();

    public void Dispose() => _rig.Dispose();

    private static Guid S(int n) => TravelFixture.S(n);
    private static Guid ProjectId => TravelFixture.ProjectId;

    private string M1Path => _rig.FullPath(TravelFixture.Media().Single(m => m.MediaId == TravelFixture.M(1)).RelativePath);

    private async Task<EditPlanDto> GenerateAsync()
    {
        var result = await _rig.UseCases.GenerateAsync(new GeneratePlanRequest(ProjectId, null, null));
        Assert.True(result.Success, result.ErrorMessage);
        return result.Value!;
    }

    private async Task<EditPlanDto> GetAsync(int revision)
    {
        var result = await _rig.UseCases.GetAsync(new GetPlanRequest(ProjectId, revision));
        Assert.True(result.Success, result.ErrorMessage);
        return result.Value!;
    }

    private Task<PlanOutcome<DecisionResultDto>> Submit(int n, string action) =>
        _rig.UseCases.SubmitDecisionAsync(new SubmitDecisionRequest(S(n), action, "user", null));

    private async Task<List<EditDecision>> HistoryAsync(int n) => await _rig.Plan.ListDecisionsBySuggestionAsync(S(n));

    private async Task<EffectiveSuggestion> EffectiveAsync(int n)
    {
        var suggestion = (await _rig.Plan.ListSuggestionsByIdsAsync([S(n)])).Single();
        return EffectiveSuggestionStatus.ResolveDetailed([suggestion], await HistoryAsync(n))[S(n)];
    }

    // ------------------------------------------------------------------ B1: leitura deriva o estado externo

    [Fact]
    public async Task B1_PlanGet_SourceFileRemovedAfterGeneration_ReportsSourceFileMissing()
    {
        await GenerateAsync();
        _rig.Files.Missing.Add(M1Path);

        var plan = await GetAsync(1);

        Assert.Contains(plan.Validation.Issues, i => i.Code == "SOURCE_FILE_MISSING" && i.Severity == "error" && i.ClipId is not null);
        Assert.False(plan.Validation.IsValid);
    }

    [Fact]
    public async Task B1_PlanGet_DecisionsChangedWithoutRegeneration_ReportsPlanOutdated()
    {
        await GenerateAsync();
        await _rig.Plan.SaveDecisionsAtomicAsync([new EditDecision
        {
            SuggestionId = S(5), Action = "defer", Reviewer = "user", TimestampUtc = DateTime.UtcNow,
        }]);

        var plan = await GetAsync(1);

        var issue = Assert.Single(plan.Validation.Issues, i => i.Code == "PLAN_OUTDATED");
        Assert.Equal("warning", issue.Severity);
        Assert.True(plan.Validation.IsValid, "PLAN_OUTDATED é aviso e não invalida o plano.");
    }

    [Fact]
    public async Task B1_PlanGet_MediaTamperedAfterGeneration_ReportsPlanTamperedFromFingerprint()
    {
        var generated = await GenerateAsync();
        // Conteúdo alterado no JSON (fora do TravelCut): o fingerprint não confere mais.
        var tampered = generated with { Clips = generated.Clips.Select(c => c with { DurationFrames = c.DurationFrames + 1 }).ToList() };
        _rig.Artifacts.Replace(generated.PlanId, 1, PlanJson.Serialize(tampered));

        var plan = await GetAsync(1);

        Assert.Contains(plan.Validation.Issues, i => i.Code == "PLAN_TAMPERED");
        Assert.False(plan.Validation.IsValid);
    }

    // ------------------------------------------------------------------ B2: estado externo não é gravado

    [Fact]
    public async Task B2_SourceFileMissing_IsNotWrittenToArtifact()
    {
        _rig.Files.Missing.Add(M1Path);

        var generated = await GenerateAsync();

        Assert.Contains(generated.Validation.Issues, i => i.Code == "SOURCE_FILE_MISSING");
        Assert.DoesNotContain("SOURCE_FILE_MISSING", Assert.Single(_rig.Artifacts.Files).Value);
    }

    [Fact]
    public async Task B2_RestoredFile_PlanGetHasNoError_AndIsValid()
    {
        _rig.Files.Missing.Add(M1Path);
        await GenerateAsync();

        _rig.Files.Missing.Clear(); // arquivo restaurado
        var plan = await GetAsync(1);

        Assert.DoesNotContain(plan.Validation.Issues, i => i.Code == "SOURCE_FILE_MISSING");
        Assert.True(plan.Validation.IsValid);
    }

    [Fact]
    public async Task B2_RestoredFile_GenerateIsIdempotent_WithoutErrorAndWithoutNewRevision()
    {
        _rig.Files.Missing.Add(M1Path);
        var first = await GenerateAsync();
        Assert.False(first.Validation.IsValid);

        _rig.Files.Missing.Clear();
        var again = await GenerateAsync();

        Assert.Equal(1, again.Revision);
        Assert.Equal(first.PlanFingerprint, again.PlanFingerprint);
        Assert.True(again.Validation.IsValid);
        Assert.DoesNotContain(again.Validation.Issues, i => i.Code == "SOURCE_FILE_MISSING");
        Assert.Single((await _rig.UseCases.ListAsync(ProjectId)).Value!.Revisions);
    }

    [Fact]
    public async Task B2_MissingAgain_GenerateIsIdempotent_ReportsErrorAgain()
    {
        await GenerateAsync();
        _rig.Files.Missing.Add(M1Path);

        var again = await GenerateAsync();

        Assert.Equal(1, again.Revision);
        Assert.Contains(again.Validation.Issues, i => i.Code == "SOURCE_FILE_MISSING");
        Assert.False(again.Validation.IsValid);
    }

    [Fact]
    public async Task B2_LegacyArtifactWithStoredSourceFileMissing_IsRemovedOnRead()
    {
        var plan = await GenerateAsync();
        // Revisão antiga: o JSON já trazia SOURCE_FILE_MISSING gravado (arquivo presente hoje).
        var legacy = plan with
        {
            Validation = plan.Validation with
            {
                IsValid = false,
                Issues = plan.Validation.Issues
                    .Append(new PlanIssueDto("error", "SOURCE_FILE_MISSING", "legado", plan.Clips[0].ClipId))
                    .ToList(),
            },
        };
        _rig.Artifacts.Replace(plan.PlanId, 1, PlanJson.Serialize(legacy));

        var read = await GetAsync(1);

        Assert.DoesNotContain(read.Validation.Issues, i => i.Code == "SOURCE_FILE_MISSING");
        Assert.True(read.Validation.IsValid);
    }

    [Fact]
    public async Task B2_FingerprintIgnoresValidation_SoDerivedIssuesDoNotChangeContentIdentity()
    {
        var plan = await GenerateAsync();
        var withIssue = plan with
        {
            Validation = plan.Validation with
            {
                Issues = plan.Validation.Issues.Append(new PlanIssueDto("error", "SOURCE_FILE_MISSING", "x", null)).ToList(),
            },
        };

        Assert.Equal(
            TravelCut.Domain.Planning.PlanFingerprint.ComputePlan(PlanMapping.ToDomain(plan)),
            TravelCut.Domain.Planning.PlanFingerprint.ComputePlan(PlanMapping.ToDomain(withIssue)));
    }

    // ------------------------------------------------------------------ B3: regeneração que lança exceção

    [Fact]
    public async Task B3_RegenerationThrowsIOException_DecisionIsKept_AndPlanErrorIsReported()
    {
        await GenerateAsync(); // revisão 1
        _rig.Artifacts.WriteFailure = new IOException("disco cheio");

        var r = await Submit(4, "approve");

        Assert.True(r.Success, r.ErrorMessage);
        Assert.Equal("PLAN_REGENERATION_FAILED", r.Value!.PlanError);
        Assert.False(r.Value.PlanChanged);
        Assert.Equal(1, r.Value.PlanRevision);
        Assert.Equal("Approved", Assert.Single(r.Value.Suggestions).Status);
        Assert.Single(await HistoryAsync(4));
        Assert.Single((await _rig.UseCases.ListAsync(ProjectId)).Value!.Revisions);
    }

    // ------------------------------------------------------------------ A1: revert com fingerprint da alvo

    [Fact]
    public async Task A1_ApproveCut_ThenRevertToEarlier_RevisionIsPlanOutdated()
    {
        await GenerateAsync();              // R1 (S4 ainda pendente)
        await Submit(4, "approve");          // R2 (corte aprovado)

        var revert = await _rig.UseCases.RevertAsync(new RevertPlanRequest(ProjectId, 1));

        Assert.True(revert.Success, revert.ErrorMessage);
        Assert.Equal(3, revert.Value!.Revision);
        Assert.Contains(revert.Value.Validation.Issues, i => i.Code == "PLAN_OUTDATED");

        var r3 = await GetAsync(3);
        Assert.Contains(r3.Validation.Issues, i => i.Code == "PLAN_OUTDATED");
        Assert.Equal((await GetAsync(1)).PlanFingerprint, r3.PlanFingerprint);
    }

    [Fact]
    public async Task A1_RevertWithoutDecisionChanges_IsNotOutdated()
    {
        await GenerateAsync(); // R1

        var revert = await _rig.UseCases.RevertAsync(new RevertPlanRequest(ProjectId, 1));

        Assert.True(revert.Success, revert.ErrorMessage);
        Assert.DoesNotContain(revert.Value!.Validation.Issues, i => i.Code == "PLAN_OUTDATED");
    }

    [Fact]
    public async Task A1_RevertToRevisionWithoutInputsFingerprint_NeverEmitsPlanOutdated()
    {
        await GenerateAsync();
        await Submit(4, "approve"); // R2
        _rig.Db.Database.ExecuteSqlRaw("UPDATE PlanRevisions SET InputsFingerprint = NULL WHERE Revision = 1");

        var revert = await _rig.UseCases.RevertAsync(new RevertPlanRequest(ProjectId, 1));

        Assert.True(revert.Success, revert.ErrorMessage);
        Assert.DoesNotContain(revert.Value!.Validation.Issues, i => i.Code == "PLAN_OUTDATED");
    }

    // ------------------------------------------------------------------ A3: undo/redo como pilha (ADR-014)

    [Fact]
    public async Task A3_Stack_ApproveReject_UndoUndoRedoRedo_FollowsTheStack()
    {
        await Submit(2, "approve");
        await Submit(2, "reject");

        var undo1 = await _rig.UseCases.UndoDecisionAsync(new UndoDecisionRequest(S(2)));
        Assert.True(undo1.Success, undo1.ErrorMessage);
        Assert.Equal(("Approved", true, true), Flags(await EffectiveAsync(2)));

        var undo2 = await _rig.UseCases.UndoDecisionAsync(new UndoDecisionRequest(S(2)));
        Assert.True(undo2.Success, undo2.ErrorMessage);
        Assert.Equal(("Proposed", false, true), Flags(await EffectiveAsync(2)));

        var redo1 = await _rig.UseCases.RedoDecisionAsync(new RedoDecisionRequest(S(2)));
        Assert.True(redo1.Success, redo1.ErrorMessage);
        Assert.Equal(("Approved", true, true), Flags(await EffectiveAsync(2)));

        var redo2 = await _rig.UseCases.RedoDecisionAsync(new RedoDecisionRequest(S(2)));
        Assert.True(redo2.Success, redo2.ErrorMessage);
        Assert.Equal(("Rejected", true, false), Flags(await EffectiveAsync(2)));

        // Histórico append-only: aprovar, rejeitar, dois undos e dois redos (redo = ação desfeita, IsRedo).
        var h = await HistoryAsync(2);
        Assert.Equal(new[] { "approve", "reject", "undo", "undo", "approve", "reject" }, h.Select(d => d.Action).ToArray());
        Assert.Equal(new[] { false, false, false, false, true, true }, h.Select(d => d.IsRedo).ToArray());
        Assert.Equal("refeito", h[4].Notes);
    }

    [Fact]
    public async Task A3_Stack_UndoAfterRedo_WorksOnTheStack()
    {
        await Submit(2, "approve");
        await Submit(2, "reject");
        await _rig.UseCases.UndoDecisionAsync(new UndoDecisionRequest(S(2)));
        await _rig.UseCases.UndoDecisionAsync(new UndoDecisionRequest(S(2)));
        await _rig.UseCases.RedoDecisionAsync(new RedoDecisionRequest(S(2)));

        var undo = await _rig.UseCases.UndoDecisionAsync(new UndoDecisionRequest(S(2)));

        Assert.True(undo.Success, undo.ErrorMessage);
        Assert.Equal(("Proposed", false, true), Flags(await EffectiveAsync(2)));
    }

    [Fact]
    public async Task A3_Stack_NewUserDecision_ClearsRedoStack()
    {
        await Submit(2, "approve");
        await Submit(2, "reject");
        await _rig.UseCases.UndoDecisionAsync(new UndoDecisionRequest(S(2)));
        Assert.True((await EffectiveAsync(2)).CanRedo);

        await Submit(2, "defer");

        Assert.False((await EffectiveAsync(2)).CanRedo);
        var redo = await _rig.UseCases.RedoDecisionAsync(new RedoDecisionRequest(S(2)));
        Assert.Equal("NOTHING_TO_REDO", redo.ErrorCode);
        Assert.Equal(SuggestionStatus.Deferred, (await EffectiveAsync(2)).Status);
    }

    [Fact]
    public async Task A3_Stack_UndoBeyondBottom_IsNothingToUndo()
    {
        await Submit(2, "approve");
        await _rig.UseCases.UndoDecisionAsync(new UndoDecisionRequest(S(2)));

        var second = await _rig.UseCases.UndoDecisionAsync(new UndoDecisionRequest(S(2)));

        Assert.Equal("NOTHING_TO_UNDO", second.ErrorCode);
        Assert.Equal(2, (await HistoryAsync(2)).Count);
    }

    [Fact]
    public async Task A3_Undo_RevalidatedInsideTransaction_RefusesWhenAnotherWriterUndidFirst()
    {
        await Submit(2, "approve");
        // Outro gravador desfaz entre a leitura do pedido e a gravação deste undo.
        var racing = UseCasesWithRace(inner => UndoByOtherWriterAsync(inner, S(2)));

        var r = await racing.UndoDecisionAsync(new UndoDecisionRequest(S(2)));

        Assert.Equal("NOTHING_TO_UNDO", r.ErrorCode);
        Assert.Equal(new[] { "approve", "undo" }, (await HistoryAsync(2)).Select(d => d.Action).ToArray());
    }

    [Fact]
    public async Task A3_Redo_RevalidatedInsideTransaction_RefusesWhenAnotherWriterRedidFirst()
    {
        await Submit(2, "approve");
        await _rig.UseCases.UndoDecisionAsync(new UndoDecisionRequest(S(2)));
        var racing = UseCasesWithRace(inner => RedoByOtherWriterAsync(inner, S(2)));

        var r = await racing.RedoDecisionAsync(new RedoDecisionRequest(S(2)));

        Assert.Equal("NOTHING_TO_REDO", r.ErrorCode);
        Assert.Equal(new[] { "approve", "undo", "approve" }, (await HistoryAsync(2)).Select(d => d.Action).ToArray());
        Assert.Equal(SuggestionStatus.Approved, (await EffectiveAsync(2)).Status);
    }

    private static (string Status, bool CanUndo, bool CanRedo) Flags(EffectiveSuggestion e) =>
        (e.Status.ToString(), e.CanUndo, e.CanRedo);

    private PlanningUseCases UseCasesWithRace(Func<IEditPlanRepository, Task> otherWriter) => new(
        _rig.Projects, _rig.Media, new RacingEditPlanRepository(_rig.Plan, otherWriter), _rig.Analysis,
        new ChronologyService(), _rig.Revisions, _rig.Artifacts, _rig.Files,
        TimeProvider.System, NullLogger<PlanningUseCases>.Instance);

    private static Task UndoByOtherWriterAsync(IEditPlanRepository inner, Guid suggestionId) =>
        inner.SaveDecisionIfAsync(suggestionId, history =>
            EffectiveSuggestionStatus.ComputeStacks(history).Active.Count == 0 ? null : new EditDecision
            {
                DecisionId = Guid.NewGuid(), SuggestionId = suggestionId, Action = "undo", Reviewer = "outro",
                Notes = "desfeito", TimestampUtc = DateTime.UtcNow,
            });

    private static Task RedoByOtherWriterAsync(IEditPlanRepository inner, Guid suggestionId) =>
        inner.SaveDecisionIfAsync(suggestionId, history =>
        {
            var undone = EffectiveSuggestionStatus.ComputeStacks(history).Undone;
            if (undone.Count == 0) return null;
            return new EditDecision
            {
                DecisionId = Guid.NewGuid(), SuggestionId = suggestionId, Action = undone[^1].Action, Reviewer = "outro",
                Notes = "refeito", TimestampUtc = DateTime.UtcNow, IsRedo = true,
                SourceStartMs = undone[^1].SourceStartMs, SourceEndMs = undone[^1].SourceEndMs,
            };
        });

    /// <summary>
    /// Repositório que executa uma gravação concorrente ANTES da primeira gravação do caso de uso: simula a corrida
    /// entre dois gravadores. Demais operações são delegadas ao repositório real.
    /// </summary>
    private sealed class RacingEditPlanRepository(IEditPlanRepository inner, Func<IEditPlanRepository, Task> otherWriter) : IEditPlanRepository
    {
        private bool _raced;

        public async Task<EditDecision?> SaveDecisionIfAsync(Guid suggestionId, Func<IReadOnlyList<EditDecision>, EditDecision?> build, CancellationToken ct = default)
        {
            if (!_raced)
            {
                _raced = true;
                await otherWriter(inner);
            }
            return await inner.SaveDecisionIfAsync(suggestionId, build, ct);
        }

        public Task<EditPlan?> GetLatestByProjectAsync(Guid projectId, CancellationToken ct = default) => inner.GetLatestByProjectAsync(projectId, ct);
        public Task SavePlanAsync(EditPlan plan, CancellationToken ct = default) => inner.SavePlanAsync(plan, ct);
        public Task<List<EditSuggestion>> ListSuggestionsByProjectAsync(Guid projectId, CancellationToken ct = default) => inner.ListSuggestionsByProjectAsync(projectId, ct);
        public Task SaveSuggestionsBatchAsync(IEnumerable<EditSuggestion> suggestions, CancellationToken ct = default) => inner.SaveSuggestionsBatchAsync(suggestions, ct);
        public Task SaveDecisionAsync(EditDecision decision, CancellationToken ct = default) => inner.SaveDecisionAsync(decision, ct);
        public Task<List<EditDecision>> ListDecisionsByProjectAsync(Guid projectId, CancellationToken ct = default) => inner.ListDecisionsByProjectAsync(projectId, ct);
        public Task<EditDecision?> GetLatestDecisionBySuggestionAsync(Guid suggestionId, CancellationToken ct = default) => inner.GetLatestDecisionBySuggestionAsync(suggestionId, ct);
        public Task<List<EditDecision>> ListDecisionsBySuggestionAsync(Guid suggestionId, CancellationToken ct = default) => inner.ListDecisionsBySuggestionAsync(suggestionId, ct);
        public Task SaveDecisionsAtomicAsync(IReadOnlyList<EditDecision> decisions, CancellationToken ct = default) => inner.SaveDecisionsAtomicAsync(decisions, ct);
        public Task<List<EditSuggestion>> ListSuggestionsByIdsAsync(IReadOnlyCollection<Guid> suggestionIds, CancellationToken ct = default) => inner.ListSuggestionsByIdsAsync(suggestionIds, ct);
        public Task<List<Guid>> FindExistingSuggestionIdsAsync(IReadOnlyCollection<Guid> suggestionIds, CancellationToken ct = default) => inner.FindExistingSuggestionIdsAsync(suggestionIds, ct);
    }

    // ------------------------------------------------------------------ limites de texto (reviewer/notes)

    [Fact]
    public async Task Text_NotesOver2000_IsInvalidArgument_AndNothingRecorded()
    {
        var r = await _rig.UseCases.SubmitDecisionAsync(new SubmitDecisionRequest(S(2), "approve", "user", new string('n', 2001)));

        Assert.Equal("INVALID_ARGUMENT", r.ErrorCode);
        Assert.Empty(await HistoryAsync(2));
    }

    [Fact]
    public async Task Text_NotesAtLimit_IsAccepted()
    {
        var r = await _rig.UseCases.SubmitDecisionAsync(new SubmitDecisionRequest(S(2), "approve", "user", new string('n', 2000)));

        Assert.True(r.Success, r.ErrorMessage);
    }

    [Fact]
    public async Task Text_ReviewerOver100_IsInvalidArgument_AndNothingRecorded()
    {
        var r = await _rig.UseCases.SubmitDecisionAsync(new SubmitDecisionRequest(S(2), "approve", new string('r', 101), null));

        Assert.Equal("INVALID_ARGUMENT", r.ErrorCode);
        Assert.Empty(await HistoryAsync(2));
    }

    [Fact]
    public async Task Text_BatchNotesOver2000_IsInvalidArgument_AndNothingRecorded()
    {
        var r = await _rig.UseCases.BatchDecideAsync(new BatchDecideRequest([S(1), S(2)], "defer", "user", new string('n', 2001)));

        Assert.Equal("INVALID_ARGUMENT", r.ErrorCode);
        Assert.Empty(await _rig.Plan.ListDecisionsByProjectAsync(ProjectId));
    }

    // ------------------------------------------------------------------ A4: corte automático só para silêncio

    [Theory]
    [InlineData("filler")]
    [InlineData("repetition")]
    [InlineData("false_start")]
    [InlineData("prolongation")]
    [InlineData("pause")]
    public void A4_HighConfidenceNonSilence_WithoutReview_IsNotAppliedAutomatically(string kind)
    {
        var plan = PlanWithSuggestion(kind);

        Assert.DoesNotContain(plan.AppliedCuts, c => c.SuggestionId == S(1));
        Assert.Contains(plan.Pending, p => p.Kind == "suggestion_pending" && p.SuggestionId == S(1));
    }

    [Fact]
    public void A4_HighConfidenceSilence_WithoutReview_IsAppliedAutomatically()
    {
        var plan = PlanWithSuggestion("silence");

        var cut = Assert.Single(plan.AppliedCuts, c => c.SuggestionId == S(1));
        Assert.Equal("auto_trivial", cut.Origin);
    }

    [Fact]
    public void A4_ApprovedNonSilence_IsStillAppliedAsApprovedByUser()
    {
        var suggestion = Sug(1, "repetition", "high", review: false, SuggestionStatus.Approved);

        var plan = TravelFixture.BuildPlan(TravelFixture.Input(suggestions: [suggestion], decisions: []));

        Assert.Equal("approved_by_user", Assert.Single(plan.AppliedCuts, c => c.SuggestionId == S(1)).Origin);
    }

    // decisions: [] isola a sugestão das decisões da fixture (que aprovam S1–S8).
    private static EditPlanDto PlanWithSuggestion(string kind) =>
        TravelFixture.BuildPlan(TravelFixture.Input(
            suggestions: [Sug(1, kind, "high", review: false, SuggestionStatus.Proposed)],
            decisions: []));

    private static EditSuggestion Sug(int n, string kind, string band, bool review, SuggestionStatus status) => new()
    {
        SuggestionId = S(n),
        MediaId = TravelFixture.M(1),
        SourceInterval = new TimeRange(10_000, 12_000),
        Kind = SuggestionKind.Silence,
        Rationale = "Sugestão de teste",
        EvidenceRefs = JsonSerializer.Serialize(new { kind }),
        ConfidenceBand = band,
        NeedsHumanReview = review,
        SuggestedAction = "cut",
        Status = status,
        Author = "vad-analyzer",
        CreatedAtUtc = new DateTime(2026, 10, 14, 9, 0, 0, DateTimeKind.Utc),
    };
}

/// <summary>Migração do IsRedo (ADR-014): marcação única de redos legados, sem reinterpretar notas novas.</summary>
public sealed class IsRedoMigrationTests : IDisposable
{
    private readonly SqliteConnection _connection = new("Filename=:memory:");

    public IsRedoMigrationTests() => _connection.Open();

    public void Dispose() => _connection.Dispose();

    private TravelCutDbContext Create() =>
        new(new DbContextOptionsBuilder<TravelCutDbContext>().UseSqlite(_connection).Options);

    private void Exec(string sql)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private void Insert(string suggestion, int version, string action, string notes) => Exec(
        "INSERT INTO EditDecisions (DecisionId, SuggestionId, Action, Reviewer, Notes, TimestampUtc, Version) VALUES " +
        $"('{Guid.NewGuid()}', '{suggestion}', '{action}', 'user', '{notes}', '2026-10-14 10:{version:D2}:00', {version});");

    [Fact]
    public void LegacyRedoRows_AreMarkedOnce_AndUserNotesAreNotReinterpretedLater()
    {
        using (var db = Create()) db.Database.EnsureCreated();
        Exec("DROP TABLE EditDecisions;");
        Exec("CREATE TABLE EditDecisions (DecisionId TEXT NOT NULL PRIMARY KEY, SuggestionId TEXT NOT NULL, " +
             "Action TEXT NOT NULL, Reviewer TEXT NOT NULL, Notes TEXT NOT NULL, TimestampUtc TEXT NOT NULL, " +
             "Version INTEGER NOT NULL DEFAULT 1, BaseRevision INTEGER NULL, SourceStartMs INTEGER NULL, SourceEndMs INTEGER NULL);");

        const string s = "22222222-2222-4222-8222-222222222222";
        const string t = "33333333-3333-4333-8333-333333333333";
        Insert(s, 1, "approve", "");
        Insert(s, 2, "undo", "desfeito");
        Insert(s, 3, "approve", "refeito");   // redo legado: ação original + "refeito" logo após o undo
        Insert(s, 4, "reject", "refeito");    // nota digitada após um approve ativo: não é redo
        Insert(t, 1, "approve", "");
        Insert(t, 2, "approve", "refeito");   // sem undo antes: não é redo

        using (var db = Create()) db.EnsureSchemaUpdated();
        // Segunda execução (startup seguinte) com uma nota "refeito" nova logo após um undo: não pode ser marcada.
        Insert(t, 3, "undo", "desfeito");
        Insert(t, 4, "reject", "refeito");
        using (var db = Create()) db.EnsureSchemaUpdated();

        using var check = Create();
        var rows = check.EditDecisions.AsNoTracking().ToList();
        bool Redo(string sug, int v) => rows.Single(r => r.SuggestionId == Guid.Parse(sug) && r.Version == v).IsRedo;

        Assert.True(Redo(s, 3));
        Assert.False(Redo(s, 4));
        Assert.False(Redo(t, 2));
        Assert.False(Redo(t, 4));
        Assert.False(Redo(s, 1));
    }

    [Fact]
    public void LegacyRowsAllVersionOne_AreRenumberedFirst_ThenRedoIsMarked_AndMarkerIsRecorded()
    {
        // Banco anterior à Version única: todas as decisões com Version=1; a ordem real só existe em TimestampUtc.
        using (var db = Create()) db.Database.EnsureCreated();
        Exec("DROP TABLE EditDecisions;");
        Exec("CREATE TABLE EditDecisions (DecisionId TEXT NOT NULL PRIMARY KEY, SuggestionId TEXT NOT NULL, " +
             "Action TEXT NOT NULL, Reviewer TEXT NOT NULL, Notes TEXT NOT NULL, TimestampUtc TEXT NOT NULL, " +
             "Version INTEGER NOT NULL DEFAULT 1);");
        // GUIDs em maiúsculas, como o EF Core grava no SQLite (a renumeração atualiza pela chave via EF).
        const string s = "44444444-4444-4444-8444-444444444444";
        void InsertAt(string action, string notes, int minute) => Exec(
            "INSERT INTO EditDecisions (DecisionId, SuggestionId, Action, Reviewer, Notes, TimestampUtc, Version) VALUES " +
            $"('{Guid.NewGuid().ToString().ToUpperInvariant()}', '{s.ToUpperInvariant()}', '{action}', 'user', '{notes}', '2026-10-14 10:{minute:D2}:00', 1);");
        InsertAt("approve", "", 1);
        InsertAt("undo", "desfeito", 2);
        InsertAt("approve", "refeito", 3);

        using (var db = Create()) db.EnsureSchemaUpdated();

        using var check = Create();
        var rows = check.EditDecisions.AsNoTracking().ToList().OrderBy(r => r.Version).ToList();
        Assert.Equal(new[] { 1, 2, 3 }, rows.Select(r => r.Version));
        Assert.Equal(new[] { "approve", "undo", "approve" }, rows.Select(r => r.Action));
        Assert.Equal(new[] { false, false, true }, rows.Select(r => r.IsRedo));

        using var marker = _connection.CreateCommand();
        marker.CommandText = "SELECT COUNT(*) FROM SchemaMarkers WHERE Name LIKE 'EditDecisions.IsRedo.backfill/%';";
        Assert.Equal(1L, Convert.ToInt64(marker.ExecuteScalar()));
    }

    [Fact]
    public void LegacyRedoRow_AfterMigration_ReplaysAsStackRedo()
    {
        using (var db = Create()) db.Database.EnsureCreated();
        Exec("DROP TABLE EditDecisions;");
        Exec("CREATE TABLE EditDecisions (DecisionId TEXT NOT NULL PRIMARY KEY, SuggestionId TEXT NOT NULL, " +
             "Action TEXT NOT NULL, Reviewer TEXT NOT NULL, Notes TEXT NOT NULL, TimestampUtc TEXT NOT NULL, " +
             "Version INTEGER NOT NULL DEFAULT 1, BaseRevision INTEGER NULL, SourceStartMs INTEGER NULL, SourceEndMs INTEGER NULL);");
        const string s = "22222222-2222-4222-8222-222222222222";
        Insert(s, 1, "approve", "");
        Insert(s, 2, "undo", "desfeito");
        Insert(s, 3, "approve", "refeito");

        using (var db = Create()) db.EnsureSchemaUpdated();

        using var check = Create();
        var history = check.EditDecisions.AsNoTracking().ToList()
            .Where(d => d.SuggestionId == Guid.Parse(s))
            .OrderBy(d => d.Version)
            .ToList();
        var stacks = EffectiveSuggestionStatus.ComputeStacks(history);
        Assert.Single(stacks.Active);
        Assert.Empty(stacks.Undone);
    }
}
