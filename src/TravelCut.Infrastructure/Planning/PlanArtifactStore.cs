using System.Text;
using System.Text.RegularExpressions;
using TravelCut.Application.Ports;
using TravelCut.Application.Planning;

namespace TravelCut.Infrastructure.Planning;

/// <summary>
/// Artefatos JSON das revisões em &lt;projeto&gt;/3-Trabalho/plans/&lt;planId&gt;/r&lt;NNNN&gt;.json.
/// Escrita: arquivo temporário exclusivo + rename sem sobrescrever (File.Move overwrite:false).
/// Revisões existentes nunca são editadas. Número ocupado → <see cref="PlanRevisionConflictException"/>.
/// </summary>
public sealed class PlanArtifactStore : IPlanArtifactStore
{
    private static readonly Regex RevisionFileName = new(@"^r(\d{4})\.json$", RegexOptions.CultureInvariant);

    public async Task WriteRevisionAsync(string projectRoot, Guid planId, int revision, string json, CancellationToken ct = default)
    {
        var finalPath = ResolvePath(projectRoot, planId, revision);
        var directory = Path.GetDirectoryName(finalPath)!;
        Directory.CreateDirectory(directory);

        if (File.Exists(finalPath))
        {
            throw new PlanRevisionConflictException($"A revisão {revision} do plano já existe; revisões são imutáveis.");
        }

        var tempPath = Path.Combine(directory, $".r{revision:D4}.{Guid.NewGuid():N}.tmp");
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

            // overwrite: false → falha se outro processo tiver criado a mesma revisão no intervalo.
            File.Move(tempPath, finalPath, overwrite: false);
        }
        catch (IOException) when (File.Exists(finalPath))
        {
            // Corrida: outro gravador ocupou o número entre a checagem e o rename.
            TryDelete(tempPath);
            throw new PlanRevisionConflictException($"A revisão {revision} do plano já existe; revisões são imutáveis.");
        }
        catch
        {
            TryDelete(tempPath);
            throw;
        }
    }

    public async Task<string?> ReadRevisionAsync(string projectRoot, Guid planId, int revision, CancellationToken ct = default)
    {
        var path = ResolvePath(projectRoot, planId, revision);
        if (!File.Exists(path)) return null;
        return await File.ReadAllTextAsync(path, Encoding.UTF8, ct);
    }

    public Task<IReadOnlyList<int>> ListRevisionNumbersAsync(string projectRoot, Guid planId, CancellationToken ct = default)
    {
        var directory = Path.Combine(Path.GetFullPath(projectRoot),
            PlanStoragePaths.PlanDirectoryRelativePath(planId).Replace('/', Path.DirectorySeparatorChar));
        if (!Directory.Exists(directory))
            return Task.FromResult<IReadOnlyList<int>>(Array.Empty<int>());

        var numbers = new List<int>();
        foreach (var file in Directory.EnumerateFiles(directory))
        {
            var match = RevisionFileName.Match(Path.GetFileName(file));
            if (match.Success && int.TryParse(match.Groups[1].ValueSpan, out var n) && n >= 1)
                numbers.Add(n);
        }
        numbers.Sort();
        return Task.FromResult<IReadOnlyList<int>>(numbers);
    }

    public Task DeleteUncommittedRevisionAsync(string projectRoot, Guid planId, int revision, CancellationToken ct = default)
    {
        TryDelete(ResolvePath(projectRoot, planId, revision));
        return Task.CompletedTask;
    }

    private static string ResolvePath(string projectRoot, Guid planId, int revision)
    {
        if (revision < 1) throw new ArgumentOutOfRangeException(nameof(revision));
        var relative = PlanStoragePaths.RelativeJsonPath(planId, revision)
            .Replace('/', Path.DirectorySeparatorChar);
        return Path.Combine(Path.GetFullPath(projectRoot), relative);
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (IOException)
        {
            // Melhor esforço: arquivo temporário órfão não é referenciado por nenhum índice.
        }
        catch (UnauthorizedAccessException)
        {
            // Melhor esforço, como acima.
        }
    }
}
