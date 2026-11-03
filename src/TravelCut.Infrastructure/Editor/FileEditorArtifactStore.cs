using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using TravelCut.Application.Editor;
using TravelCut.Application.Ports;
using TravelCut.Contracts.Dtos;

namespace TravelCut.Infrastructure.Editor;

/// <summary>
/// Artefatos do editor em &lt;projeto&gt;/3-Trabalho/editor/&lt;planId&gt;/ (manifestos, registros de aplicação, snapshots).
/// - Escrita atômica: arquivo temporário exclusivo + rename (File.Move).
/// - Manifesto (r&lt;NNNN&gt;.assembly.json) é derivado: pode ser substituído.
/// - Registro de aplicação nunca é sobrescrito (conflito → <see cref="EditorArtifactConflictException"/>).
/// - Todo caminho final é verificado: precisa ficar dentro de &lt;raiz&gt;/3-Trabalho/editor.
/// </summary>
public sealed class FileEditorArtifactStore : IEditorArtifactStore
{
    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private static readonly JsonSerializerOptions ReadOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly ILogger<FileEditorArtifactStore> _logger;

    public FileEditorArtifactStore(ILogger<FileEditorArtifactStore> logger)
    {
        _logger = logger;
    }

    public async Task<string> WriteAssemblyAsync(string projectRoot, EditorAssemblyDto assembly, CancellationToken ct = default)
    {
        var relative = EditorStoragePaths.RelativeAssemblyPath(assembly.PlanId, assembly.Revision);
        var final = ResolveInside(projectRoot, relative);
        var json = JsonSerializer.Serialize(assembly, WriteOptions);
        await WriteAtomicAsync(final, json, overwrite: true, ct);
        return relative;
    }

    public async Task<string> WriteApplicationAsync(string projectRoot, EditorApplyResultDto application, CancellationToken ct = default)
    {
        var relative = EditorStoragePaths.RelativeApplicationPath(application.PlanId, application.ApplicationId);
        if (!string.Equals(application.RecordRelativePath, relative, StringComparison.Ordinal))
            throw new ArgumentException("Caminho do registro de aplicação inconsistente com o plano.", nameof(application));

        var final = ResolveInside(projectRoot, relative);
        var json = JsonSerializer.Serialize(application, WriteOptions);
        await WriteAtomicAsync(final, json, overwrite: false, ct);
        return relative;
    }

    public async Task<IReadOnlyList<EditorApplyResultDto>> ListApplicationsAsync(string projectRoot, CancellationToken ct = default)
    {
        var result = new List<EditorApplyResultDto>();
        var editorDirectory = ResolveInside(projectRoot, EditorStoragePaths.EditorFolder);
        if (!Directory.Exists(editorDirectory)) return result;

        foreach (var planDirectory in Directory.EnumerateDirectories(editorDirectory))
        {
            var applicationsDirectory = Path.Combine(planDirectory, "applications");
            if (!Directory.Exists(applicationsDirectory)) continue;

            foreach (var file in Directory.EnumerateFiles(applicationsDirectory, "*.json"))
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    var json = await File.ReadAllTextAsync(file, Encoding.UTF8, ct);
                    var dto = JsonSerializer.Deserialize<EditorApplyResultDto>(json, ReadOptions);
                    if (dto is null || dto.ApplicationId == Guid.Empty)
                        throw new JsonException("Registro sem applicationId.");
                    result.Add(dto);
                }
                catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
                {
                    // Corrompido ou em gravação: não derruba a listagem. Só o nome do arquivo entra no log.
                    _logger.LogWarning("Registro de aplicação ignorado por estar inválido: {FileName}", Path.GetFileName(file));
                }
            }
        }

        return result;
    }

    public string GetSnapshotAbsolutePath(string projectRoot, Guid planId, string editorProjectName)
    {
        if (!EditorStoragePaths.IsSafeArtifactName(editorProjectName))
            throw new ArgumentException("Nome de projeto do editor inválido para arquivo.", nameof(editorProjectName));
        return ResolveInside(projectRoot, $"{EditorStoragePaths.PlanDirectory(planId)}/snapshots/{editorProjectName}.drp");
    }

    public string GetPreviewDirectory(string projectRoot, Guid planId, string editorProjectName)
    {
        if (!EditorStoragePaths.IsSafeArtifactName(editorProjectName))
            throw new ArgumentException("Nome de projeto do editor inválido para arquivo.", nameof(editorProjectName));
        return ResolveInside(projectRoot, $"{EditorStoragePaths.PlanDirectory(planId)}/previews/{editorProjectName}");
    }

    public string? TryGetRelativePath(string projectRoot, string absolutePath)
    {
        try
        {
            var root = Path.GetFullPath(projectRoot);
            var full = Path.GetFullPath(absolutePath);
            var editorBase = EditorBase(root);
            if (!full.StartsWith(editorBase, StringComparison.OrdinalIgnoreCase)) return null;
            return Path.GetRelativePath(root, full).Replace(Path.DirectorySeparatorChar, '/');
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    /// <summary>Caminho absoluto de um relativo ('/'), garantido dentro de 3-Trabalho/editor. Fora disso → exceção.</summary>
    private static string ResolveInside(string projectRoot, string relative)
    {
        var root = Path.GetFullPath(projectRoot);
        var full = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
        var editorBase = EditorBase(root);
        var editorDir = editorBase.TrimEnd(Path.DirectorySeparatorChar);
        if (!full.Equals(editorDir, StringComparison.OrdinalIgnoreCase) && !full.StartsWith(editorBase, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Caminho de artefato do editor fora de 3-Trabalho/editor.");
        return full;
    }

    private static string EditorBase(string fullRoot) =>
        Path.GetFullPath(Path.Combine(fullRoot, EditorStoragePaths.EditorFolder.Replace('/', Path.DirectorySeparatorChar)))
            .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;

    private static async Task WriteAtomicAsync(string finalPath, string json, bool overwrite, CancellationToken ct)
    {
        var directory = Path.GetDirectoryName(finalPath)!;
        Directory.CreateDirectory(directory);

        var tempPath = Path.Combine(directory, $".{Path.GetFileName(finalPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            var bytes = Encoding.UTF8.GetBytes(json);
            await using (var stream = new FileStream(
                tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await stream.WriteAsync(bytes, ct);
                await stream.FlushAsync(ct);
            }

            File.Move(tempPath, finalPath, overwrite);
        }
        catch (IOException) when (!overwrite && File.Exists(finalPath))
        {
            TryDelete(tempPath);
            throw new EditorArtifactConflictException("O registro de aplicação já existe; registros não são sobrescritos.");
        }
        catch
        {
            TryDelete(tempPath);
            throw;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (IOException)
        {
            // Melhor esforço: temporário órfão não é referenciado por nenhum artefato.
        }
        catch (UnauthorizedAccessException)
        {
            // Melhor esforço, como acima.
        }
    }
}
