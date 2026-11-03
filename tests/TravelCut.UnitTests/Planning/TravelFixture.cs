using System.Text.Json;
using TravelCut.Application.Planning;
using TravelCut.Application.Services;
using TravelCut.Contracts.Dtos;
using TravelCut.Domain.Entities;
using TravelCut.Domain.Enums;
using TravelCut.Domain.ValueObjects;

namespace TravelCut.UnitTests.Planning;

/// <summary>
/// Viagem determinística: 3 sessões, 8 mídias (conversa, estrada com/sem fala, estrada sem transcrição, drone,
/// áudio só, mídia sem metadados e mídia sem data de criação) e sugestões aprovadas, rejeitadas, adiadas e propostas.
/// Fuso de exibição: UTC (títulos "Sessão N — dd/MM HH:mm" usam o horário real em UTC nos testes).
/// </summary>
public static class TravelFixture
{
    public static readonly Guid ProjectId = Guid.Parse("6f1c2b3a-1111-4222-8333-444455556666");
    public static readonly TimeZoneInfo Zone = TimeZoneInfo.Utc;
    public static readonly DateTime CreatedAtUtc = new(2026, 11, 2, 12, 0, 0, DateTimeKind.Utc);

    public static Guid M(int n) => Guid.Parse($"00000000-0000-4000-8000-{n:D12}");
    public static Guid S(int n) => Guid.Parse($"10000000-0000-4000-8000-{n:D12}");
    public static Guid D(int n) => Guid.Parse($"20000000-0000-4000-8000-{n:D12}");

    public static PlanBuildContext Context(int revision = 1, int? baseRevision = null) =>
        new(PlanId: Guid.Parse("7a7a7a7a-0000-4000-8000-000000000001"), Revision: revision, BaseRevision: baseRevision,
            CreatedAtUtc: CreatedAtUtc, Author: EditPlanner.Author);

    /// <summary>Mídias da viagem. projectId permite criar um segundo projeto com as mesmas definições.</summary>
    public static List<MediaAsset> Media(Guid? projectId = null)
    {
        var p = projectId ?? ProjectId;
        var list = new List<MediaAsset>
        {
            // Sessão 1 (14/10 09:00–09:12)
            Build(p, 1, "dia1/01-conversa-jantar.mp4", MediaKind.Video, new DateTime(2026, 10, 14, 9, 0, 0, DateTimeKind.Utc), new DateTime(2026, 10, 14, 9, 1, 0, DateTimeKind.Utc), 60.0, 29.97, 2),
            Build(p, 2, "dia1/02-estrada-com-fala.mp4", MediaKind.Video, new DateTime(2026, 10, 14, 9, 5, 0, DateTimeKind.Utc), new DateTime(2026, 10, 14, 9, 6, 0, DateTimeKind.Utc), 40.0, 29.97, 2),
            Build(p, 3, "dia1/03-estrada-sem-fala.mp4", MediaKind.Video, new DateTime(2026, 10, 14, 9, 8, 0, DateTimeKind.Utc), new DateTime(2026, 10, 14, 9, 9, 0, DateTimeKind.Utc), 30.0, 30.0, 2),
            // Sessão 2 (14/10 11:00–11:04)
            Build(p, 4, "dia2/01-estrada-sem-transcricao.mp4", MediaKind.Video, new DateTime(2026, 10, 14, 11, 0, 0, DateTimeKind.Utc), new DateTime(2026, 10, 14, 11, 1, 0, DateTimeKind.Utc), 40.0, 29.97, 2),
            Build(p, 5, "dia2/02-drone.mp4", MediaKind.Video, new DateTime(2026, 10, 14, 11, 2, 0, DateTimeKind.Utc), new DateTime(2026, 10, 14, 11, 2, 30, DateTimeKind.Utc), 25.0, 29.97, 0),
            Build(p, 6, "dia2/03-audio-gravacao.wav", MediaKind.Audio, new DateTime(2026, 10, 14, 11, 3, 0, DateTimeKind.Utc), new DateTime(2026, 10, 14, 11, 3, 20, DateTimeKind.Utc), 20.0, 0, 2),
            // Sessão 3 (15/10 17:00): sem creation_time → fallback do sistema de arquivos.
            Build(p, 8, "dia3/05-conversa-final.mp4", MediaKind.Video, null, new DateTime(2026, 10, 15, 17, 0, 0, DateTimeKind.Utc), 20.0, 29.97, 2),
            // Excluída: metadados do FFprobe falharam.
            Build(p, 7, "dia3/04-sem-metadados.mp4", MediaKind.Video, new DateTime(2026, 10, 14, 17, 10, 0, DateTimeKind.Utc), new DateTime(2026, 10, 14, 17, 10, 0, DateTimeKind.Utc), 0.0, 29.97, 2, MediaMetadataStatus.Failed),
        };
        return list;
    }

