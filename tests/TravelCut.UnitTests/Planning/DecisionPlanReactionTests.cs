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
using TravelCut.Infrastructure.Persistence;
using TravelCut.Infrastructure.Planning;
using TravelCut.Infrastructure.Repositories;
using Xunit;

namespace TravelCut.UnitTests.Planning;

/// <summary>
/// Ambiente de decisões e plano: EF + SQLite em memória (índice único, transações reais), artefatos em memória,
/// existência de arquivo controlada pelo teste e análises da viagem de referência. Nenhum disco é tocado.
/// </summary>
public sealed class DecisionRig : IDisposable
{
    /// <summary>Raiz lógica do projeto. Com <see cref="FakeFileExistence"/> nada é lido do disco.</summary>
    public const string Root = @"C:\fake\decisoes";

    private readonly SqliteConnection _connection;

    public DecisionRig()
    {
        _connection = new SqliteConnection("Filename=:memory:");
        _connection.Open();
        Db = new TravelCutDbContext(new DbContextOptionsBuilder<TravelCutDbContext>().UseSqlite(_connection).Options);
        Db.Database.EnsureCreated();

        Projects = new SqliteProjectRepository(Db);
        Media = new SqliteMediaRepository(Db);
        Plan = new SqliteEditPlanRepository(Db);
        Revisions = new SqlitePlanRevisionRepository(Db);

        Projects.SaveAsync(new Project { ProjectId = TravelFixture.ProjectId, Name = "Viagem", RootPath = Root }).GetAwaiter().GetResult();
        Media.SaveBatchAsync(TravelFixture.Media()).GetAwaiter().GetResult();
        foreach (var s in TravelFixture.Suggestions()) Plan.SaveSuggestionsBatchAsync([s]).GetAwaiter().GetResult();
        foreach (var (id, analysis) in TravelFixture.Analyses()) Analysis.Analyses[id] = analysis;

        UseCases = new PlanningUseCases(
            Projects, Media, Plan, Analysis, new ChronologyService(), Revisions, Artifacts, Files,
            TimeProvider.System, NullLogger<PlanningUseCases>.Instance);

        // Dispatcher: só o planejamento e o repositório de decisões são usados pelas rotas testadas aqui.
        Dispatcher = new IpcDispatcher(
            null!, null!, null!, null!, null!, null!, null!, null!, null!, Plan, null!, null!, null!, null!, null!,
            UseCases, NullLogger<IpcDispatcher>.Instance);
    }

    public TravelCutDbContext Db { get; }
    public SqliteProjectRepository Projects { get; }
    public SqliteMediaRepository Media { get; }
    public SqliteEditPlanRepository Plan { get; }
    public SqlitePlanRevisionRepository Revisions { get; }
    public FakeFileExistence Files { get; } = new();
    public InMemoryPlanArtifactStore Artifacts { get; } = new();
    public FakePipelineAnalysis Analysis { get; } = new();
    public PlanningUseCases UseCases { get; }
    public IpcDispatcher Dispatcher { get; }

    public string FullPath(string relativePath) => Path.Combine(Root, relativePath);

    public void Dispose()
    {
        Db.Dispose();
        _connection.Dispose();
    }
}

/// <summary>
/// F07.2: decisões (submit/modify/undo/redo/lote), estado efetivo com intervalo, reação do plano (idempotente),
/// validações PLAN_OUTDATED e SOURCE_FILE_MISSING, e histórico append-only.
/// Sugestões da viagem: S1 (M1, 10,0–12,4 s, proposta de corte automático), S4 (M1, 50–52 s, revisão humana),
/// S5 (M2, defer), S8 (M7, mídia excluída do plano: decidir não muda o plano).
/// </summary>
public sealed class DecisionPlanReactionTests : IDisposable
{
    private readonly DecisionRig _rig = new();

    public void Dispose() => _rig.Dispose();

    private static Guid S(int n) => TravelFixture.S(n);
    private static Guid ProjectId => TravelFixture.ProjectId;

    private Task<PlanOutcome<DecisionResultDto>> Submit(int n, string action, double? start = null, double? end = null) =>
        _rig.UseCases.SubmitDecisionAsync(new SubmitDecisionRequest(S(n), action, "user", null, start, end));

