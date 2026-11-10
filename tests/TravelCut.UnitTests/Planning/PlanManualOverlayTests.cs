using TravelCut.Application.Planning;
using TravelCut.Contracts.Dtos;
using TravelCut.Domain.Entities;
using TravelCut.Domain.Enums;
using TravelCut.Domain.Planning;
using Xunit;

namespace TravelCut.UnitTests.Planning;

/// <summary>
/// F14.7/04 (incremento 3): o motor de overlay ligado ao fluxo real. Save baseado em estado (Derive/Compact + Apply(Build, E)),
/// regeneração que reaplica o overlay (R1), recusa segura (R4), fingerprints antigos e migração de manual legado (R5),
/// idempotência e precedência decisão humana x edição.
/// </summary>
public sealed class PlanManualOverlayTests : IDisposable
{
    private readonly DecisionRig _rig = new();

    public void Dispose() => _rig.Dispose();

    private static Guid ProjectId => TravelFixture.ProjectId;

    private async Task<EditPlanDto> GenerateAsync()
    {
        var r = await _rig.UseCases.GenerateAsync(new GeneratePlanRequest(ProjectId, null, null));
        Assert.True(r.Success, r.ErrorCode + " " + r.ErrorMessage);
        return r.Value!;
    }

    private async Task<EditPlanDto> SaveAsync(EditPlanDto basis, EditPlanDto edited)
    {
        var r = await _rig.UseCases.SaveManualRevisionAsync(new SavePlanRevisionRequest(ProjectId, basis.Revision, edited, Author: "user"));
        Assert.True(r.Success, r.ErrorCode + " " + r.ErrorMessage);
        return r.Value!;
    }

    private async Task<EditPlanDto> LatestAsync() => (await _rig.UseCases.GetAsync(new GetPlanRequest(ProjectId, null))).Value!;

    private static (Guid Media, long In, long Out) LastSegment(EditPlanDto p)
    {
        var c = p.Clips.OrderBy(x => x.TimelineStartFrame).Last();
        return (c.MediaId, c.SourceInUs, c.SourceOutUs);
    }

    private static bool Contains(EditPlanDto p, (Guid Media, long In, long Out) s) =>
        p.Clips.Any(c => c.MediaId == s.Media && c.SourceInUs == s.In && c.SourceOutUs == s.Out);

    // -------------------------------------------------------------- Item 1: save baseado em estado

    [Fact]
    public async Task Save_WritesOverlay_PinsBasis_StoresRealInputsFingerprint()
    {
        var generated = await GenerateAsync();
        var dropped = LastSegment(generated);
        var saved = await SaveAsync(generated, ManualPlanEdits.DropLastPair(generated));

        Assert.Equal(generated.Revision + 1, saved.Revision);
        Assert.Equal("user", saved.Author);
        Assert.Equal(generated.Profile, saved.Profile);
        Assert.Equal(generated.TimeBase, saved.TimeBase);
        Assert.Equal(generated.TimeZoneId, saved.TimeZoneId);
        Assert.NotEmpty(saved.ManualEdits!);
        Assert.All(saved.ManualEdits!, e => Assert.Equal(saved.Revision, e.IntroducedInRevision));
        Assert.False(Contains(saved, dropped));
        Assert.Equal(PlanFingerprint.ComputePlan(PlanMapping.ToDomain(saved)), saved.PlanFingerprint);

        var entry = await _rig.Revisions.GetLatestAsync(ProjectId);
        Assert.False(string.IsNullOrWhiteSpace(entry!.InputsFingerprint));
        var json = await _rig.Artifacts.ReadRevisionAsync(DecisionRig.Root, entry.PlanId, entry.Revision);
        Assert.True(PlanJson.TryDeserialize(json!, out var stored));
        Assert.DoesNotContain(stored!.Validation.Issues, i => i.Code is "SOURCE_FILE_MISSING" or "PLAN_OUTDATED" or "MEDIA_CHANGED" or "PLAN_TAMPERED");
        // PLAN_OUTDATED continua correto: recem-gravado nao esta desatualizado.
        Assert.DoesNotContain((await LatestAsync()).Validation.Issues, i => i.Code == PlanningUseCases.PlanOutdatedCode);
    }

