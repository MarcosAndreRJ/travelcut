using System.Text.Json;
using TravelCut.Application.Planning;
using TravelCut.Contracts.Dtos;
using TravelCut.Contracts.Ipc;
using Xunit;

namespace TravelCut.UnitTests.Planning;

/// <summary>
/// Corte direto (cut.apply): primeiro corte sem plano, idempotência (requestId e intervalo equivalente), falha de gravação,
/// revisão base obsoleta, plano protegido, desfazer/refazer refletidos no plano e intervalos inválidos.
/// M1 (60 s) não tem sugestão decidida entre 20 e 23 s: o corte 20,0–21,0 s é "do usuário".
/// </summary>
public sealed class DirectCutTests : IDisposable
{
    private readonly DecisionRig _rig = new();

    public void Dispose() => _rig.Dispose();

    private static Guid ProjectId => TravelFixture.ProjectId;
    private static Guid M1 => TravelFixture.M(1);

    private Task<PlanOutcome<DirectCutResultDto>> Cut(
        double start, double end, Guid? requestId = null, int? baseRevision = null, Guid? projectId = null, Guid? mediaId = null) =>
        _rig.UseCases.ApplyDirectCutAsync(new DirectCutRequest(
            projectId ?? ProjectId, mediaId ?? M1, start, end, null, requestId, baseRevision));

    private async Task<int> SuggestionCountAsync() => (await _rig.Plan.ListSuggestionsByProjectAsync(ProjectId)).Count;
    private async Task<int> DecisionCountAsync() => (await _rig.Plan.ListDecisionsByProjectAsync(ProjectId)).Count;
    private async Task<int> RevisionCountAsync() => (await _rig.Revisions.ListAsync(ProjectId)).Count;
    private async Task<int?> LatestRevisionAsync() => (await _rig.Revisions.GetLatestAsync(ProjectId))?.Revision;

    private async Task<EditPlanDto> LatestPlanAsync() =>
        (await _rig.UseCases.GetAsync(new GetPlanRequest(ProjectId, null))).Value!;

    private async Task<EditPlanDto> GenerateAsync()
    {
        var result = await _rig.UseCases.GenerateAsync(new GeneratePlanRequest(ProjectId, null, null));
        Assert.True(result.Success, result.ErrorMessage);
        return result.Value!;
    }

    private static bool HasCut(EditPlanDto plan, Guid suggestionId) =>
        plan.AppliedCuts.Any(c => c.SuggestionId == suggestionId && c.Origin == EditPlanner.OriginApproved)
        && plan.AppliedEffects!.Any(e => e.SuggestionId == suggestionId && e.Kind == "cut" && e.Origin == "human");

    // ------------------------------------------------------------------ primeiro corte sem plano

    [Fact]
    public async Task FirstCut_WithoutPlan_GeneratesRevision_AndIsConfirmedByThePlan()
    {
        Assert.Null(await LatestRevisionAsync());
        int suggestionsBefore = await SuggestionCountAsync();

        var r = await Cut(20.0, 21.0, Guid.NewGuid());

        Assert.True(r.Success, r.ErrorMessage);
        var v = r.Value!;
        Assert.True(v.Applied);
        Assert.False(v.AlreadyApplied);
        Assert.True(v.PlanChanged);
        Assert.Equal(1, v.PlanRevision);
        Assert.Null(v.PlanError);
        Assert.Null(v.PlanSkippedReason);
        Assert.Equal(20.0, v.AppliedSourceStartSeconds!.Value, 3);
        Assert.Equal(21.0, v.AppliedSourceEndSeconds!.Value, 3);

        Assert.Equal("Approved", v.Suggestion.Status);
        Assert.Equal("cut", v.Suggestion.SuggestedAction);
        Assert.Equal("user", v.Suggestion.Author);
        Assert.Equal(M1, v.Suggestion.MediaId);
        Assert.True(v.Suggestion.CanUndo);
        Assert.Equal("human", v.Suggestion.Origin);

        Assert.Equal(suggestionsBefore + 1, await SuggestionCountAsync());
        var decision = Assert.Single((await _rig.Plan.ListDecisionsByProjectAsync(ProjectId)));
        Assert.Equal("approve", decision.Action);
        Assert.Null(decision.BaseRevision); // projeto sem plano na hora da decisão
        Assert.Equal(20_000, decision.SourceStartMs);
        Assert.Equal(21_000, decision.SourceEndMs);

        var plan = await LatestPlanAsync();
        Assert.Equal(1, plan.Revision);
        Assert.True(HasCut(plan, v.Suggestion.SuggestionId));
    }

