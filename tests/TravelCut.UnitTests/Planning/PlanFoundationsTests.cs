using TravelCut.Application.Planning;
using TravelCut.Contracts.Dtos;
using TravelCut.Domain.Planning;
using Xunit;

namespace TravelCut.UnitTests.Planning;

/// <summary>
/// F14.7a: reconcile como previa sem efeito colateral (E02), recusa segura sobre revisao manual ilegivel (guarda B),
/// TimeBase herdada no pos-analise e concorrencia save/generate/decisao (R3). Usa o DecisionRig (EF + SQLite em memoria).
/// </summary>
public sealed class PlanFoundationsTests : IDisposable
{
    private readonly DecisionRig _rig = new();

    public void Dispose() => _rig.Dispose();

    private static Guid ProjectId => TravelFixture.ProjectId;

    private async Task<EditPlanDto> GenerateAsync()
    {
        var result = await _rig.UseCases.GenerateAsync(new GeneratePlanRequest(ProjectId, null, null));
        Assert.True(result.Success, result.ErrorMessage);
        return result.Value!;
    }

    private async Task<EditPlanDto> SaveManualAsync(EditPlanDto basis, string author = "user")
    {
        var saved = await _rig.UseCases.SaveManualRevisionAsync(new SavePlanRevisionRequest(ProjectId, basis.Revision, basis, Author: author));
        Assert.True(saved.Success, saved.ErrorCode + " " + saved.ErrorMessage);
        return saved.Value!;
    }

    /// <summary>Indice + latest + hash dos artefatos do plano: tudo que um reconcile NAO pode alterar.</summary>
    private async Task<string> SnapshotAsync()
    {
        var sb = new System.Text.StringBuilder();
        foreach (var e in await _rig.Revisions.ListAsync(ProjectId))
            sb.Append($"idx|{e.Revision}|{e.PlanFingerprint}|{e.InputsFingerprint}|{e.Author}|{e.IsValid}\n");
        var latest = await _rig.Revisions.GetLatestAsync(ProjectId);
        sb.Append($"latest|{latest?.Revision}\n");
        foreach (var f in _rig.Artifacts.Files.OrderBy(k => k.Key.Revision))
        {
            sb.Append($"file|{f.Key.PlanId}|{f.Key.Revision}|")
              .Append(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(f.Value)))).Append('\n');
        }
        return sb.ToString();
    }

    // R2: reconcile e previa. Antes criava revisao como efeito colateral (GenerateAsync) e com perfil null.
    [Fact]
    public async Task Reconcile_IsAPurePreview_NothingIsWritten()
    {
        var generated = await GenerateAsync();
        var manual = await SaveManualAsync(generated);
        Assert.Equal(2, manual.Revision);
        var before = await SnapshotAsync();

        var outcome = await _rig.UseCases.ReconcileAsync(new ReconcilePlanRequest(ProjectId));

        Assert.True(outcome.Success, outcome.ErrorCode + " " + outcome.ErrorMessage);
        Assert.Equal(before, await SnapshotAsync());
        Assert.Equal(2, (await _rig.Revisions.GetLatestAsync(ProjectId))!.Revision);

        var preview = outcome.Value!.ReconciledPlan;
        Assert.Equal(manual.Profile, preview.Profile);
        Assert.Equal(manual.TimeBase, preview.TimeBase);
        Assert.Equal(manual.TimeZoneId, preview.TimeZoneId);
        Assert.Equal(manual.PlanId, preview.PlanId);
        Assert.Equal(manual.Revision, preview.BaseRevision);
        Assert.Equal(PlanFingerprint.ComputePlan(PlanMapping.ToDomain(preview)), preview.PlanFingerprint);
    }

    [Fact]
    public async Task Reconcile_OfAnEarlierTarget_IsAlsoWriteFree()
    {
        var generated = await GenerateAsync();
        await SaveManualAsync(generated);
        var before = await SnapshotAsync();

        var outcome = await _rig.UseCases.ReconcileAsync(new ReconcilePlanRequest(ProjectId, 1));

        Assert.True(outcome.Success, outcome.ErrorCode);
        Assert.Equal(before, await SnapshotAsync());
    }

    [Fact]
    public void Reconciler_CandidateDoesNotInheritStaleFingerprintOrDanglingTransitions()
    {
        var generated = _rig.UseCases.GenerateAsync(new GeneratePlanRequest(ProjectId, null, null)).GetAwaiter().GetResult().Value!;
        var auto = generated with
        {
            Revision = 9,
            Transitions = new List<PlanTransitionDto> { new(Guid.NewGuid(), Guid.NewGuid(), "fade", 10, false) }
        };
        var manual = generated with { Revision = 3, Author = "user" };

        var result = PlanReconciler.Reconcile(auto, manual).ReconciledPlan;

        Assert.Empty(result.Transitions);
        Assert.Equal(3, result.BaseRevision);
        Assert.Equal(PlanFingerprint.ComputePlan(PlanMapping.ToDomain(result)), result.PlanFingerprint);
    }
}
