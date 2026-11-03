using TravelCut.Contracts.Dtos;

namespace TravelCut.Application.Motion;

public enum MotionParameterKind
{
    Text,
    HexColor,   // #RRGGBB
    AssetPath   // relativo a 2-Assets; extensão de imagem permitida
}

public sealed record MotionParameterSpec(string Name, bool Required, int MaxLength, MotionParameterKind Kind);

public sealed record MotionTemplateSpec(string TemplateId, string Description, IReadOnlyList<MotionParameterSpec> Parameters);

/// <summary>
/// Catálogo fechado de templates de animação (F09.5). Somente parâmetros listados são aceitos.
/// Os nomes de parâmetros são chaves técnicas estáveis; os textos exibidos ao usuário ficam na UI.
/// </summary>
public static class MotionTemplateCatalog
{
    public static readonly IReadOnlyList<MotionTemplateSpec> Templates = new[]
    {
        new MotionTemplateSpec(
            MotionTemplateIds.TitleCard,
            "Cartão de título com subtítulo opcional",
            new[]
            {
                new MotionParameterSpec("title", Required: true, MaxLength: 120, MotionParameterKind.Text),
                new MotionParameterSpec("subtitle", Required: false, MaxLength: 160, MotionParameterKind.Text),
                new MotionParameterSpec("accentColor", Required: false, MaxLength: 7, MotionParameterKind.HexColor),
                new MotionParameterSpec("backgroundAsset", Required: false, MaxLength: 260, MotionParameterKind.AssetPath),
            }),
        new MotionTemplateSpec(
            MotionTemplateIds.LowerThird,
            "Faixa inferior com duas linhas de texto",
            new[]
            {
                new MotionParameterSpec("line1", Required: true, MaxLength: 80, MotionParameterKind.Text),
                new MotionParameterSpec("line2", Required: false, MaxLength: 80, MotionParameterKind.Text),
                new MotionParameterSpec("accentColor", Required: false, MaxLength: 7, MotionParameterKind.HexColor),
            }),
        new MotionTemplateSpec(
            MotionTemplateIds.MapRoute,
            "Rota entre dois pontos sobre mapa autorizado",
            new[]
            {
                new MotionParameterSpec("origin", Required: true, MaxLength: 80, MotionParameterKind.Text),
                new MotionParameterSpec("destination", Required: true, MaxLength: 80, MotionParameterKind.Text),
                new MotionParameterSpec("routeLabel", Required: false, MaxLength: 120, MotionParameterKind.Text),
                new MotionParameterSpec("accentColor", Required: false, MaxLength: 7, MotionParameterKind.HexColor),
                new MotionParameterSpec("mapAsset", Required: false, MaxLength: 260, MotionParameterKind.AssetPath),
            }),
    };

    public const int MaxParameterCount = 16;

    public static bool TryGet(string? templateId, out MotionTemplateSpec? spec)
    {
        spec = Templates.FirstOrDefault(t => string.Equals(t.TemplateId, templateId, StringComparison.Ordinal));
        return spec is not null;
    }
}
