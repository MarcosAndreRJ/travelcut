using System.IO;
using TravelCut.Domain.Enums;

namespace TravelCut.Application.Services;

public sealed record ExpandedMediaFile(
    string FullPath,
    string FileName,
    string OriginFolder,
    MediaKind Kind,
    string Container,
    long ByteLength);

public sealed record MediaExpansionResult(
    IReadOnlyList<ExpandedMediaFile> Files,
    bool Truncated,
    IReadOnlyList<string> Errors);

/// <summary>
/// Expande arquivos e pastas (recursivamente) em arquivos de mídia.
/// Somente leitura. Pula ocultos/sistema, reparse points e arquivos *.tcpart.
/// Erros usam apenas nomes de arquivo/pasta, nunca caminhos completos.
/// </summary>
public static class MediaFileExpander
{
    public const string PartialFileExtension = ".tcpart";

    public static MediaExpansionResult Expand(IEnumerable<string> paths, int limit = ImportPathRules.MaxFilesPerImport, CancellationToken ct = default)
    {
        var files = new Dictionary<string, ExpandedMediaFile>(StringComparer.OrdinalIgnoreCase);
        var errors = new List<string>();
        bool truncated = false;

        foreach (var raw in paths ?? Array.Empty<string>())
        {
            ct.ThrowIfCancellationRequested();
            if (truncated) break;

            if (string.IsNullOrWhiteSpace(raw))
                continue;

            string full;
            try
            {
                full = Path.GetFullPath(raw.Trim());
            }
            catch
            {
                errors.Add("Caminho inválido ignorado.");
                continue;
            }

            if (File.Exists(full))
            {
                truncated = !TryAddFile(new FileInfo(full), files, errors, limit, explicitSelection: true);
                continue;
            }

            if (Directory.Exists(full))
            {
                var dirInfo = new DirectoryInfo(full);
                if (IsSkippedEntry(dirInfo.Attributes))
                {
                    errors.Add($"Pasta ignorada (oculta, de sistema ou ponto de reparse): {dirInfo.Name}");
                    continue;
                }

                truncated = !WalkDirectory(dirInfo, files, errors, limit, ct);
                continue;
            }

            errors.Add($"Caminho não encontrado: {Path.GetFileName(full)}");
        }

        var ordered = files.Values
            .OrderBy(f => f.FullPath, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new MediaExpansionResult(ordered, truncated, errors);
    }

    /// <summary>Retorna false quando o limite foi atingido.</summary>
    private static bool WalkDirectory(
        DirectoryInfo root,
        Dictionary<string, ExpandedMediaFile> files,
        List<string> errors,
        int limit,
        CancellationToken ct)
    {
        var pending = new Stack<DirectoryInfo>();
        pending.Push(root);

        while (pending.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            var dir = pending.Pop();

            List<FileSystemInfo> entries;
            try
            {
                entries = dir.EnumerateFileSystemInfos().ToList();
            }
            catch (UnauthorizedAccessException)
            {
                errors.Add($"Acesso negado: {dir.Name}");
                continue;
            }
            catch (IOException)
            {
                errors.Add($"Falha ao ler pasta: {dir.Name}");
                continue;
            }

            var subdirs = new List<DirectoryInfo>();
            foreach (var entry in entries.OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase))
            {
                if (IsSkippedEntry(entry.Attributes))
                    continue;

                if (entry is DirectoryInfo childDir)
                {
                    subdirs.Add(childDir);
                }
                else if (entry is FileInfo fileInfo)
                {
                    if (!TryAddFile(fileInfo, files, errors, limit, explicitSelection: false))
                        return false;
                }
            }

            // Empilha em ordem inversa para processar as subpastas em ordem alfabética.
            for (int i = subdirs.Count - 1; i >= 0; i--)
                pending.Push(subdirs[i]);
        }

        return true;
    }

    /// <summary>Retorna false somente quando o limite já estava atingido e o arquivo precisaria ser adicionado.</summary>
    private static bool TryAddFile(
        FileInfo fi,
        Dictionary<string, ExpandedMediaFile> files,
        List<string> errors,
        int limit,
        bool explicitSelection)
    {
        if (IsSkippedEntry(fi.Attributes))
        {
            if (explicitSelection)
            {
                errors.Add(CloudPlaceholderClassifier.IsOnDemandCloudFile(fi.Attributes)
                    ? $"Arquivo ignorado ({CloudPlaceholderClassifier.OneDriveOnDemandMessage}): {fi.Name}"
                    : $"Arquivo ignorado (oculto, de sistema ou reparse): {fi.Name}");
            }
            return true;
        }

        if (fi.Name.EndsWith(PartialFileExtension, StringComparison.OrdinalIgnoreCase))
        {
            if (explicitSelection) errors.Add($"Arquivo temporário ignorado: {fi.Name}");
            return true;
        }

        var kind = MediaScannerService.ClassifyExtension(fi.Extension);
        if (kind == null)
        {
            if (explicitSelection) errors.Add($"Tipo não suportado: {fi.Name}");
            return true;
        }

        if (files.ContainsKey(fi.FullName))
            return true;

        if (files.Count >= limit)
            return false;

        files[fi.FullName] = new ExpandedMediaFile(
            FullPath: fi.FullName,
            FileName: fi.Name,
            OriginFolder: fi.Directory?.Name ?? string.Empty,
            Kind: kind.Value,
            Container: fi.Extension.TrimStart('.').ToUpperInvariant(),
            ByteLength: fi.Length);
        return true;
    }

    private static bool IsSkippedEntry(FileAttributes attributes)
        => (attributes & (FileAttributes.Hidden | FileAttributes.System | FileAttributes.ReparsePoint)) != 0;
}
