using Microsoft.Extensions.Logging;
using TravelCut.Application.Ports;
using TravelCut.Domain.Entities;
using TravelCut.Domain.Enums;
using TravelCut.Domain.ValueObjects;

namespace TravelCut.Application.Editorial;

/// <summary>
/// Etapa de pós-análise (E04b): gera sugestões de zoom pelo ÚNICO recomendador (<see cref="ISmartZoomRecommender"/>) a partir
/// dos trechos de fala transcritos. O planejador apenas consome essas sugestões (aprovado/autoaplicado → clip.zoom).
/// Idempotente e respeitoso com decisões: nunca cria sugestão que sobreponha outra de zoom já existente na mesma mídia
/// (qualquer estado, inclusive rejeitada/desfeita), então reexecutar não duplica nem ressuscita o que o usuário decidiu.
/// </summary>
public interface IZoomSuggestionGenerator
{
    /// <summary>Retorna quantas sugestões NOVAS foram gravadas.</summary>
    Task<int> GenerateAsync(Guid projectId, CancellationToken ct = default);
}

public sealed class ZoomSuggestionGenerator : IZoomSuggestionGenerator
{
    /// <summary>Intervalo entre palavras que separa dois trechos de fala.</summary>
    public const double SpeechGapSeconds = 0.8;

    private readonly IMediaRepository _media;
    private readonly ITranscriptRepository _transcripts;
    private readonly IEditPlanRepository _plans;
    private readonly ISceneClassificationService _scenes;
    private readonly ISmartZoomRecommender _recommender;
    private readonly ILogger<ZoomSuggestionGenerator> _logger;

    public ZoomSuggestionGenerator(
        IMediaRepository media,
        ITranscriptRepository transcripts,
        IEditPlanRepository plans,
        ISceneClassificationService scenes,
        ISmartZoomRecommender recommender,
        ILogger<ZoomSuggestionGenerator> logger)
    {
        _media = media;
        _transcripts = transcripts;
        _plans = plans;
        _scenes = scenes;
        _recommender = recommender;
        _logger = logger;
    }

    public async Task<int> GenerateAsync(Guid projectId, CancellationToken ct = default)
    {
        var existing = (await _plans.ListSuggestionsByProjectAsync(projectId, ct))
            .Where(s => s.SuggestedAction == "zoom")
            .ToList();

        var created = new List<EditSuggestion>();
        foreach (var media in (await _media.ListByProjectAsync(projectId, ct)).OrderBy(m => m.RelativePath, StringComparer.Ordinal))
        {
            ct.ThrowIfCancellationRequested();
            if (media.Kind != MediaKind.Video) continue;
            var transcript = await _transcripts.GetByMediaIdAsync(media.MediaId, ct);
            if (transcript is not { Words.Count: > 0 }) continue;

            var segments = SpeechSegments(transcript);
            var taken = existing.Where(s => s.MediaId == media.MediaId).Select(s => s.SourceInterval).ToList();
            for (int i = 0; i < segments.Count; i++)
            {
                var classification = await _scenes.GetEffectiveClassificationAtAsync(media.MediaId, segments[i].StartTicks / 1000.0, ct);
                var suggestion = _recommender.RecommendZoomForSegment(media.MediaId, segments[i], classification, i);
                if (suggestion is null) continue;
                if (taken.Any(t => t.Overlaps(suggestion.SourceInterval))) continue; // idempotência + respeita decisões
                taken.Add(suggestion.SourceInterval);
                created.Add(suggestion);
            }
        }

        if (created.Count > 0)
        {
            await _plans.SaveSuggestionsBatchAsync(created, ct);
            _logger.LogInformation("Projeto {ProjectId}: {Count} sugestão(ões) de zoom gerada(s)", projectId, created.Count);
        }
        return created.Count;
    }

    /// <summary>Trechos de fala (ms da mídia fonte) agrupando palavras com pausa menor que <see cref="SpeechGapSeconds"/>.</summary>
    internal static List<TimeRange> SpeechSegments(Transcript transcript)
    {
        var result = new List<TimeRange>();
        double? start = null, end = null;
        foreach (var w in transcript.Words.Where(w => w.EndSeconds > w.StartSeconds).OrderBy(w => w.StartSeconds))
        {
            if (start is null) { start = w.StartSeconds; end = w.EndSeconds; continue; }
            if (w.StartSeconds - end!.Value > SpeechGapSeconds)
            {
                result.Add(ToRange(start.Value, end.Value));
                start = w.StartSeconds;
            }
            end = Math.Max(end!.Value, w.EndSeconds);
        }
        if (start is not null) result.Add(ToRange(start.Value, end!.Value));
        return result;
    }

    private static TimeRange ToRange(double startSeconds, double endSeconds) =>
        new((long)Math.Round(startSeconds * 1000), (long)Math.Round(endSeconds * 1000));
}
