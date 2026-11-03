using TravelCut.Application.Motion;
using TravelCut.Contracts.Dtos;
using Xunit;

namespace TravelCut.Tests.Motion;

public class MotionValidationTests : IDisposable
{
    private readonly TempMotionProject _project = new();

    public void Dispose() => _project.Dispose();

    private MotionValidation Validate(MotionRenderRequestDto request) =>
        MotionRequestValidator.Validate(request, _project.Root);

    [Fact]
    public void ValidRequest_Passes()
    {
        var result = Validate(TempMotionProject.ValidRequest());

        Assert.True(result.IsValid, result.Message);
    }

    [Fact]
    public void UnknownTemplate_IsRejected()
    {
        var result = Validate(TempMotionProject.ValidRequest(templateId: "video_bomba"));

        Assert.Equal(MotionRequestValidator.UnknownTemplateCode, result.Code);
    }

    [Fact]
    public void ParameterNotAllowedForTemplate_IsRejected()
    {
        var request = TempMotionProject.ValidRequest(
            templateId: MotionTemplateIds.LowerThird,
            parameters: new Dictionary<string, string> { ["line1"] = "Ana", ["title"] = "x" });

        var result = Validate(request);

        Assert.Equal(MotionRequestValidator.ParameterNotAllowedCode, result.Code);
    }

    [Fact]
    public void MissingRequiredParameter_IsRejected()
    {
        var request = TempMotionProject.ValidRequest(
            templateId: MotionTemplateIds.MapRoute,
            parameters: new Dictionary<string, string> { ["origin"] = "Lisboa" });

        var result = Validate(request);

        Assert.Equal(MotionRequestValidator.ParameterRequiredCode, result.Code);
        Assert.Contains("destination", result.Message);
    }

    [Fact]
    public void ParameterAboveMaxLength_IsRejected()
    {
        var request = TempMotionProject.ValidRequest(parameters: new Dictionary<string, string> { ["title"] = new string('a', 121) });

        Assert.Equal(MotionRequestValidator.ParameterTooLongCode, Validate(request).Code);
    }

    [Theory]
    [InlineData("linha\u0007com controle")]
    [InlineData(" com espaco no inicio")]
    [InlineData("   ")]
    public void ControlCharactersOrPaddingOrBlank_AreRejected(string value)
    {
        var request = TempMotionProject.ValidRequest(parameters: new Dictionary<string, string> { ["title"] = value });

        Assert.False(Validate(request).IsValid);
    }

    [Theory]
    [InlineData("red")]
    [InlineData("#GGGGGG")]
    [InlineData("#12345")]
    public void InvalidHexColor_IsRejected(string color)
    {
        var request = TempMotionProject.ValidRequest(parameters: new Dictionary<string, string>
        {
            ["title"] = "Veneza",
            ["accentColor"] = color,
        });

        Assert.Equal(MotionRequestValidator.ParameterInvalidCode, Validate(request).Code);
    }

    [Fact]
    public void ValidHexColor_Passes()
    {
        var request = TempMotionProject.ValidRequest(parameters: new Dictionary<string, string>
        {
            ["title"] = "Veneza",
            ["accentColor"] = "#1A2b3C",
        });

        Assert.True(Validate(request).IsValid);
    }

    [Fact]
    public void TooManyParameters_IsRejected()
    {
        var parameters = new Dictionary<string, string> { ["title"] = "x" };
        for (int i = 0; i < MotionTemplateCatalog.MaxParameterCount; i++)
            parameters["p" + i] = "v";

        Assert.Equal(MotionRequestValidator.ParametersTooManyCode, Validate(TempMotionProject.ValidRequest(parameters: parameters)).Code);
    }

    [Theory]
    [InlineData("1bad")]
    [InlineData("nome-com-hifen")]
    [InlineData("../title")]
    public void InvalidParameterName_IsRejected(string name)
    {
        var request = TempMotionProject.ValidRequest(parameters: new Dictionary<string, string>
        {
            ["title"] = "Veneza",
            [name] = "x",
        });

        Assert.Equal(MotionRequestValidator.ParameterNameInvalidCode, Validate(request).Code);
    }

    [Theory]
    [InlineData("..\\1-Bruto\\original.png")]
    [InlineData("../1-Bruto/original.png")]
    [InlineData("sub/../../1-Bruto/x.png")]
    [InlineData("C:\\Windows\\win.png")]
    [InlineData("\\\\servidor\\share\\x.png")]
    [InlineData("/etc/x.png")]
    public void AssetOutsideAssetsFolder_IsRejected(string asset)
    {
        var request = TempMotionProject.ValidRequest(
            parameters: new Dictionary<string, string> { ["title"] = "Veneza", ["backgroundAsset"] = asset });

        Assert.Equal(MotionRequestValidator.AssetOutsideAssetsCode, Validate(request).Code);
    }