    [Fact]
    public async Task Cut_WithExistingPlan_CreatesNewRevision_WhenBaseRevisionMatches()
    {
        var generated = await GenerateAsync();

        var r = await Cut(20.0, 21.0, Guid.NewGuid(), baseRevision: generated.Revision);

        Assert.True(r.Success, r.ErrorMessage);
        Assert.True(r.Value!.Applied);
        Assert.True(r.Value.PlanChanged);
        Assert.Equal(generated.Revision + 1, r.Value.PlanRevision);
        var decision = Assert.Single(await _rig.Plan.ListDecisionsByProjectAsync(ProjectId));
        Assert.Equal(generated.Revision, decision.BaseRevision);
        Assert.True(HasCut(await LatestPlanAsync(), r.Value.Suggestion.SuggestionId));
    }

    [Fact]
    public async Task Cut_WithoutPlan_AcceptsBaseRevisionZero()
    {
        var r = await Cut(20.0, 21.0, Guid.NewGuid(), baseRevision: 0);

        Assert.True(r.Success, r.ErrorMessage);
        Assert.True(r.Value!.Applied);
    }

    // ------------------------------------------------------------------ idempotência

    [Fact]
    public async Task Repeat_SameRequestId_ConvergesToOneSuggestionOneDecisionSameRevision()
    {
        var requestId = Guid.NewGuid();
        var first = await Cut(20.0, 21.0, requestId, baseRevision: 0);
        Assert.True(first.Value!.Applied);
        int suggestions = await SuggestionCountAsync();
        int decisions = await DecisionCountAsync();
        int revisions = await RevisionCountAsync();

        // Mesmo requestId e mesma baseRevision (0, agora obsoleta): a repetição converge, não falha.
        var again = await Cut(20.0, 21.0, requestId, baseRevision: 0);

        Assert.True(again.Success, again.ErrorMessage);
        Assert.True(again.Value!.Applied);
        Assert.True(again.Value.AlreadyApplied);
        Assert.False(again.Value.PlanChanged);
        Assert.Equal(first.Value.Suggestion.SuggestionId, again.Value.Suggestion.SuggestionId);
        Assert.Equal(first.Value.PlanRevision, again.Value.PlanRevision);
        Assert.Equal(suggestions, await SuggestionCountAsync());
        Assert.Equal(decisions, await DecisionCountAsync());
        Assert.Equal(revisions, await RevisionCountAsync());
    }

    [Fact]
    public async Task Repeat_EquivalentIntervalWithinTolerance_WithNewRequestId_Converges()
    {
        var first = await Cut(20.0, 21.0, Guid.NewGuid());
        int suggestions = await SuggestionCountAsync();
        int decisions = await DecisionCountAsync();

        var again = await Cut(20.05, 21.06, Guid.NewGuid());

        Assert.True(again.Success, again.ErrorMessage);
        Assert.True(again.Value!.Applied);
        Assert.True(again.Value.AlreadyApplied);
        Assert.Equal(first.Value!.Suggestion.SuggestionId, again.Value.Suggestion.SuggestionId);
        Assert.Equal(suggestions, await SuggestionCountAsync());
        Assert.Equal(decisions, await DecisionCountAsync());
        Assert.Equal(1, await RevisionCountAsync());
    }