    private static MediaAsset Build(
        Guid projectId,
        int n,
        string path,
        MediaKind kind,
        DateTime? creationUtc,
        DateTime lastWriteUtc,
        double durationSeconds,
        double frameRate,
        int audioChannels,
        string status = MediaMetadataStatus.Ok)
    {
        var meta = new MediaMetadata
        {
            MediaId = M(n),
            Codec = "h264",
            Width = 3840,
            Height = 2160,
            FrameRate = frameRate,
            DurationSeconds = durationSeconds,
            DurationTicks = (long)Math.Round(durationSeconds * 1000),
            AudioChannels = audioChannels,
            AudioSampleRate = audioChannels > 0 ? 48000 : 0,
            CreationTimeUtc = creationUtc,
            SourceDateEvidence = creationUtc is null ? string.Empty : "creation_time",
            MetadataStatus = status,
        };
        return new MediaAsset
        {
            MediaId = M(n),
            ProjectId = projectId,
            RelativePath = path,
            ByteLength = 1000 + n,
            LastWriteUtc = lastWriteUtc,
            Fingerprint = $"fp-{n}",
            Kind = kind,
            Metadata = meta,
        };
    }

    public static Dictionary<Guid, MediaAnalysisDto> Analyses()
    {
        return new Dictionary<Guid, MediaAnalysisDto>
        {
            [M(1)] = Analysis(1, "completed", Transcript(1, (0, 4), (5, 9), (14, 18), (22, 26), (34, 38), (45, 49), (54, 58)),
                Scene(1, "conversa", hypothesis: false)),
            [M(2)] = Analysis(2, "completed", Transcript(2, (1, 6), (15, 25)), Scene(2, "estrada", hypothesis: true)),
            [M(3)] = Analysis(3, "completed", Transcript(3), Scene(3, "estrada", hypothesis: true)),
            [M(4)] = Analysis(4, "unavailable", null, Scene(4, "estrada", hypothesis: true), "Whisper indisponível nesta máquina."),
            [M(5)] = Analysis(5, "completed", Transcript(5), Scene(5, "drone", hypothesis: false)),
            [M(8)] = Analysis(8, "not_run", null, Scene(8, "conversa", hypothesis: true)),
        };
    }

    private static MediaAnalysisDto Analysis(int n, string status, TranscriptDto? transcript, SceneClassificationDto? scene, string? reason = null) => new(
        MediaId: M(n),
        TranscriptionStatus: status,
        TranscriptionReason: reason,
        Transcript: transcript,
        SilenceStatus: "not_run",
        SilenceReason: null,
        Silences: new List<VadSilenceSegmentDto>(),
        Hesitations: new List<HesitationCandidateDto>(),
        Scene: scene,
        CutCandidates: new List<EditSuggestionDto>(),
        AnalyzedAtUtc: null);

    private static TranscriptDto Transcript(int n, params (double Start, double End)[] spans) => new(
        TranscriptId: Guid.Parse($"30000000-0000-4000-8000-{n:D12}"),
        MediaId: M(n),
        ModelId: "large-v3",
        Language: "pt",
        FullText: string.Empty,
        AudioDurationSeconds: 60,
        Words: spans.Select((s, i) => new TranscriptWordDto(i + 1, s.Start, s.End, "palavra", 0.9)).ToList());

    private static SceneClassificationDto Scene(int n, string category, bool hypothesis) => new(
        MediaId: M(n),
        Category: category,
        ConfidenceBand: "medium",
        IsHypothesis: hypothesis,
        Evidence: new List<string>(),
        HasSpeech: null);

