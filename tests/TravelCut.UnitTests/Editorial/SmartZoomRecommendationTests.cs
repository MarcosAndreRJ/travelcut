using Microsoft.Extensions.Logging.Abstractions;
using TravelCut.Application.Editorial;
using TravelCut.Contracts.Dtos;
using TravelCut.Domain.Enums;
using TravelCut.Domain.ValueObjects;
using Xunit;

namespace TravelCut.UnitTests.Editorial;

public class SmartZoomRecommendationTests
{
    private readonly SmartZoomRecommender _recommender;

    public SmartZoomRecommendationTests()
    {
        _recommender = new SmartZoomRecommender(NullLogger<SmartZoomRecommender>.Instance);
    }

    [Theory]
    [InlineData(SceneClassificationConstants.InteractionDirectToCamera, 1.05, 1.12, 1.08, SmartZoomConstants.TypePunchIn, true)]
    [InlineData(SceneClassificationConstants.InteractionInterview, 1.03, 1.10, 1.06, SmartZoomConstants.TypePunchIn, true)]
    [InlineData(SceneClassificationConstants.InteractionDialogue, 1.00, 1.07, 1.04, SmartZoomConstants.TypePunchIn, true)]
    [InlineData(SceneClassificationConstants.InteractionBroll, 1.02, 1.08, 1.05, SmartZoomConstants.TypePushIn, true)]
    [InlineData(SceneClassificationConstants.InteractionLandscape, 1.00, 1.05, 1.02, SmartZoomConstants.TypePushIn, true)]
    public void Profile_ReturnsAccurateEditorialScales(
        string interaction,
        double expectedMin,
        double expectedMax,
        double expectedDefault,
        string expectedType,
        bool expectedAllow)
    {
        var profile = _recommender.GetProfileForInteraction(interaction);

        Assert.NotNull(profile);
        Assert.Equal(expectedMin, profile.MinScale);
        Assert.Equal(expectedMax, profile.MaxScale);
        Assert.Equal(expectedDefault, profile.DefaultScale);
        Assert.Equal(expectedType, profile.RecommendedType);
        Assert.Equal(expectedAllow, profile.AllowZoom);
    }

    [Fact]
    public void MovingCamera_DoesNotRecommendZoom()
    {
        var mediaId = Guid.NewGuid();
        var interval = new TimeRange(0, 30_000_000); // 3s
        var movingClassification = new EditorialSceneClassificationDto(
            EntryId: Guid.NewGuid().ToString(),
            ProjectId: Guid.NewGuid().ToString(),
            MediaId: mediaId.ToString(),
            StartSeconds: 0,
            EndSeconds: 3,
            IsFullVideo: true,
            Environment: SceneClassificationConstants.EnvExterior,
            Interaction: SceneClassificationConstants.InteractionDirectToCamera,
            Framing: SceneClassificationConstants.FramingMedium,
            Movement: SceneClassificationConstants.MovementMoving, // Camera em movimento
            EditorialProfile: SceneClassificationConstants.ProfileNatural,
            Notes: null,
            CreatedAtUtc: DateTime.UtcNow.ToString("O"),
            UpdatedAtUtc: DateTime.UtcNow.ToString("O")
        );

        var suggestion = _recommender.RecommendZoomForSegment(mediaId, interval, movingClassification, 1);

        // Nao deve recomendar zoom quando a camera esta em movimento
        Assert.Null(suggestion);
    }

    [Fact]
    public void DialogueSegmentOf3000Ms_RecommendsZoomWithThreeSecondsDuration()
    {
        var mediaId = Guid.NewGuid();
        var interval = new TimeRange(0, 3000); // TimeRange em ms: 3 s
        var dialogue = new EditorialSceneClassificationDto(
            EntryId: Guid.NewGuid().ToString(), ProjectId: Guid.NewGuid().ToString(), MediaId: mediaId.ToString(),
            StartSeconds: 0, EndSeconds: 3, IsFullVideo: true,
            Environment: SceneClassificationConstants.EnvInterior,
            Interaction: SceneClassificationConstants.InteractionDialogue,
            Framing: SceneClassificationConstants.FramingMedium,
            Movement: SceneClassificationConstants.MovementStatic,
            EditorialProfile: SceneClassificationConstants.ProfileNatural, Notes: null,
            CreatedAtUtc: DateTime.UtcNow.ToString("O"), UpdatedAtUtc: DateTime.UtcNow.ToString("O"));

        var suggestion = _recommender.RecommendZoomForSegment(mediaId, interval, dialogue, 1);

        Assert.NotNull(suggestion);
        var parameters = System.Text.Json.JsonSerializer.Deserialize<SmartZoomParametersDto>(suggestion!.EvidenceRefs!);
        Assert.NotNull(parameters);
        Assert.Equal(3.0, parameters!.DurationSeconds, 3);

        // Segmento de 1000 ms (< 1,2 s) não recebe zoom: com a unidade errada passaria a valer 0,0001 s e também não, mas 1500 ms precisa valer.
        Assert.NotNull(_recommender.RecommendZoomForSegment(mediaId, new TimeRange(0, 1500), dialogue, 1));
        Assert.Null(_recommender.RecommendZoomForSegment(mediaId, new TimeRange(0, 1000), dialogue, 1));
    }

    [Fact]
    public void StaticDirectToCamera_RecommendsPunchInZoom_NeverAutoApproved()
    {
        var mediaId = Guid.NewGuid();
        var interval = new TimeRange(0, 40_000_000); // 4s
        var staticClassification = new EditorialSceneClassificationDto(
            EntryId: Guid.NewGuid().ToString(),
            ProjectId: Guid.NewGuid().ToString(),
            MediaId: mediaId.ToString(),
            StartSeconds: 0,
            EndSeconds: 4,
            IsFullVideo: true,
            Environment: SceneClassificationConstants.EnvInterior,
            Interaction: SceneClassificationConstants.InteractionDirectToCamera,
            Framing: SceneClassificationConstants.FramingCloseUp,
            Movement: SceneClassificationConstants.MovementStatic,
            EditorialProfile: SceneClassificationConstants.ProfileNatural,
            Notes: null,
            CreatedAtUtc: DateTime.UtcNow.ToString("O"),
            UpdatedAtUtc: DateTime.UtcNow.ToString("O")
        );

        var suggestion = _recommender.RecommendZoomForSegment(mediaId, interval, staticClassification, 1);

        Assert.NotNull(suggestion);
        Assert.Equal(SuggestionKind.SubtleZoom, suggestion!.Kind);
        Assert.Equal("zoom", suggestion.SuggestedAction);
        // Regra central: NUNCA auto-aprovado!
        Assert.Equal(SuggestionStatus.Proposed, suggestion.Status);
        Assert.True(suggestion.NeedsHumanReview);
    }
}
