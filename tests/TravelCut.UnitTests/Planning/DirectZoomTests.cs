using System.Text.Json;
using TravelCut.Application.Planning;
using TravelCut.Contracts.Dtos;
using TravelCut.Contracts.Ipc;
using Xunit;

namespace TravelCut.UnitTests.Planning;

/// <summary>
/// Zoom direto por intervalo (zoom.apply): janela na fonte + rampas no plano, idempotência, coexistência com cortes, remoção por
/// undo/reject preservando cortes e outros zooms, um zoom por trecho mantido, validação de parâmetros e overlay manual.
/// M1 (60 s). Cortes do usuário em 20–21 s dividem M1 em [..,20) e [21,..).
/// </summary>
public sealed class DirectZoomTests : IDisposable
{
    private readonly DecisionRig _rig = new();

    public void Dispose() => _rig.Dispose();

    private static Guid ProjectId => TravelFixture.ProjectId;
    private static Guid M1 => TravelFixture.M(1);

    private Task<PlanOutcome<DirectZoomResultDto>> Zoom(
        double start, double end, double scale = 1.08, double fx = 0.4, double fy = 0.6, double tin = 0.5, double tout = 0.5,
        Guid? requestId = null, int? baseRevision = null) =>
        _rig.UseCases.ApplyDirectZoomAsync(new DirectZoomRequest(
            ProjectId, M1, start, end, scale, fx, fy, tin, tout, null, requestId, baseRevision));

    private Task<PlanOutcome<DirectCutResultDto>> Cut(double start, double end) =>
        _rig.UseCases.ApplyDirectCutAsync(new DirectCutRequest(ProjectId, M1, start, end, null, Guid.NewGuid(), null));

    private async Task<EditPlanDto> LatestPlanAsync() =>
        (await _rig.UseCases.GetAsync(new GetPlanRequest(ProjectId, null))).Value!;

    private async Task<int> DecisionCountAsync() => (await _rig.Plan.ListDecisionsByProjectAsync(ProjectId)).Count;
    private async Task<int> RevisionCountAsync() => (await _rig.Revisions.ListAsync(ProjectId)).Count;

    private static List<PlanClipDto> ZoomedClips(EditPlanDto plan) =>
        plan.Clips.Where(c => c.TrackId == "V1" && c.Zoom is not null).OrderBy(c => c.SourceInUs).ToList();

    private static bool HasCut(EditPlanDto plan, Guid id) =>
        plan.AppliedCuts.Any(c => c.SuggestionId == id && c.Origin == EditPlanner.OriginApproved);

    // ------------------------------------------------------------------ aplicação

    [Fact]
    public async Task FirstZoom_WithoutPlan_GeneratesPlan_WithWindowRampsAndEffect()
    {
        var r = await Zoom(14.0, 18.0, requestId: Guid.NewGuid(), baseRevision: 0);

        Assert.True(r.Success, r.ErrorMessage);
        var v = r.Value!;
        Assert.True(v.Applied);
        Assert.False(v.AlreadyApplied);
        Assert.True(v.PlanChanged);
        Assert.Null(v.PlanError);
        Assert.Equal("Approved", v.Suggestion.Status);
        Assert.Equal("zoom", v.Suggestion.SuggestedAction);
        Assert.Equal("human", v.Suggestion.Origin);
        Assert.Equal(1.08, v.Zoom!.Scale);
        Assert.Equal(0.4, v.Zoom.FocalPointX);
        Assert.Equal(0.6, v.Zoom.FocalPointY);
        Assert.Equal(0.5, v.Zoom.TransitionInSeconds);
        Assert.Equal(0.5, v.Zoom.TransitionOutSeconds);
        Assert.Equal(14.0, v.Zoom.SourceStartSeconds, 3);
        Assert.Equal(18.0, v.Zoom.SourceEndSeconds, 3);

        var plan = await LatestPlanAsync();
        var clip = Assert.Single(ZoomedClips(plan));
        Assert.Equal(1.08, clip.Zoom);
        Assert.Equal(14_000_000, clip.ZoomSourceInUs);
        Assert.Equal(18_000_000, clip.ZoomSourceOutUs);
        Assert.Equal(0.5, clip.ZoomTransitionInSeconds);
        Assert.Equal(0.5, clip.ZoomTransitionOutSeconds);
        Assert.Equal(0.4, clip.ZoomFocalPointX);
        Assert.Equal(4.0, clip.ZoomDurationSeconds!.Value, 3);
        Assert.True(clip.SourceInUs <= 14_000_000 && clip.SourceOutUs >= 18_000_000);

        var fx = Assert.Single(plan.AppliedEffects!, e => e.Kind == "zoom");
        Assert.Equal("human", fx.Origin);
        Assert.Equal(v.Suggestion.SuggestionId, fx.SuggestionId);
        Assert.Equal(1.08, fx.Scale);
        Assert.Equal(14_000_000, fx.SourceInUs);
        Assert.Equal(18_000_000, fx.SourceOutUs);
        Assert.Equal(0.5, fx.TransitionInSeconds);
        // A cobertura mínima de 0,5 do zoom de clipe inteiro não se aplica: 4 s de um trecho de ~50 s.
    }

