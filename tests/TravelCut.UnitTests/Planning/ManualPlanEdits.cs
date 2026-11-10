using TravelCut.Contracts.Dtos;

namespace TravelCut.UnitTests.Planning;

/// <summary>Edicoes de teste: o save manual so cria revisao quando ha diferenca de estado (Derive(r, r) = vazio).</summary>
internal static class ManualPlanEdits
{
    /// <summary>Remove o ultimo par de clipes da timeline (edicao manual real: vira exclude no overlay).</summary>
    public static EditPlanDto DropLastPair(EditPlanDto plan)
    {
        var last = plan.Clips.Max(c => c.TimelineStartFrame);
        var clips = plan.Clips.Where(c => c.TimelineStartFrame != last).ToList();
        return plan with { Clips = clips };
    }
}