    [Fact]
    public async Task Repeat_AfterUndo_WithSameRequestId_DoesNotReapplyAnUndoneCut()
    {
        var requestId = Guid.NewGuid();
        var first = await Cut(20.0, 21.0, requestId);
        var undo = await _rig.UseCases.UndoDecisionAsync(new UndoDecisionRequest(first.Value!.Suggestion.SuggestionId));
        Assert.True(undo.Success, undo.ErrorMessage);
        int decisions = await DecisionCountAsync();
        int revisions = await RevisionCountAsync();

        var again = await Cut(20.0, 21.0, requestId);

        Assert.True(again.Success, again.ErrorMessage);
        Assert.False(again.Value!.Applied);
        Assert.Equal(decisions, await DecisionCountAsync());
        Assert.Equal(revisions, await RevisionCountAsync());
        Assert.False(HasCut(await LatestPlanAsync(), first.Value.Suggestion.SuggestionId));
    }

    [Fact]
    public async Task DifferentIntervals_CreateDistinctSuggestions()
    {
        var a = await Cut(20.0, 21.0, Guid.NewGuid());
        var b = await Cut(23.0, 24.0, Guid.NewGuid());

        Assert.True(a.Value!.Applied);
        Assert.True(b.Value!.Applied);
        Assert.NotEqual(a.Value.Suggestion.SuggestionId, b.Value.Suggestion.SuggestionId);
        Assert.Equal(2, await RevisionCountAsync());
        var plan = await LatestPlanAsync();
        Assert.True(HasCut(plan, a.Value.Suggestion.SuggestionId));
        Assert.True(HasCut(plan, b.Value.Suggestion.SuggestionId));
    }

    // ------------------------------------------------------------------ falha de gravação

    [Fact]
    public async Task WriteFailure_KeepsDecision_ReportsNotApplied_AndRetryWithSameRequestIdRecovers()
    {
        var requestId = Guid.NewGuid();
        _rig.Artifacts.WriteFailure = new IOException("disco cheio");

        var failed = await Cut(20.0, 21.0, requestId);

        Assert.True(failed.Success, failed.ErrorMessage);
        Assert.False(failed.Value!.Applied);
        Assert.False(failed.Value.AlreadyApplied);
        Assert.Equal(PlanningUseCases.PlanRegenerationFailedCode, failed.Value.PlanError);
        Assert.Null(failed.Value.PlanSkippedReason);
        Assert.Null(failed.Value.AppliedSourceStartSeconds);
        Assert.Equal("Approved", failed.Value.Suggestion.Status); // decisão gravada, estado parcial exposto
        Assert.Equal(1, await DecisionCountAsync());
        Assert.Null(await LatestRevisionAsync());

        // Recuperação: o disco voltou; a mesma requisição reaproveita a sugestão/decisão e materializa o corte.
        _rig.Artifacts.WriteFailure = null;
        var retry = await Cut(20.0, 21.0, requestId);

        Assert.True(retry.Success, retry.ErrorMessage);
        Assert.True(retry.Value!.Applied);
        Assert.Equal(failed.Value.Suggestion.SuggestionId, retry.Value.Suggestion.SuggestionId);
        Assert.Equal(1, await DecisionCountAsync());
        Assert.Equal(1, await RevisionCountAsync());
        Assert.True(HasCut(await LatestPlanAsync(), retry.Value.Suggestion.SuggestionId));
    }

    // ------------------------------------------------------------------ revisão base obsoleta

    [Fact]
    public async Task StaleBaseRevision_WithPlan_IsRefused_AndNothingIsWritten()
    {
        var generated = await GenerateAsync();
        int suggestions = await SuggestionCountAsync();

        var r = await Cut(20.0, 21.0, Guid.NewGuid(), baseRevision: generated.Revision + 5);

        Assert.False(r.Success);
        Assert.Equal(PlanningUseCases.PlanStaleBaseCode, r.ErrorCode);
        Assert.Equal(suggestions, await SuggestionCountAsync());
        Assert.Equal(0, await DecisionCountAsync());
        Assert.Equal(generated.Revision, await LatestRevisionAsync());
    }

    [Fact]
    public async Task StaleBaseRevision_UiThinksThereIsNoPlan_ButOneExists_IsRefused()
    {
        await GenerateAsync();

        var r = await Cut(20.0, 21.0, Guid.NewGuid(), baseRevision: 0);

        Assert.Equal(PlanningUseCases.PlanStaleBaseCode, r.ErrorCode);
        Assert.Equal(0, await DecisionCountAsync());
    }