    [Fact]
    public async Task Repeat_SameRequestId_And_SameParameters_Converge()
    {
        var requestId = Guid.NewGuid();
        var first = await Zoom(14.0, 18.0, requestId: requestId);
        int decisions = await DecisionCountAsync();
        int revisions = await RevisionCountAsync();

        var again = await Zoom(14.0, 18.0, requestId: requestId);
        Assert.True(again.Success, again.ErrorMessage);
        Assert.True(again.Value!.Applied);
        Assert.True(again.Value.AlreadyApplied);
        Assert.False(again.Value.PlanChanged);
        Assert.Equal(first.Value!.Suggestion.SuggestionId, again.Value.Suggestion.SuggestionId);

        var equivalent = await Zoom(14.04, 18.05);
        Assert.True(equivalent.Value!.AlreadyApplied);
        Assert.Equal(first.Value.Suggestion.SuggestionId, equivalent.Value.Suggestion.SuggestionId);
        Assert.Equal(decisions, await DecisionCountAsync());
        Assert.Equal(revisions, await RevisionCountAsync());
    }

    [Fact]
    public async Task SameInterval_DifferentScale_EditsTheZoom_NewRevision_UndoRestoresPrevious()
    {
        var first = await Zoom(14.0, 18.0, scale: 1.05);
        var fp1 = (await LatestPlanAsync()).PlanFingerprint;

        var edited = await Zoom(14.0, 18.0, scale: 1.10);
        Assert.True(edited.Value!.Applied);
        Assert.False(edited.Value.AlreadyApplied);
        Assert.True(edited.Value.PlanChanged);
        Assert.Equal(first.Value!.Suggestion.SuggestionId, edited.Value.Suggestion.SuggestionId);
        var plan2 = await LatestPlanAsync();
        Assert.Equal(1.10, Assert.Single(ZoomedClips(plan2)).Zoom);
        Assert.NotEqual(fp1, plan2.PlanFingerprint);

        var undo = await _rig.UseCases.UndoDecisionAsync(new UndoDecisionRequest(first.Value.Suggestion.SuggestionId));
        Assert.True(undo.Success, undo.ErrorMessage);
        Assert.Equal(1.05, Assert.Single(ZoomedClips(await LatestPlanAsync())).Zoom);
    }

    [Fact]
    public async Task WindowIsClippedToTheLargestKeptSegment_AndCutsAreKept()
    {
        var cut = await Cut(20.0, 21.0);
        Assert.True(cut.Value!.Applied);

        // 19–23 s: 1 s antes do corte e 2 s depois; o trecho depois do corte tem maior sobreposição.
        var r = await Zoom(19.0, 23.0);

        Assert.True(r.Value!.Applied, r.Value.PlanError);
        var plan = await LatestPlanAsync();
        Assert.True(HasCut(plan, cut.Value.Suggestion.SuggestionId));
        var clip = Assert.Single(ZoomedClips(plan));
        Assert.Equal(21_000_000, clip.ZoomSourceInUs);
        Assert.Equal(23_000_000, clip.ZoomSourceOutUs);
        Assert.Equal(21.0, r.Value.Zoom!.SourceStartSeconds, 3);
        Assert.Equal(2.0, clip.ZoomDurationSeconds!.Value, 3);
    }

    // ------------------------------------------------------------------ remover / desfazer / rejeitar

