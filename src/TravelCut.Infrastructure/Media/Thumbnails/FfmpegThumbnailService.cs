using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using TravelCut.Application.Ports;

namespace TravelCut.Infrastructure.Media.Thumbnails;

/// <summary>
/// Miniaturas via subprocesso FFmpeg (ArgumentList, sem shell). Cache em
/// &lt;projeto&gt;/3-Trabalho/cache/thumbs/&lt;sha256(fullPath|size|mtime|t)&gt;.jpg.
/// Sem FFmpeg, devolve ffmpeg_unavailable — nunca uma imagem fabricada.
/// </summary>
public sealed class FfmpegThumbnailService : IThumbnailService
{
    public const string FfmpegPathEnvironmentVariable = "TRAVELCUT_FFMPEG_PATH";
    public static readonly TimeSpan ProcessTimeout = TimeSpan.FromSeconds(15);

    /// <summary>Máximo de FFmpeg de miniatura em execução ao mesmo tempo, no processo inteiro.</summary>
    public const int MaxConcurrentGenerations = 2;

    /// <summary>Semáforo global (estático): várias instâncias do serviço dividem as mesmas 2 vagas.</summary>
    private static readonly SemaphoreSlim GenerationSlots = new(MaxConcurrentGenerations, MaxConcurrentGenerations);

    /// <summary>Extensões de imagem fixa: entrada com prefixo "file:" (nomes com % ou ":" não viram padrão/protocolo).</summary>
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".tif", ".tiff", ".bmp", ".gif", ".webp"
    };

    /// <summary>Execução do subprocesso (padrão: FFmpeg real). Injetável para testes de concorrência.</summary>
    public delegate Task<(int ExitCode, bool TimedOut)> ThumbnailProcessRunner(
        string ffmpegPath, string inputArgument, double atSeconds, string outputPath, CancellationToken ct);

    private readonly ILogger<FfmpegThumbnailService> _logger;
    private readonly Lazy<string?> _ffmpegPath;
    private readonly ThumbnailProcessRunner _runner;

    public FfmpegThumbnailService(ILogger<FfmpegThumbnailService> logger)
        : this(logger, FfmpegLocator.Locate)
    {
    }

    /// <summary>Construtor para testes: permite injetar a localização do executável.</summary>
    public FfmpegThumbnailService(ILogger<FfmpegThumbnailService> logger, Func<string?> locate)
        : this(logger, locate, null)
    {
    }

    /// <summary>Construtor para testes: localização e execução injetáveis (null = FFmpeg real).</summary>
    public FfmpegThumbnailService(ILogger<FfmpegThumbnailService> logger, Func<string?> locate, ThumbnailProcessRunner? runner)
    {
        _logger = logger;
        _ffmpegPath = new Lazy<string?>(locate, LazyThreadSafetyMode.ExecutionAndPublication);
        _runner = runner ?? RunFfmpegAsync;
    }

    /// <summary>Argumento "-i": imagem fixa recebe prefixo "file:"; vídeo segue o caminho puro.</summary>
    public static string BuildInputArgument(string mediaPath)
        => ImageExtensions.Contains(Path.GetExtension(mediaPath)) ? "file:" + mediaPath : mediaPath;

    public async Task<ThumbnailResult> GenerateAsync(string projectRoot, string mediaPath, double atSeconds, CancellationToken ct = default)
    {
        string? ffmpeg = _ffmpegPath.Value;
        if (ffmpeg == null)
        {
            return new ThumbnailResult(
                ThumbnailStatus.FfmpegUnavailable,
                null,
                $"FFmpeg não encontrado. Instale-o ou defina a variável {FfmpegPathEnvironmentVariable}.");
        }

        if (string.IsNullOrWhiteSpace(mediaPath) || !File.Exists(mediaPath))
        {
            return new ThumbnailResult(ThumbnailStatus.Failed, null, "Arquivo de mídia não encontrado.");
        }

        if (!double.IsFinite(atSeconds) || atSeconds < 0)
        {
            atSeconds = 0;
        }

        var info = new FileInfo(mediaPath);
        string keySource = string.Join("|",
            info.FullName,
            info.Length.ToString(CultureInfo.InvariantCulture),
            info.LastWriteTimeUtc.Ticks.ToString(CultureInfo.InvariantCulture),
            atSeconds.ToString("F3", CultureInfo.InvariantCulture));
        string cacheKey = Sha256Hex(keySource);

        string thumbsDir = Path.Combine(Path.GetFullPath(projectRoot), "3-Trabalho", "cache", "thumbs");
        string cachePath = Path.Combine(thumbsDir, cacheKey + ".jpg");

        if (File.Exists(cachePath))
        {
            return new ThumbnailResult(ThumbnailStatus.Ok, ToDataUrl(await File.ReadAllBytesAsync(cachePath, ct)), null);
        }

        Directory.CreateDirectory(thumbsDir);
        string tempPath = Path.Combine(thumbsDir, $"{cacheKey}.{Guid.NewGuid():N}.tmp.jpg");

        try
        {
            // Vaga global: no máximo MaxConcurrentGenerations FFmpegs de miniatura ao mesmo tempo.
            (int ExitCode, bool TimedOut) run;
            await GenerationSlots.WaitAsync(ct);
            try
            {
                run = await _runner(ffmpeg, BuildInputArgument(mediaPath), atSeconds, tempPath, ct);
            }
            finally
            {
                GenerationSlots.Release();
            }

            if (run.TimedOut)
            {
                return new ThumbnailResult(ThumbnailStatus.Failed, null, $"Tempo limite de {ProcessTimeout.TotalSeconds:0} s excedido ao gerar miniatura.");
            }

            if (run.ExitCode != 0 || !File.Exists(tempPath) || new FileInfo(tempPath).Length == 0)
            {
                _logger.LogWarning("FFmpeg não gerou miniatura de {File} (código {Code})", info.Name, run.ExitCode);
                return new ThumbnailResult(ThumbnailStatus.Failed, null, "FFmpeg não conseguiu gerar miniatura neste arquivo/instante.");
            }

            // Nunca sobrescreve: se outro processo já gerou, reaproveita.
            if (!File.Exists(cachePath))
            {
                File.Move(tempPath, cachePath, overwrite: false);
            }

            return new ThumbnailResult(ThumbnailStatus.Ok, ToDataUrl(await File.ReadAllBytesAsync(cachePath, ct)), null);
        }
        finally
        {
            try
            {
                if (File.Exists(tempPath)) File.Delete(tempPath);
            }
            catch (IOException)
            {
                // Temporário residual: será ignorado pelo cache.
            }
        }
    }

    private static async Task<(int ExitCode, bool TimedOut)> RunFfmpegAsync(
        string ffmpeg,
        string inputArgument,
        double atSeconds,
        string outputPath,
        CancellationToken ct)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = ffmpeg,
            RedirectStandardError = true,
            RedirectStandardOutput = false,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var arg in new[]
                 {
                     "-hide_banner", "-nostdin", "-loglevel", "error",
                     "-ss", atSeconds.ToString("F3", CultureInfo.InvariantCulture),
                     "-i", inputArgument,
                     "-frames:v", "1",
                     "-vf", "scale=320:-2",
                     "-q:v", "4",
                     "-y", outputPath
                 })
        {
            startInfo.ArgumentList.Add(arg);
        }

        using var process = new Process { StartInfo = startInfo };
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(ProcessTimeout);

        bool timedOut = false;
        try
        {
            process.Start();
            // Lê stderr em paralelo para não travar o processo com pipe cheio.
            var stderrTask = process.StandardError.ReadToEndAsync();

            await process.WaitForExitAsync(timeoutCts.Token);
            await stderrTask;
            return (process.ExitCode, false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            timedOut = true;
            return (-1, timedOut);
        }
        catch (Win32Exception)
        {
            return (-1, false);
        }
        finally
        {
            KillIfRunning(process);
        }
    }

    private static void KillIfRunning(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
            // Processo não iniciado ou já encerrado.
        }
        catch (Win32Exception)
        {
            // Sem permissão ou já encerrado.
        }
    }

    private static string ToDataUrl(byte[] jpeg) => "data:image/jpeg;base64," + Convert.ToBase64String(jpeg);

    private static string Sha256Hex(string text)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}