    [Fact]
    public async Task Save_Twice_WithoutChange_CreatesOnlyOneRevision()
    {
        var generated = await GenerateAsync();
        var first = await SaveAsync(generated, ManualPlanEdits.DropLastPair(generated));
        var again = await SaveAsync(first, first);
        var third = await SaveAsync(first, first with { Clips = first.Clips.AsEnumerable().Reverse().ToList() });

        Assert.Equal(first.Revision, again.Revision);
        Assert.Equal(first.Revision, third.Revision);
        Assert.Equal(2, (await _rig.Revisions.ListAsync(ProjectId)).Count);
    }

    // R6: o save usa o estado dos clipes, nao um log de operacoes: editar e desfazer no cliente (estado == base) nao grava nada.
    [Fact]
    public async Task Save_UsesStateNotOperationLog_EditThenUndoWritesNothing()
    {
        var generated = await GenerateAsync();
        var result = await SaveAsync(generated, generated with { Clips = generated.Clips.ToList() });

        Assert.Equal(generated.Revision, result.Revision);
        Assert.Single(await _rig.Revisions.ListAsync(ProjectId));
    }

    // -------------------------------------------------------------- Item 2 / R1: regeneracao reaplica o overlay

    [Fact]
    public async Task R1_Approve_UnrelatedSuggestion_ThenReprocess_KeepsManualEditsAndLeavesRevisionHashUntouched()
    {
        var generated = await GenerateAsync();
        var dropped = LastSegment(generated);
        var manual = await SaveAsync(generated, ManualPlanEdits.DropLastPair(generated));
        var entry = (await _rig.Revisions.GetAsync(ProjectId, manual.Revision))!;
        var jsonBefore = await _rig.Artifacts.ReadRevisionAsync(DecisionRig.Root, manual.PlanId, manual.Revision);

        var decided = await _rig.UseCases.SubmitDecisionAsync(new SubmitDecisionRequest(TravelFixture.S(5), "defer", "user", null, null, null));
        Assert.True(decided.Success, decided.ErrorCode);
        var regenerated = await _rig.UseCases.GenerateAsync(new GeneratePlanRequest(ProjectId, null, null)); // pos-analise / plan.generate
        Assert.True(regenerated.Success, regenerated.ErrorCode);

        var latest = await LatestAsync();
        Assert.False(Contains(latest, dropped));
        Assert.Equal(manual.ManualEdits!.Count, latest.ManualEdits!.Count);
        Assert.Equal("planner-rules/1+manual", latest.Author == "user" ? "planner-rules/1+manual" : latest.Author);
        Assert.Equal(jsonBefore, await _rig.Artifacts.ReadRevisionAsync(DecisionRig.Root, manual.PlanId, manual.Revision));
        Assert.Equal(entry.PlanFingerprint, (await _rig.Revisions.GetAsync(ProjectId, manual.Revision))!.PlanFingerprint);
        Assert.Equal(manual.Clips.Count, latest.Clips.Count);
    }

    [Fact]
    public async Task Generate_OverManualRevision_AppliesOverlay_AuthorPlannerPlusManual_WhenInputsChange()
    {
        var generated = await GenerateAsync();
        var dropped = LastSegment(generated);
        await SaveAsync(generated, ManualPlanEdits.DropLastPair(generated));
        var before = await _rig.Revisions.GetLatestAsync(ProjectId);

        // Mudanca real de entradas: aprovar uma sugestao muda o estado efetivo (e o fingerprint das entradas).
        var approved = await _rig.UseCases.SubmitDecisionAsync(new SubmitDecisionRequest(TravelFixture.S(1), "approve", "user", null, null, null));
        Assert.True(approved.Success, approved.ErrorCode);

        var latest = await LatestAsync();
        Assert.False(Contains(latest, dropped));
        Assert.NotEmpty(latest.ManualEdits!);
        if (latest.Revision > before!.Revision) Assert.Equal("planner-rules/1+manual", latest.Author);
        Assert.Empty(latest.Validation.Issues.Where(i => i.Code is "PLAN_OUTDATED"));
    }

    [Fact]
    public async Task Reprocess_KeepsDecisionsAndManualEdits_Idempotent()
    {
        await GenerateAsync();
        // usuario restaurou pausa / rejeitou zoom / ajustou corte / mudou enquadramento: decisoes humanas
        foreach (var (n, action, s, e) in new (int, string, double?, double?)[] { (1, "modify", 10.5, 11.5), (4, "reject", null, null), (5, "defer", null, null) })
            Assert.True((await _rig.UseCases.SubmitDecisionAsync(new SubmitDecisionRequest(TravelFixture.S(n), action, "user", null, s, e))).Success);
        var withDecisions = await LatestAsync();
        var manual = await SaveAsync(withDecisions, ManualPlanEdits.DropLastPair(withDecisions));

        for (int i = 0; i < 2; i++) Assert.True((await _rig.UseCases.GenerateAsync(new GeneratePlanRequest(ProjectId, null, null))).Success);

        var latest = await LatestAsync();
        Assert.Equal(manual.Revision, latest.Revision);
        Assert.Equal(manual.PlanFingerprint, latest.PlanFingerprint);
    }

