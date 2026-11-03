namespace TravelCut.Infrastructure.Motion;

/// <summary>
/// Configuração do renderer HyperFrames. O caminho do CLI vem de TRAVELCUT_HYPERFRAMES_CLI (executável ou script .js/.mjs/.cjs local).
/// O Node.js é resolvido para caminho absoluto: TRAVELCUT_NODE_PATH (opcional, arquivo existente) ou o PATH do host, sem
/// considerar o diretório atual. Nenhum download, nenhum npx e nenhuma instalação global é feito pelo TravelCut.
/// </summary>
public sealed record HyperFramesOptions(
    string? CliPath,
    TimeSpan NodeCheckTimeout,
    TimeSpan CliVersionTimeout,
    TimeSpan RenderTimeout,
    string? NodePath = null)
{
    public const string CliEnvironmentVariable = "TRAVELCUT_HYPERFRAMES_CLI";
    public const string NodeEnvironmentVariable = "TRAVELCUT_NODE_PATH";

    public static readonly TimeSpan DefaultNodeCheckTimeout = TimeSpan.FromSeconds(10);
    public static readonly TimeSpan DefaultCliVersionTimeout = TimeSpan.FromSeconds(15);
    public static readonly TimeSpan DefaultRenderTimeout = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Monta as opções a partir do valor bruto de TRAVELCUT_HYPERFRAMES_CLI (aspas e espaços são removidos) e lê
    /// TRAVELCUT_NODE_PATH do ambiente. Sem TRAVELCUT_NODE_PATH, o Node é procurado no PATH.
    /// </summary>
    public static HyperFramesOptions FromCliSetting(string? rawValue) =>
        new(
            CliPath: NormalizeCliSetting(rawValue),
            NodeCheckTimeout: DefaultNodeCheckTimeout,
            CliVersionTimeout: DefaultCliVersionTimeout,
            RenderTimeout: DefaultRenderTimeout,
            NodePath: NormalizeCliSetting(Environment.GetEnvironmentVariable(NodeEnvironmentVariable)));

    public static string? NormalizeCliSetting(string? rawValue)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
            return null;

        string trimmed = rawValue.Trim().Trim('"').Trim();
        return trimmed.Length == 0 ? null : trimmed;
    }
}
