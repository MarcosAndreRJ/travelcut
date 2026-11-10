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
        if (request.Plan is null)
            return PlanOutcome<EditPlanDto>.Fail("INVALID_ARGUMENT", "Plano ausente.");

        var project = await _projects.GetByIdAsync(request.ProjectId, ct);
        if (project is null || string.IsNullOrWhiteSpace(project.RootPath))
            return PlanOutcome<EditPlanDto>.Fail("PROJECT_NOT_FOUND", "Projeto nao encontrado.");

        var manualPlan = request.Plan;

        // O ProjectId do cliente nunca e confiado: o plano precisa pertencer ao projeto da requisicao.
        if (manualPlan.ProjectId != project.ProjectId)
        {
            _logger.LogWarning("Save manual recusado: plano {PlanId} declara o projeto {Declared}, requisicao e do projeto {Project}",
                manualPlan.PlanId, manualPlan.ProjectId, project.ProjectId);
            return PlanOutcome<EditPlanDto>.Fail(PlanProjectMismatchCode, "O plano enviado pertence a outro projeto.");
        }

        var clips = manualPlan.Clips ?? new List<PlanClipDto>();

        if (clips.Select(c => c.ClipId).Distinct().Count() != clips.Count)
            return PlanOutcome<EditPlanDto>.Fail("INVALID_ARGUMENT", "O plano contem ClipId duplicado.");

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

        // Leitura da revisao vigente, CAS e gravacao sob o mesmo lock do projeto que GenerateAsync/RevertAsync usam.
        return await WithProjectLockAsync(project.ProjectId, async () =>
        {
            var latest = await _revisions.GetLatestAsync(project.ProjectId, ct);
            if (latest is null)
                return PlanOutcome<EditPlanDto>.Fail("PLAN_NOT_FOUND", "O projeto nao tem plano para receber a edicao manual.");

            if (manualPlan.PlanId != latest.PlanId)
            {
                _logger.LogWarning("Save manual recusado: PlanId {PlanId} difere do plano vigente {Latest} do projeto {Project}",
                    manualPlan.PlanId, latest.PlanId, project.ProjectId);
                return PlanOutcome<EditPlanDto>.Fail(PlanIdMismatchCode, "O plano enviado nao e o plano vigente do projeto.");
            }

            if (request.BaseRevision != latest.Revision)
            {
                _logger.LogInformation("Save manual recusado: baseRevision {Base} difere da revisao vigente {Latest} (PLAN_STALE_BASE)",
                    request.BaseRevision, latest.Revision);
                return PlanOutcome<EditPlanDto>.Fail(PlanStaleBaseCode,
                    $"O plano foi alterado (revisao {latest.Revision}) desde a revisao base {request.BaseRevision}. Recarregue antes de salvar.");
            }

            var baseRead = await ReadPlanAsync(project.RootPath, latest, ct);
            if (baseRead.ErrorCode is not null)
                return PlanOutcome<EditPlanDto>.Fail(baseRead.ErrorCode, baseRead.ErrorMessage!);
            var basis = baseRead.Plan!;

            // Overlay da revisao base (inclui migracao de manual legada); base adulterada/ilegivel recusa sem gravar nada.
            var baseOverlay = await ResolveOverlayAsync(project, latest, ct);
            if (baseOverlay.ErrorCode is not null)
                return PlanOutcome<EditPlanDto>.Fail(baseOverlay.ErrorCode, baseOverlay.ErrorMessage!);
            if (!IsFingerprintIntact(basis))
                return PlanOutcome<EditPlanDto>.Fail(TamperedCode, "A revisao base esta adulterada: save recusado. Gere um novo plano.");

            // Dos clipes do cliente so se usa a DIFERENCA em relacao a rBase (estado, nao log de operacoes).
            var basisDomain = PlanMapping.ToDomain(basis) with { ManualEdits = baseOverlay.Edits.Count > 0 ? baseOverlay.Edits.ToList() : null };
            var delta = ManualEditDeriver.DeriveDelta(basisDomain, PlanMapping.ToDomain(manualPlan).Clips);
            if (delta.Count == 0)
            {
                // Derive(r, r) = vazio: nada mudou, nenhuma revisao nova (idempotente).
                _logger.LogInformation("Save manual sem diferenca em relacao a revisao {Revision}; nenhuma revisao nova criada", latest.Revision);
                var same = await ReadDerivedAsync(project.ProjectId, project.RootPath, latest, latest.InputsFingerprint, ct);
                return same;
            }
            var eNew = ManualEdits.Compact((basisDomain.ManualEdits ?? Array.Empty<PlanManualEdit>()).Concat(delta));

            if (!TryResolveZone(basis.TimeZoneId, out var zone, out var zoneId, out var zoneError))
                return PlanOutcome<EditPlanDto>.Fail("INVALID_ARGUMENT", zoneError!);

            // Perfil, base de tempo e fuso vem da revisao vigente (nao do cliente); o servidor grava Apply(Build(entradas atuais), E_new).
            var auto = await BuildAutoCoreAsync(project, basis.Profile, basis.TimeBase, zone, zoneId, latest, ct);
            if (auto.Built.Plan is null)
                return PlanOutcome<EditPlanDto>.Fail("AUTOGEN_FAILED",
                    $"Falha ao gerar o plano base da edicao manual: {auto.Built.ErrorMessage ?? auto.Built.ErrorCode}");

            // Uma edicao manual nunca e gravada como autor automatico (senao burlaria a protecao da regeneracao).
            var author = string.IsNullOrWhiteSpace(request.Author) || request.Author!.StartsWith("planner-rules/", StringComparison.Ordinal)
                ? AuthorUser
                : request.Author!;

            var applied = ApplyOverlay(auto.Built.Plan, eNew, auto.Input, author);
            if (applied.Plan is null)
                return PlanOutcome<EditPlanDto>.Fail(applied.ErrorCode!, applied.ErrorMessage!);

            var now = _clock.GetUtcNow().UtcDateTime;
            var candidate = applied.Plan with { CreatedAtUtc = now };

            // Persistido: so a validacao estrutural (sem issues de estado externo), como no revert.
            var analyses = auto.Input.Analyses;
            var structural = PlanValidation.Run(PlanMapping.ToDomain(candidate), auto.Input.Media, analyses)
                .Where(i => !DerivedIssueCodes.Contains(i.Code))
                .ToList();
            var finalPlan = candidate with { Validation = PlanMapping.ToValidationDto(structural, now) };

            // Idempotente tambem pelo conteudo: o resultado igual ao da revisao vigente nao cria revisao nova.
            var inputsFingerprint = InputsFingerprintOf(finalPlan, auto.Input.Media, auto.Input.Suggestions, auto.Input.Decisions);
            if (string.Equals(latest.PlanFingerprint, finalPlan.PlanFingerprint, StringComparison.OrdinalIgnoreCase))
                return await ReadDerivedAsync(project.ProjectId, project.RootPath, latest, latest.InputsFingerprint, ct);

            var conflict = await TryPersistAsync(project.RootPath, finalPlan, inputsFingerprint, ct);
            if (conflict is not null) return conflict;

            _logger.LogInformation("Plano {PlanId}: revisao manual {Revision} gravada (base {Base}, {Edits} edicao(oes) no overlay)",
                finalPlan.PlanId, finalPlan.Revision, finalPlan.BaseRevision, finalPlan.ManualEdits?.Count ?? 0);
            var derived = await DeriveValidationAsync(project.ProjectId, project.RootPath, finalPlan, inputsFingerprint, ct);
            return PlanOutcome<EditPlanDto>.Ok(finalPlan with { Validation = derived });
        }, ct);
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

        // Previa pura: perfil, base de tempo e fuso vem da revisao base (nao do padrao); nada e gravado (nem indice, nem
        // artefato) e o lock nao e necessario porque nao ha escrita. Usa o nucleo, nunca GenerateAsync.
        if (!TryResolveZone(manualPlan.TimeZoneId, out var zone, out var zoneId, out var zoneError))
            return PlanOutcome<ReconcilePlanResponse>.Fail("INVALID_ARGUMENT", zoneError!);

        var project = await _projects.GetByIdAsync(request.ProjectId, ct);
        if (project is null || string.IsNullOrWhiteSpace(project.RootPath))
            return PlanOutcome<ReconcilePlanResponse>.Fail("PROJECT_NOT_FOUND", NoProjectMessage);

        var latest = await _revisions.GetLatestAsync(project.ProjectId, ct);
        var candidate = await BuildCandidateCoreAsync(
            project, manualPlan.Profile, manualPlan.TimeBase, zone, zoneId, latest, ct);
        // Overlay recusado (base adulterada/ilegivel, aplicacao estrutural recusada): nao e falha de geracao, e protecao do trabalho do usuario.
        if (candidate.Built.Plan is null && candidate.Refused)
            return PlanOutcome<ReconcilePlanResponse>.Fail(
                candidate.Built.ErrorCode ?? PlanManualProtectedCode, candidate.Built.ErrorMessage ?? ManualProtectedMessage);
        if (candidate.Built.Plan is null)
            return PlanOutcome<ReconcilePlanResponse>.Fail("AUTOGEN_FAILED",
                $"Falha ao gerar plano candidato: {candidate.Built.ErrorMessage ?? candidate.Built.ErrorCode}");

        var result = PlanReconciler.Reconcile(candidate.Built.Plan, manualPlan);
        // A validacao do candidato e derivada agora, sobre o conteudo reconciliado (nao herdada do plano automatico).
        var validation = await DeriveValidationAsync(project.ProjectId, project.RootPath, result.ReconciledPlan, null, ct);
        return PlanOutcome<ReconcilePlanResponse>.Ok(result with { ReconciledPlan = result.ReconciledPlan with { Validation = validation } });
    }
}