    [Fact]
    public void AssetThatDoesNotExist_IsRejected()
    {
        var request = TempMotionProject.ValidRequest(
            parameters: new Dictionary<string, string> { ["title"] = "Veneza", ["backgroundAsset"] = "marca/logo.png" });

        Assert.Equal(MotionRequestValidator.AssetNotFoundCode, Validate(request).Code);
    }

    [Fact]
    public void AssetOfUnsupportedType_IsRejected()
    {
        _project.AddAsset("marca/video.mp4");
        var request = TempMotionProject.ValidRequest(
            parameters: new Dictionary<string, string> { ["title"] = "Veneza", ["backgroundAsset"] = "marca/video.mp4" });

        Assert.Equal(MotionRequestValidator.AssetTypeNotAllowedCode, Validate(request).Code);
    }

    [Fact]
    public void ExistingAssetInsideAssets_Passes()
    {
        _project.AddAsset("marca/fundo.webp");
        var request = TempMotionProject.ValidRequest(
            parameters: new Dictionary<string, string> { ["title"] = "Veneza", ["backgroundAsset"] = "marca/fundo.webp" });

        Assert.True(Validate(request).IsValid);
    }

    [Theory]
    [InlineData(1919, 1080)]
    [InlineData(1920, 1081)]
    [InlineData(200, 1080)]
    [InlineData(1920, 4320)]
    [InlineData(7680, 1080)]
    public void InvalidDimensions_AreRejected(int width, int height)
    {
        var request = TempMotionProject.ValidRequest() with { Width = width, Height = height };

        Assert.Equal(MotionRequestValidator.DimensionsInvalidCode, Validate(request).Code);
    }

    [Theory]
    [InlineData(0L, 1L)]
    [InlineData(30L, 0L)]
    [InlineData(1L, 1L * 2)]   // fps 0,5 (abaixo de 1)
    [InlineData(241000L, 1L)]  // numerador acima do limite
    [InlineData(30000L, 1002L)] // denominador acima do limite
    [InlineData(30000L, 100L)] // 300 fps (acima de 120)
    public void InvalidFrameRate_IsRejected(long num, long den)
    {
        var request = TempMotionProject.ValidRequest() with { FrameRateNum = num, FrameRateDen = den };

        Assert.Equal(MotionRequestValidator.FrameRateInvalidCode, Validate(request).Code);
    }

    [Fact]
    public void NtscFrameRate_Passes()
    {
        var request = TempMotionProject.ValidRequest() with { FrameRateNum = 30000, FrameRateDen = 1001, DurationFrames = 300 };

        Assert.True(Validate(request).IsValid);
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(7201L)]
    public void DurationFramesOutOfRange_IsRejected(long frames)
    {
        var request = TempMotionProject.ValidRequest() with { DurationFrames = frames };

        Assert.Equal(MotionRequestValidator.DurationInvalidCode, Validate(request).Code);
    }

    [Fact]
    public void DurationAboveTwoMinutes_IsRejected()
    {
        // 3600 quadros a 25 fps = 144 s (acima de 120 s), dentro do limite de quadros.
        var request = TempMotionProject.ValidRequest() with { DurationFrames = 3600, FrameRateNum = 25, FrameRateDen = 1 };

        Assert.Equal(MotionRequestValidator.DurationInvalidCode, Validate(request).Code);
    }

    [Theory]
    [InlineData("curta")]
    [InlineData("chave com espaco")]
    [InlineData("x")]
    public void InvalidIdempotencyKey_IsRejected(string key)
    {
        var request = TempMotionProject.ValidRequest(idempotencyKey: key);

        Assert.Equal(MotionRequestValidator.IdempotencyKeyInvalidCode, Validate(request).Code);
    }

    [Fact]
    public void EmptyProjectId_IsRejected()
    {
        var request = TempMotionProject.ValidRequest() with { ProjectId = Guid.Empty };

        Assert.Equal(MotionRequestValidator.ProjectIdInvalidCode, Validate(request).Code);
    }

    [Fact]
    public void RelativeProjectRoot_IsRejected()
    {
        var result = MotionRequestValidator.Validate(TempMotionProject.ValidRequest(), "relativo\\projeto");

        Assert.Equal(MotionRequestValidator.ProjectRootInvalidCode, result.Code);
    }

    [Fact]
    public void ValidatorMessages_NeverContainAbsolutePaths()
    {
        var request = TempMotionProject.ValidRequest(
            parameters: new Dictionary<string, string> { ["title"] = "Veneza", ["backgroundAsset"] = "C:\\segredo\\x.png" });

        var result = Validate(request);

        Assert.False(result.IsValid);
        Assert.DoesNotContain("C:\\", result.Message);
        Assert.DoesNotContain(_project.Root, result.Message);
    }
}