    // -------------------------------------------------------------- R4: manual corrompida nao gera revisao nova

    [Fact]
    public async Task R4_CorruptLatestManual_GenerateDecisionSaveWriteNothing()
    {
        var generated = await GenerateAsync();
        var manual = await SaveAsync(generated, ManualPlanEdits.DropLastPair(generated));
        _rig.Artifacts.Replace(manual.PlanId, manual.Revision, "{ json quebrado");
        var filesBefore = _rig.Artifacts.Files.Count;

        var gen = await _rig.UseCases.GenerateAsync(new GeneratePlanRequest(ProjectId, null, null));
        var save = await _rig.UseCases.SaveManualRevisionAsync(new SavePlanRevisionRequest(ProjectId, manual.Revision, generated, Author: "user"));
        var dec = await _rig.UseCases.SubmitDecisionAsync(new SubmitDecisionRequest(TravelFixture.S(1), "approve", "user", null, null, null));

        Assert.Equal(PlanningUseCases.PlanManualEditsUnreadableCode, gen.ErrorCode);
        Assert.False(save.Success);
        Assert.True(dec.Success);
        Assert.False(dec.Value!.PlanChanged);
        Assert.Equal(manual.Revision, (await _rig.Revisions.GetLatestAsync(ProjectId))!.Revision);
        Assert.Equal(filesBefore, _rig.Artifacts.Files.Count);
    }

    [Fact]
    public async Task R4_TamperedOverlayRevision_IsProtected_NothingWritten()
    {
        var generated = await GenerateAsync();
        var manual = await SaveAsync(generated, ManualPlanEdits.DropLastPair(generated));
        // adulteracao: conteudo muda, fingerprint gravado nao
        var tampered = manual with { ManualEdits = manual.ManualEdits!.Select(e => e with { SourceOutUs = e.SourceOutUs + 1 }).ToList() };
        _rig.Artifacts.Replace(manual.PlanId, manual.Revision, PlanJson.Serialize(tampered));
        var filesBefore = _rig.Artifacts.Files.Count;

        var gen = await _rig.UseCases.GenerateAsync(new GeneratePlanRequest(ProjectId, null, null));

        Assert.Equal(PlanningUseCases.PlanManualProtectedCode, gen.ErrorCode);
        Assert.True(PlanningUseCases.IsPlanPreservedCode(gen.ErrorCode));
        Assert.Equal(filesBefore, _rig.Artifacts.Files.Count);
    }

    // Reconcile (previa) com overlay recusado devolve a protecao do trabalho do usuario, nao uma falha de geracao.
    [Fact]
    public async Task R4_Reconcile_WhenOverlayIsRefused_ReturnsManualProtected_NotAutogenFailed()
    {
        var generated = await GenerateAsync();
        var manual = await SaveAsync(generated, ManualPlanEdits.DropLastPair(generated));
        var tampered = manual with { ManualEdits = manual.ManualEdits!.Select(e => e with { SourceOutUs = e.SourceOutUs + 1 }).ToList() };
        _rig.Artifacts.Replace(manual.PlanId, manual.Revision, PlanJson.Serialize(tampered));
        var filesBefore = _rig.Artifacts.Files.Count;

        var outcome = await _rig.UseCases.ReconcileAsync(new ReconcilePlanRequest(ProjectId));

        Assert.False(outcome.Success);
        Assert.Equal(PlanningUseCases.PlanManualProtectedCode, outcome.ErrorCode);
        Assert.NotEqual("AUTOGEN_FAILED", outcome.ErrorCode);
        Assert.Equal(filesBefore, _rig.Artifacts.Files.Count);
        Assert.Equal(manual.Revision, (await _rig.Revisions.GetLatestAsync(ProjectId))!.Revision);

        // Via IPC o erro carrega as acoes sugeridas.
        var response = await _rig.Dispatcher.DispatchAsync(new TravelCut.Contracts.Ipc.IpcRequest
        {
            RequestId = "rc1", Type = "plan.reconcile", Payload = new ReconcilePlanRequest(ProjectId),
        });
        Assert.False(response.Ok);
        Assert.Equal(PlanningUseCases.PlanManualProtectedCode, response.Error!.Code);
        Assert.Equal("suggestedActions:" + PlanningUseCases.ManualProtectedSuggestedActions, response.Error.Details);
    }

