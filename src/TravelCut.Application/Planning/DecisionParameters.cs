using System.Text.Json;
using TravelCut.Contracts.Dtos;

namespace TravelCut.Application.Planning;

/// <summary>
/// F14.8: parâmetros opcionais da decisão (hoje só zoom). Validação/normalização na gravação e leitura tolerante
/// (JSON ausente ou inválido = nenhum parâmetro; projetos antigos seguem sem parâmetros).
/// A decisão humana vence a política: o planejador usa estes valores, sempre limitados a <see cref="MaxZoomScale"/>.
/// </summary>
public static class DecisionParameters
{
    public const double MinZoomScale = 1.01;
    public const double MaxZoomScale = 1.12;

    /// <summary>Valida e normaliza (escala limitada a 1,01–1,12). Retorna (json, null) ou (null, mensagem de erro).</summary>
    public static (string? Json, string? Error) Normalize(DecisionParametersDto? parameters, string suggestedAction)
    {
        if (parameters is null || parameters.Zoom is null) return (null, null);
        if (!string.Equals(suggestedAction, "zoom", StringComparison.Ordinal))
            return (null, "parameters.zoom só se aplica a sugestões de zoom.");

        var z = parameters.Zoom;
        if (!double.IsFinite(z.Scale) || z.Scale <= 0)
            return (null, "parameters.zoom.scale inválida: deve ser finita e positiva.");
        if (!double.IsFinite(z.FocalPointX) || !double.IsFinite(z.FocalPointY)
            || z.FocalPointX < 0 || z.FocalPointX > 1 || z.FocalPointY < 0 || z.FocalPointY > 1)
            return (null, "parameters.zoom: ponto focal deve estar entre 0 e 1.");
        if (!double.IsFinite(z.DurationSeconds) || z.DurationSeconds < 0)
            return (null, "parameters.zoom.durationSeconds inválida: deve ser finita e não negativa.");

        double scale = Math.Clamp(z.Scale, MinZoomScale, MaxZoomScale);
        var normalized = z with
        {
            ZoomType = string.IsNullOrWhiteSpace(z.ZoomType) ? SmartZoomConstants.TypePunchIn : z.ZoomType.Trim(),
            Scale = scale,
            StartScale = 1.0,
            EndScale = scale,
        };
        return (JsonSerializer.Serialize(new DecisionParametersDto(normalized)), null);
    }

    /// <summary>Leitura tolerante: null/vazio/JSON inválido → null.</summary>
    public static DecisionParametersDto? Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonSerializer.Deserialize<DecisionParametersDto>(json); }
        catch (JsonException) { return null; }
    }
}