/// <summary>Localiza o executável do FFmpeg: variável de ambiente, locais comuns e PATH.</summary>
public static class FfmpegLocator
{
    private static readonly string[] CommonWindowsLocations =
    {
        @"C:\Program Files\ffmpeg\bin\ffmpeg.exe",
        @"C:\ffmpeg\bin\ffmpeg.exe"
    };

    /// <summary>Caminho absoluto do ffmpeg encontrado, ou null. Mesma ordem de busca usada pelo ffprobe.</summary>
    public static string? Locate()
    {
        string? found = LocateCore();
        if (found == null) return null;
        try
        {
            return Path.GetFullPath(found);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    private static string? LocateCore()
    {
        string? configured = Environment.GetEnvironmentVariable(FfmpegThumbnailService.FfmpegPathEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured))
            return configured;

        if (OperatingSystem.IsWindows())
        {
            foreach (var candidate in CommonWindowsLocations)
            {
                if (File.Exists(candidate)) return candidate;
            }
        }

        string exeName = OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg";
        string pathVariable = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var dir in pathVariable.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                string candidate = Path.Combine(dir.Trim('"'), exeName);
                if (File.Exists(candidate)) return candidate;
            }
            catch (ArgumentException)
            {
                // Entrada inválida no PATH: ignora.
            }
        }

        return null;
    }
}
