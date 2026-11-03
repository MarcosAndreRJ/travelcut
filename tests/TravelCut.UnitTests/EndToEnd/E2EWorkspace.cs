using System.Security.Cryptography;
using Microsoft.Data.Sqlite;

namespace TravelCut.UnitTests.EndToEnd;

/// <summary>
/// Pasta temporária de um cenário E2E: banco SQLite em "app dados" (com espaço) e projetos sob "projetos".
/// Também oferece a "árvore" de uma pasta (tamanho, SHA-256 e data de escrita de cada item) para provar
/// que a origem não mudou e que os artefatos ficaram só em 3-Trabalho.
/// </summary>
public sealed class E2EWorkspace : IDisposable
{
    public E2EWorkspace()
    {
        Base = Path.Combine(Path.GetTempPath(), "tc_e2e_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Base);
    }

    public string Base { get; }

    public string DatabasePath
    {
        get
        {
            var dir = Path.Combine(Base, "app dados");
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, "travelcut.db");
        }
    }

    public string ProjectRoot(string folderName) => Path.Combine(Base, "projetos", folderName);

    /// <summary>Grava um arquivo sintético de bytes determinísticos (não é mídia real).</summary>
    public static string WriteSyntheticMedia(string path, int seed, int length = 4096)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var bytes = new byte[length];
        new Random(seed).NextBytes(bytes);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    /// <summary>
    /// Árvore de uma pasta: chave = caminho relativo; valor = "DIR" ou "tamanho|sha256|ticksUltimaEscrita".
    /// Ordenada, para comparação direta com Assert.Equal.
    /// </summary>
    public static SortedDictionary<string, string> Tree(string root)
    {
        var tree = new SortedDictionary<string, string>(StringComparer.Ordinal);
        if (!Directory.Exists(root)) return tree;

        foreach (var dir in Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories))
        {
            tree[Path.GetRelativePath(root, dir)] = "DIR";
        }
        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            var info = new FileInfo(file);
            using var stream = File.OpenRead(file);
            string sha = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
            tree[Path.GetRelativePath(root, file)] = $"{info.Length}|{sha}|{info.LastWriteTimeUtc.Ticks}";
        }
        return tree;
    }

    /// <summary>Diferença entre duas árvores: itens criados, removidos e alterados (caminhos relativos).</summary>
    public static (List<string> Added, List<string> Removed, List<string> Changed) Diff(
        SortedDictionary<string, string> before,
        SortedDictionary<string, string> after)
    {
        var added = after.Keys.Except(before.Keys).ToList();
        var removed = before.Keys.Except(after.Keys).ToList();
        var changed = before.Keys.Intersect(after.Keys).Where(k => before[k] != after[k]).ToList();
        return (added, removed, changed);
    }

    /// <summary>Primeiro segmento de um caminho relativo (aceita '\' e '/').</summary>
    public static string TopSegment(string relative) =>
        relative.Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries)[0];

    public void Dispose()
    {
        // O pool do Microsoft.Data.Sqlite pode manter o arquivo aberto: libera antes de apagar.
        SqliteConnection.ClearAllPools();
        try
        {
            if (Directory.Exists(Base)) Directory.Delete(Base, recursive: true);
        }
        catch (IOException)
        {
            // Limpeza melhor esforço: o Windows pode manter um handle por instantes.
        }
        catch (UnauthorizedAccessException)
        {
            // Idem (antivírus/indexador).
        }
    }
}