    [Fact]
    public async Task UndoZoom_RemovesOnlyTheZoom_KeepsCuts_AndRedoBringsItBack()
    {
        var cut = await Cut(20.0, 21.0);
        var zoom = await Zoom(14.0, 18.0);
        Assert.True(zoom.Value!.Applied);
        var id = zoom.Value.Suggestion.SuggestionId;

        var undo = await _rig.UseCases.UndoDecisionAsync(new UndoDecisionRequest(id));
        Assert.True(undo.Success, undo.ErrorMessage);
        Assert.True(undo.Value!.PlanChanged);
        var afterUndo = await LatestPlanAsync();
        Assert.Empty(ZoomedClips(afterUndo));
        Assert.DoesNotContain(afterUndo.AppliedEffects!, e => e.Kind == "zoom" && e.SuggestionId == id);
        Assert.True(HasCut(afterUndo, cut.Value!.Suggestion.SuggestionId));
        Assert.All(afterUndo.Clips, c => Assert.Null(c.ZoomSourceInUs));

        var redo = await _rig.UseCases.RedoDecisionAsync(new RedoDecisionRequest(id));
        Assert.True(redo.Success, redo.ErrorMessage);
        var afterRedo = await LatestPlanAsync();
        var clip = Assert.Single(ZoomedClips(afterRedo));
        Assert.Equal(14_000_000, clip.ZoomSourceInUs);
        Assert.True(HasCut(afterRedo, cut.Value.Suggestion.SuggestionId));
    }

    [Fact]
    public async Task RejectZoom_RemovesZoom_KeepsOtherZoomsAndCuts()
    {
        var cut = await Cut(20.0, 21.0);
        var early = await Zoom(5.0, 8.0);     // trecho antes do corte
        var late = await Zoom(24.0, 27.0);    // trecho depois do corte
        Assert.True(early.Value!.Applied);
        Assert.True(late.Value!.Applied);
        Assert.Equal(2, ZoomedClips(await LatestPlanAsync()).Count);

        var reject = await _rig.UseCases.SubmitDecisionAsync(
            new SubmitDecisionRequest(early.Value.Suggestion.SuggestionId, "reject", "user", null));
        Assert.True(reject.Success, reject.ErrorMessage);

        var plan = await LatestPlanAsync();
        var remaining = Assert.Single(ZoomedClips(plan));
        Assert.Equal(24_000_000, remaining.ZoomSourceInUs);
        Assert.True(HasCut(plan, cut.Value!.Suggestion.SuggestionId));
    }

    [Fact]
    public async Task RejectingTheFirstOfTwoCuts_RestoresIt_KeepsSecondCutAndZoom_AndRegenerationConverges()
    {
        var cut1 = await Cut(20.0, 21.0);
        var cut2 = await Cut(25.0, 26.0);
        var zoom = await Zoom(14.0, 18.0);
        Assert.True(cut1.Value!.Applied && cut2.Value!.Applied && zoom.Value!.Applied);

        var reject = await _rig.UseCases.SubmitDecisionAsync(
            new SubmitDecisionRequest(cut1.Value.Suggestion.SuggestionId, "reject", "user", null));
        Assert.True(reject.Success, reject.ErrorMessage);
        Assert.True(reject.Value!.PlanChanged);

        var plan = await LatestPlanAsync();
        Assert.False(HasCut(plan, cut1.Value.Suggestion.SuggestionId));
        Assert.True(HasCut(plan, cut2.Value!.Suggestion.SuggestionId));
        Assert.Contains(plan.AppliedEffects!, e => e.Kind == "zoom" && e.SuggestionId == zoom.Value.Suggestion.SuggestionId);
        Assert.Single(ZoomedClips(plan));
        // O trecho 20–21 voltou: algum clipe V1 de M1 o cobre.
        Assert.Contains(plan.Clips, c => c.TrackId == "V1" && c.MediaId == M1 && c.SourceInUs <= 20_000_000 && c.SourceOutUs >= 21_000_000);

        // "Reabrir": regerar a partir só de SQLite + artefatos converge para o mesmo conteúdo.
        int revisions = await RevisionCountAsync();
        var regenerated = await _rig.UseCases.GenerateAsync(new GeneratePlanRequest(ProjectId, null, null));
        Assert.True(regenerated.Success, regenerated.ErrorMessage);
        Assert.Equal(plan.PlanFingerprint, regenerated.Value!.PlanFingerprint);
        Assert.Equal(revisions, await RevisionCountAsync());
    }

