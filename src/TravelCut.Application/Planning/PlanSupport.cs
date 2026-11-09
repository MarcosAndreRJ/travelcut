using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using TravelCut.Application.Ports;
using TravelCut.Contracts.Dtos;
using TravelCut.Domain.Entities;
using TravelCut.Domain.Enums;
using TravelCut.Domain.Planning;
using TravelCut.Domain.ValueObjects;

namespace TravelCut.Application.Planning;

/// <summary>Elegibilidade de mídias para o plano e conversões de tempo/fala (funções puras).</summary>
public static class PlanEligibility
{
    /// <summary>
    /// Limiar de trecho residual curto (0,25 s). Trecho abaixo dele NÃO é descartado: permanece como clipe com
    /// revisão humana (pending "residual_segment") desde que arredonde para ≥ 1 quadro. Só sai o que arredonda para 0.
    /// </summary>
    public const long MinSegmentUs = 250_000;

    /// <summary>Valor de SourceDateEvidence/TimestampSource quando o horário veio da data de modificação do arquivo.</summary>
    public const string FallbackTimestampSource = "file_system_last_write_fallback";

    public static long DurationUs(MediaAsset media)
    {
        var seconds = media.Metadata?.DurationSeconds ?? 0;
        if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds <= 0) return 0;
        return (long)Math.Round(seconds * 1_000_000d, MidpointRounding.AwayFromZero);
    }

    /// <summary>Motivo de exclusão ou null quando a mídia entra no plano (Video/Audio, metadados ok, duração &gt; 0).</summary>
    public static string? ExclusionReason(MediaAsset media)
    {
        if (media.Kind is not (MediaKind.Video or MediaKind.Audio))
            return $"tipo '{media.Kind}' não entra na linha do tempo";
        var meta = media.Metadata;
        if (meta is null) return "sem metadados do FFprobe";
        if (!string.Equals(meta.MetadataStatus, MediaMetadataStatus.Ok, StringComparison.Ordinal))
            return $"sem metadados do FFprobe (status: {meta.MetadataStatus})";
        if (DurationUs(media) <= 0) return "duração zero ou ausente nos metadados";
        return null;
    }

    public static bool IsEligible(MediaAsset media) => ExclusionReason(media) is null;

    /// <summary>Mídias elegíveis sem duplicar MediaId, ordenadas por MediaId (ordem da lista não influencia o resultado).</summary>
    public static List<MediaAsset> Eligible(IEnumerable<MediaAsset> media) =>
        media.Where(IsEligible).DistinctBy(m => m.MediaId).OrderBy(m => m.MediaId).ToList();

    /// <summary>Fala só é "conhecida" com transcrição concluída. Sem transcrição, a fala é desconhecida (não acelerar).</summary>
    public static bool SpeechKnown(MediaAnalysisDto? analysis) =>
        analysis is { TranscriptionStatus: "completed", Transcript: not null };

    public static IReadOnlyList<(long StartUs, long EndUs)> SpeechIntervals(TranscriptDto transcript) =>
        transcript.Words
            .Where(w => w.EndSeconds > w.StartSeconds)
            .Select(w => (StartUs: ToUs(w.StartSeconds), EndUs: ToUs(w.EndSeconds)))
            .ToList();

    public static Dictionary<Guid, IReadOnlyList<(long StartUs, long EndUs)>> SpeechByMedia(
        IEnumerable<MediaAsset> eligible,
        IReadOnlyDictionary<Guid, MediaAnalysisDto> analyses)
    {
        var result = new Dictionary<Guid, IReadOnlyList<(long StartUs, long EndUs)>>();
        foreach (var media in eligible)
        {
            if (analyses.TryGetValue(media.MediaId, out var analysis) && SpeechKnown(analysis))
            {
                result[media.MediaId] = SpeechIntervals(analysis!.Transcript!);
            }
        }
        return result;
    }

    public static bool Overlaps(IReadOnlyList<(long StartUs, long EndUs)> speech, long startUs, long endUs) =>
        speech.Any(s => s.StartUs < endUs && s.EndUs > startUs);

    public static long ToUs(double seconds) =>
        Math.Max(0, (long)Math.Round(seconds * 1_000_000d, MidpointRounding.AwayFromZero));
}

/// <summary>Validação de um plano contra o estado ATUAL das mídias (usada na geração, plan.validate e revert).</summary>
public static class PlanValidation
{
    public static Dictionary<Guid, MediaFacts> FactsOf(IEnumerable<MediaAsset> media) =>
        PlanEligibility.Eligible(media).ToDictionary(
            m => m.MediaId,
            m => new MediaFacts(m.MediaId, PlanEligibility.DurationUs(m), m.Fingerprint ?? string.Empty, m.RelativePath));

    public static string MediaSetFingerprintOf(IEnumerable<MediaAsset> media) =>
        PlanFingerprint.ComputeMediaSet(FactsOf(media).Values);

    public static IReadOnlyList<PlanIssue> Run(
        PlanRevision plan,
        IEnumerable<MediaAsset> media,
        IReadOnlyDictionary<Guid, MediaAnalysisDto> analyses)
    {
        var eligible = PlanEligibility.Eligible(media);
        var facts = FactsOf(eligible);
        var speech = PlanEligibility.SpeechByMedia(eligible, analyses);
        var fingerprint = PlanFingerprint.ComputeMediaSet(facts.Values);
        return PlanValidator.Validate(plan, facts, speech, fingerprint);
    }
}

