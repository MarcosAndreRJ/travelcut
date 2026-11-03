using TravelCut.Application.Editor;
using TravelCut.Contracts.Dtos;
using Xunit;
using static TravelCut.UnitTests.Editor.EditorTestData;

namespace TravelCut.UnitTests.Editor;

public class EditorAssemblyBuilderTests
{
    private static EditorAssemblyBuildResult Build(EditPlanDto plan, Dictionary<Guid, EditorMediaSource>? media = null, string name = "Viagem") =>
        EditorAssemblyBuilder.Build(plan, media ?? Sources(Src(1)), name, NowUtc);

    private static EditorAssemblyDto Ok(EditorAssemblyBuildResult result)
    {
        Assert.Null(result.ErrorCode);
        Assert.NotNull(result.Assembly);
        return result.Assembly!;
    }

    [Fact]
    public void SameRate2997_ConvertsMicrosecondsToFrames_AndKeepsPlanDuration()
    {
        // 1 s e 2 s em 29,97 fps → frames 30 e 60; duração do plano 30.
        var plan = Plan(Timeline2997, [Clip(1, "V1", 1, 1_000_000, 2_000_000, start: 0, duration: 30)]);

        var asm = Ok(Build(plan));

        Assert.Equal("editor-assembly/1", asm.SchemaVersion);
        Assert.Equal("Manifesto de montagem do TravelCut — não é um projeto DaVinci.", asm.Notice);
        Assert.Equal("29.97", asm.TimelineFrameRate);
        var clip = Assert.Single(asm.Clips);
        Assert.Equal(1, clip.TrackIndex);
        Assert.Equal("video_audio", clip.MediaType);
        Assert.Equal(30, clip.SourceStartFrame);
        Assert.Equal(60, clip.SourceEndFrameExclusive);
        Assert.Equal(0, clip.RecordFrame);
        Assert.Equal(30, clip.DurationFrames);
        Assert.Empty(asm.Warnings);
        Assert.Empty(asm.Rejected);
    }

    [Fact]
    public void Timeline25_SameRate_UsesExactFrameIndices()
    {
        var media = Sources(Src(1, fpsNum: 25, fpsDen: 1));
        var plan = Plan(Timeline25, [Clip(1, "V1", 1, 0, 2_000_000, start: 0, duration: 50)]);

        var asm = Ok(Build(plan, media));

        var clip = Assert.Single(asm.Clips);
        Assert.Equal("25", asm.TimelineFrameRate);
        Assert.Equal(0, clip.SourceStartFrame);
        Assert.Equal(50, clip.SourceEndFrameExclusive);
        Assert.Empty(asm.Warnings);
    }

    [Fact]
    public void MixedRate_RoundsHalfEven_AndWarns()
    {
        // Fonte 50 fps, timeline 25 fps. 10 000 µs = 0,5 quadro → 0 (par); 30 000 µs = 1,5 → 2 (par); 50 000 µs = 2,5 → 2 (par).
        var media = Sources(Src(1, fpsNum: 50, fpsDen: 1));
        var plan = Plan(Timeline25,
        [
            Clip(1, "V1", 1, 10_000, 1_000_000, start: 0, duration: 50),
            Clip(2, "V1", 1, 30_000, 1_000_000, start: 50, duration: 49),
            Clip(3, "V1", 1, 50_000, 1_000_000, start: 99, duration: 48),
        ]);

        var asm = Ok(Build(plan, media));

        var byId = asm.Clips.ToDictionary(c => c.ClipId);
        Assert.Equal(0, byId[ClipId(1)].SourceStartFrame);
        Assert.Equal(50, byId[ClipId(1)].SourceEndFrameExclusive); // 1 000 000 µs · 50 fps = 50
        Assert.Equal(2, byId[ClipId(2)].SourceStartFrame);
        Assert.Equal(2, byId[ClipId(3)].SourceStartFrame);
        Assert.Equal(3, asm.Warnings.Count(w => w.Code == "MIXED_FRAME_RATE"));
        Assert.Empty(asm.Rejected);
    }

    [Fact]
    public void SameRate_PlanDurationWins_AndFrameRoundingWarningWhenDifferent()
    {
        // 25 fps: 1 000 000 µs = 25 quadros, mas o plano diz 26 → fim forçado em 26 e aviso FRAME_ROUNDING.
        var media = Sources(Src(1, fpsNum: 25, fpsDen: 1));
        var plan = Plan(Timeline25, [Clip(1, "V1", 1, 0, 1_000_000, start: 0, duration: 26)]);

        var asm = Ok(Build(plan, media));

        var clip = Assert.Single(asm.Clips);
        Assert.Equal(26, clip.SourceEndFrameExclusive);
        var warning = Assert.Single(asm.Warnings);
        Assert.Equal("FRAME_ROUNDING", warning.Code);
        Assert.Equal(ClipId(1), warning.ClipId);
    }