    // ------------------------------------------------------------------ um zoom por trecho mantido

    [Fact]
    public async Task SecondZoomInTheSameKeptSegment_IsRefused_BeforeWritingAnything()
    {
        var first = await Zoom(14.0, 18.0);
        Assert.True(first.Value!.Applied);
        int decisions = await DecisionCountAsync();
        int revisions = await RevisionCountAsync();

        var second = await Zoom(24.0, 27.0);

        Assert.False(second.Success);
        Assert.Equal(PlanningUseCases.ZoomClipOccupiedCode, second.ErrorCode);
        Assert.Equal(decisions, await DecisionCountAsync());
        Assert.Equal(revisions, await RevisionCountAsync());
        Assert.Single(ZoomedClips(await LatestPlanAsync()));
    }

    [Fact]
    public async Task ZoomsOnDifferentKeptSegments_Coexist()
    {
        await Cut(20.0, 21.0);
        var a = await Zoom(5.0, 8.0, scale: 1.05);
        var b = await Zoom(24.0, 27.0, scale: 1.10);

        Assert.True(a.Value!.Applied);
        Assert.True(b.Value!.Applied);
        var zoomed = ZoomedClips(await LatestPlanAsync());
        Assert.Equal(2, zoomed.Count);
        Assert.Equal(1.05, zoomed[0].Zoom);
        Assert.Equal(1.10, zoomed[1].Zoom);
    }

    // ------------------------------------------------------------------ validação

    [Theory]
    [InlineData(1.20, 0.5, 0.5, 0.4, 0.6)]        // escala > 1,12: recusada (sem clamp)
    [InlineData(1.00, 0.5, 0.5, 0.4, 0.6)]        // escala < 1,01
    [InlineData(1.05, 2.5, 2.0, 0.4, 0.6)]        // rampas somam 4,5 s > 4 s
    [InlineData(1.05, -0.1, 0.5, 0.4, 0.6)]       // rampa negativa
    [InlineData(1.05, double.NaN, 0.5, 0.4, 0.6)] // rampa não finita
    [InlineData(1.05, 0.5, 0.5, 1.4, 0.6)]        // foco fora de 0–1
    [InlineData(double.NaN, 0.5, 0.5, 0.4, 0.6)]  // escala não finita
    public async Task InvalidParameters_AreRefused_WithInvalidArgument_AndNothingIsWritten(
        double scale, double tin, double tout, double fx, double fy)
    {
        var r = await Zoom(14.0, 18.0, scale, fx, fy, tin, tout);

        Assert.False(r.Success);
        Assert.Equal("INVALID_ARGUMENT", r.ErrorCode);
        Assert.Equal(0, await DecisionCountAsync());
        Assert.Equal(0, await RevisionCountAsync());
    }

    [Fact]
    public async Task InvalidInterval_StaleBase_AndUnknownMedia_AreRefused()
    {
        Assert.Equal("INVALID_INTERVAL", (await Zoom(34.0, 30.0)).ErrorCode);
        Assert.Equal(PlanningUseCases.PlanStaleBaseCode, (await Zoom(14.0, 18.0, baseRevision: 3)).ErrorCode);
        var other = await _rig.UseCases.ApplyDirectZoomAsync(new DirectZoomRequest(ProjectId, Guid.NewGuid(), 30, 34, 1.05));
        Assert.Equal("MEDIA_NOT_FOUND", other.ErrorCode);
        Assert.Equal(0, await DecisionCountAsync());
    }

    [Fact]
    public void DecisionParameters_IntervalScope_RejectsAboveCap_ButLegacyScopeStillClamps()
    {
        var interval = new SmartZoomParametersDto("punch_in", 1.5, 0.5, 0.5, 4, 1.0, 1.5, null, true, null, 0.5, 0.5, "interval");
        Assert.NotNull(DecisionParameters.Normalize(new DecisionParametersDto(interval), "zoom", 4).Error);

        var legacy = new SmartZoomParametersDto("punch_in", 1.5, 0.5, 0.5, 4, 1.0, 1.5, null, true);
        var (json, error) = DecisionParameters.Normalize(new DecisionParametersDto(legacy), "zoom");
        Assert.Null(error);
        var parsed = DecisionParameters.Parse(json)!.Zoom!;
        Assert.Equal(1.12, parsed.Scale);
        Assert.Null(parsed.Scope);
        Assert.DoesNotContain("transitionInSeconds", json);

        var ramps = new SmartZoomParametersDto("punch_in", 1.05, 0.5, 0.5, 0, 1.0, 1.05, null, true, null, 0.5, 0.5, null);
        Assert.NotNull(DecisionParameters.Normalize(new DecisionParametersDto(ramps), "zoom").Error); // rampas só com scope interval
    }

