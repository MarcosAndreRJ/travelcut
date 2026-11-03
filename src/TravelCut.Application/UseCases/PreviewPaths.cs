using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using TravelCut.Application.Services;

namespace TravelCut.Application.UseCases;

/// <summary>
/// Regras puras da prévia (F07): host virtual de mídia, URLs, MIME, playabilidade estimada por metadado
/// e nomes determinísticos de cache/proxy. Sem I/O.
/// </summary>
public static class PreviewPaths
{
    /// <summary>
    /// Host virtual que expõe SOMENTE o projeto aberto, somente leitura (WebView2, GET). Nunca aceito como
    /// origem de mensagens IPC nem como destino de navegação de topo (ver IpcOriginPolicy).
    /// </summary>
    public const string MediaHost = "media.travelcut.studio";
    public const string MediaOrigin = "https://" + MediaHost + "/";

    public const string ProxyStatusReady = "ready";
    public const string ProxyStatusGenerating = "generating";
    public const string ProxyStatusFailed = "failed";
    public const string ProxyStatusNone = "none";
    public const string ProxyStatusFfmpegUnavailable = "ffmpeg_unavailable";

    public const string PlaybackStatusOriginal = "original";
    public const string PlaybackStatusProxy = "proxy";
    public const string PlaybackStatusUnavailable = "unavailable";

    public const string ReasonFileNotFound = "arquivo não encontrado";
    public const string ReasonUnsafePath = "caminho relativo inseguro";

    /// <summary>Taxa de amostragem usada para a forma de onda (mono, PCM s16le).</summary>
    public const int WaveformSampleRate = 8000;
    public const int MinBuckets = 16;
    public const int MaxBuckets = 2000;

    /// <summary>Limite de mídias por job de proxy.</summary>
    public const int MaxProxyMediaPerRequest = 200;

    public const string ProxyFolderRelative = "3-Trabalho/cache/proxies";
    public const string WaveformFolderRelative = "3-Trabalho/cache/waveforms";

    private static readonly HashSet<string> PlayableCodecs = new(StringComparer.OrdinalIgnoreCase)
    {
        "h264", "avc1", "aac", "vp8", "vp9", "av1", "opus", "mp3"
    };

    private static readonly HashSet<string> PlayableContainers = new(StringComparer.OrdinalIgnoreCase)
    {
        "mp4", "m4v", "mov", "webm", "m4a", "mp3", "wav"
    };

    private static readonly string[] NonPlayableCodecTokens =
    {
        "prores", "dnxhd", "dnxhr", "braw", "insv", "cfhd", "mxf"
    };

    private static readonly string[] HevcCodecTokens = { "hevc", "h265" };

    /// <summary>
    /// True se o caminho relativo é seguro: não vazio, não absoluto, sem ".." nem "." nem segmentos vazios,
    /// e dentro da raiz quando combinado (checado por <see cref="TryResolveInsideRoot"/>).
    /// </summary>
    public static bool IsSafeRelativePath(string? relativePath)
    {
        if (!ImportPathRules.IsSafeLibraryRelativePath(relativePath))
            return false;

        foreach (var segment in relativePath!.Trim().Replace('\\', '/').Split('/'))
        {
            if (segment.Length == 0 || segment == ".")
                return false;
        }

        return true;
    }

    /// <summary>Resolve o caminho absoluto e confirma que continua dentro da raiz do projeto.</summary>
    public static bool TryResolveInsideRoot(string? projectRoot, string? relativePath, out string fullPath)
    {
        fullPath = string.Empty;
        if (!IsSafeRelativePath(relativePath) || string.IsNullOrWhiteSpace(projectRoot))
            return false;

        try
        {
            string root = Path.GetFullPath(projectRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                          + Path.DirectorySeparatorChar;
            string candidate = Path.GetFullPath(Path.Combine(root, relativePath!.Trim().Replace('/', Path.DirectorySeparatorChar)));
            if (!candidate.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                return false;

            fullPath = candidate;
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (NotSupportedException)
        {
            return false;
        }
    }

    /// <summary>https://media.travelcut.studio/&lt;segmentos escapados por segmento&gt;.</summary>
    public static string BuildMediaUrl(string relativePathFromRoot)
    {
        var segments = relativePathFromRoot.Trim().Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Select(Uri.EscapeDataString);
        return MediaOrigin + string.Join('/', segments);
    }

    public static string MimeTypeFor(string? fileNameOrExtension)
    {
        string ext = (Path.GetExtension(fileNameOrExtension ?? string.Empty) is { Length: > 0 } e
            ? e
            : (fileNameOrExtension ?? string.Empty)).TrimStart('.').ToLowerInvariant();

        return ext switch
        {
            "mp4" => "video/mp4",
            "m4v" => "video/mp4",
            "mov" => "video/quicktime",
            "webm" => "video/webm",
            "mkv" => "video/x-matroska",
            "avi" => "video/x-msvideo",
            "mts" or "m2ts" => "video/mp2t",
            "m4a" => "audio/mp4",
            "mp3" => "audio/mpeg",
            "wav" => "audio/wav",
            "aac" => "audio/aac",
            "flac" => "audio/flac",
            "ogg" => "audio/ogg",
            "jpg" or "jpeg" => "image/jpeg",
            "png" => "image/png",
            "webp" => "image/webp",
            "heic" => "image/heic",
            "tif" or "tiff" => "image/tiff",
            _ => "application/octet-stream"
        };
    }

    /// <summary>
    /// Playabilidade PROVÁVEL no WebView2 a partir do metadado. Nunca true sem base:
    /// true só para codec conhecido e reproduzível em contêiner listado; false para codecs que o Chromium
    /// não decodifica; null quando depende do sistema (HEVC) ou é desconhecido.
    /// </summary>
    public static bool? ClassifyPlayable(string? codec, string? extension)
    {
        if (string.IsNullOrWhiteSpace(codec))
            return null;

        string c = codec.Trim().ToLowerInvariant();

        if (NonPlayableCodecTokens.Any(t => c.Contains(t, StringComparison.Ordinal)))
            return false;

        if (HevcCodecTokens.Any(t => c.Contains(t, StringComparison.Ordinal)))
            return null;

        if (!PlayableCodecs.Contains(c))
            return null;

        string container = (extension ?? string.Empty).TrimStart('.').ToLowerInvariant();
        return PlayableContainers.Contains(container) ? true : null;
    }

    /// <summary>Nome do proxy final: &lt;mediaId&gt;-&lt;12 hex de sha256(fingerprint)&gt;.mp4. Nunca usa o fingerprint cru (caracteres livres).</summary>
    public static string ProxyFileName(Guid mediaId, string fingerprint)
        => $"{mediaId:D}-{ShortHash(fingerprint)}.mp4";

    /// <summary>Arquivo parcial (escrito pelo FFmpeg e renomeado sem sobrescrita ao final).</summary>
    public static string ProxyPartialFileName(Guid mediaId, string fingerprint)
        => $"{mediaId:D}-{ShortHash(fingerprint)}.tcpart.mp4";

    /// <summary>Chave do cache de forma de onda: sha256(fingerprint|start|end|buckets), com formato invariante.</summary>
    public static string WaveformCacheKey(string fingerprint, double startSeconds, double endSeconds, int buckets)
    {
        string text = string.Join("|",
            fingerprint,
            startSeconds.ToString("F6", CultureInfo.InvariantCulture),
            endSeconds.ToString("F6", CultureInfo.InvariantCulture),
            buckets.ToString(CultureInfo.InvariantCulture));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
    }

    public static string ShortHash(string text)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text ?? string.Empty))).ToLowerInvariant()[..12];
}