    [Fact]
    public async Task StaleBaseRevision_ClaimsPlanButProjectHasNone_IsRefused()
    {
        int suggestions = await SuggestionCountAsync();

        var r = await Cut(20.0, 21.0, Guid.NewGuid(), baseRevision: 3);

        Assert.Equal(PlanningUseCases.PlanStaleBaseCode, r.ErrorCode);
        Assert.Equal(suggestions, await SuggestionCountAsync());
        Assert.Null(await LatestRevisionAsync());
    }

    // ------------------------------------------------------------------ plano protegido

    private async Task<EditPlanDto> GenerateThenSaveManualAsync()
    {
        var generated = await GenerateAsync();
        var saved = await _rig.UseCases.SaveManualRevisionAsync(
            new SavePlanRevisionRequest(ProjectId, generated.Revision, ManualPlanEdits.DropLastPair(generated), Author: "user"));
        Assert.True(saved.Success, saved.ErrorCode + " " + saved.ErrorMessage);
        return saved.Value!;
    }

    [Theory]
    [InlineData("corrupt", PlanningUseCases.PlanManualEditsUnreadableCode)]
    [InlineData("tampered", PlanningUseCases.PlanManualProtectedCode)]
    public async Task ProtectedManualPlan_KeepsDecision_NotApplied_WithSkippedReason(string breakage, string expectedReason)
    {
        var manual = await GenerateThenSaveManualAsync();
        if (breakage == "corrupt")
        {
            _rig.Artifacts.Replace(manual.PlanId, manual.Revision, "{ json quebrado");
        }
        else
        {
            var tampered = manual with { ManualEdits = manual.ManualEdits!.Select(e => e with { SourceOutUs = e.SourceOutUs + 1 }).ToList() };
            _rig.Artifacts.Replace(manual.PlanId, manual.Revision, PlanJson.Serialize(tampered));
        }
        int revisionsBefore = await RevisionCountAsync();

        var r = await Cut(20.0, 21.0, Guid.NewGuid(), baseRevision: manual.Revision);

        Assert.True(r.Success, r.ErrorMessage);
        Assert.False(r.Value!.Applied);
        Assert.False(r.Value.PlanChanged);
        Assert.Null(r.Value.PlanError);
        Assert.Equal(expectedReason, r.Value.PlanSkippedReason);
        Assert.Equal("Approved", r.Value.Suggestion.Status);
        Assert.Equal(1, await DecisionCountAsync());
        Assert.Equal(revisionsBefore, await RevisionCountAsync());
        Assert.Equal(manual.Revision, await LatestRevisionAsync());
    }

    // ------------------------------------------------------------------ desfazer / refazer

    [Fact]
    public async Task UndoAndRedo_AreReflectedInThePlan()
    {
        var cut = await Cut(20.0, 21.0, Guid.NewGuid());
        var id = cut.Value!.Suggestion.SuggestionId;
        Assert.True(HasCut(await LatestPlanAsync(), id));

        var undo = await _rig.UseCases.UndoDecisionAsync(new UndoDecisionRequest(id));
        Assert.True(undo.Success, undo.ErrorMessage);
        Assert.True(undo.Value!.PlanChanged);
        var afterUndo = await LatestPlanAsync();
        Assert.DoesNotContain(afterUndo.AppliedCuts, c => c.SuggestionId == id);
        Assert.DoesNotContain(afterUndo.AppliedEffects!, e => e.SuggestionId == id);
        Assert.Equal(2, afterUndo.Revision);

        var redo = await _rig.UseCases.RedoDecisionAsync(new RedoDecisionRequest(id));
        Assert.True(redo.Success, redo.ErrorMessage);
        Assert.True(redo.Value!.PlanChanged);
        var afterRedo = await LatestPlanAsync();
        Assert.True(HasCut(afterRedo, id));
        Assert.Equal(3, afterRedo.Revision);
    }