    [Fact]
    public async Task Modify_ShrinkingTheInterval_BelowTheInheritedRamps_IsRefused()
    {
        var zoom = await Zoom(14.0, 18.0, tin: 1.5, tout: 1.5);
        Assert.True(zoom.Value!.Applied);

        var modify = await _rig.UseCases.SubmitDecisionAsync(new SubmitDecisionRequest(
            zoom.Value.Suggestion.SuggestionId, "modify", "user", null, 30.0, 32.0));

        Assert.False(modify.Success);
        Assert.Equal("INVALID_ARGUMENT", modify.ErrorCode);
    }

    // ------------------------------------------------------------------ plano: validação, fingerprint, overlay manual

    [Fact]
    public async Task Validator_RejectsInconsistentWindowOrRamps()
    {
        await Zoom(14.0, 18.0);
        var plan = await LatestPlanAsync();
        var domain = PlanMapping.ToDomain(plan);
        var facts = TravelFixture.Media().ToDictionary(m => m.MediaId, m => new TravelCut.Domain.Planning.MediaFacts(
            m.MediaId, TravelCut.Application.Planning.PlanEligibility.DurationUs(m), "", m.RelativePath));
        var speech = facts.Keys.ToDictionary(k => k, _ => (IReadOnlyList<(long StartUs, long EndUs)>)Array.Empty<(long, long)>());

        var bad = domain with
        {
            Clips = domain.Clips.Select(c => c.Zoom is null ? c : c with { ZoomSourceOutUs = c.SourceOutUs + 5, ZoomTransitionInSeconds = 100 }).ToList(),
        };
        var issues = TravelCut.Domain.Planning.PlanValidator.Validate(bad, facts, speech, domain.MediaSetFingerprint);
        Assert.Contains(issues, i => i.Code == "ZOOM_WINDOW_INVALID");

        var good = TravelCut.Domain.Planning.PlanValidator.Validate(domain, facts, speech, domain.MediaSetFingerprint);
        Assert.DoesNotContain(good, i => i.Code == "ZOOM_WINDOW_INVALID");
    }

    [Fact]
    public async Task ManualRevision_KeepsTheIntervalZoom()
    {
        await Cut(20.0, 21.0);
        await Zoom(5.0, 8.0);
        var generated = await LatestPlanAsync();
        var lastStart = generated.Clips.Max(c => c.TimelineStartFrame);
        Assert.DoesNotContain(generated.Clips, c => c.TimelineStartFrame == lastStart && c.Zoom is not null); // zoom não está no último par

        var saved = await _rig.UseCases.SaveManualRevisionAsync(
            new SavePlanRevisionRequest(ProjectId, generated.Revision, ManualPlanEdits.DropLastPair(generated), Author: "user"));
        Assert.True(saved.Success, saved.ErrorCode + " " + saved.ErrorMessage);

        var clip = Assert.Single(ZoomedClips(saved.Value!));
        Assert.Equal(5_000_000, clip.ZoomSourceInUs);
        Assert.Equal(8_000_000, clip.ZoomSourceOutUs);
        Assert.Equal(0.5, clip.ZoomTransitionInSeconds);
        Assert.Contains(saved.Value!.AppliedEffects!, e => e.Kind == "zoom" && e.SourceInUs == 5_000_000);
    }

    // ------------------------------------------------------------------ suggestions.list / IPC

