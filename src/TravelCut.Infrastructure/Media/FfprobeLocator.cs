using TravelCut.Infrastructure.Media.Thumbnails;

namespace TravelCut.Infrastructure.Media;

/// <summary>
/// Localiza o ffprobe com a mesma política do FFmpeg: variável de ambiente, pasta do FFmpeg encontrado
/// (ffprobe ao lado do ffmpeg), locais comuns no Windows e PATH. Sempre devolve caminho absoluto ou null.
/// Nunca executa nada: só verifica a existência do arquivo.
/// </summary>
public static class FfprobeLocator
{
    public const string FfprobePathEnvironmentVariable = "TRAVELCUT_FFPROBE_PATH";

    private static readonly string[] CommonWindowsLocations =
    {
        @"C:\Program Files\ffmpeg\bin\ffprobe.exe",
        @"C:\ffmpeg\bin\ffprobe.exe"
    };

    /// <summary>Localização a partir do ambiente real (variáveis, PATH e FFmpeg encontrado).</summary>
    public static string? Locate()
    {
        return Resolve(
            configured: Environment.GetEnvironmentVariable(FfprobePathEnvironmentVariable),
            ffmpegPath: FfmpegLocator.Locate(),
            fileExists: File.Exists,
            pathDirectories: SplitPath(Environment.GetEnvironmentVariable("PATH")),
            commonWindowsLocations: OperatingSystem.IsWindows() ? CommonWindowsLocations : Array.Empty<string>(),
            isWindows: OperatingSystem.IsWindows());
    }

    /// <summary>
    /// Regra pura de resolução (testável): 1) caminho configurado; 2) ffprobe na mesma pasta do ffmpeg;
    /// 3) locais comuns (Windows); 4) diretórios do PATH. Retorna caminho absoluto existente ou null.
    /// </summary>
    public static string? Resolve(
        string? configured,
        string? ffmpegPath,
        Func<string, bool> fileExists,
        IEnumerable<string> pathDirectories,
        IEnumerable<string> commonWindowsLocations,
        bool isWindows)
    {
        string exe = isWindows ? "ffprobe.exe" : "ffprobe";

        if (!string.IsNullOrWhiteSpace(configured) && TryAbsolute(configured, out string? configuredFull) && fileExists(configuredFull!))
            return configuredFull;

        if (!string.IsNullOrWhiteSpace(ffmpegPath) && TryAbsolute(ffmpegPath, out string? ffmpegFull))
        {
            string? dir = Path.GetDirectoryName(ffmpegFull);
            if (!string.IsNullOrEmpty(dir))
            {
                string sibling = Path.Combine(dir, exe);
                if (fileExists(sibling)) return sibling;
            }
        }

        foreach (var candidate in commonWindowsLocations)
        {
            if (TryAbsolute(candidate, out string? full) && fileExists(full!)) return full;
        }

        foreach (var dir in pathDirectories)
        {
            if (string.IsNullOrWhiteSpace(dir)) continue;
            try
            {
                string candidate = Path.Combine(dir.Trim('"'), exe);
                if (TryAbsolute(candidate, out string? full) && fileExists(full!)) return full;
            }
            catch (ArgumentException)
            {
                // Entrada inválida no PATH: ignora.
            }
        }

        return null;
    }

    private static bool TryAbsolute(string path, out string? full)
    {
        try
        {
            full = Path.GetFullPath(path.Trim().Trim('"'));
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            full = null;
            return false;
        }
    }

    private static IEnumerable<string> SplitPath(string? pathVariable)
        => (pathVariable ?? string.Empty).Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
}
