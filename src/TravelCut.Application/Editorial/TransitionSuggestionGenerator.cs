using Microsoft.Extensions.Logging;
using TravelCut.Application.Planning;
using TravelCut.Application.Ports;
using TravelCut.Contracts.Dtos;
using TravelCut.Domain.Entities;
using TravelCut.Domain.Enums;
using TravelCut.Domain.ValueObjects;

namespace TravelCut.Application.Editorial;

/// <summary>
/// Etapa de pós-análise (E05): gera sugestões de transição a partir das BORDAS REAIS dos clipes V1 do plano vigente (mídia/ponto de saída
/// do clipe A e mídia/ponto de entrada do clipe B), não de pares de mídia. Candidatos limitados (nunca todo corte vira animação):
/// salto de fonte relevante no mesmo arquivo → fade; mudança de seção entre arquivos → dissolve. Quem decide aplicar é o
/// <c>EditPlanner</c> (handles verdadeiros, política e precedência manual &gt; projeto &gt; política).
/// Idempotente e respeitoso com decisões: nunca recria sugestão para a mesma fronteira, em qualquer estado (inclusive rejeitada/desfeita).
/// </summary>
public interface ITransitionSuggestionGenerator
{
    /// <summary>Retorna quantas sugestões NOVAS foram gravadas.</summary>
    Task<int> GenerateAsync(Guid projectId, EditPlanDto plan, CancellationToken ct = default);
}

public sealed class TransitionSuggestionGenerator : ITransitionSuggestionGenerator
{
    /// <summary>Salto de fonte mínimo (µs) entre clipes do mesmo arquivo para valer uma sugestão de fade.</summary>
    public const long MinJumpUs = 1_000_000;

    private readonly IEditPlanRepository _plans;
    private readonly ILogger<TransitionSuggestionGenerator> _logger;

    public TransitionSuggestionGenerator(IEditPlanRepository plans, ILogger<TransitionSuggestionGenerator> logger)
    {
        _plans = plans;
        _logger = logger;
    }

    public async Task<int> GenerateAsync(Guid projectId, EditPlanDto plan, CancellationToken ct = default)
    {
        var existing = (await _plans.ListSuggestionsByProjectAsync(projectId, ct))
            .Where(s => s.SuggestedAction == TransitionSupport.SuggestedAction)
            .Select(s => TransitionSupport.Parse(s.EvidenceRefs))
            .Where(p => p is not null)
            .Select(p => Key(p!.FromMediaId, p.FromOutUs, p.ToMediaId, p.ToInUs))
            .ToHashSet();

        double secondsPerFrame = plan.TimeBase.FrameDurationDen > 0
            ? plan.TimeBase.FrameDurationNum / (double)plan.TimeBase.FrameDurationDen
            : 0;
        var v1 = plan.Clips.Where(c => c.TrackId == EditPlanner.TrackV1)
            .OrderBy(c => c.TimelineStartFrame).ThenBy(c => c.ClipId).ToList();

        var created = new List<EditSuggestion>();
        for (int i = 0; i + 1 < v1.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var a = v1[i];
            var b = v1[i + 1];
            if (a.TimelineStartFrame + a.DurationFrames != b.TimelineStartFrame) continue; // só fronteiras contíguas

            string? kind = null;
            if (a.MediaId == b.MediaId)
            {
                if (b.SourceInUs - a.SourceOutUs >= MinJumpUs) kind = TransitionSupport.Fade;
            }
            else if (a.SectionId != b.SectionId)
            {
                kind = TransitionSupport.Dissolve;
            }
            if (kind is null) continue;

            if (!existing.Add(Key(a.MediaId, a.SourceOutUs, b.MediaId, b.SourceInUs))) continue; // idempotência + respeita decisões

            var p = new TransitionSuggestionParams(TransitionSupport.DtoKind, kind, TransitionSupport.DefaultFrames,
                a.MediaId, a.SourceOutUs, b.MediaId, b.SourceInUs);
            long outMs = a.SourceOutUs / 1000;
            long durationMs = (long)Math.Round(TransitionSupport.DefaultFrames * secondsPerFrame * 1000);
            string what = kind == TransitionSupport.Fade ? "fade sobre salto de fonte no mesmo arquivo" : "dissolve na mudança de seção";
            created.Add(new EditSuggestion
            {
                MediaId = a.MediaId,
                SourceInterval = new TimeRange(Math.Max(0, outMs - durationMs), outMs),
                Kind = SuggestionKind.Transition,
                Rationale = $"Transição sugerida ({what}, {TransitionSupport.DefaultFrames} quadros) na fronteira real entre clipes do plano.",
                EvidenceRefs = TransitionSupport.Serialize(p),
                ConfidenceBand = "medium",
                NeedsHumanReview = true,
                SuggestedAction = TransitionSupport.SuggestedAction,
                Status = SuggestionStatus.Proposed,
                Author = TransitionSupport.Author,
                CreatedAtUtc = DateTime.UtcNow,
            });
        }

        if (created.Count > 0)
        {
            await _plans.SaveSuggestionsBatchAsync(created, ct);
            _logger.LogInformation("Projeto {ProjectId}: {Count} sugestão(ões) de transição gerada(s)", projectId, created.Count);
        }
        return created.Count;
    }

    private static string Key(Guid fromMedia, long fromOutUs, Guid toMedia, long toInUs) =>
        $"{fromMedia:D}|{fromOutUs}|{toMedia:D}|{toInUs}";
}
