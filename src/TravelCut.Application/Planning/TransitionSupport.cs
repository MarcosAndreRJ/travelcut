using System.Text.Json;
using System.Text.Json.Serialization;

namespace TravelCut.Application.Planning;

/// <summary>
/// Parâmetros de uma sugestão "transition" (guardados em <c>EditSuggestion.EvidenceRefs</c>, como o zoom guarda os seus).
/// Descrevem a fronteira REAL entre dois clipes V1 do plano: mídia/ponto de saída do clipe A e mídia/ponto de entrada do clipe B
/// (µs na fonte). Se o plano mudar e a fronteira deixar de existir, a sugestão não casa mais e simplesmente não é aplicada.
/// O campo "kind" = "transition" permite que <c>SuggestionStorageMapper.ResolveDtoKind</c> resolva o kind do DTO.
/// </summary>
public sealed record TransitionSuggestionParams(
    [property: JsonPropertyName("kind")] string Kind,
    [property: JsonPropertyName("transitionKind")] string TransitionKind,
    [property: JsonPropertyName("durationFrames")] int DurationFrames,
    [property: JsonPropertyName("fromMediaId")] Guid FromMediaId,
    [property: JsonPropertyName("fromOutUs")] long FromOutUs,
    [property: JsonPropertyName("toMediaId")] Guid ToMediaId,
    [property: JsonPropertyName("toInUs")] long ToInUs);

/// <summary>Vocabulário, serialização e validação pura de handles das transições do plano (sem IO).</summary>
public static class TransitionSupport
{
    public const string SuggestedAction = "transition";
    public const string DtoKind = "transition";
    public const string Fade = "fade";
    public const string Dissolve = "dissolve";
    public const string Crossfade = "crossfade";
    public const string TransitionSuggestionKind = "transition_suggestion";
    public const string Author = "transition-recommender/2";

    /// <summary>Duração padrão das transições sugeridas (12 quadros, igual ao dissolve legado por seção).</summary>
    public const int DefaultFrames = 12;

    public static bool IsKnownKind(string? kind) => kind is Fade or Dissolve or Crossfade;

    public static string Serialize(TransitionSuggestionParams p) => JsonSerializer.Serialize(p);

    /// <summary>Leitura tolerante: null/vazio/JSON inválido/tipo desconhecido/duração não positiva → null.</summary>
    public static TransitionSuggestionParams? Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json) || json[0] != '{') return null;
        try
        {
            var p = JsonSerializer.Deserialize<TransitionSuggestionParams>(json);
            return p is not null && p.Kind == DtoKind && IsKnownKind(p.TransitionKind) && p.DurationFrames > 0 ? p : null;
        }
        catch (JsonException) { return null; }
    }

    /// <summary>
    /// Mesma regra de <c>EditorialEffectsService.ValidateHandlesAsync</c>, sem IO. Dissolve/crossfade exigem metade da duração em CADA
    /// lado (cauda do clipe A = duração da mídia − saída; cabeça do clipe B = entrada). Fade simples não consome handle, mas exige taxa
    /// de quadros conhecida. Bordas, duração da mídia ou taxa desconhecidas nunca são presumidas (Unknown).
    /// </summary>
    public static TransitionHandleState EvaluateHandles(
        string kind, long durationFrames, double timelineFps,
        double? fpsA, long mediaADurationUs, long outAUs,
        double? fpsB, long inBUs)
    {
        if (!IsKnownKind(kind) || durationFrames <= 0 || !(timelineFps is > 0 and < 1000)) return TransitionHandleState.Unknown;
        bool audioOnly = kind == Crossfade;

        bool fpsOk = ValidFps(fpsA) is not null && ValidFps(fpsB) is not null;
        if (!audioOnly && !fpsOk) return TransitionHandleState.Unknown;
        if (kind == Fade) return TransitionHandleState.Sufficient;

        if (mediaADurationUs <= 0 || outAUs < 0 || outAUs > mediaADurationUs || inBUs < 0) return TransitionHandleState.Unknown;
        double perSide = durationFrames / timelineFps / 2.0;
        double tailA = (mediaADurationUs - outAUs) / 1_000_000d;
        double headB = inBUs / 1_000_000d;

        if (audioOnly) return tailA + 1e-9 >= perSide && headB + 1e-9 >= perSide ? TransitionHandleState.Sufficient : TransitionHandleState.Insufficient;

        int reqA = (int)Math.Ceiling(perSide * fpsA!.Value - 1e-9);
        int reqB = (int)Math.Ceiling(perSide * fpsB!.Value - 1e-9);
        int availA = (int)Math.Floor(tailA * fpsA.Value + 1e-9);
        int availB = (int)Math.Floor(headB * fpsB.Value + 1e-9);
        return availA >= reqA && availB >= reqB ? TransitionHandleState.Sufficient : TransitionHandleState.Insufficient;
    }

    private static double? ValidFps(double? fps) => fps is > 0 and < 1000 ? fps : null;
}