    private async Task<EditPlanDto> GenerateAsync()
    {
        var result = await _rig.UseCases.GenerateAsync(new GeneratePlanRequest(ProjectId, null, null));
        Assert.True(result.Success, result.ErrorMessage);
        return result.Value!;
    }

    private async Task<EditPlanDto> RevisionAsync(int revision) =>
        (await _rig.UseCases.GetAsync(new GetPlanRequest(ProjectId, revision))).Value!;

    private async Task<PlanValidationDto> ValidateAsync(int revision) =>
        (await _rig.UseCases.ValidateAsync(new ValidatePlanRequest(ProjectId, revision))).Value!;

    private async Task<List<(int Version, string Action, long? StartMs, long? EndMs, int? BaseRevision)>> HistoryAsync(int n) =>
        (await _rig.Plan.ListDecisionsBySuggestionAsync(S(n)))
            .Select(d => (d.Version, d.Action, d.SourceStartMs, d.SourceEndMs, d.BaseRevision))
            .ToList();

    private async Task<HashSet<Guid>> AllDecisionIdsAsync() =>
        (await _rig.Plan.ListDecisionsByProjectAsync(ProjectId)).Select(d => d.DecisionId).ToHashSet();

    private async Task<int> DecisionCountAsync() => (await _rig.Plan.ListDecisionsByProjectAsync(ProjectId)).Count;

    private async Task<EffectiveSuggestion> EffectiveAsync(int n)
    {
        var suggestion = (await _rig.Plan.ListSuggestionsByIdsAsync([S(n)])).Single();
        var history = await _rig.Plan.ListDecisionsBySuggestionAsync(S(n));
        return EffectiveSuggestionStatus.ResolveDetailed([suggestion], history)[S(n)];
    }

    // ------------------------------------------------------------------ modify

    [Fact]
    public async Task Modify_ValidInterval_IsApproved_WithAdjustedInterval_AndRecordsSource()
    {
        var r = await Submit(1, "modify", 10.5, 11.5);

        Assert.True(r.Success, r.ErrorMessage);
        var s = Assert.Single(r.Value!.Suggestions);
        Assert.Equal("Approved", s.Status);
        Assert.Equal(10.5, s.EffectiveStartSeconds);
        Assert.Equal(11.5, s.EffectiveEndSeconds);
        Assert.True(s.CanUndo);
        Assert.False(s.CanRedo);
        Assert.Equal(1, r.Value.RecordedCount);

        var h = Assert.Single(await HistoryAsync(1));
        Assert.Equal("modify", h.Action);
        Assert.Equal(10_500, h.StartMs);
        Assert.Equal(11_500, h.EndMs);
    }

    [Theory]
    [InlineData(58.0, 61.0)]   // fim além da duração (60 s)
    [InlineData(10.0, 10.0)]   // vazio
    [InlineData(12.0, 11.0)]   // invertido
    [InlineData(10.0, 10.005)] // menor que 1 quadro (1/120 s)
    [InlineData(-1.0, 2.0)]    // negativo
    [InlineData(double.NaN, 11.0)]
    public async Task Modify_InvalidInterval_IsRefused_AndNothingIsRecorded(double start, double end)
    {
        var r = await Submit(1, "modify", start, end);

        Assert.False(r.Success);
        Assert.Equal("INVALID_INTERVAL", r.ErrorCode);
        Assert.Empty(await HistoryAsync(1));
    }

    [Fact]
    public async Task Modify_WithoutValues_IsInvalidInterval()
    {
        var r = await Submit(1, "modify", null, null);

        Assert.Equal("INVALID_INTERVAL", r.ErrorCode);
        Assert.Empty(await HistoryAsync(1));
    }

    [Fact]
    public async Task Modify_UnknownMediaDuration_IsInvalidInterval_WithReason()
    {
        // S8 pertence à mídia com metadados FFprobe falhos: duração desconhecida.
        var r = await Submit(8, "modify", 1.0, 1.5);

        Assert.Equal("INVALID_INTERVAL", r.ErrorCode);
        Assert.Contains("duração da mídia desconhecida", r.ErrorMessage);
        Assert.Empty(await HistoryAsync(8));
    }