    // -------------------------------------------------------------- R5: fixtures antigas e migracao

    [Fact]
    public void R5_InputsFingerprint_WithoutOverlay_IsIdenticalToLegacy()
    {
        var plan = ManualEditFixture_Plan();
        var profile = plan.Profile;
        var legacy = PlanInputsFingerprint.Compute("m", profile, plan.TimeBase, "UTC", []);
        Assert.Equal(legacy, PlanInputsFingerprint.Compute("m", profile, plan.TimeBase, "UTC", [], null));
        Assert.Equal(legacy, PlanInputsFingerprint.Compute("m", profile, plan.TimeBase, "UTC", [], new List<PlanManualEdit>()));
        var edit = PlanManualEdit.Create(1, ManualEditKinds.Exclude, Guid.NewGuid(), 0, 1_000_000);
        Assert.NotEqual(legacy, PlanInputsFingerprint.Compute("m", profile, plan.TimeBase, "UTC", [], new[] { edit }));
    }

    private static EditPlanDto ManualEditFixture_Plan() => TravelCut.Tests.Planning.ManualEditFixture.Default();

    /// <summary>Revisao manual legada: autor user, sem manualEdits, BaseRevision apontando para a automatica.</summary>
    private async Task<(EditPlanDto Auto, EditPlanDto Legacy)> SeedLegacyManualAsync()
    {
        var auto = await GenerateAsync();
        var legacy = ManualPlanEdits.DropLastPair(auto) with { Revision = auto.Revision + 1, BaseRevision = auto.Revision, Author = "user", ManualEdits = null };
        legacy = legacy with { PlanFingerprint = PlanFingerprint.ComputePlan(PlanMapping.ToDomain(legacy)) };
        await _rig.Artifacts.WriteRevisionAsync(DecisionRig.Root, legacy.PlanId, legacy.Revision, PlanJson.Serialize(legacy));
        await _rig.Revisions.AddAsync(PlanMapping.ToIndexEntry(legacy));
        return (auto, legacy);
    }

    [Fact]
    public async Task R5_LegacyManualRevision_IsMigratedOnFirstRegeneration()
    {
        var (auto, legacy) = await SeedLegacyManualAsync();
        var dropped = LastSegment(auto);
        Assert.False(Contains(legacy, dropped));

        // Entrada mudou (decisao): regeneracao real, com E = Derive(base lida do disco, revisao manual).
        var approved = await _rig.UseCases.SubmitDecisionAsync(new SubmitDecisionRequest(TravelFixture.S(1), "approve", "user", null, null, null));
        Assert.True(approved.Success, approved.ErrorCode);
        var regenerated = await _rig.UseCases.GenerateAsync(new GeneratePlanRequest(ProjectId, null, null));
        Assert.True(regenerated.Success, regenerated.ErrorCode);

        var latest = await LatestAsync();
        Assert.False(Contains(latest, dropped));
        if (latest.Revision > legacy.Revision)
        {
            Assert.Equal("planner-rules/1+manual", latest.Author);
            Assert.NotEmpty(latest.ManualEdits!);
        }
        Assert.Equal(PlanFingerprint.ComputePlan(PlanMapping.ToDomain(latest)), latest.PlanFingerprint);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("corrupt")]
    [InlineData("tampered")]
    public async Task R5_LegacyManual_WithUnusableBase_IsRefusedWithManualProtected(string breakage)
    {
        var (auto, legacy) = await SeedLegacyManualAsync();
        switch (breakage)
        {
            case "missing": _rig.Artifacts.Remove(auto.PlanId, auto.Revision); break;
            case "corrupt": _rig.Artifacts.Replace(auto.PlanId, auto.Revision, "{ json quebrado"); break;
            default: _rig.Artifacts.Replace(auto.PlanId, auto.Revision, PlanJson.Serialize(auto with { Clips = auto.Clips.Take(auto.Clips.Count - 2).ToList() })); break;
        }
        var filesBefore = _rig.Artifacts.Files.Count;

        var gen = await _rig.UseCases.GenerateAsync(new GeneratePlanRequest(ProjectId, null, null));

        Assert.Equal(PlanningUseCases.PlanManualProtectedCode, gen.ErrorCode);
        Assert.Contains("Reconcilie", gen.ErrorMessage);
        Assert.Equal(legacy.Revision, (await _rig.Revisions.GetLatestAsync(ProjectId))!.Revision);
        Assert.Equal(filesBefore, _rig.Artifacts.Files.Count);
    }

