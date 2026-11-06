using System.Diagnostics;
using TravelCut.Application.Ports;

namespace TravelCut.Infrastructure.Tooling;

/// <summary>
/// Utilitário seguro para localização de ferramentas no Windows sem shell arbitrário.
/// </summary>
public static class ToolPathResolver
{
    public static string? FindInPath(string binaryName)
    {
        var pathEnv = System.Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(pathEnv)) return null;

        var extensions = OperatingSystem.IsWindows()
            ? [".exe", ".cmd", ".bat", ""]
            : new[] { "" };

        var paths = pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var dir in paths)
        {
            foreach (var ext in extensions)
            {
                var candidate = Path.Combine(dir, binaryName.EndsWith(ext, StringComparison.OrdinalIgnoreCase) ? binaryName : binaryName + ext);
                if (File.Exists(candidate)) return Path.GetFullPath(candidate);
            }
        }

        return null;
    }

    public static List<string> FindAllInPath(string binaryName)
    {
        var results = new List<string>();
        var pathEnv = System.Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(pathEnv)) return results;

        var extensions = OperatingSystem.IsWindows()
            ? [".exe", ".cmd", ".bat", ""]
            : new[] { "" };

        var paths = pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var dir in paths)
        {
            foreach (var ext in extensions)
            {
                var candidate = Path.Combine(dir, binaryName.EndsWith(ext, StringComparison.OrdinalIgnoreCase) ? binaryName : binaryName + ext);
                if (File.Exists(candidate))
                {
                    var full = Path.GetFullPath(candidate);
                    if (!results.Contains(full, StringComparer.OrdinalIgnoreCase))
                        results.Add(full);
                }
            }
        }

        return results;
    }

    public static List<string> FindPythonCandidates(string? configuredPath, string? appBaseDirectory = null)
    {
        appBaseDirectory ??= AppContext.BaseDirectory;
        var candidates = new List<string>();

        void AddIfValid(string? path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            var clean = path.Trim('"', ' ', '\t', '\r', '\n');
            if (File.Exists(clean))
            {
                var full = Path.GetFullPath(clean);
                if (!candidates.Contains(full, StringComparer.OrdinalIgnoreCase))
                    candidates.Add(full);
            }
        }

        // 1. Caminho configurado explicitamente
        AddIfValid(configuredPath);

        // 2. Ambiente virtual persistente em %LOCALAPPDATA%\TravelCutStudio\tools\python-venv
        AddIfValid(GetPersistentPythonVenvExecutable());

        // 3. Variável TRAVELCUT_PYTHON_PATH
        AddIfValid(System.Environment.GetEnvironmentVariable("TRAVELCUT_PYTHON_PATH"));

        // 3. Venv local da aplicação (workers/.venv)
        var localVenv = Path.Combine(appBaseDirectory, "workers", ".venv", "Scripts", "python.exe");
        AddIfValid(localVenv);

        var repoVenv = Path.Combine(appBaseDirectory, "..", "..", "workers", ".venv", "Scripts", "python.exe");
        AddIfValid(repoVenv);

        // 4. PATH do sistema (python.exe, py.exe)
        foreach (var p in FindAllInPath("python.exe")) AddIfValid(p);
        foreach (var p in FindAllInPath("py.exe")) AddIfValid(p);

        // 5. Pastas comuns do Windows (AppData Local e Program Files)
        var localAppData = System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData);
        var pyPrograms = Path.Combine(localAppData, "Programs", "Python");
        if (Directory.Exists(pyPrograms))
        {
            try
            {
                foreach (var dir in Directory.GetDirectories(pyPrograms, "Python3*"))
                {
                    AddIfValid(Path.Combine(dir, "python.exe"));
                }
            }
            catch { }
        }

        var progFiles = System.Environment.GetFolderPath(System.Environment.SpecialFolder.ProgramFiles);
        if (Directory.Exists(progFiles))
        {
            try
            {
                foreach (var dir in Directory.GetDirectories(progFiles, "Python3*"))
                {
                    AddIfValid(Path.Combine(dir, "python.exe"));
                }
            }
            catch { }
        }

        // 6. Diretório raiz C:\Python3*
        try
        {
            if (Directory.Exists(@"C:\"))
            {
                foreach (var dir in Directory.GetDirectories(@"C:\", "Python3*"))
                {
                    AddIfValid(Path.Combine(dir, "python.exe"));
                }
            }
        }
        catch { }

        return candidates;
    }

    public static List<string> FindNodeCandidates(string? configuredPath)
    {
        var candidates = new List<string>();

        void AddIfValid(string? path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            var clean = path.Trim('"', ' ', '\t', '\r', '\n');
            if (File.Exists(clean))
            {
                var full = Path.GetFullPath(clean);
                if (!candidates.Contains(full, StringComparer.OrdinalIgnoreCase))
                    candidates.Add(full);
            }
        }

        AddIfValid(configuredPath);
        AddIfValid(System.Environment.GetEnvironmentVariable("TRAVELCUT_NODE_PATH"));

        foreach (var p in FindAllInPath("node.exe")) AddIfValid(p);

        var progFiles = System.Environment.GetFolderPath(System.Environment.SpecialFolder.ProgramFiles);
        AddIfValid(Path.Combine(progFiles, "nodejs", "node.exe"));

        var progFilesX86 = System.Environment.GetFolderPath(System.Environment.SpecialFolder.ProgramFilesX86);
        AddIfValid(Path.Combine(progFilesX86, "nodejs", "node.exe"));

        var localAppData = System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData);
        AddIfValid(Path.Combine(localAppData, "Programs", "nodejs", "node.exe"));

        return candidates;
    }

    public static List<string> FindHyperFramesCandidates(string? configuredPath, string? appBaseDirectory = null)
    {
        appBaseDirectory ??= AppContext.BaseDirectory;
        var candidates = new List<string>();

        void AddIfValid(string? path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            var clean = path.Trim('"', ' ', '\t', '\r', '\n');
            if (File.Exists(clean))
            {
                var full = Path.GetFullPath(clean);
                if (!candidates.Contains(full, StringComparer.OrdinalIgnoreCase))
                    candidates.Add(full);
            }
        }

        // 1. Configurado explicitamente
        AddIfValid(configuredPath);

        // 2. Instalação persistente em %LOCALAPPDATA%\TravelCutStudio\tools\hyperframes
        AddIfValid(GetPersistentHyperFramesCliPath());

        // 3. TRAVELCUT_HYPERFRAMES_CLI
        AddIfValid(System.Environment.GetEnvironmentVariable("TRAVELCUT_HYPERFRAMES_CLI"));

        // 3. Workers locais no app
        var localWorker = Path.Combine(appBaseDirectory, "workers", "motion", "node_modules", "hyperframes", "dist", "cli.js");
        AddIfValid(localWorker);

        var localWorkerBin = Path.Combine(appBaseDirectory, "workers", "node_modules", "hyperframes", "dist", "cli.js");
        AddIfValid(localWorkerBin);

        // 4. Npm global do usuário (%APPDATA%\npm\node_modules\hyperframes\dist\cli.js)
        var appData = System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData);
        AddIfValid(Path.Combine(appData, "npm", "node_modules", "hyperframes", "dist", "cli.js"));
        AddIfValid(Path.Combine(appData, "npm", "node_modules", "hyperframes", "bin", "hyperframes.js"));

        // 5. Npm global do Node em Program Files
        var progFiles = System.Environment.GetFolderPath(System.Environment.SpecialFolder.ProgramFiles);
        AddIfValid(Path.Combine(progFiles, "nodejs", "node_modules", "hyperframes", "dist", "cli.js"));

        return candidates;
    }

    public static List<string> FindHyperFramesCliCandidates(string? configuredPath, string? appBaseDirectory = null)
        => FindHyperFramesCandidates(configuredPath, appBaseDirectory);

    public static List<string> FindFfmpegCandidates(string? configuredPath)
    {
        var candidates = new List<string>();

        void AddIfValid(string? path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            var clean = path.Trim('"', ' ', '\t', '\r', '\n');
            if (File.Exists(clean))
            {
                var full = Path.GetFullPath(clean);
                if (!candidates.Contains(full, StringComparer.OrdinalIgnoreCase))
                    candidates.Add(full);
            }
        }

        AddIfValid(configuredPath);
        AddIfValid(System.Environment.GetEnvironmentVariable("TRAVELCUT_FFMPEG_PATH"));
        foreach (var p in FindAllInPath("ffmpeg.exe")) AddIfValid(p);

        var common = new[]
        {
            @"C:\ffmpeg\bin\ffmpeg.exe",
            @"C:\Tools\ffmpeg\bin\ffmpeg.exe",
            @"C:\Program Files\ffmpeg\bin\ffmpeg.exe",
            Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData), "Programs", "ffmpeg", "bin", "ffmpeg.exe")
        };
        foreach (var c in common) AddIfValid(c);

        return candidates;
    }

    public static List<string> FindFfprobeCandidates(string? configuredPath, string? ffmpegPath)
    {
        var candidates = new List<string>();

        void AddIfValid(string? path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            var clean = path.Trim('"', ' ', '\t', '\r', '\n');
            if (File.Exists(clean))
            {
                var full = Path.GetFullPath(clean);
                if (!candidates.Contains(full, StringComparer.OrdinalIgnoreCase))
                    candidates.Add(full);
            }
        }

        AddIfValid(configuredPath);
        AddIfValid(System.Environment.GetEnvironmentVariable("TRAVELCUT_FFPROBE_PATH"));

        if (!string.IsNullOrWhiteSpace(ffmpegPath) && File.Exists(ffmpegPath))
        {
            var dir = Path.GetDirectoryName(ffmpegPath);
            if (!string.IsNullOrEmpty(dir))
            {
                AddIfValid(Path.Combine(dir, "ffprobe.exe"));
            }
        }

        foreach (var p in FindAllInPath("ffprobe.exe")) AddIfValid(p);

        var common = new[]
        {
            @"C:\ffmpeg\bin\ffprobe.exe",
            @"C:\Tools\ffmpeg\bin\ffprobe.exe",
            @"C:\Program Files\ffmpeg\bin\ffprobe.exe",
            Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData), "Programs", "ffmpeg", "bin", "ffprobe.exe")
        };
        foreach (var c in common) AddIfValid(c);

        return candidates;
    }

    public static string GetWhisperCacheDir()
    {
        var xdg = System.Environment.GetEnvironmentVariable("XDG_CACHE_HOME");
        if (!string.IsNullOrWhiteSpace(xdg))
        {
            return Path.Combine(xdg, "whisper");
        }

        var userProfile = System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile);
        return Path.Combine(userProfile, ".cache", "whisper");
    }

    public static string GetLargeV3ModelPath()
    {
        return Path.Combine(GetWhisperCacheDir(), "large-v3.pt");
    }

    public static bool IsLargeV3ModelCached()
    {
        var modelPath = GetLargeV3ModelPath();
        if (!File.Exists(modelPath)) return false;

        var fi = new FileInfo(modelPath);
        // large-v3.pt tem aproximadamente 2.88 GB (3.094.792.307 bytes)
        return fi.Length > 2_500_000_000L;
    }

    /// <summary>
    /// Hash SHA-256 oficial do modelo large-v3 publicado pelo OpenAI Whisper (whisper/__init__.py).
    /// </summary>
    public const string OfficialWhisperLargeV3Sha256 = "e5b1a55b89c1367dacf97e3e19bfd829a01529dbfdeefa8caeb59b3f1b81dadb";

    /// <summary>
    /// URL oficial do Azure Blob Storage para o modelo large-v3 do OpenAI Whisper.
    /// </summary>
    public const string OfficialWhisperLargeV3Url = "https://openaipublic.azureedge.net/main/whisper/models/e5b1a55b89c1367dacf97e3e19bfd829a01529dbfdeefa8caeb59b3f1b81dadb/large-v3.pt";

    public static string GetToolsDirectory()
    {
        var localAppData = System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(localAppData, "TravelCutStudio", "tools");
    }

    public static string GetPersistentPythonVenvDirectory()
    {
        return Path.Combine(GetToolsDirectory(), "python-venv");
    }

    public static string GetPersistentPythonVenvExecutable()
    {
        return Path.Combine(GetPersistentPythonVenvDirectory(), "Scripts", "python.exe");
    }

    public static string GetPersistentHyperFramesDirectory()
    {
        return Path.Combine(GetToolsDirectory(), "hyperframes");
    }

    public static string GetPersistentHyperFramesCliPath()
    {
        return Path.Combine(GetPersistentHyperFramesDirectory(), "node_modules", "hyperframes", "dist", "cli.js");
    }

    public static string? FindWinGet()
    {
        var onPath = FindInPath("winget.exe");
        if (onPath != null) return onPath;

        var localAppData = System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData);
        var candidate = Path.Combine(localAppData, "Microsoft", "WindowsApps", "winget.exe");
        return File.Exists(candidate) ? candidate : null;
    }

    public static string ComputeFileSha256(string filePath)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException("Arquivo não encontrado para verificação de hash.", filePath);

        using var stream = File.OpenRead(filePath);
        using var sha = System.Security.Cryptography.SHA256.Create();
        var bytes = sha.ComputeHash(stream);
        return Convert.ToHexStringLower(bytes);
    }

    public static bool VerifyFileSha256(string filePath, string expectedSha256)
    {
        if (!File.Exists(filePath)) return false;
        try
        {
            var hash = ComputeFileSha256(filePath);
            return string.Equals(hash, expectedSha256, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }
}