    [Fact]
    public async Task Modify_OneFrameAt120Fps_IsAccepted()
    {
        var r = await Submit(1, "modify", 10.0, 10.009);

        Assert.True(r.Success, r.ErrorMessage);
        Assert.Equal(10.0, r.Value!.Suggestions.Single().EffectiveStartSeconds);
    }

    [Fact]
    public async Task Modify_IsNotAllowedInBatch()
    {
        var r = await _rig.UseCases.BatchDecideAsync(new BatchDecideRequest([S(1), S(2)], "modify", "user", null));

        Assert.Equal("INVALID_ACTION", r.ErrorCode);
        Assert.Empty(await HistoryAsync(1));
        Assert.Empty(await HistoryAsync(2));
    }

    [Fact]
    public async Task Planner_AppliesModifiedInterval_NotTheOriginal()
    {
        await Submit(1, "modify", 10.5, 11.5);

        var plan = await GenerateAsync();

        var cut = Assert.Single(plan.AppliedCuts, c => c.SuggestionId == S(1));
        Assert.Equal(10_500_000, cut.SourceInUs);
        Assert.Equal(11_500_000, cut.SourceOutUs);
        Assert.Equal("approved_by_user", cut.Origin);
    }

    [Fact]
    public async Task Approve_AfterModify_KeepsAdjustedInterval()
    {
        await Submit(1, "modify", 10.5, 11.5);

        await Submit(1, "approve");

        var h = await HistoryAsync(1);
        Assert.Equal(("approve", 10_500L, 11_500L), (h[1].Action, h[1].StartMs!.Value, h[1].EndMs!.Value));
        var effective = await EffectiveAsync(1);
        Assert.Equal((10_500L, 11_500L), (effective.StartMs, effective.EndMs));
    }

    // ------------------------------------------------------------------ undo / redo

    [Fact]
    public async Task Undo_ThenRedo_RestoresPreviousState_IncludingModifiedInterval()
    {
        await Submit(1, "modify", 10.5, 11.5);

        var undo = await _rig.UseCases.UndoDecisionAsync(new UndoDecisionRequest(S(1)));

        Assert.True(undo.Success, undo.ErrorMessage);
        var afterUndo = undo.Value!.Suggestions.Single();
        Assert.Equal("Proposed", afterUndo.Status);
        Assert.Null(afterUndo.EffectiveStartSeconds);
        Assert.False(afterUndo.CanUndo);
        Assert.True(afterUndo.CanRedo);

        var redo = await _rig.UseCases.RedoDecisionAsync(new RedoDecisionRequest(S(1)));

        Assert.True(redo.Success, redo.ErrorMessage);
        var afterRedo = redo.Value!.Suggestions.Single();
        Assert.Equal("Approved", afterRedo.Status);
        Assert.Equal(10.5, afterRedo.EffectiveStartSeconds);
        Assert.Equal(11.5, afterRedo.EffectiveEndSeconds);
        Assert.True(afterRedo.CanUndo);
        Assert.False(afterRedo.CanRedo);

        // Append-only: modify, undo, e o redo regravando a mesma ação e o mesmo intervalo.
        var h = await HistoryAsync(1);
        Assert.Equal(new[] { (1, "modify"), (2, "undo"), (3, "modify") }, h.Select(x => (x.Version, x.Action)).ToList());
        Assert.Equal((h[0].StartMs, h[0].EndMs), (h[2].StartMs, h[2].EndMs));
    }

    [Fact]
    public async Task Undo_WithoutActiveDecision_IsNothingToUndo()
    {
        var r = await _rig.UseCases.UndoDecisionAsync(new UndoDecisionRequest(S(2)));

        Assert.Equal("NOTHING_TO_UNDO", r.ErrorCode);
        Assert.Empty(await HistoryAsync(2));
    }

    [Fact]
    public async Task Undo_TwiceInARow_SecondIsNothingToUndo()
    {
        await Submit(2, "approve");
        Assert.True((await _rig.UseCases.UndoDecisionAsync(new UndoDecisionRequest(S(2)))).Success);

        var second = await _rig.UseCases.UndoDecisionAsync(new UndoDecisionRequest(S(2)));

        Assert.Equal("NOTHING_TO_UNDO", second.ErrorCode);
    }

