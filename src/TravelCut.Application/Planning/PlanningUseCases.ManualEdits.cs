using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TravelCut.Application.Ports;
using TravelCut.Contracts.Dtos;
using TravelCut.Domain.Entities;
using TravelCut.Domain.Planning;

namespace TravelCut.Application.Planning;

public sealed partial class PlanningUseCases
{
    public async Task<PlanOutcome<EditPlanDto>> SaveManualRevisionAsync(
        SavePlanRevisionRequest request,
        CancellationToken ct = default)
    {
        if (request.ProjectId == Guid.Empty)
            return PlanOutcome<EditPlanDto>.Fail("INVALID_ARGUMENT", "ID de projeto invalido.");

        var project = await _projects.GetByIdAsync(request.ProjectId, ct);
        if (project is null || string.IsNullOrWhiteSpace(project.RootPath))
            return PlanOutcome<EditPlanDto>.Fail("PROJECT_NOT_FOUND", "Projeto nao encontrado.");

        var manualPlan = request.Plan;
        var clips = manualPlan.Clips ?? new List<PlanClipDto>();

        // Validacao V1 / A1 sync
        var clipMap = clips.ToDictionary(c => c.ClipId);
        foreach (var clip in clips.Where(c => c.LinkedClipId.HasValue))
        {
            if (clipMap.TryGetValue(clip.LinkedClipId!.Value, out var linked))
            {
                if (clip.TimelineStartFrame != linked.TimelineStartFrame ||
                    clip.DurationFrames != linked.DurationFrames)
                {
                    return PlanOutcome<EditPlanDto>.Fail("LINK_MISMATCH",
                        $"Clip {clip.ClipId} e par vinculado {linked.ClipId} estao dessincronizados.");
                }
            }
        }

        // Validacao de limites de quadros e duracao
        foreach (var clip in clips)
        {
            if (clip.TimelineStartFrame < 0 || clip.DurationFrames <= 0)
            {
                return PlanOutcome<EditPlanDto>.Fail("INVALID_FRAME_INTERVAL",
                    $"Clip {clip.ClipId} possui intervalo temporal invalido.");
            }
            if (clip.SourceInUs < 0 || clip.SourceOutUs <= clip.SourceInUs)
            {
                return PlanOutcome<EditPlanDto>.Fail("INVALID_SOURCE_INTERVAL",
                    $"Clip {clip.ClipId} possui intervalo de fonte invalido.");
            }
        }

        // Validacao de sobreposicao na mesma faixa
        var tracks = clips.GroupBy(c => c.TrackId);
        foreach (var track in tracks)
        {
            var sorted = track.OrderBy(c => c.TimelineStartFrame).ToList();
            for (int i = 0; i < sorted.Count - 1; i++)
            {
                var cur = sorted[i];
                var next = sorted[i + 1];
                if (cur.TimelineStartFrame + cur.DurationFrames > next.TimelineStartFrame)
                {
                    return PlanOutcome<EditPlanDto>.Fail("TRACK_OVERLAP",
                        $"Sobreposicao na faixa {cur.TrackId} entre clipes {cur.ClipId} e {next.ClipId}.");
                }
            }
        }

        var indexLatest = await _revisions.GetLatestAsync(manualPlan.PlanId, ct);
        var nextRevision = await NextRevisionAsync(project.RootPath, manualPlan.PlanId, indexLatest?.Revision, ct);

        long maxEndFrame = clips.Count > 0 ? clips.Max(c => c.TimelineStartFrame + c.DurationFrames) : 0;
        var updatedSummary = manualPlan.Summary with { TimelineDurationFrames = maxEndFrame };

        var candidatePlan = manualPlan with
        {
            Revision = nextRevision,
            BaseRevision = indexLatest?.Revision ?? manualPlan.Revision,
            CreatedAtUtc = DateTime.UtcNow,
            Author = request.Author ?? "user",
            Summary = updatedSummary
        };

        var derivedValidation = await DeriveValidationAsync(project.ProjectId, project.RootPath, candidatePlan, null, ct);
        var domainModel = PlanMapping.ToDomain(candidatePlan with { Validation = derivedValidation });
        var fingerprint = PlanFingerprint.ComputePlan(domainModel);

        var finalPlan = candidatePlan with
        {
            Validation = derivedValidation,
            PlanFingerprint = fingerprint
        };

        var conflict = await TryPersistAsync(project.RootPath, finalPlan, inputsFingerprint: null, ct);
        if (conflict is not null)
            return conflict;

        return PlanOutcome<EditPlanDto>.Ok(finalPlan);
    }

    public async Task<PlanOutcome<ReconcilePlanResponse>> ReconcileAsync(
        ReconcilePlanRequest request,
        CancellationToken ct = default)
    {
        if (request.ProjectId == Guid.Empty)
            return PlanOutcome<ReconcilePlanResponse>.Fail("INVALID_ARGUMENT", "ID de projeto invalido.");

        var planOutcome = await GetAsync(new GetPlanRequest(request.ProjectId, request.TargetRevision), ct);
        if (!planOutcome.Success || planOutcome.Value is null)
            return PlanOutcome<ReconcilePlanResponse>.Fail(planOutcome.ErrorCode ?? "NOT_FOUND", planOutcome.ErrorMessage ?? "Plano nao encontrado.");

        var manualPlan = planOutcome.Value;
        var autoGenOutcome = await GenerateAsync(new GeneratePlanRequest(request.ProjectId, null, null), ct);
        if (!autoGenOutcome.Success || autoGenOutcome.Value is null)
            return PlanOutcome<ReconcilePlanResponse>.Fail("AUTOGEN_FAILED", $"Falha ao gerar plano candidato: {autoGenOutcome.ErrorMessage}");

        var candidateAuto = autoGenOutcome.Value;
        var result = PlanReconciler.Reconcile(candidateAuto, manualPlan);

        return PlanOutcome<ReconcilePlanResponse>.Ok(result);
    }
}
