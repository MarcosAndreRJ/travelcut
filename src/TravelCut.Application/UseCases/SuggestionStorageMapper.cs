using System.Text.Json;
using TravelCut.Contracts.Dtos;
using TravelCut.Domain.Entities;
using TravelCut.Domain.Enums;
using TravelCut.Domain.ValueObjects;

namespace TravelCut.Application.UseCases;

/// <summary>
/// Conversão única entre candidatos de corte (DTO) e <see cref="EditSuggestion"/> persistida.
/// Regras:
/// - Tempos são guardados em milissegundos da mídia fonte (<see cref="TicksPerSecond"/>), nunca em FPS fixo.
/// - O kind exato do DTO (silence/pause/hesitation/filler/repetition/false_start) é guardado em
///   <see cref="EditSuggestion.EvidenceRefs"/> porque o enum de domínio não tem todos os valores.
/// </summary>
public static class SuggestionStorageMapper
{
    public const long TicksPerSecond = 1000;

    public static long ToTicks(double seconds) =>
        (long)Math.Round(seconds * TicksPerSecond, MidpointRounding.AwayFromZero);

    public static double ToSeconds(long ticks) => ticks / (double)TicksPerSecond;

    public static SuggestionKind ToDomainKind(string dtoKind) => dtoKind switch
    {
        "silence" or "pause" => SuggestionKind.Silence,
        "false_start" => SuggestionKind.FalseStart,
        "repetition" => SuggestionKind.Repetition,
        _ => SuggestionKind.Hesitation
    };

    public static EditSuggestion ToEntity(EditSuggestionDto dto, DateTime createdAtUtc)
    {
        long start = ToTicks(dto.StartSeconds);
        long end = Math.Max(start, ToTicks(dto.EndSeconds));

        return new EditSuggestion
        {
            SuggestionId = dto.SuggestionId,
            MediaId = dto.MediaId,
            SourceInterval = new TimeRange(start, end),
            Kind = ToDomainKind(dto.Kind),
            Rationale = dto.Rationale,
            EvidenceRefs = JsonSerializer.Serialize(new { kind = dto.Kind }),
            ConfidenceBand = dto.ConfidenceBand,
            NeedsHumanReview = dto.NeedsHumanReview,
            SuggestedAction = dto.SuggestedAction,
            Status = SuggestionStatus.Proposed,
            Author = dto.Author,
            CreatedAtUtc = createdAtUtc
        };
    }

    public static EditSuggestionDto ToDto(EditSuggestion s) => new(
        SuggestionId: s.SuggestionId,
        MediaId: s.MediaId,
        StartSeconds: ToSeconds(s.SourceInterval.StartTicks),
        EndSeconds: ToSeconds(s.SourceInterval.EndTicks),
        Kind: ResolveDtoKind(s),
        Rationale: s.Rationale,
        ConfidenceBand: s.ConfidenceBand,
        NeedsHumanReview: s.NeedsHumanReview,
        SuggestedAction: s.SuggestedAction,
        Status: s.Status.ToString(),
        Author: s.Author);

    /// <summary>Kind do DTO recuperado de EvidenceRefs; sem JSON válido, deriva do enum de domínio.</summary>
    public static string ResolveDtoKind(EditSuggestion s)
    {
        var fromRefs = TryReadKindFromEvidenceRefs(s.EvidenceRefs);
        if (fromRefs != null) return fromRefs;

        return s.Kind switch
        {
            SuggestionKind.Silence => "silence",
            SuggestionKind.Hesitation => "hesitation",
            SuggestionKind.FalseStart => "false_start",
            SuggestionKind.Repetition => "repetition",
            _ => s.Kind.ToString().ToLowerInvariant()
        };
    }

    /// <summary>Mesmo intervalo e mesmo kind: usado para não duplicar uma sugestão já decidida pelo usuário.</summary>
    public static bool SameSuggestion(EditSuggestion existing, EditSuggestionDto candidate)
    {
        long start = ToTicks(candidate.StartSeconds);
        long end = Math.Max(start, ToTicks(candidate.EndSeconds));
        return existing.SourceInterval.StartTicks == start
            && existing.SourceInterval.EndTicks == end
            && string.Equals(ResolveDtoKind(existing), candidate.Kind, StringComparison.Ordinal);
    }

    private static string? TryReadKindFromEvidenceRefs(string? evidenceRefs)
    {
        if (string.IsNullOrWhiteSpace(evidenceRefs) || evidenceRefs[0] != '{') return null;
        try
        {
            using var doc = JsonDocument.Parse(evidenceRefs);
            if (doc.RootElement.TryGetProperty("kind", out var kind) && kind.ValueKind == JsonValueKind.String)
            {
                return kind.GetString();
            }
        }
        catch (JsonException)
        {
            // EvidenceRefs livre (formato antigo): cai no derivado do enum.
        }
        return null;
    }
}
