using System.IO;
using System.Security.Cryptography;
using TravelCut.Domain.Enums;

namespace TravelCut.Application.Services;

public record DiscoveredMediaFile(
    string FullPath,
    string RelativePath,
    long ByteLength,
    DateTime LastWriteTimeUtc,
    string QuickFingerprint,
    MediaKind Kind,
    string Extension,
    string Subfolder
);

public interface IMediaScannerService
{
    IReadOnlyList<DiscoveredMediaFile> ScanDirectory(string rootPath, IEnumerable<string>? allowedSubfolders = null);
    Task<string> ComputeSha256StreamingAsync(string filePath, CancellationToken ct = default);
    bool IsPathSafe(string rootPath, string candidatePath);

    /// <summary>
    /// Descreve um arquivo específico dentro da raiz do projeto (mesmo formato de <see cref="ScanDirectory"/>).
    /// Retorna null se o caminho estiver fora da raiz, não for mídia suportada ou não existir.
    /// </summary>
    DiscoveredMediaFile? DescribeFile(string rootPath, string filePath);
}

public class MediaScannerService : IMediaScannerService
{
    private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".mov", ".m4v", ".mkv", ".avi", ".mts", ".m2ts", ".mxf", ".webm",
        // Câmeras de viagem: Insta360 (.insv) e Blackmagic RAW (.braw; FFprobe pode não decodificar → metadados "failed").
        // .lrf (proxy de baixa resolução da GoPro) é excluído de propósito para não duplicar clipes.
        ".insv", ".braw"
    };

    private static readonly HashSet<string> AudioExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".m4a", ".mp3", ".wav", ".aac", ".flac", ".ogg", ".wma"
    };

    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".heic", ".webp", ".tif", ".tiff"
    };

    private static readonly string[] DefaultAllowedFolders = new[]
    {
        "1-Bruto",
        "2-Assets"
    };

    public IReadOnlyList<DiscoveredMediaFile> ScanDirectory(string rootPath, IEnumerable<string>? allowedSubfolders = null)
    {
        if (string.IsNullOrWhiteSpace(rootPath))
            throw new ArgumentException("O caminho raiz não pode ser vazio.", nameof(rootPath));

        string normalizedRoot = Path.GetFullPath(rootPath);
        if (!Directory.Exists(normalizedRoot))
            throw new DirectoryNotFoundException($"Diretório raiz não encontrado: {normalizedRoot}");

        var subfolders = (allowedSubfolders ?? DefaultAllowedFolders).ToList();
        var results = new List<DiscoveredMediaFile>();

        foreach (var sub in subfolders)
        {
            string targetDir = Path.Combine(normalizedRoot, sub);
            if (!Directory.Exists(targetDir))
            {
                // Se a pasta padrão não existe, continua silenciosamente
                continue;
            }

            // Expansão segura: não segue reparse points (junções/links simbólicos), ocultos nem *.tcpart.
            var expansion = MediaFileExpander.Expand(new[] { targetDir }, int.MaxValue);
            foreach (var file in expansion.Files)
            {
                var described = BuildDiscovered(normalizedRoot, file.FullPath, file.Kind, sub);
                if (described != null) results.Add(described);
            }
        }

        // Se nenhuma das subpastas padrão existia, efetua varredura direta segura na raiz (sem recursão)
        if (results.Count == 0)
        {
            foreach (var file in Directory.EnumerateFiles(normalizedRoot, "*.*", SearchOption.TopDirectoryOnly))
            {
                var kind = DetermineKind(Path.GetExtension(file));
                if (kind == null)
                    continue;

                var described = BuildDiscovered(normalizedRoot, file, kind.Value, ".");
                if (described != null) results.Add(described);
            }
        }

        return results.OrderBy(r => r.RelativePath, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public async Task<string> ComputeSha256StreamingAsync(string filePath, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("Caminho do arquivo não pode ser nulo.", nameof(filePath));

        if (!File.Exists(filePath))
            throw new FileNotFoundException("Arquivo não encontrado para hash.", filePath);

        // Streaming com buffer de 64KB para evitar sobrecarga de memória RAM em arquivos gigabytes
        const int bufferSize = 64 * 1024;
        await using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize, useAsync: true);
        using var sha256 = SHA256.Create();
        byte[] hashBytes = await sha256.ComputeHashAsync(stream, ct);
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    public bool IsPathSafe(string rootPath, string candidatePath)
    {
        if (string.IsNullOrWhiteSpace(rootPath) || string.IsNullOrWhiteSpace(candidatePath))
            return false;

        try
        {
            string fullRoot = Path.GetFullPath(rootPath);
            if (!fullRoot.EndsWith(Path.DirectorySeparatorChar))
                fullRoot += Path.DirectorySeparatorChar;

            string fullCandidate = Path.GetFullPath(candidatePath);

            // Verifica se o caminho absoluto do candidato começa com o caminho da raiz
            return fullCandidate.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    public DiscoveredMediaFile? DescribeFile(string rootPath, string filePath)
    {
        if (string.IsNullOrWhiteSpace(rootPath) || string.IsNullOrWhiteSpace(filePath))
            return null;

        try
        {
            string normalizedRoot = Path.GetFullPath(rootPath);
            string file = Path.GetFullPath(filePath);
            if (!IsPathSafe(normalizedRoot, file))
                return null;

            string ext = Path.GetExtension(file);
            var kind = DetermineKind(ext);
            if (kind == null)
                return null;

            string relative = Path.GetRelativePath(normalizedRoot, file).Replace('\\', '/');
            int slash = relative.IndexOf('/');
            string subfolder = slash > 0 ? relative[..slash] : ".";

            return BuildDiscovered(normalizedRoot, file, kind.Value, subfolder);
        }
        catch
        {
            return null;
        }
    }

    private static bool IsInsideRoot(string rootPath, string candidatePath)
    {
        string fullRoot = Path.GetFullPath(rootPath);
        if (!fullRoot.EndsWith(Path.DirectorySeparatorChar))
            fullRoot += Path.DirectorySeparatorChar;

        return Path.GetFullPath(candidatePath).StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Classifica uma extensão (com ponto) como Video/Audio/Image, ou null se não for mídia suportada.</summary>
    public static MediaKind? ClassifyExtension(string extension) => DetermineKind(extension);

    /// <summary>
    /// Monta o registro descoberto. Recusa: caminho fora da raiz, reparse point (junção/link), RelativePath inseguro
    /// e arquivo inexistente. Nunca lança: retorna null.
    /// </summary>
    private static DiscoveredMediaFile? BuildDiscovered(string normalizedRoot, string file, MediaKind kind, string subfolder)
    {
        try
        {
            if (!IsInsideRoot(normalizedRoot, file))
                return null;

            string relative = Path.GetRelativePath(normalizedRoot, file).Replace('\\', '/');
            if (!ImportPathRules.IsSafeLibraryRelativePath(relative))
                return null;

            var fi = new FileInfo(file);
            if (!fi.Exists || (fi.Attributes & FileAttributes.ReparsePoint) != 0)
                return null;

            return new DiscoveredMediaFile(
                FullPath: fi.FullName,
                RelativePath: relative,
                ByteLength: fi.Length,
                LastWriteTimeUtc: fi.LastWriteTimeUtc,
                QuickFingerprint: $"q1_{fi.Length}_{fi.LastWriteTimeUtc.Ticks}",
                Kind: kind,
                Extension: Path.GetExtension(file).ToLowerInvariant(),
                Subfolder: subfolder);
        }
        catch (Exception)
        {
            // Arquivo inacessível/bloqueado durante a varredura: ignorado.
            return null;
        }
    }

    private static MediaKind? DetermineKind(string extension)
    {
        if (VideoExtensions.Contains(extension))
            return MediaKind.Video;
        if (AudioExtensions.Contains(extension))
            return MediaKind.Audio;
        if (ImageExtensions.Contains(extension))
            return MediaKind.Image;

        return null;
    }
}
