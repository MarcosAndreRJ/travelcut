namespace TravelCut.Application.Services;

/// <summary>
/// Regras puras da raiz de projeto (sem IO além de Path.GetFullPath).
/// Normalização: caminho absoluto, sem barra final (raiz de unidade mantém "C:\"). A comparação de RootPath é
/// case-insensitive (Windows): quem busca deve usar <see cref="Normalize"/> e comparação sem diferenciar caixa.
/// </summary>
public static class ProjectRootPathRules
{
    private static readonly char[] Separators = { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar };

    /// <summary>Caminho absoluto, sem separador final. Lança ArgumentException/NotSupportedException se o caminho for inválido.</summary>
    public static string Normalize(string rootPath)
    {
        string full = Path.GetFullPath(rootPath.Trim());
        string trimmed = full.TrimEnd(Separators);
        if (trimmed.Length == 0)
        {
            // Raiz POSIX "/" (ou equivalente): mantém o separador.
            return full;
        }

        // "C:" sozinho significa "diretório atual da unidade": a raiz de unidade fica "C:\".
        if (trimmed.Length == 2 && trimmed[1] == ':')
        {
            return trimmed + Path.DirectorySeparatorChar;
        }

        return trimmed;
    }

    /// <summary>
    /// Motivo pelo qual <paramref name="normalizedRoot"/> não pode ser raiz de projeto, ou null quando é aceitável.
    /// - Raiz de unidade ou de compartilhamento UNC ("C:\", "\\servidor\share").
    /// - Pasta de sistema e tudo abaixo dela: Windows, Program Files (x86 incluso), ProgramData.
    /// - Pastas AppData exatas (Roaming, Local, LocalLow e a própria pasta AppData do perfil): só a pasta, não os
    ///   descendentes, porque o diretório temporário do usuário fica dentro de AppData\Local.
    /// - O próprio perfil do usuário.
    /// </summary>
    public static string? FindViolation(string normalizedRoot, ProjectRootSystemFolders folders)
    {
        if (string.IsNullOrEmpty(normalizedRoot))
        {
            return "Caminho vazio.";
        }

        string? pathRoot = SafeGetPathRoot(normalizedRoot);
        if (pathRoot != null && string.Equals(Normalize(pathRoot), normalizedRoot, StringComparison.OrdinalIgnoreCase))
        {
            return "A raiz de uma unidade ou de um compartilhamento não pode ser pasta de projeto.";
        }

        foreach (var folder in folders.ContainingFolders)
        {
            if (IsSameOrDescendant(normalizedRoot, folder))
            {
                return "Pasta de sistema não pode ser pasta de projeto.";
            }
        }

        foreach (var folder in folders.ExactFolders)
        {
            if (IsSame(normalizedRoot, folder))
            {
                return "Pasta do sistema ou do perfil do usuário não pode ser pasta de projeto.";
            }
        }

        return null;
    }

    /// <summary>Pastas proibidas do ambiente atual. Entradas vazias ou inexistentes são ignoradas.</summary>
    public static ProjectRootSystemFolders CurrentSystemFolders()
    {
        string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string appData = Path.Combine(profile, "AppData");

        var containing = new List<string?>
        {
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        };

        var exact = new List<string?>
        {
            profile,
            appData,
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            Path.Combine(appData, "LocalLow"),
        };

        return new ProjectRootSystemFolders(
            ContainingFolders: Normalize(containing),
            ExactFolders: Normalize(exact));
    }

    private static IReadOnlyList<string> Normalize(IEnumerable<string?> folders)
    {
        var result = new List<string>();
        foreach (var folder in folders)
        {
            if (string.IsNullOrWhiteSpace(folder)) continue;
            try
            {
                result.Add(Normalize(folder));
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                // Pasta de ambiente inválida: ignorada (não impede outras regras).
            }
        }
        return result;
    }

    private static string? SafeGetPathRoot(string path)
    {
        try
        {
            string? root = Path.GetPathRoot(path);
            return string.IsNullOrWhiteSpace(root) ? null : root;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static bool IsSame(string path, string folder)
        => string.Equals(path, folder, StringComparison.OrdinalIgnoreCase);

    private static bool IsSameOrDescendant(string path, string folder)
    {
        if (IsSame(path, folder)) return true;
        string prefix = folder.EndsWith(Path.DirectorySeparatorChar) ? folder : folder + Path.DirectorySeparatorChar;
        return path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>
/// Pastas do sistema usadas por <see cref="ProjectRootPathRules.FindViolation"/>.
/// ContainingFolders: a pasta e tudo abaixo dela são proibidos. ExactFolders: só a própria pasta é proibida.
/// </summary>
public sealed record ProjectRootSystemFolders(
    IReadOnlyList<string> ContainingFolders,
    IReadOnlyList<string> ExactFolders);
