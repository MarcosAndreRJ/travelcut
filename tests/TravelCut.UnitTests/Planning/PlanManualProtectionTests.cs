using Microsoft.Extensions.DependencyInjection;
using TravelCut.Application.PostAnalysis;
using TravelCut.Application.Planning;
using TravelCut.Application.Ports;
using TravelCut.Contracts.Dtos;
using Xunit;

namespace TravelCut.UnitTests.Planning;

/// <summary>
/// Guarda B (F14.7a): regenerar nunca supersede uma revisao manual (autor nao automatico) que nao pode ser lida.
/// A recusa e "plano preservado" para decisoes e pos-analise, nao falha.
/// </summary>
public sealed class PlanManualProtectionTests : IDisposable
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

    private async Task<EditPlanDto> GenerateThenSaveManualAsync(string author = "user")
    {
        var generated = await GenerateAsync();
        var saved = await _rig.UseCases.SaveManualRevisionAsync(
            new SavePlanRevisionRequest(ProjectId, generated.Revision, ManualPlanEdits.DropLastPair(generated), Author: author));
        Assert.True(saved.Success, saved.ErrorCode + " " + saved.ErrorMessage);
        return saved.Value!;
    }

    private async Task<int> LatestRevisionAsync() => (await _rig.Revisions.GetLatestAsync(ProjectId))!.Revision;

    [Theory]
    [InlineData("missing")]
    [InlineData("corrupt")]
    public async Task Generate_RefusesToSupersedeUnreadableManualRevision(string breakage)
    {
        var manual = await GenerateThenSaveManualAsync();
        if (breakage == "missing") _rig.Artifacts.Remove(manual.PlanId, manual.Revision);
        else _rig.Artifacts.Replace(manual.PlanId, manual.Revision, "{ json quebrado");
        var filesBefore = _rig.Artifacts.Files.Count;

        var result = await _rig.UseCases.GenerateAsync(new GeneratePlanRequest(ProjectId, null, null));

        Assert.False(result.Success);
        Assert.Equal(PlanningUseCases.PlanManualEditsUnreadableCode, result.ErrorCode);
        Assert.Equal(manual.Revision, await LatestRevisionAsync());
        Assert.Equal(filesBefore, _rig.Artifacts.Files.Count);
    }

    [Fact]
    public async Task Generate_RefusesForRevertAuthorToo()
    {
        var generated = await GenerateAsync();
        var reverted = await _rig.UseCases.RevertAsync(new RevertPlanRequest(ProjectId, generated.Revision));
        Assert.True(reverted.Success, reverted.ErrorCode);
        Assert.Equal(PlanningUseCases.AuthorRevert, reverted.Value!.Author);
        _rig.Artifacts.Remove(reverted.Value.PlanId, reverted.Value.Revision);

        var result = await _rig.UseCases.GenerateAsync(new GeneratePlanRequest(ProjectId, null, null));

        Assert.Equal(PlanningUseCases.PlanManualEditsUnreadableCode, result.ErrorCode);
        Assert.Equal(reverted.Value.Revision, await LatestRevisionAsync());
    }

    [Fact]
    public async Task Generate_OverAnUnreadableAutomaticRevision_IsNotBlockedByTheGuard()
    {
        var generated = await GenerateAsync();
        _rig.Artifacts.Remove(generated.PlanId, generated.Revision);

        var result = await _rig.UseCases.GenerateAsync(new GeneratePlanRequest(ProjectId, null, null));

        Assert.NotEqual(PlanningUseCases.PlanManualEditsUnreadableCode, result.ErrorCode);
    }

    [Fact]
    public async Task Generate_OverAReadableManualRevision_StillWorks()
    {
        var manual = await GenerateThenSaveManualAsync();

        var result = await _rig.UseCases.GenerateAsync(new GeneratePlanRequest(ProjectId, null, null));

        Assert.True(result.Success, result.ErrorCode);
        Assert.True(await LatestRevisionAsync() >= manual.Revision);
    }

    [Fact]
    public async Task Decision_OverUnreadableManualRevision_PreservesThePlanWithoutError()
    {
        var manual = await GenerateThenSaveManualAsync();
        _rig.Artifacts.Replace(manual.PlanId, manual.Revision, "{ json quebrado");

        var result = await _rig.UseCases.SubmitDecisionAsync(
            new SubmitDecisionRequest(TravelFixture.S(1), "modify", "user", null, 10.5, 11.5));

        Assert.True(result.Success, result.ErrorCode);
        Assert.Null(result.Value!.PlanError);
        Assert.False(result.Value.PlanChanged);
        Assert.Equal(manual.Revision, result.Value.PlanRevision);
        Assert.Equal(manual.Revision, await LatestRevisionAsync());
    }
}

public sealed class PlanManualProtectionPostAnalysisTests : PostAnalysisHostBase
{
    public PlanManualProtectionPostAnalysisTests() : base(seed: true) { }

    [Fact]
    public async Task PostAnalysis_OverUnreadableManualRevision_IsNotAJobFailure()
    {
        var first = await RunAsync();
        Assert.Equal("completed", first.State);

        EditPlanDto manual;
        string root;
        using (var scope = _services.CreateScope())
        {
            var planning = scope.ServiceProvider.GetRequiredService<PlanningUseCases>();
            var latest = (await planning.GetAsync(new GetPlanRequest(_projectId, null))).Value!;
            var saved = await planning.SaveManualRevisionAsync(new SavePlanRevisionRequest(_projectId, latest.Revision, ManualPlanEdits.DropLastPair(latest), Author: "user"));
            Assert.True(saved.Success, saved.ErrorCode);
            manual = saved.Value!;
            root = (await scope.ServiceProvider.GetRequiredService<IProjectRepository>().GetByIdAsync(_projectId))!.RootPath;
        }
        var file = Path.Combine(root, PlanStoragePaths.RelativeJsonPath(manual.PlanId, manual.Revision));
        File.WriteAllText(file, "{ json quebrado");

        var second = await RunAsync();

        Assert.Equal(0, second.Failures);
        Assert.DoesNotContain(second.Issues, i => i.StartsWith(PostAnalysisOrchestrator.IssuePlanFailed, StringComparison.Ordinal));
        using var check = _services.CreateScope();
        var after = await check.ServiceProvider.GetRequiredService<IPlanRevisionRepository>().GetLatestAsync(_projectId);
        Assert.Equal(manual.Revision, after!.Revision);
    }
}