    [Fact]
    public async Task SuggestionsList_ReturnsEffectiveZoomParameters_AndStatus_AfterApplyAndUndo()
    {
        var zoom = await Zoom(14.0, 18.0, scale: 1.07, fx: 0.3, fy: 0.7, tin: 0.4, tout: 0.6);
        var id = zoom.Value!.Suggestion.SuggestionId;

        JsonElement Find()
        {
            var response = _rig.Dispatcher.DispatchAsync(new IpcRequest
            {
                RequestId = "l1", Type = "suggestions.list", Payload = new { projectId = ProjectId },
            }).GetAwaiter().GetResult();
            Assert.True(response.Ok, response.Error?.Message);
            var json = JsonSerializer.SerializeToElement(response.Payload);
            return json.GetProperty("suggestions").EnumerateArray().First(s => s.GetProperty("suggestionId").GetGuid() == id);
        }

        var applied = Find();
        Assert.Equal("Approved", applied.GetProperty("status").GetString());
        Assert.True(applied.GetProperty("canUndo").GetBoolean());
        var p = applied.GetProperty("parameters").GetProperty("zoom");
        Assert.Equal(1.07, p.GetProperty("scale").GetDouble());
        Assert.Equal(0.3, p.GetProperty("focalPointX").GetDouble());
        Assert.Equal(0.7, p.GetProperty("focalPointY").GetDouble());
        Assert.Equal(0.4, p.GetProperty("transitionInSeconds").GetDouble());
        Assert.Equal(0.6, p.GetProperty("transitionOutSeconds").GetDouble());
        Assert.Equal("interval", p.GetProperty("scope").GetString());
        Assert.Equal(14.0, applied.GetProperty("startSeconds").GetDouble(), 3);
        Assert.Equal(18.0, applied.GetProperty("endSeconds").GetDouble(), 3);

        await _rig.UseCases.UndoDecisionAsync(new UndoDecisionRequest(id));
        var undone = Find();
        Assert.Equal("Proposed", undone.GetProperty("status").GetString());
        Assert.False(undone.GetProperty("canUndo").GetBoolean());
    }

    [Fact]
    public async Task Dispatcher_ZoomApply_UsesDirectZoomResultShape()
    {
        var ok = await _rig.Dispatcher.DispatchAsync(new IpcRequest
        {
            RequestId = "z1",
            Type = "zoom.apply",
            Payload = new DirectZoomRequest(ProjectId, M1, 14.0, 18.0, 1.08, 0.4, 0.6, 0.5, 0.5, null, Guid.NewGuid(), 0),
        });

        Assert.True(ok.Ok, ok.Error?.Message);
        var json = JsonSerializer.SerializeToElement(ok.Payload);
        Assert.True(json.GetProperty("applied").GetBoolean());
        Assert.Equal(1, json.GetProperty("planRevision").GetInt32());
        var zoom = json.GetProperty("zoom");
        Assert.Equal(1.08, zoom.GetProperty("scale").GetDouble());
        Assert.Equal(14.0, zoom.GetProperty("sourceStartSeconds").GetDouble(), 3);
        Assert.Equal(18.0, zoom.GetProperty("sourceEndSeconds").GetDouble(), 3);

        var bad = await _rig.Dispatcher.DispatchAsync(new IpcRequest
        {
            RequestId = "z2", Type = "zoom.apply",
            Payload = new DirectZoomRequest(ProjectId, M1, 40.0, 44.0, 1.5),
        });
        Assert.False(bad.Ok);
        Assert.Equal("INVALID_ARGUMENT", bad.Error!.Code);
    }

    [Fact]
    public async Task WriteFailure_KeepsDecision_ReportsNotApplied_AndRetryRecovers()
    {
        var requestId = Guid.NewGuid();
        _rig.Artifacts.WriteFailure = new IOException("disco cheio");

        var failed = await Zoom(14.0, 18.0, requestId: requestId);

        Assert.True(failed.Success, failed.ErrorMessage);
        Assert.False(failed.Value!.Applied);
        Assert.Null(failed.Value.Zoom);
        Assert.Equal(PlanningUseCases.PlanRegenerationFailedCode, failed.Value.PlanError);
        Assert.Equal("Approved", failed.Value.Suggestion.Status);

        _rig.Artifacts.WriteFailure = null;
        var retry = await Zoom(14.0, 18.0, requestId: requestId);
        Assert.True(retry.Value!.Applied);
        Assert.Equal(failed.Value.Suggestion.SuggestionId, retry.Value.Suggestion.SuggestionId);
        Assert.Equal(1, await DecisionCountAsync());
    }
}