    [Fact]
    public async Task Redo_WithoutUndo_IsNothingToRedo()
    {
        await Submit(1, "approve");

        var r = await _rig.UseCases.RedoDecisionAsync(new RedoDecisionRequest(S(1)));

        Assert.Equal("NOTHING_TO_REDO", r.ErrorCode);
        Assert.Single(await HistoryAsync(1));
    }

    [Fact]
    public async Task Redo_TwiceAfterOneUndo_SecondIsNothingToRedo()
    {
        await Submit(3, "reject");
        await _rig.UseCases.UndoDecisionAsync(new UndoDecisionRequest(S(3)));
        Assert.True((await _rig.UseCases.RedoDecisionAsync(new RedoDecisionRequest(S(3)))).Success);

        var second = await _rig.UseCases.RedoDecisionAsync(new RedoDecisionRequest(S(3)));

        Assert.Equal("NOTHING_TO_REDO", second.ErrorCode);
        Assert.Equal(SuggestionStatus.Rejected, (await EffectiveAsync(3)).Status);
    }

    [Fact]
    public async Task Redo_OnSuggestionWithoutDecisions_IsNothingToRedo()
    {
        var r = await _rig.UseCases.RedoDecisionAsync(new RedoDecisionRequest(S(4)));

        Assert.Equal("NOTHING_TO_REDO", r.ErrorCode);
    }

    // ------------------------------------------------------------------ baseRevision

    [Fact]
    public async Task BaseRevision_IsNull_WhenProjectHasNoPlan()
    {
        await Submit(5, "defer");

        var h = await HistoryAsync(5);
        Assert.Null(Assert.Single(h).BaseRevision);
    }

    [Fact]
    public async Task BaseRevision_IsTheLatestRevisionAtDecisionTime()
    {
        await GenerateAsync(); // revisão 1

        await Submit(5, "defer");

        var latestBeforeSecond = await _rig.Revisions.GetLatestAsync(ProjectId);
        await Submit(3, "reject");

        var h5 = await HistoryAsync(5);
        var h3 = await HistoryAsync(3);
        Assert.Equal(1, h5.Single().BaseRevision);
        Assert.Equal(latestBeforeSecond!.Revision, h3.Single().BaseRevision);
    }

    // ------------------------------------------------------------------ lote

    [Fact]
    public async Task BatchDecide_WriteFailsInTheMiddle_NothingIsRecorded()
    {
        // Gatilho: a terceira gravação do lote falha. Sem transação única, S1 e S2 ficariam gravadas.
        _rig.Db.Database.ExecuteSqlRaw(
            "CREATE TRIGGER falha_terceira BEFORE INSERT ON EditDecisions " +
            "WHEN (SELECT COUNT(*) FROM EditDecisions) >= 2 " +
            "BEGIN SELECT RAISE(ABORT, 'falha injetada'); END;");

        await Assert.ThrowsAnyAsync<Exception>(() =>
            _rig.UseCases.BatchDecideAsync(new BatchDecideRequest([S(1), S(2), S(3)], "reject", "user", null)));

        Assert.Empty(await _rig.Plan.ListDecisionsByProjectAsync(ProjectId));
        Assert.Equal(SuggestionStatus.Proposed, (await EffectiveAsync(1)).Status);
        Assert.Equal(SuggestionStatus.Proposed, (await EffectiveAsync(2)).Status);

        // Sem a falha, o mesmo lote grava tudo (prova que o problema era só a injeção).
        _rig.Db.Database.ExecuteSqlRaw("DROP TRIGGER falha_terceira;");
        var ok = await _rig.UseCases.BatchDecideAsync(new BatchDecideRequest([S(1), S(2), S(3)], "reject", "user", null));

        Assert.True(ok.Success, ok.ErrorMessage);
        Assert.Equal(3, ok.Value!.RecordedCount);
        Assert.Equal(3, await DecisionCountAsync());
    }

    [Fact]
    public async Task BatchDecide_ReportsMissingIds_AndRecordsOnlyExisting()
    {
        var randomId = Guid.NewGuid();

        var r = await _rig.UseCases.BatchDecideAsync(new BatchDecideRequest([S(1), randomId, S(2)], "defer", "user", null));

        Assert.True(r.Success, r.ErrorMessage);
        Assert.Equal(2, r.Value!.RecordedCount);
        Assert.Equal(new[] { randomId }, r.Value.MissingSuggestionIds);
        Assert.Equal(2, r.Value.Suggestions.Count);
    }