    [Fact]
    public void SpeedNotOne_RejectsClip_WithoutShiftingOthers()
    {
        var plan = Plan(Timeline2997,
        [
            Clip(1, "V1", 1, 0, 1_000_000, start: 0, duration: 30, speedNum: 2, speedDen: 1),
            Clip(2, "V1", 1, 1_000_000, 2_000_000, start: 30, duration: 30),
        ]);

        var asm = Ok(Build(plan));

        var clip = Assert.Single(asm.Clips);
        Assert.Equal(ClipId(2), clip.ClipId);
        Assert.Equal(30, clip.RecordFrame); // não deslocado
        var rejected = Assert.Single(asm.Rejected);
        Assert.Equal("speed", rejected.Kind);
        Assert.Equal("CAPABILITY_UNSUPPORTED", rejected.Code);
        Assert.Equal(ClipId(1), rejected.ClipId);
    }

    [Fact]
    public void LinkedAudioA1_CoveredByIncludedV1_WarnsButIsNotRejected()
    {
        var plan = Plan(Timeline2997,
        [
            Clip(1, "V1", 1, 1_000_000, 2_000_000, start: 0, duration: 30),
            Clip(2, "A1", 1, 1_000_000, 2_000_000, start: 0, duration: 30, linked: ClipId(1)),
        ]);

        var asm = Ok(Build(plan));

        Assert.Single(asm.Clips);
        Assert.Empty(asm.Rejected);
        var warning = Assert.Single(asm.Warnings);
        Assert.Equal("LINKED_AUDIO_COVERED", warning.Code);
        Assert.Equal(ClipId(2), warning.ClipId);
    }

    [Fact]
    public void A2Music_IsRejectedAsUnsupportedTrack()
    {
        var plan = Plan(Timeline2997,
        [
            Clip(1, "V1", 1, 1_000_000, 2_000_000, start: 0, duration: 30),
            Clip(2, "A2", 2, 0, 5_000_000, start: 0, duration: 150),
        ]);

        var asm = Ok(Build(plan, Sources(Src(1), Src(2))));

        Assert.Single(asm.Clips);
        var rejected = Assert.Single(asm.Rejected);
        Assert.Equal("track", rejected.Kind);
        Assert.Equal("CAPABILITY_UNSUPPORTED", rejected.Code);
        Assert.Equal(ClipId(2), rejected.ClipId);
    }

    [Fact]
    public void UnlinkedA1_IsRejectedAsTrack()
    {
        var plan = Plan(Timeline2997, [Clip(1, "A1", 1, 0, 1_000_000, start: 0, duration: 30)]);

        var asm = Ok(Build(plan));

        Assert.Empty(asm.Clips);
        var rejected = Assert.Single(asm.Rejected);
        Assert.Equal("track", rejected.Kind);
        Assert.Equal("CAPABILITY_UNSUPPORTED", rejected.Code);
    }

    [Fact]
    public void TransitionOtherThanCut_IsRejected_ClipsKept()
    {
        var plan = Plan(Timeline2997,
        [
            Clip(1, "V1", 1, 1_000_000, 2_000_000, start: 0, duration: 30),
            Clip(2, "V1", 1, 2_000_000, 3_000_000, start: 30, duration: 30),
        ], [Transition(1, 2, "dissolve")]);

        var asm = Ok(Build(plan));

        Assert.Equal(2, asm.Clips.Count);
        var rejected = Assert.Single(asm.Rejected);
        Assert.Equal("transition", rejected.Kind);
        Assert.Equal("CAPABILITY_UNSUPPORTED", rejected.Code);
        Assert.Equal(ClipId(1), rejected.ClipId);
    }

    [Fact]
    public void CutTransition_IsNotRejected()
    {
        var plan = Plan(Timeline2997,
        [
            Clip(1, "V1", 1, 1_000_000, 2_000_000, start: 0, duration: 30),
            Clip(2, "V1", 1, 2_000_000, 3_000_000, start: 30, duration: 30),
        ], [Transition(1, 2, "cut")]);

        var asm = Ok(Build(plan));

        Assert.Equal(2, asm.Clips.Count);
        Assert.Empty(asm.Rejected);
    }

    [Fact]
    public void VariableFrameRateSource_WarnsVfr()
    {
        var media = Sources(Src(1, vfr: true));
        var plan = Plan(Timeline2997, [Clip(1, "V1", 1, 1_000_000, 2_000_000, start: 0, duration: 30)]);

        var asm = Ok(Build(plan, media));

        Assert.Single(asm.Clips);
        Assert.Contains(asm.Warnings, w => w.Code == "VFR_SOURCE" && w.ClipId == ClipId(1));
        Assert.True(asm.Media.Single().IsVariableFrameRate);
    }

    [Fact]
    public void MissingMedia_RejectsClipAsMediaNotFound()
    {
        var plan = Plan(Timeline2997, [Clip(1, "V1", 9, 0, 1_000_000, start: 0, duration: 30)]);

        var asm = Ok(Build(plan, Sources()));

        Assert.Empty(asm.Clips);
        var rejected = Assert.Single(asm.Rejected);
        Assert.Equal("clip", rejected.Kind);
        Assert.Equal("MEDIA_NOT_FOUND", rejected.Code);
    }