    [Fact]
    public async Task Cut_AfterUndo_WithSameInterval_ReusesSuggestion_AndReappliesWithoutDuplicates()
    {
        var first = await Cut(20.0, 21.0, Guid.NewGuid());
        var id = first.Value!.Suggestion.SuggestionId;
        await _rig.UseCases.UndoDecisionAsync(new UndoDecisionRequest(id));
        int suggestions = await SuggestionCountAsync();

        var again = await Cut(20.0, 21.0, Guid.NewGuid());

        Assert.True(again.Success, again.ErrorMessage);
        Assert.True(again.Value!.Applied);
        Assert.False(again.Value.AlreadyApplied);
        Assert.Equal(id, again.Value.Suggestion.SuggestionId);
        Assert.Equal(suggestions, await SuggestionCountAsync());
        Assert.True(HasCut(await LatestPlanAsync(), id));
    }

    // ------------------------------------------------------------------ validação

    [Theory]
    [InlineData(21.0, 20.0)]   // invertido
    [InlineData(20.0, 20.0)]   // vazio
    [InlineData(58.0, 61.0)]   // fim além da duração (60 s)
    [InlineData(-1.0, 2.0)]    // negativo
    [InlineData(20.0, 20.005)] // menor que 1 quadro
    [InlineData(double.NaN, 21.0)]
    public async Task InvalidInterval_IsRefused_AndNothingIsWritten(double start, double end)
    {
        int suggestions = await SuggestionCountAsync();

        var r = await Cut(start, end, Guid.NewGuid());

        Assert.False(r.Success);
        Assert.Equal("INVALID_INTERVAL", r.ErrorCode);
        Assert.Equal(suggestions, await SuggestionCountAsync());
        Assert.Equal(0, await DecisionCountAsync());
        Assert.Null(await LatestRevisionAsync());
    }

    [Fact]
    public async Task UnknownMediaOrProject_IsRefused()
    {
        var unknownMedia = await Cut(20.0, 21.0, mediaId: Guid.NewGuid());
        Assert.Equal("MEDIA_NOT_FOUND", unknownMedia.ErrorCode);

        var emptyProject = await Cut(20.0, 21.0, projectId: Guid.Empty);
        Assert.Equal("INVALID_PROJECT", emptyProject.ErrorCode);

        var emptyMedia = await Cut(20.0, 21.0, mediaId: Guid.Empty);
        Assert.Equal("INVALID_MEDIA", emptyMedia.ErrorCode);

        var otherProject = await Cut(20.0, 21.0, projectId: Guid.NewGuid());
        Assert.Equal("MEDIA_NOT_FOUND", otherProject.ErrorCode);
    }

    // ------------------------------------------------------------------ IPC

    [Fact]
    public async Task Dispatcher_CutApply_UsesDirectCutResultShape_AndFailsWithStableCode()
    {
        var ok = await _rig.Dispatcher.DispatchAsync(new IpcRequest
        {
            RequestId = "c1",
            Type = "cut.apply",
            Payload = new DirectCutRequest(ProjectId, M1, 20.0, 21.0, null, Guid.NewGuid(), 0),
        });

        Assert.True(ok.Ok, ok.Error?.Message);
        var json = JsonSerializer.SerializeToElement(ok.Payload);
        Assert.True(json.GetProperty("applied").GetBoolean());
        Assert.False(json.GetProperty("alreadyApplied").GetBoolean());
        Assert.Equal(1, json.GetProperty("planRevision").GetInt32());
        Assert.True(json.GetProperty("planChanged").GetBoolean());
        Assert.Equal("Approved", json.GetProperty("suggestion").GetProperty("status").GetString());
        Assert.Equal(20.0, json.GetProperty("appliedSourceStartSeconds").GetDouble(), 3);

        var stale = await _rig.Dispatcher.DispatchAsync(new IpcRequest
        {
            RequestId = "c2",
            Type = "cut.apply",
            Payload = new DirectCutRequest(ProjectId, M1, 30.0, 31.0, null, Guid.NewGuid(), 0),
        });

        Assert.False(stale.Ok);
        Assert.Equal("PLAN_STALE_BASE", stale.Error!.Code);
    }
}
