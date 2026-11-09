using Microsoft.Extensions.Logging.Abstractions;
using TravelCut.Application.Editorial;
using TravelCut.Contracts.Dtos;
using TravelCut.Domain.Entities;
using Xunit;

namespace TravelCut.UnitTests.Editorial;

/// <summary>E05: handles validados de verdade (sem 30 fps fixo, sem presumir bordas, handle exigido em cada lado).</summary>
public class HandleValidationTests
{
    private readonly InMemoryMediaRepo _media = new();
    private readonly EditorialEffectsService _svc;

    public HandleValidationTests()
    {
        _svc = new EditorialEffectsService(new InMemoryTransitionRepo(), _media, NullLogger<EditorialEffectsService>.Instance);
    }

    private Guid Add(double fps, double duration = 100.0)
    {
        var id = Guid.NewGuid();
        _media.Add(new MediaAsset { MediaId = id, Metadata = new MediaMetadata { DurationSeconds = duration, FrameRate = fps } });
        return id;
    }

    private Task<ValidateHandlesResultDto> Validate(Guid a, Guid b, double d, string type, double? endA, double? startB) =>
        _svc.ValidateHandlesAsync(new ValidateHandlesRequest(a.ToString(), b.ToString(), d, type, endA, startB));

    [Fact]
    public async Task ZeroHandles_WithHalfSecondDissolve_IsNotEnough()
    {
        var a = Add(30); var b = Add(30);

        var r = await Validate(a, b, 0.5, TransitionConstants.TypeVideoDissolve, endA: 100.0, startB: 0.0);

        Assert.False(r.HasEnoughHandles);
        Assert.Equal(0, r.FramesAAvailable);
        Assert.Equal(0, r.FramesBAvailable);
        Assert.NotNull(r.WarningMessage);
    }

    [Fact]
    public async Task OneSidedHandle_IsNotEnough_EvenIfTotalWouldBe()
    {
        var a = Add(30); var b = Add(30);

        // A com 10 s de cauda, B sem cabeça: a soma passaria, mas cada lado precisa de 0,25 s.
        var r = await Validate(a, b, 0.5, TransitionConstants.TypeVideoDissolve, endA: 90.0, startB: 0.0);

        Assert.False(r.HasEnoughHandles);
        Assert.True(r.FramesAAvailable >= 8);
    }

    [Fact]
    public async Task BothSidesWithHandle_IsEnough_UsingRealFrameRatePerMedia()
    {
        var a = Add(24); var b = Add(60);

        // 0,5 s: A precisa de 6 quadros (24 fps), B de 15 (60 fps).
        var ok = await Validate(a, b, 0.5, TransitionConstants.TypeVideoDissolve, endA: 99.75, startB: 0.25);
        Assert.True(ok.IsValid);
        Assert.True(ok.HasEnoughHandles);
        Assert.Equal(6, ok.FramesAAvailable);
        Assert.Equal(15, ok.FramesBAvailable);
        Assert.Equal(21, ok.FramesRequired);

        // Mesma folga em tempo, mas B a 60 fps exige 15 quadros: 0,2 s = 12 quadros < 15.
        var shortB = await Validate(a, b, 0.5, TransitionConstants.TypeVideoDissolve, endA: 99.75, startB: 0.2);
        Assert.False(shortB.HasEnoughHandles);
    }

    [Fact]
    public async Task UnknownEdgesOrFrameRate_AreNeverValidNorPresumed()
    {
        var a = Add(30); var b = Add(30);
        var noEdges = await Validate(a, b, 0.5, TransitionConstants.TypeVideoDissolve, endA: null, startB: null);
        Assert.False(noEdges.IsValid);
        Assert.False(noEdges.HasEnoughHandles);

        var noFps = await Validate(Add(0), b, 0.5, TransitionConstants.TypeVideoDissolve, endA: 90.0, startB: 5.0);
        Assert.False(noFps.IsValid);
        Assert.False(noFps.HasEnoughHandles);

        var missingMedia = await Validate(Guid.NewGuid(), b, 0.5, TransitionConstants.TypeVideoDissolve, endA: 90.0, startB: 5.0);
        Assert.False(missingMedia.IsValid);
        Assert.False(missingMedia.HasEnoughHandles);
    }

    [Fact]
    public async Task SimpleFade_NeedsNoHandles()
    {
        var a = Add(30); var b = Add(30);

        var r = await Validate(a, b, 0.5, TransitionConstants.TypeVideoFadeOut, endA: 100.0, startB: 0.0);

        Assert.True(r.IsValid);
        Assert.True(r.HasEnoughHandles);
    }
}