/// <summary>IDs determinísticos (UUIDv5-like sobre SHA-256). Necessários para que a mesma entrada gere o mesmo planFingerprint.</summary>
public static class DeterministicIds
{
    public static Guid Create(params string[] parts)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('|', parts)));
        var bytes = new byte[16];
        Array.Copy(hash, bytes, 16);
        bytes[6] = (byte)((bytes[6] & 0x0F) | 0x50);
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);
        return new Guid(bytes);
    }
}

/// <summary>Perfil editorial "viagens" v1 e validação dos valores aceitos.</summary>
public static class EditorialProfiles
{
    public const string ViagensId = "viagens";

    private static readonly string[] SilencePolicies = ["conservative", "balanced", "aggressive"];
    private static readonly string[] Paces = ["calm", "normal", "fast"];
    private static readonly string[] JokePolicies = ["strict", "normal"];
    private static readonly string[] ZoomStrengths = ["off", "subtle", "medium"];
    private static readonly string[] SpeedBrollValues = ["off", "2x", "4x", "auto"];
    private static readonly string[] MusicPolicies = ["suggest_only", "off"];

    public static EditorialProfileDto Default() => new(
        ProfileId: ViagensId,
        Version: 1,
        SilencePolicy: "balanced", // perfil da política editorial automática (default Balanced)
        Pace: "normal",
        JokePreservation: "strict",
        ZoomStrength: "subtle",
        SpeedBroll: "auto",
        MusicPolicy: "suggest_only",
        ReviewAmbiguous: true,
        AutoApplyTrivialCuts: true);

    /// <summary>
    /// Resolve o perfil pedido. Null = padrão. Valores fora do vocabulário geram <paramref name="error"/>.
    /// reviewAmbiguous é regra crítica: sempre true (mesmo se o cliente enviar false).
    /// </summary>
    public static EditorialProfileDto Resolve(EditorialProfileDto? requested, out string? error)
    {
        error = null;
        if (requested is null) return Default();
        if (!string.Equals(requested.ProfileId, ViagensId, StringComparison.Ordinal))
        {
            error = "Perfil editorial desconhecido (na v1 somente 'viagens').";
            return Default();
        }
        if (requested.Version != 1)
        {
            error = "Versão de perfil não suportada (na v1 somente versão 1).";
            return Default();
        }
        if (!Allowed(requested.SilencePolicy, SilencePolicies)) error = "silencePolicy inválida.";
        else if (!Allowed(requested.Pace, Paces)) error = "pace inválido.";
        else if (!Allowed(requested.JokePreservation, JokePolicies)) error = "jokePreservation inválida.";
        else if (!Allowed(requested.ZoomStrength, ZoomStrengths)) error = "zoomStrength inválido.";
        else if (!Allowed(requested.SpeedBroll, SpeedBrollValues)) error = "speedBroll inválido.";
        else if (!Allowed(requested.MusicPolicy, MusicPolicies)) error = "musicPolicy inválida.";

        if (error is not null) return Default();
        return requested with { ReviewAmbiguous = true };
    }

    private static bool Allowed(string? value, string[] allowed) =>
        value is not null && allowed.Contains(value, StringComparer.Ordinal);
}

/// <summary>Serialização do artefato JSON de uma revisão (EditPlanDto).</summary>
public static class PlanJson
{
    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private static readonly JsonSerializerOptions ReadOptions = new() { PropertyNameCaseInsensitive = true };

    public static string Serialize(EditPlanDto plan) => JsonSerializer.Serialize(plan, WriteOptions);

    /// <summary>
    /// Lê uma revisão. Falso para JSON inválido, documento "null" ou estrutura incompleta (campos obrigatórios ausentes
    /// ou listas nulas). Nunca devolve um plano vazio no lugar de um arquivo corrompido.
    /// </summary>
    public static bool TryDeserialize(string json, out EditPlanDto? plan)
    {
        plan = null;
        EditPlanDto? dto;
        try
        {
            dto = JsonSerializer.Deserialize<EditPlanDto>(json, ReadOptions);
        }
        catch (JsonException)
        {
            return false;
        }

        if (dto is null || !IsStructurallyComplete(dto)) return false;
        plan = dto;
        return true;
    }

    private static bool IsStructurallyComplete(EditPlanDto p) =>
        p.PlanId != Guid.Empty
        && p.ProjectId != Guid.Empty
        && !string.IsNullOrEmpty(p.PlanFingerprint)
        && !string.IsNullOrEmpty(p.TimeZoneId)
        && !string.IsNullOrEmpty(p.MediaSetFingerprint)
        && p.Profile is not null
        && p.TimeBase is not null
        && p.TimeBase.FrameDurationNum > 0
        && p.TimeBase.FrameDurationDen > 0
        && p.Summary is not null
        && p.Validation is not null
        && p.Validation.Issues is not null
        && p.Tracks is not null && p.Tracks.All(t => t is not null)
        && p.Sections is not null && p.Sections.All(s => s is not null)
        && p.Clips is not null && p.Clips.All(c => c is not null && c.Evidence is not null && c.SuggestionIds is not null)
        && p.Transitions is not null && p.Transitions.All(t => t is not null)
        && p.AppliedCuts is not null && p.AppliedCuts.All(c => c is not null)
        && p.Pending is not null && p.Pending.All(i => i is not null)
        && p.Validation.Issues.All(i => i is not null);
}

/// <summary>Caminhos relativos à raiz do projeto (3-Trabalho é a única pasta de escrita do planejador).</summary>
public static class PlanStoragePaths
{
    public const string PlansFolder = "3-Trabalho/plans";

    public static string RelativeJsonPath(Guid planId, int revision) =>
        $"{PlansFolder}/{planId:D}/r{revision:D4}.json";

    public static string PlanDirectoryRelativePath(Guid planId) => $"{PlansFolder}/{planId:D}";
}
