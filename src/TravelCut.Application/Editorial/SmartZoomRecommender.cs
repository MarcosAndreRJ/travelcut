using System.Text.Json;
using Microsoft.Extensions.Logging;
using TravelCut.Contracts.Dtos;
using TravelCut.Domain.Entities;
using TravelCut.Domain.Entities.Editorial;
using TravelCut.Domain.Enums;
using TravelCut.Domain.ValueObjects;

namespace TravelCut.Application.Editorial;

public interface ISmartZoomRecommender
{
    SmartZoomProfileDto GetProfileForInteraction(string interaction);
    EditSuggestion? RecommendZoomForSegment(
        Guid mediaId,
        TimeRange sourceInterval,
        EditorialSceneClassificationDto? classification,
        int segmentIndex);
    IReadOnlyList<EditSuggestion> GenerateZoomSuggestions(
        Guid mediaId,
        IEnumerable<TimeRange> speechSegments,
        EditorialSceneClassificationDto? effectiveClassification);
}

public sealed class SmartZoomRecommender : ISmartZoomRecommender
{
    /// <summary>Unidade de <see cref="TimeRange"/> em sugestões: milissegundos.</summary>
    private const double MillisecondsPerSecond = 1000.0;

    private readonly ILogger<SmartZoomRecommender> _logger;

    public SmartZoomRecommender(ILogger<SmartZoomRecommender> logger)
    {
        _logger = logger;
    }

    public SmartZoomProfileDto GetProfileForInteraction(string interaction)
    {
        var norm = (interaction ?? string.Empty).Trim().ToLowerInvariant();
        return norm switch
        {
            SceneClassificationConstants.InteractionDirectToCamera or "direct_to_camera" =>
                new SmartZoomProfileDto(norm, 1.05, 1.12, 1.08, SmartZoomConstants.TypePunchIn, true),
            SceneClassificationConstants.InteractionInterview or "interview" =>
                new SmartZoomProfileDto(norm, 1.03, 1.10, 1.06, SmartZoomConstants.TypePunchIn, true),
            SceneClassificationConstants.InteractionDialogue or "dialogue" =>
                new SmartZoomProfileDto(norm, 1.00, 1.07, 1.04, SmartZoomConstants.TypePunchIn, true),
            SceneClassificationConstants.InteractionBroll or "broll" =>
                new SmartZoomProfileDto(norm, 1.02, 1.08, 1.05, SmartZoomConstants.TypePushIn, true),
            SceneClassificationConstants.InteractionLandscape or "landscape" =>
                new SmartZoomProfileDto(norm, 1.00, 1.05, 1.02, SmartZoomConstants.TypePushIn, true),
            _ => new SmartZoomProfileDto(norm, 1.00, 1.00, 1.00, SmartZoomConstants.TypePunchIn, false)
        };
    }

    public EditSuggestion? RecommendZoomForSegment(
        Guid mediaId,
        TimeRange sourceInterval,
        EditorialSceneClassificationDto? classification,
        int segmentIndex)
    {
        if (classification != null &&
            classification.Movement.Equals(SceneClassificationConstants.MovementMoving, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        string interaction = classification?.Interaction ?? SceneClassificationConstants.InteractionDirectToCamera;
        var profile = GetProfileForInteraction(interaction);
        if (!profile.AllowZoom) return null;

        if (segmentIndex % 2 != 1) return null;

        // E04a: TimeRange de sugestões editoriais está em MILISSEGUNDOS (não em ticks de 100 ns).
        double durationSeconds = sourceInterval.DurationTicks / MillisecondsPerSecond;
        if (durationSeconds < 1.2) return null;

        double scale = Math.Clamp(profile.DefaultScale, profile.MinScale, profile.MaxScale);
        var parameters = new SmartZoomParametersDto(
            ZoomType: profile.RecommendedType,
            Scale: scale,
            FocalPointX: 0.5,
            FocalPointY: 0.5,
            DurationSeconds: durationSeconds,
            StartScale: 1.0,
            EndScale: scale,
            Framing: classification?.Framing,
            FocalPointKnown: IsFocalPointKnown(classification)
        );

        string rationale = $"Zoom editorial sugerido ({scale:0.00}x, {profile.RecommendedType}) para cena de {interaction}.";
        return new EditSuggestion
        {
            MediaId = mediaId,
            SourceInterval = sourceInterval,
            Kind = SuggestionKind.SubtleZoom,
            Rationale = rationale,
            EvidenceRefs = JsonSerializer.Serialize(parameters),
            ConfidenceBand = "high",
            NeedsHumanReview = true,
            SuggestedAction = "zoom",
            Status = SuggestionStatus.Proposed,
            Author = "smart-zoom-engine",
            CreatedAtUtc = DateTime.UtcNow
        };
    }

    /// <summary>
    /// O ponto focal (0,5; 0,5) só é confiável com enquadramento classificado (médio/close) de cena com pessoa
    /// (direto à câmera, entrevista, diálogo). Sem classificação, plano aberto, b-roll ou paisagem: incerto → revisão humana.
    /// </summary>
    internal static bool IsFocalPointKnown(EditorialSceneClassificationDto? c)
    {
        if (c is null) return false;
        bool framingKnown = c.Framing is SceneClassificationConstants.FramingMedium or SceneClassificationConstants.FramingCloseUp;
        bool personScene = c.Interaction is SceneClassificationConstants.InteractionDirectToCamera
            or SceneClassificationConstants.InteractionInterview or SceneClassificationConstants.InteractionDialogue;
        return framingKnown && personScene;
    }

    public IReadOnlyList<EditSuggestion> GenerateZoomSuggestions(
        Guid mediaId,
        IEnumerable<TimeRange> speechSegments,
        EditorialSceneClassificationDto? effectiveClassification)
    {
        var list = new List<EditSuggestion>();
        int index = 0;
        foreach (var interval in speechSegments)
        {
            var suggestion = RecommendZoomForSegment(mediaId, interval, effectiveClassification, index);
            if (suggestion != null) list.Add(suggestion);
            index++;
        }
        return list;
    }
}
