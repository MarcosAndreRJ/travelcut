namespace TravelCut.Application.Services;

/// <summary>
/// Regras puras de integridade da importação: destino permitido, subpasta segura,
/// normalização de tags e verificação de contenção de caminho.
/// </summary>
public static class ImportPathRules
{
    public const string BrutoFolder = "1-Bruto";
    public const string AssetsFolder = "2-Assets";
    public const int MaxTags = 20;
    public const int MaxFilesPerImport = 2000;

    /// <summary>Margem de segurança exigida além dos bytes a copiar (1 GiB).</summary>
    public const long SafetyMarginBytes = 1L << 30;

    /// <summary>Aceita apenas "1-Bruto" ou "2-Assets" (sem diferenciar maiúsculas) e devolve o nome canônico.</summary>
    public static bool TryResolveDestination(string? destination, out string folderName)
    {
        folderName = string.Empty;
        if (string.Equals(destination, BrutoFolder, StringComparison.OrdinalIgnoreCase))
        {
            folderName = BrutoFolder;
            return true;
        }

        if (string.Equals(destination, AssetsFolder, StringComparison.OrdinalIgnoreCase))
        {
            folderName = AssetsFolder;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Sanitiza a subpasta relativa ao destino. Vazia/nula = raiz do destino.
    /// Rejeita caminhos absolutos/com unidade, segmentos "..", "." e caracteres inválidos em nomes de arquivo.
    /// </summary>
    public static bool TryNormalizeSubfolder(string? subfolder, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(subfolder))
            return true;

        string raw = subfolder.Trim();
        if (raw.StartsWith('/') || raw.StartsWith('\\') || Path.IsPathRooted(raw))
            return false;

        var invalid = Path.GetInvalidFileNameChars();
        var segments = new List<string>();
        foreach (var part in raw.Replace('\\', '/').Split('/'))
        {
            string segment = part.Trim();
            if (segment.Length == 0)
                continue;

            if (segment == "." || segment == "..")
                return false;

            if (segment.IndexOfAny(invalid) >= 0)
                return false;

            segments.Add(segment);
        }

        normalized = string.Join('/', segments);
        return true;
    }

    /// <summary>Trim, remove vazias e duplicadas (sem diferenciar maiúsculas) e limita a <see cref="MaxTags"/>.</summary>
    public static IReadOnlyList<string> NormalizeTags(IEnumerable<string>? tags)
    {
        var result = new List<string>();
        if (tags == null)
            return result;

        foreach (var raw in tags)
        {
            string tag = (raw ?? string.Empty).Trim();
            if (tag.Length == 0)
                continue;

            if (result.Any(t => string.Equals(t, tag, StringComparison.OrdinalIgnoreCase)))
                continue;

            result.Add(tag);
            if (result.Count >= MaxTags)
                break;
        }

        return result;
    }

    /// <summary>True se <paramref name="candidateFullPath"/> estiver estritamente dentro de <paramref name="directoryFullPath"/>.</summary>
    public static bool IsInsideDirectory(string directoryFullPath, string candidateFullPath)
    {
        if (string.IsNullOrWhiteSpace(directoryFullPath) || string.IsNullOrWhiteSpace(candidateFullPath))
            return false;

        try
        {
            string dir = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directoryFullPath)) + Path.DirectorySeparatorChar;
            string candidate = Path.GetFullPath(candidateFullPath);
            return candidate.StartsWith(dir, StringComparison.OrdinalIgnoreCase)
                   && candidate.Length > dir.Length;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Caminho relativo à raiz do projeto com barras "/" (formato de MediaAsset.RelativePath).</summary>
    public static string ToLibraryRelativePath(string projectRootFullPath, string fileFullPath)
        => Path.GetRelativePath(Path.GetFullPath(projectRootFullPath), Path.GetFullPath(fileFullPath)).Replace('\\', '/');

    /// <summary>
    /// True se o RelativePath de um asset é seguro para a biblioteca: não vazio, não rooted (absoluto, unidade ou UNC)
    /// e sem segmento ".." (ex.: "..", "../x", "a/../b"). Nomes como "..clip.mp4" são válidos.
    /// </summary>
    public static bool IsSafeLibraryRelativePath(string? relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
            return false;

        string raw = relativePath.Trim();
        if (raw.StartsWith('/') || raw.StartsWith('\\') || Path.IsPathRooted(raw))
            return false;

        foreach (var segment in raw.Replace('\\', '/').Split('/'))
        {
            if (segment == "..")
                return false;
        }

        return true;
    }
}
