using System.IO;
using Microsoft.Extensions.Logging;
using TravelCut.Application.Services;
using TravelCut.Contracts.Dtos;
using TravelCut.Domain.Enums;

namespace TravelCut.Application.UseCases;

/// <summary>
/// fs.listDirectory: somente leitura. null = unidades prontas; caso contrario, subpastas e arquivos de midia.
/// </summary>
public class ListDirectoryUseCase
{
    public const int MaxEntries = 5000;

    private readonly ILogger<ListDirectoryUseCase> _logger;

    public ListDirectoryUseCase(ILogger<ListDirectoryUseCase> logger)
    {
        _logger = logger;
    }

    public Task<(bool Success, ListDirectoryResponse? Response, string? ErrorCode, string? ErrorMessage)> ExecuteAsync(
        string? path,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return Task.Run(() => List(path), ct);
    }

    private (bool Success, ListDirectoryResponse? Response, string? ErrorCode, string? ErrorMessage) List(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            var drives = new List<DirectoryEntryDto>();
            try
            {
                foreach (var drive in DriveInfo.GetDrives().Where(d => d.IsReady).OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase))
                {
                    drives.Add(new DirectoryEntryDto(
                        Name: drive.Name,
                        FullPath: drive.RootDirectory.FullName,
                        IsDirectory: true,
                        ByteLength: null,
                        Kind: null));
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Falha ao listar unidades");
                return (false, null, "LIST_FAILED", "Não foi possível listar as unidades.");
            }

            return (true, new ListDirectoryResponse(null, null, drives, Truncated: false), null, null);
        }

        string full;
        try
        {
            full = Path.GetFullPath(path.Trim());
        }
        catch (Exception)
        {
            return (false, null, "INVALID_PATH", "Caminho inválido.");
        }

        var dirInfo = new DirectoryInfo(full);
        if (!dirInfo.Exists)
        {
            return (false, null, "DIRECTORY_NOT_FOUND", $"Pasta não encontrada: {dirInfo.Name}");
        }

        List<FileSystemInfo> children;
        try
        {
            children = dirInfo.EnumerateFileSystemInfos().ToList();
        }
        catch (UnauthorizedAccessException)
        {
            _logger.LogWarning("Acesso negado ao listar pasta {Folder}", dirInfo.Name);
            return (false, null, "ACCESS_DENIED", "Acesso negado a esta pasta.");
        }
        catch (IOException ex)
        {
            _logger.LogWarning(ex, "Falha de IO ao listar pasta {Folder}", dirInfo.Name);
            return (false, null, "LIST_FAILED", "Falha ao ler a pasta.");
        }

        var folders = new List<DirectoryEntryDto>();
        var mediaFiles = new List<DirectoryEntryDto>();
        int cloudPlaceholders = 0;

        foreach (var child in children)
        {
            if ((child.Attributes & (FileAttributes.Hidden | FileAttributes.System | FileAttributes.ReparsePoint)) != 0)
            {
                // Placeholder do OneDrive: continua fora da lista (não é aceito), mas o motivo fica registrado.
                if (child is FileInfo && CloudPlaceholderClassifier.IsOnDemandCloudFile(child.Attributes))
                    cloudPlaceholders++;
                continue;
            }

            if (child is DirectoryInfo sub)
            {
                folders.Add(new DirectoryEntryDto(sub.Name, sub.FullName, true, null, null));
            }
            else if (child is FileInfo file)
            {
                var kind = MediaScannerService.ClassifyExtension(file.Extension);
                if (kind == null) continue;

                mediaFiles.Add(new DirectoryEntryDto(file.Name, file.FullName, false, file.Length, KindName(kind.Value)));
            }
        }

        if (cloudPlaceholders > 0)
        {
            _logger.LogInformation("{Count} item(ns) de nuvem ignorado(s) em {Folder}: {Message}",
                cloudPlaceholders, dirInfo.Name, CloudPlaceholderClassifier.OneDriveOnDemandMessage);
        }

        folders.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        mediaFiles.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));

        var (entries, truncated) = LimitEntries(folders.Concat(mediaFiles).ToList(), MaxEntries);
        if (truncated)
        {
            _logger.LogInformation("Listagem de {Folder} limitada a {Max} entradas", dirInfo.Name, MaxEntries);
        }

        string? parent = dirInfo.Parent?.FullName;
        return (true, new ListDirectoryResponse(dirInfo.FullName, parent, entries, truncated), null, null);
    }

    /// <summary>Corta a lista no limite. <c>Truncated</c> é true somente quando havia entradas além do limite.</summary>
    public static (IReadOnlyList<DirectoryEntryDto> Entries, bool Truncated) LimitEntries(
        IReadOnlyList<DirectoryEntryDto> entries,
        int max = MaxEntries)
    {
        if (entries.Count <= max)
        {
            return (entries, false);
        }

        return (entries.Take(max).ToList(), true);
    }

    internal static string KindName(MediaKind kind) => kind switch
    {
        MediaKind.Video => "Video",
        MediaKind.Audio => "Audio",
        MediaKind.Image => "Image",
        _ => "Other"
    };
}