    [Fact]
    public async Task Revert_RestoresOverlayOfTheTargetRevision()
    {
        var generated = await GenerateAsync();
        var manual = await SaveAsync(generated, ManualPlanEdits.DropLastPair(generated));
        Assert.True((await _rig.UseCases.RevertAsync(new RevertPlanRequest(ProjectId, generated.Revision))).Success);

        var back = await _rig.UseCases.RevertAsync(new RevertPlanRequest(ProjectId, manual.Revision));

        Assert.True(back.Success, back.ErrorCode);
        Assert.Equal(manual.ManualEdits!.Select(e => e.EditId), back.Value!.ManualEdits!.Select(e => e.EditId));
        Assert.Equal(manual.PlanFingerprint, back.Value.PlanFingerprint);
        // regenerar sobre o revert preserva o overlay restaurado
        await _rig.UseCases.SubmitDecisionAsync(new SubmitDecisionRequest(TravelFixture.S(1), "approve", "user", null, null, null));
        Assert.False(Contains(await LatestAsync(), LastSegment(generated)));
    }

    // -------------------------------------------------------------- Item 3: precedencia

    private static EditSuggestion Sug(Guid media, long startMs, long endMs) => new()
    {
        SuggestionId = Guid.NewGuid(), MediaId = media, SourceInterval = new TravelCut.Domain.ValueObjects.TimeRange(startMs, endMs),
    };

    private static EditDecision Dec(Guid suggestion, string action, int version, int? baseRevision, bool undo = false) => new()
    {
        DecisionId = Guid.NewGuid(), SuggestionId = suggestion, Action = undo ? "undo" : action, Version = version, BaseRevision = baseRevision,
    };

    [Fact]
    public void Precedence_LaterHumanDecisionWins_EarlierDoesNot_UndoReactivates()
    {
        var m = Guid.NewGuid();
        var edit = PlanManualEdit.Create(1, ManualEditKinds.Exclude, m, 10_000_000, 12_000_000, introducedInRevision: 5);
        var s = Sug(m, 10_000, 12_400);
        var unrelated = Sug(m, 30_000, 31_000);
        EditSuggestion[] sugs = [s, unrelated];

        // decisao ANTERIOR a edicao (BaseRevision 4 < 5): a edicao manual vence
        Assert.Single(ManualPrecedence.Active([edit], sugs, [Dec(s.SuggestionId, "reject", 1, 4)]));
        // decisao POSTERIOR sobreposta (BaseRevision 5 >= 5): a decisao vence; a edicao e ignorada, nao apagada
        var later = new[] { Dec(s.SuggestionId, "reject", 1, 5) };
        Assert.Empty(ManualPrecedence.Active([edit], sugs, later));
        Assert.Single(ManualPrecedence.Ignored([edit], sugs, later));
        // decisao posterior sobre outro trecho: sem efeito
        Assert.Single(ManualPrecedence.Active([edit], sugs, [Dec(unrelated.SuggestionId, "approve", 1, 9)]));
        // desfazer a decisao reativa a edicao
        Assert.Single(ManualPrecedence.Active([edit], sugs, [Dec(s.SuggestionId, "reject", 1, 5), Dec(s.SuggestionId, "", 2, 5, undo: true)]));
        // defer nao altera o plano: nao vence
        Assert.Single(ManualPrecedence.Active([edit], sugs, [Dec(s.SuggestionId, "defer", 1, 9)]));
    }

    [Fact]
    public async Task Precedence_LaterDecision_DoesNotEraseTheOverlay()
    {
        var generated = await GenerateAsync();
        var manual = await SaveAsync(generated, ManualPlanEdits.DropLastPair(generated));

        // decisao humana (BaseRevision = revisao manual) sobre a sugestao S1: o overlay completo permanece gravado
        Assert.True((await _rig.UseCases.SubmitDecisionAsync(new SubmitDecisionRequest(TravelFixture.S(1), "approve", "user", null, null, null))).Success);
        var latest = await LatestAsync();
        Assert.Equal(manual.ManualEdits!.Select(e => e.EditId), latest.ManualEdits!.Select(e => e.EditId));
    }
}