    [Fact]
    public void MediaWithoutValidFps_RejectsClipAsInvalidTimebase()
    {
        var media = Sources(Src(1, fpsNum: 0, fpsDen: 0));
        var plan = Plan(Timeline2997, [Clip(1, "V1", 1, 0, 1_000_000, start: 0, duration: 30)]);

        var asm = Ok(Build(plan, media));

        Assert.Empty(asm.Clips);
        var rejected = Assert.Single(asm.Rejected);
        Assert.Equal("clip", rejected.Kind);
        Assert.Equal("INVALID_TIMEBASE", rejected.Code);
    }

    [Fact]
    public void UnsupportedTimelineBase_ReturnsErrorResult_NotException()
    {
        var plan = Plan(new TimelineTimeBaseDto(1, 48, "48 fps"), [Clip(1, "V1", 1, 0, 1_000_000, start: 0, duration: 48)]);

        var result = Build(plan);

        Assert.Null(result.Assembly);
        Assert.Equal("INVALID_TIMEBASE", result.ErrorCode);
        Assert.False(string.IsNullOrWhiteSpace(result.ErrorMessage));
    }

    [Theory]
    [InlineData(1001, 24000, "23.976")]
    [InlineData(1, 24, "24")]
    [InlineData(1, 25, "25")]
    [InlineData(1001, 30000, "29.97")]
    [InlineData(1, 30, "30")]
    [InlineData(1, 50, "50")]
    [InlineData(1001, 60000, "59.94")]
    [InlineData(1, 60, "60")]
    [InlineData(2, 48, "24")]     // 2/48 == 1/24 por valor racional
    [InlineData(1, 48, null)]
    [InlineData(0, 30, null)]
    public void TimelineFrameRate_MapsFrameDurationToResolveSetting(long num, long den, string? expected)
    {
        Assert.Equal(expected, EditorTimebases.TimelineFrameRate(num, den));
    }

    [Fact]
    public void FingerprintIsStable_AcrossDryRunsWithDifferentTimes()
    {
        var plan = Plan(Timeline2997, [Clip(1, "V1", 1, 1_000_000, 2_000_000, start: 0, duration: 30)]);

        var first = EditorAssemblyBuilder.Build(plan, Sources(Src(1)), "Viagem", NowUtc);
        var second = EditorAssemblyBuilder.Build(plan, Sources(Src(1)), "Viagem", NowUtc.AddHours(3));

        Assert.NotEqual(first.Assembly!.EditorProjectName, second.Assembly!.EditorProjectName);
        Assert.Equal(first.Assembly.AssemblyFingerprint, second.Assembly.AssemblyFingerprint);
        Assert.Matches("^[0-9a-f]{64}$", first.Assembly.AssemblyFingerprint);
    }

    [Fact]
    public void FingerprintChanges_WhenAClipChanges()
    {
        var original = Plan(Timeline2997, [Clip(1, "V1", 1, 1_000_000, 2_000_000, start: 0, duration: 30)]);
        var changed = Plan(Timeline2997, [Clip(1, "V1", 1, 1_000_000, 2_000_000, start: 0, duration: 31)]);

        var a = Ok(Build(original)).AssemblyFingerprint;
        var b = Ok(Build(changed)).AssemblyFingerprint;

        Assert.NotEqual(a, b);
    }

    [Fact]
    public void EditorProjectName_UsesPrefixRevisionAndUtcStamp()
    {
        var name = EditorAssemblyBuilder.EditorProjectName("Viagem", 3, new DateTime(2026, 11, 2, 12, 34, 56, DateTimeKind.Utc));

        Assert.Equal("TravelCut_Viagem_r0003_20261102T123456", name);
        Assert.Equal("TravelCut r0003", EditorAssemblyBuilder.TimelineName(3));
    }

    [Fact]
    public void SanitizeName_KeepsOnlySafeCharactersAndLimitsLength()
    {
        Assert.Equal("Viagem_____Teste_", EditorAssemblyBuilder.SanitizeName("Viagem Ç & Teste!"));
        Assert.Equal(40, EditorAssemblyBuilder.SanitizeName(new string('a', 60)).Length);
        Assert.Equal("Projeto", EditorAssemblyBuilder.SanitizeName("   "));
    }

    [Fact]
    public void Output_IsDeterministicallyOrdered()
    {
        var plan = Plan(Timeline2997,
        [
            Clip(2, "V1", 1, 1_000_000, 2_000_000, start: 30, duration: 30),
            Clip(1, "V1", 1, 1_000_000, 2_000_000, start: 0, duration: 30),
        ]);

        var asm = Ok(Build(plan));

        Assert.Equal(new[] { ClipId(1), ClipId(2) }, asm.Clips.Select(c => c.ClipId).ToArray());
        Assert.Equal(new[] { 0L, 30L }, asm.Clips.Select(c => c.RecordFrame).ToArray());
    }
}