    // ------------------------------------------------------------------ reação do plano

    [Fact]
    public async Task ProjectWithoutPlan_DecisionCreatesNoPlan()
    {
        var r = await Submit(5, "defer");

        Assert.True(r.Success);
        Assert.Null(r.Value!.PlanRevision);
        Assert.False(r.Value.PlanChanged);
        Assert.Null(r.Value.PlanError);
        Assert.Empty((await _rig.UseCases.ListAsync(ProjectId)).Value!.Revisions);
        Assert.Empty(_rig.Artifacts.Files);
    }

    [Fact]
    public async Task DecisionThatDoesNotChangePlanContent_CreatesNoRevision()
    {
        await GenerateAsync(); // revisão 1

        // S8 está numa mídia excluída do plano: decidir não altera o conteúdo.
        var r = await Submit(8, "approve");

        Assert.True(r.Success, r.ErrorMessage);
        Assert.False(r.Value!.PlanChanged);
        Assert.Equal(1, r.Value.PlanRevision);
        Assert.Null(r.Value.PlanError);
        Assert.Single((await _rig.UseCases.ListAsync(ProjectId)).Value!.Revisions);
    }

    [Fact]
    public async Task ApprovingCut_CreatesNewRevision_WithTheCut()
    {
        await GenerateAsync(); // revisão 1: S4 (revisão humana) ainda pendente

        var r = await Submit(4, "approve");

        Assert.True(r.Success, r.ErrorMessage);
        Assert.True(r.Value!.PlanChanged);
        Assert.Equal(2, r.Value.PlanRevision);
        var rev2 = await RevisionAsync(2);
        Assert.Equal(1, rev2.BaseRevision);
        var cut = Assert.Single(rev2.AppliedCuts, c => c.SuggestionId == S(4));
        Assert.Equal(50_000_000, cut.SourceInUs);
        Assert.Equal(52_000_000, cut.SourceOutUs);
        Assert.Equal("approved_by_user", cut.Origin);
    }

    [Fact]
    public async Task UndoOfCut_RegeneratesPlan_BackToEarlierContent()
    {
        await GenerateAsync();
        await Submit(4, "approve"); // revisão 2

        var undo = await _rig.UseCases.UndoDecisionAsync(new UndoDecisionRequest(S(4)));

        Assert.True(undo.Success, undo.ErrorMessage);
        Assert.True(undo.Value!.PlanChanged);
        Assert.Equal(3, undo.Value.PlanRevision);
        Assert.Equal((await RevisionAsync(1)).PlanFingerprint, (await RevisionAsync(3)).PlanFingerprint);
    }

    [Fact]
    public async Task PlanRegenerationFailure_DoesNotUndoTheDecision()
    {
        var plan = await GenerateAsync();
        _rig.Artifacts.Remove(plan.PlanId, 1); // revisão existe no índice, arquivo sumiu

        var r = await Submit(5, "defer");

        Assert.True(r.Success, r.ErrorMessage);
        Assert.Equal("PLAN_FILE_MISSING", r.Value!.PlanError);
        Assert.False(r.Value.PlanChanged);
        Assert.Equal(1, r.Value.PlanRevision);
        Assert.Single(await HistoryAsync(5));
    }

    [Fact]
    public async Task Dispatcher_DecisionResponses_UseDecisionResultShape()
    {
        var submit = await _rig.Dispatcher.DispatchAsync(new IpcRequest
        {
            RequestId = "r1",
            Type = "decision.submit",
            Payload = new SubmitDecisionRequest(S(1), "modify", "user", null, 10.5, 11.5),
        });

        Assert.True(submit.Ok, submit.Error?.Message);
        var json = JsonSerializer.SerializeToElement(submit.Payload);
        Assert.Equal(1, json.GetProperty("recordedCount").GetInt32());
        Assert.Equal(10.5, json.GetProperty("suggestions")[0].GetProperty("effectiveStartSeconds").GetDouble());
        Assert.True(json.GetProperty("suggestions")[0].GetProperty("canUndo").GetBoolean());
        Assert.Equal(JsonValueKind.Null, json.GetProperty("planRevision").ValueKind);

        var redo = await _rig.Dispatcher.DispatchAsync(new IpcRequest
        {
            RequestId = "r2",
            Type = "decision.redo",
            Payload = new RedoDecisionRequest(S(2)),
        });
        Assert.False(redo.Ok);
        Assert.Equal("NOTHING_TO_REDO", redo.Error!.Code);
    }

