using TravelCut.Application.Planning;
using Xunit;

namespace TravelCut.UnitTests.Planning;

public class EditorialProfileTests
{
    [Fact]
    public void Default_IsViagensV1_Conservative()
    {
        var p = EditorialProfiles.Default();

        Assert.Equal("viagens", p.ProfileId);
        Assert.Equal(1, p.Version);
        Assert.Equal("balanced", p.SilencePolicy);
        Assert.Equal("normal", p.Pace);
        Assert.Equal("strict", p.JokePreservation);
        Assert.Equal("subtle", p.ZoomStrength);
        Assert.Equal("auto", p.SpeedBroll);
        Assert.Equal("suggest_only", p.MusicPolicy);
        Assert.True(p.ReviewAmbiguous);
        Assert.True(p.AutoApplyTrivialCuts);
    }

    [Fact]
    public void Resolve_Null_ReturnsDefault()
    {
        var p = EditorialProfiles.Resolve(null, out var error);

        Assert.Null(error);
        Assert.Equal(EditorialProfiles.Default(), p);
    }

    [Fact]
    public void Resolve_ForcesReviewAmbiguousTrue_EvenWhenClientSendsFalse()
    {
        var requested = EditorialProfiles.Default() with { ReviewAmbiguous = false };

        var p = EditorialProfiles.Resolve(requested, out var error);

        Assert.Null(error);
        Assert.True(p.ReviewAmbiguous);
    }

    [Theory]
    [InlineData("outro", "viagens-x")]
    [InlineData("viagens", "zoom")]
    public void Resolve_RejectsUnknownProfileOrValues(string profileId, string zoomStrength)
    {
        var requested = EditorialProfiles.Default() with { ProfileId = profileId, ZoomStrength = zoomStrength };

        EditorialProfiles.Resolve(requested, out var error);

        Assert.NotNull(error);
    }
}
