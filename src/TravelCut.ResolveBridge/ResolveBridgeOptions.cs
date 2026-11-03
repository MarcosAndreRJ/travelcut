namespace TravelCut.ResolveBridge;

/// <summary>
/// Configuração da bridge DaVinci (workers/resolve_bridge.py). Caminhos aqui NUNCA são copiados para
/// mensagens exibidas à UI ou logs de erro do adaptador.
/// </summary>
/// <param name="PythonExecutable">Null = descoberta (<c>py -3</c>, depois <c>python</c> no PATH).</param>
/// <param name="BridgeScriptPath">Caminho do resolve_bridge.py.</param>
/// <param name="ResolveExecutablePath">Resolve.exe; null = não verificar instalação por arquivo.</param>
/// <param name="ProbeTimeout">Timeout de probe e chamadas leves.</param>
/// <param name="EndFrameInclusive">Convenção do endFrame de AppendToTimeline (a confirmar no host; padrão true).</param>
public sealed record ResolveBridgeOptions(
    string? PythonExecutable,
    string BridgeScriptPath,
    string? ResolveExecutablePath,
    TimeSpan ProbeTimeout,
    bool EndFrameInclusive)
{
    public const string PythonEnvVar = "TRAVELCUT_PYTHON";
    public const string ResolvePathEnvVar = "TRAVELCUT_RESOLVE_PATH";
    public const string EndInclusiveEnvVar = "TRAVELCUT_RESOLVE_END_INCLUSIVE";

    public const string BridgeRelativeDirectory = "workers";
    public const string BridgeScriptFileName = "resolve_bridge.py";

    public static readonly TimeSpan DefaultProbeTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Lê variáveis de ambiente e caminhos padrão. O script é procurado em
    /// <c>&lt;appBase&gt;\workers</c>; em builds Debug também sobe diretórios (desenvolvimento a partir do repositório).
    /// </summary>
    public static ResolveBridgeOptions FromEnvironment(string appBaseDirectory)
    {
        if (string.IsNullOrWhiteSpace(appBaseDirectory))
        {
            throw new ArgumentException("Diretório base do aplicativo é obrigatório.", nameof(appBaseDirectory));
        }

        var python = ReadEnv(PythonEnvVar);

        var resolvePath = ReadEnv(ResolvePathEnvVar) ?? Path.Combine(
            ReadEnv("ProgramFiles") ?? @"C:\Program Files",
            "Blackmagic Design", "DaVinci Resolve", "Resolve.exe");

        var endInclusive = !string.Equals(ReadEnv(EndInclusiveEnvVar), "false", StringComparison.OrdinalIgnoreCase);

        return new ResolveBridgeOptions(
            PythonExecutable: python,
            BridgeScriptPath: FindBridgeScript(appBaseDirectory),
            ResolveExecutablePath: resolvePath,
            ProbeTimeout: DefaultProbeTimeout,
            EndFrameInclusive: endInclusive);
    }

    private static string FindBridgeScript(string appBaseDirectory)
    {
        var direct = Path.Combine(appBaseDirectory, BridgeRelativeDirectory, BridgeScriptFileName);
        if (File.Exists(direct))
        {
            return direct;
        }

#if DEBUG
        // Desenvolvimento: sobe a partir do diretório do executável até achar <repo>\workers.
        var current = new DirectoryInfo(appBaseDirectory);
        for (var depth = 0; current is not null && depth < 8; depth++, current = current.Parent)
        {
            var candidate = Path.Combine(current.FullName, BridgeRelativeDirectory, BridgeScriptFileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }
#endif

        // Inexistente: o adaptador reporta BRIDGE_SCRIPT_MISSING sem expor o caminho.
        return direct;
    }

    private static string? ReadEnv(string name)
    {
        var value = Environment.GetEnvironmentVariable(name);
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