    [Fact]
    public async Task SuggestionsList_ExposesEffectiveInterval_AndUndoRedoFlags()
    {
        await Submit(1, "modify", 10.5, 11.5);

        var list = await _rig.Dispatcher.DispatchAsync(new IpcRequest
        {
            RequestId = "r3",
            Type = "suggestions.list",
            Payload = new ProjectRequest(ProjectId),
        });

        Assert.True(list.Ok, list.Error?.Message);
        var s1 = JsonSerializer.SerializeToElement(list.Payload).GetProperty("suggestions").EnumerateArray()
            .Single(s => s.GetProperty("suggestionId").GetGuid() == S(1));
        Assert.Equal("Approved", s1.GetProperty("status").GetString());
        Assert.Equal(10.5, s1.GetProperty("effectiveStartSeconds").GetDouble());
        Assert.Equal(11.5, s1.GetProperty("effectiveEndSeconds").GetDouble());
        Assert.True(s1.GetProperty("canUndo").GetBoolean());
        Assert.False(s1.GetProperty("canRedo").GetBoolean());
    }

    // ------------------------------------------------------------------ validação: PLAN_OUTDATED

    [Fact]
    public async Task Validate_FreshRevision_HasNoPlanOutdated()
    {
        await GenerateAsync();

        var v = await ValidateAsync(1);

        Assert.DoesNotContain(v.Issues, i => i.Code == "PLAN_OUTDATED");
    }

    [Fact]
    public async Task Validate_AfterDecisionWithoutRegeneration_ReportsPlanOutdatedWarning()
    {
        await GenerateAsync();
        // Decisão gravada direto no repositório: sem regeneração, a revisão 1 ficou para trás.
        await _rig.Plan.SaveDecisionsAtomicAsync([new EditDecision
        {
            SuggestionId = S(5), Action = "defer", Reviewer = "user", TimestampUtc = DateTime.UtcNow,
        }]);

        var v = await ValidateAsync(1);

        var issue = Assert.Single(v.Issues, i => i.Code == "PLAN_OUTDATED");
        Assert.Equal("warning", issue.Severity);
        Assert.True(v.IsValid, "PLAN_OUTDATED é aviso: não invalida o plano.");
    }

    [Fact]
    public async Task DecisionWithoutPlanEffect_DoesNotLeaveRevisionMarkedOutdated()
    {
        await GenerateAsync(); // revisão 1

        // S8 está numa mídia excluída do plano: regeneração idempotente, revisão 1 continua vigente.
        var r = await Submit(8, "approve");
        Assert.False(r.Value!.PlanChanged);

        var v = await ValidateAsync(1);

        Assert.DoesNotContain(v.Issues, i => i.Code == "PLAN_OUTDATED");
    }

    [Fact]
    public async Task Validate_RevisionWithoutInputsFingerprint_NeverEmitsPlanOutdated()
    {
        await GenerateAsync();
        await _rig.Plan.SaveDecisionsAtomicAsync([new EditDecision
        {
            SuggestionId = S(5), Action = "defer", Reviewer = "user", TimestampUtc = DateTime.UtcNow,
        }]);
        // Revisão antiga (antes do campo existir): índice sem fingerprint de entradas.
        _rig.Db.Database.ExecuteSqlRaw("UPDATE PlanRevisions SET InputsFingerprint = NULL");

        var v = await ValidateAsync(1);

        Assert.DoesNotContain(v.Issues, i => i.Code == "PLAN_OUTDATED");
    }

    // ------------------------------------------------------------------ validação: SOURCE_FILE_MISSING

