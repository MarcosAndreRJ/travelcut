using System.Text.RegularExpressions;
using TravelCut.Application.Services;
using TravelCut.Contracts.Dtos;

namespace TravelCut.Application.Motion;

public sealed record MotionValidation(string? Code, string? Message)
{
    public bool IsValid => Code is null;

    public static MotionValidation Ok { get; } = new(null, null);

    public static MotionValidation Fail(string code, string message) => new(code, message);
}

/// <summary>
/// Validação pura de pedidos de render (F09.5). Mensagens nunca contêm caminhos absolutos.
/// Ordem: projeto, chave, template, dimensões, fps, duração, parâmetros (conhecidos, obrigatórios, tamanho, tipo, assets).
/// </summary>
public static class MotionRequestValidator
{
    public const string ProjectRootInvalidCode = "PROJECT_ROOT_INVALID";
    public const string ProjectIdInvalidCode = "PROJECT_ID_INVALID";
    public const string IdempotencyKeyInvalidCode = "IDEMPOTENCY_KEY_INVALID";
    public const string UnknownTemplateCode = "UNKNOWN_TEMPLATE";
    public const string DimensionsInvalidCode = "DIMENSIONS_INVALID";
    public const string FrameRateInvalidCode = "FRAME_RATE_INVALID";
    public const string DurationInvalidCode = "DURATION_INVALID";
    public const string ParametersTooManyCode = "PARAMETERS_TOO_MANY";
    public const string ParameterNameInvalidCode = "PARAMETER_NAME_INVALID";
    public const string ParameterNotAllowedCode = "PARAMETER_NOT_ALLOWED";
    public const string ParameterRequiredCode = "PARAMETER_REQUIRED";
    public const string ParameterTooLongCode = "PARAMETER_TOO_LONG";
    public const string ParameterInvalidCode = "PARAMETER_INVALID";
    public const string AssetOutsideAssetsCode = "ASSET_OUTSIDE_ASSETS";
    public const string AssetTypeNotAllowedCode = "ASSET_TYPE_NOT_ALLOWED";
    public const string AssetNotFoundCode = "ASSET_NOT_FOUND";
    /// <summary>Link, junção ou atalho em qualquer segmento entre o projeto e o asset/saída.</summary>
    public const string PermissionDeniedCode = "PERMISSION_DENIED";

    public const int MinDimension = 320;
    public const int MaxWidth = 3840;
    public const int MaxHeight = 2160;
    public const long MaxFramesPerSecond = 120;
    public const long MaxFrameRateNum = 240000;
    public const long MaxFrameRateDen = 1001;
    public const long MaxDurationFrames = 7200;
    public const long MaxDurationSeconds = 120;

    public static readonly IReadOnlyList<string> AllowedAssetExtensions = new[] { ".png", ".jpg", ".jpeg", ".webp" };

    private static readonly Regex IdempotencyKeyPattern = new(@"^[A-Za-z0-9._-]{8,128}$", RegexOptions.CultureInvariant);
    private static readonly Regex ParameterNamePattern = new(@"^[A-Za-z][A-Za-z0-9]{0,31}$", RegexOptions.CultureInvariant);
    private static readonly Regex HexColorPattern = new(@"^#[0-9A-Fa-f]{6}$", RegexOptions.CultureInvariant);