    /// <summary>
    /// S1 approved silêncio 10,0–12,4 s · S2 rejected 30–33 · S3 proposed trivial 40,0–40,5 (auto) ·
    /// S4 proposed hesitação 50–52 (revisão) · S5 deferred 2–3 · S6 approved speed2x 0–20 em M4 (sem transcrição) ·
    /// S7 approved silêncio 10,0–24,9 em M5 (deixa 0,1 s descartado).
    /// </summary>
    public static List<EditSuggestion> Suggestions()
    {
        var t = new DateTime(2026, 10, 14, 9, 0, 0, DateTimeKind.Utc);
        return
        [
            Sug(1, 1, 10.0, 12.4, "silence", "cut", "high", false, t),
            Sug(2, 1, 30.0, 33.0, "silence", "cut", "high", false, t.AddSeconds(1)),
            Sug(3, 1, 40.0, 40.5, "silence", "cut", "high", false, t.AddSeconds(2)),
            Sug(4, 1, 50.0, 52.0, "hesitation", "cut", "medium", true, t.AddSeconds(3)),
            Sug(5, 2, 2.0, 3.0, "silence", "cut", "high", false, t.AddSeconds(4)),
            Sug(6, 4, 0.0, 20.0, "speed", "speed2x", "medium", true, t.AddSeconds(5)),
            Sug(7, 5, 10.0, 24.9, "silence", "cut", "high", false, t.AddSeconds(6)),
            Sug(8, 7, 1.0, 2.0, "silence", "cut", "high", false, t.AddSeconds(7)),
        ];
    }

    public static List<EditDecision> Decisions()
    {
        var t = new DateTime(2026, 10, 14, 10, 0, 0, DateTimeKind.Utc);
        return
        [
            Decision(1, 1, "approve", t),
            Decision(2, 2, "reject", t.AddSeconds(1)),
            Decision(5, 5, "defer", t.AddSeconds(2)),
            Decision(6, 6, "approve", t.AddSeconds(3)),
            Decision(7, 7, "approve", t.AddSeconds(4)),
        ];
    }

    private static EditSuggestion Sug(int n, int mediaN, double startS, double endS, string kind, string action,
        string band, bool review, DateTime created) => new()
    {
        SuggestionId = S(n),
        MediaId = M(mediaN),
        SourceInterval = new TimeRange((long)Math.Round(startS * 1000), (long)Math.Round(endS * 1000)),
        Kind = SuggestionKind.Silence,
        Rationale = $"Sugestão de teste {n}",
        EvidenceRefs = JsonSerializer.Serialize(new { kind }),
        ConfidenceBand = band,
        NeedsHumanReview = review,
        SuggestedAction = action,
        Status = SuggestionStatus.Proposed,
        Author = "vad-analyzer",
        CreatedAtUtc = created,
    };

    private static EditDecision Decision(int n, int suggestionN, string action, DateTime at) => new()
    {
        DecisionId = D(n),
        SuggestionId = S(suggestionN),
        Action = action,
        Reviewer = "user",
        Notes = string.Empty,
        TimestampUtc = at,
    };

    /// <summary>Snapshot completo como o use case montaria (cronologia só com mídias elegíveis).</summary>
    public static PlanningInput Input(
        EditorialProfileDto? profile = null,
        TimelineTimeBaseDto? timeBase = null,
        IEnumerable<MediaAsset>? media = null,
        IEnumerable<EditSuggestion>? suggestions = null,
        IEnumerable<EditDecision>? decisions = null,
        IReadOnlyDictionary<Guid, MediaAnalysisDto>? analyses = null)
    {
        var all = (media ?? Media()).ToList();
        var eligible = all.Where(PlanEligibility.IsEligible).ToList();
        var chronology = new ChronologyService().BuildChronology(eligible.Select(m => (Asset: m, Metadata: m.Metadata!)));
        return new PlanningInput(
            ProjectId: all.FirstOrDefault()?.ProjectId ?? ProjectId,
            Profile: profile ?? EditorialProfiles.Default(),
            RequestedTimeBase: timeBase,
            Media: all,
            Chronology: chronology,
            Analyses: analyses ?? Analyses(),
            Suggestions: (suggestions ?? Suggestions()).ToList(),
            Decisions: (decisions ?? Decisions()).ToList(),
            DisplayZone: Zone,
            TimeZoneId: Zone.Id);
    }

    public static EditPlanDto BuildPlan(PlanningInput input, int revision = 1, int? baseRevision = null)
    {
        var result = EditPlanner.Build(input, Context(revision, baseRevision));
        return result.Plan ?? throw new InvalidOperationException($"Planejador não gerou plano: {result.ErrorCode}");
    }
}