    [Fact]
    public async Task Validate_SourceFileRemovedFromDisk_IsSourceFileMissingError()
    {
        await GenerateAsync();
        var m1 = TravelFixture.Media().Single(m => m.MediaId == TravelFixture.M(1));
        _rig.Files.Missing.Add(_rig.FullPath(m1.RelativePath));

        var v = await ValidateAsync(1);

        // Um erro por clipe da mídia ausente (V1 e A1 vinculado, mais trechos), cada um com o ClipId.
        var plan = (await _rig.UseCases.GetAsync(new GetPlanRequest(ProjectId, 1))).Value!;
        var expectedClipIds = plan.Clips.Where(c => c.MediaId == m1.MediaId).Select(c => c.ClipId).OrderBy(id => id).ToList();
        var issues = v.Issues.Where(i => i.Code == "SOURCE_FILE_MISSING").ToList();
        Assert.NotEmpty(issues);
        Assert.All(issues, i => Assert.Equal("error", i.Severity));
        Assert.Equal(expectedClipIds, issues.Select(i => i.ClipId!.Value).OrderBy(id => id).ToList());
        Assert.False(v.IsValid);
    }

    [Fact]
    public async Task Generate_SourceFileMissing_ReturnsErrorNow_ButArtifactAndIndexStayStructural()
    {
        var m1 = TravelFixture.Media().Single(m => m.MediaId == TravelFixture.M(1));
        _rig.Files.Missing.Add(_rig.FullPath(m1.RelativePath));

        var plan = await GenerateAsync();

        // A resposta de geração já traz o erro derivado...
        Assert.False(plan.Validation.IsValid);
        Assert.Contains(plan.Validation.Issues, i => i.Code == "SOURCE_FILE_MISSING");
        // ...mas o JSON e o índice guardam só a validação estrutural (SOURCE_FILE_MISSING não é pegajoso).
        Assert.True(Assert.Single((await _rig.UseCases.ListAsync(ProjectId)).Value!.Revisions).IsValid);
        Assert.DoesNotContain("SOURCE_FILE_MISSING", Assert.Single(_rig.Artifacts.Files).Value);
    }

    // ------------------------------------------------------------------ idempotência da geração

    [Fact]
    public async Task Generate_AfterDecisionThatChangedNothing_ReturnsExistingRevision()
    {
        var first = await GenerateAsync();
        await _rig.Plan.SaveDecisionsAtomicAsync([new EditDecision
        {
            SuggestionId = S(8), Action = "approve", Reviewer = "user", TimestampUtc = DateTime.UtcNow,
        }]);

        var again = await GenerateAsync();

        Assert.Equal(1, again.Revision);
        Assert.Equal(first.PlanFingerprint, again.PlanFingerprint);
        Assert.Single((await _rig.UseCases.ListAsync(ProjectId)).Value!.Revisions);
    }

    // ------------------------------------------------------------------ histórico append-only

    [Fact]
    public async Task History_NeverLosesRows_AcrossSubmitUndoRedoBatch()
    {
        await Submit(1, "modify", 10.5, 11.5);
        var a = await AllDecisionIdsAsync();
        Assert.Equal(1, a.Count);

        await _rig.UseCases.UndoDecisionAsync(new UndoDecisionRequest(S(1)));
        var b = await AllDecisionIdsAsync();
        Assert.Equal(2, b.Count);
        Assert.True(a.IsSubsetOf(b));

        await _rig.UseCases.RedoDecisionAsync(new RedoDecisionRequest(S(1)));
        var c = await AllDecisionIdsAsync();
        Assert.Equal(3, c.Count);
        Assert.True(b.IsSubsetOf(c));

        await Submit(1, "reject");
        var d = await AllDecisionIdsAsync();
        Assert.Equal(4, d.Count);
        Assert.True(c.IsSubsetOf(d));

        await _rig.UseCases.BatchDecideAsync(new BatchDecideRequest([S(2)], "defer", "user", null));
        var e = await AllDecisionIdsAsync();
        Assert.Equal(5, e.Count);
        Assert.True(d.IsSubsetOf(e));

        // Nenhuma decisão anterior foi alterada: a última por versão do S1 é a rejeição.
        Assert.Equal(new[] { 1, 2, 3, 4 }, (await HistoryAsync(1)).Select(h => h.Version).ToArray());
    }
}
