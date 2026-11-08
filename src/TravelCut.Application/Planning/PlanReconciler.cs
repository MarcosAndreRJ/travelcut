using System;
using System.Collections.Generic;
using System.Linq;
using TravelCut.Contracts.Dtos;

namespace TravelCut.Application.Planning;

public static class PlanReconciler
{
    public static ReconcilePlanResponse Reconcile(
        EditPlanDto candidateAuto,
        EditPlanDto manualPlan,
        IReadOnlyList<PlanEditorialOperationDto>? operations = null)
    {
        var conflicts = new List<PlanConflictDto>();
        var resultClips = new List<PlanClipDto>();

        var autoClips = candidateAuto.Clips;
        var manualClips = manualPlan.Clips;

        var autoClipByMedia = autoClips
            .GroupBy(c => c.MediaId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var processed = new HashSet<Guid>();

        foreach (var mClip in manualClips)
        {
            if (processed.Contains(mClip.ClipId))
                continue;

            if (!autoClipByMedia.TryGetValue(mClip.MediaId, out var matchingAutoClips) || matchingAutoClips.Count == 0)
            {
                conflicts.Add(new PlanConflictDto(
                    Guid.NewGuid(),
                    mClip.ClipId,
                    mClip.LinkedClipId,
                    "MEDIA_REMOVED",
                    $"A mídia associada ao clipe manual '{mClip.RelativePath}' foi removida ou excluída no plano automático.",
                    "warning",
                    "Manter clipe manual para inspeção do editor."));
                resultClips.Add(mClip);
                processed.Add(mClip.ClipId);
                continue;
            }

            bool sourceSurvives = matchingAutoClips.Any(a =>
                mClip.SourceInUs < a.SourceOutUs && mClip.SourceOutUs > a.SourceInUs);

            if (!sourceSurvives)
            {
                conflicts.Add(new PlanConflictDto(
                    Guid.NewGuid(),
                    mClip.ClipId,
                    mClip.LinkedClipId,
                    "CUT_REOPENED",
                    $"O trecho do clipe manual ({mClip.SourceInUs / 1000}ms - {mClip.SourceOutUs / 1000}ms) sofreu corte ou alteração na revisão automática da F14.1.",
                    "warning",
                    "Ajustar intervalo manualmente ou aceitar novo corte automático."));
            }

            resultClips.Add(mClip);
            processed.Add(mClip.ClipId);

            if (mClip.LinkedClipId.HasValue && !processed.Contains(mClip.LinkedClipId.Value))
            {
                var linked = manualClips.FirstOrDefault(c => c.ClipId == mClip.LinkedClipId.Value);
                if (linked != null)
                {
                    resultClips.Add(linked);
                    processed.Add(linked.ClipId);
                }
            }
        }

        foreach (var aClip in autoClips)
        {
            if (processed.Contains(aClip.ClipId))
                continue;

            bool alreadyCovered = manualClips.Any(m =>
                m.MediaId == aClip.MediaId &&
                Math.Max(m.SourceInUs, aClip.SourceInUs) < Math.Min(m.SourceOutUs, aClip.SourceOutUs));

            if (!alreadyCovered)
            {
                resultClips.Add(aClip);
                processed.Add(aClip.ClipId);
            }
        }

        var orderedClips = resultClips
            .OrderBy(c => c.TrackId)
            .ThenBy(c => c.TimelineStartFrame)
            .ToList();

        long maxEndFrame = orderedClips.Count > 0
            ? orderedClips.Max(c => c.TimelineStartFrame + c.DurationFrames)
            : 0;

        var reconciledPlan = candidateAuto with
        {
            Clips = orderedClips,
            Author = "planner+reconciler",
            Summary = candidateAuto.Summary with { TimelineDurationFrames = maxEndFrame }
        };

        return new ReconcilePlanResponse(
            reconciledPlan,
            conflicts.Count > 0,
            conflicts);
    }
}