    public static MotionValidation Validate(MotionRenderRequestDto request, string projectRoot)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!MotionPaths.IsUsableProjectRoot(projectRoot))
            return MotionValidation.Fail(ProjectRootInvalidCode, "Pasta do projeto indisponível para o render.");

        if (request.ProjectId == Guid.Empty)
            return MotionValidation.Fail(ProjectIdInvalidCode, "Identificador do projeto inválido.");

        if (request.IdempotencyKey is null || !IdempotencyKeyPattern.IsMatch(request.IdempotencyKey))
            return MotionValidation.Fail(IdempotencyKeyInvalidCode,
                "Chave de idempotência deve ter de 8 a 128 caracteres: letras, dígitos, ponto, sublinhado ou hífen.");

        if (!MotionTemplateCatalog.TryGet(request.TemplateId, out var spec) || spec is null)
            return MotionValidation.Fail(UnknownTemplateCode, "Template de animação desconhecido.");

        if (!IsValidDimensions(request.Width, request.Height))
            return MotionValidation.Fail(DimensionsInvalidCode,
                $"Dimensões fora da faixa: largura de {MinDimension} a {MaxWidth} e altura de {MinDimension} a {MaxHeight}, ambas pares.");

        if (!IsValidFrameRate(request.FrameRateNum, request.FrameRateDen))
            return MotionValidation.Fail(FrameRateInvalidCode,
                $"Taxa de quadros fora da faixa: de 1 a {MaxFramesPerSecond} fps (numerador até {MaxFrameRateNum}, denominador até {MaxFrameRateDen}).");

        if (!IsValidDuration(request.DurationFrames, request.FrameRateNum, request.FrameRateDen))
            return MotionValidation.Fail(DurationInvalidCode,
                $"Duração fora da faixa: de 1 a {MaxDurationFrames} quadros e no máximo {MaxDurationSeconds} s.");

        var parameters = request.Parameters ?? new Dictionary<string, string>();
        if (parameters.Count > MotionTemplateCatalog.MaxParameterCount)
            return MotionValidation.Fail(ParametersTooManyCode,
                $"Número de parâmetros acima do limite de {MotionTemplateCatalog.MaxParameterCount}.");

        foreach (var key in parameters.Keys)
        {
            if (!ParameterNamePattern.IsMatch(key))
                return MotionValidation.Fail(ParameterNameInvalidCode, "Nome de parâmetro inválido.");

            if (!spec.Parameters.Any(p => string.Equals(p.Name, key, StringComparison.Ordinal)))
                return MotionValidation.Fail(ParameterNotAllowedCode, $"Parâmetro não permitido para este template: {key}.");
        }

        foreach (var p in spec.Parameters)
        {
            if (!parameters.TryGetValue(p.Name, out var value))
            {
                if (p.Required)
                    return MotionValidation.Fail(ParameterRequiredCode, $"Parâmetro obrigatório ausente: {p.Name}.");
                continue;
            }

            if (string.IsNullOrWhiteSpace(value))
                return MotionValidation.Fail(p.Required ? ParameterRequiredCode : ParameterInvalidCode,
                    $"Parâmetro vazio: {p.Name}.");

            if (value.Length > p.MaxLength)
                return MotionValidation.Fail(ParameterTooLongCode,
                    $"Parâmetro {p.Name} excede {p.MaxLength} caracteres.");

            if (value != value.Trim() || value.Any(char.IsControl))
                return MotionValidation.Fail(ParameterInvalidCode,
                    $"Parâmetro {p.Name} contém espaços nas extremidades ou caracteres de controle.");

            var check = p.Kind switch
            {
                MotionParameterKind.HexColor when !HexColorPattern.IsMatch(value) =>
                    MotionValidation.Fail(ParameterInvalidCode, $"Parâmetro {p.Name} deve estar no formato #RRGGBB."),
                MotionParameterKind.AssetPath => ValidateAsset(p.Name, value, projectRoot),
                _ => MotionValidation.Ok,
            };

            if (!check.IsValid)
                return check;
        }

        return MotionValidation.Ok;
    }

    public static bool IsValidDimensions(int width, int height) =>
        width >= MinDimension && width <= MaxWidth && height >= MinDimension && height <= MaxHeight
        && width % 2 == 0 && height % 2 == 0;

    /// <summary>fps = num/den em [1, 120]; den até 1001 (cobre 30000/1001) e num limitado.</summary>
    public static bool IsValidFrameRate(long num, long den) =>
        num > 0 && den > 0 && den <= MaxFrameRateDen && num <= MaxFrameRateNum
        && num >= den && num <= MaxFramesPerSecond * den;

    public static bool IsValidDuration(long durationFrames, long num, long den) =>
        durationFrames >= 1 && durationFrames <= MaxDurationFrames
        && IsValidFrameRate(num, den)
        && durationFrames * den <= MaxDurationSeconds * num;

    private static MotionValidation ValidateAsset(string name, string value, string projectRoot)
    {
        if (!ImportPathRules.IsSafeLibraryRelativePath(value))
            return MotionValidation.Fail(AssetOutsideAssetsCode,
                $"Asset de {name} deve ser caminho relativo dentro de 2-Assets do projeto.");

        string extension = Path.GetExtension(value).ToLowerInvariant();
        if (!AllowedAssetExtensions.Contains(extension))
            return MotionValidation.Fail(AssetTypeNotAllowedCode,
                $"Asset de {name} deve ser PNG, JPG ou WebP.");

        if (!MotionPaths.TryResolveAssetPath(projectRoot, value, out var full))
            return MotionValidation.Fail(AssetOutsideAssetsCode,
                $"Asset de {name} deve estar dentro de 2-Assets do projeto.");

        // Qualquer segmento entre o projeto e o arquivo (2-Assets, subpastas, arquivo) não pode ser link, junção ou atalho.
        if (MotionPaths.HasReparsePointBetween(projectRoot, full))
            return MotionValidation.Fail(PermissionDeniedCode,
                $"Asset de {name} não pode estar em caminho com link, junção ou atalho.");

        if (!File.Exists(full))
            return MotionValidation.Fail(AssetNotFoundCode, $"Asset de {name} não encontrado em 2-Assets.");

        return MotionValidation.Ok;
    }
}
