using Microsoft.Extensions.Logging;
using TravelCut.Application.UseCases;
using TravelCut.Domain.Entities;
using TravelCut.Domain.Enums;

namespace TravelCut.Application.Planning;

/// <summary>
/// Elegibilidade ao lote "aprovar sugestões confiáveis" decidida pela AutoEditorialPolicy (fonte única; o frontend só lê a flag).
/// </summary>
public sealed partial class PlanningUseCases
{
    /// <summary>
    /// Para cada sugestão: true somente quando é um corte ainda pendente (Proposed), sem decisão explícita no histórico,
    /// e a política do perfil de automação do projeto daria veredito Apply (mesmos fatos que o planejador usa:
    /// transcrição alinhada, intervalo efetivo, sinalização do analisador). Zoom/transição/demais ações: false.
    /// Falha de leitura de análise/mídia: false (nunca presume elegibilidade).
    /// </summary>
    public async Task<IReadOnlyDictionary<Guid, bool>> LoadBatchApprovabilityAsync(
        Guid projectId,
        IReadOnlyList<EditSuggestion> suggestions,
        IReadOnlyDictionary<Guid, EffectiveSuggestion> effective,
        IReadOnlyCollection<Guid> decidedSuggestionIds,
        CancellationToken ct = default)
    {
        var result = suggestions.ToDictionary(s => s.SuggestionId, _ => false);
        var candidates = suggestions
            .Where(s => s.SuggestedAction == "cut"
                        && effective.TryGetValue(s.SuggestionId, out var e) && e.Status == SuggestionStatus.Proposed
                        && !decidedSuggestionIds.Contains(s.SuggestionId))
            .ToList();
        if (candidates.Count == 0 || projectId == Guid.Empty) return result;

        try
        {
            var project = await _projects.GetByIdAsync(projectId, ct);
            var profile = AutoEditorialProfiles.TryParse(project?.AutomationProfile, out var parsed)
                ? parsed : AutoEditorialProfiles.Default;

            foreach (var group in candidates.GroupBy(s => s.MediaId))
            {
                var media = await _media.GetByIdAsync(group.Key, ct);
                if (media is null || media.Metadata is null) continue;
                var analysis = await _analysis.GetMediaAnalysisAsync(projectId, group.Key, ct);
                bool speechKnown = PlanEligibility.SpeechKnown(analysis);
                var words = speechKnown
                    ? analysis!.Transcript!.Words
                        .Select(x => new PolicyWord(x.Text, PlanEligibility.ToUs(x.StartSeconds), PlanEligibility.ToUs(x.EndSeconds)))
                        .ToList()
                    : new List<PolicyWord>();
                long durationUs = PlanEligibility.DurationUs(media);

                foreach (var s in group)
                {
                    var eff = effective[s.SuggestionId];
                    long a = Math.Clamp(eff.StartMs * 1000, 0, durationUs);
                    long b = Math.Clamp(eff.EndMs * 1000, 0, durationUs);
                    if (b <= a) continue;
                    var verdict = AutoEditorialPolicy.Instance.Evaluate(
                        new AutoEditCandidate(SuggestionStorageMapper.ResolveDtoKind(s), a, b, speechKnown, words,
                            s.NeedsHumanReview, HasExplicitDecision: false),
                        profile);
                    result[s.SuggestionId] = verdict.Action == PolicyAction.Apply;
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Não foi possível avaliar a elegibilidade de lote (projeto {ProjectId})", projectId);
            foreach (var key in result.Keys.ToList()) result[key] = false;
        }
        return result;
    }
}
