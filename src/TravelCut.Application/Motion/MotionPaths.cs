using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using TravelCut.Application.Services;

namespace TravelCut.Application.Motion;

/// <summary>
/// Caminhos de motion (F09.5). Toda escrita fica em 3-Trabalho/motion/&lt;renderId&gt;; leitura de assets só em 2-Assets.
/// Funções puras (sem escrita), exceto a checagem de existência de assets.
/// </summary>
public static class MotionPaths
{
    public const string WorkFolderRelative = "3-Trabalho/motion";
    public const string RequestFileName = "request.json";
    public const string ResultFileName = "result.json";
    public const string OutputFileName = "output.mp4";

    private static readonly Regex RenderIdPattern = new(@"^mr-[0-9a-f]{32}$", RegexOptions.CultureInvariant);

    private static readonly char[] Separators = { '\\', '/' };

    private static readonly char[] WildcardChars = { '*', '?' };

    /// <summary>Nome curto 8.3 do Windows: "~" seguido de dígito dentro do segmento (ex.: PASTA~1, MYLINK~1).</summary>
    private static readonly Regex ShortNameMarker = new(@"~[0-9]", RegexOptions.CultureInvariant);

    /// <summary>Identidade determinística do render: mesmo projeto + mesma chave = mesma pasta.</summary>
    public static string RenderIdFor(Guid projectId, string idempotencyKey)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(projectId.ToString("D") + "\n" + idempotencyKey));
        return "mr-" + Convert.ToHexString(hash, 0, 16).ToLowerInvariant();
    }

    public static bool IsValidRenderId(string? renderId) => renderId is not null && RenderIdPattern.IsMatch(renderId);

    public static string ManifestRelative(string renderId) => $"{WorkFolderRelative}/{renderId}/{RequestFileName}";

    public static string ResultRelative(string renderId) => $"{WorkFolderRelative}/{renderId}/{ResultFileName}";

    public static string OutputRelative(string renderId) => $"{WorkFolderRelative}/{renderId}/{OutputFileName}";

    /// <summary>Raiz do projeto utilizável: caminho absoluto, sem caracteres vazios.</summary>
    public static bool IsUsableProjectRoot(string? projectRoot)
    {
        if (string.IsNullOrWhiteSpace(projectRoot))
            return false;

        try
        {
            return Path.IsPathFullyQualified(projectRoot);
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    /// <summary>Monta o contexto confinado de um render. Falso se a raiz ou o renderId forem inválidos.</summary>
    public static bool TryCreateContext(string? projectRoot, string? renderId, out MotionRenderContext? context)
    {
        context = null;
        if (!IsUsableProjectRoot(projectRoot) || !IsValidRenderId(renderId))
            return false;

        try
        {
            string root = TrimmedFullPath(projectRoot!);
            string work = Path.Combine(root, "3-Trabalho", "motion");
            string outputDir = Path.Combine(work, renderId!);
            string assetsRoot = Path.Combine(root, ImportPathRules.AssetsFolder);

            if (!IsInside(work, outputDir) || !IsInside(root, assetsRoot))
                return false;

            context = new MotionRenderContext(root, renderId!, outputDir, assetsRoot);
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

    /// <summary>
    /// Resolve um asset relativo a 2-Assets. Falso se o caminho for inseguro (absoluto, "..", unidade) ou
    /// se, após normalização, sair de 2-Assets. Não verifica existência (ver <see cref="TryResolveAsset"/>).
    /// </summary>
    public static bool TryResolveAssetPath(string projectRoot, string? relativePath, out string fullPath)
    {
        fullPath = string.Empty;
        if (!IsUsableProjectRoot(projectRoot) || !ImportPathRules.IsSafeLibraryRelativePath(relativePath))
            return false;

        if (relativePath!.Trim().Split(Separators).Any(s => s.Length == 0 || s == "."))
            return false;

        // Curingas e nomes 8.3 não são aceitos: o caminho deve ser literal e ter o nome longo do próprio arquivo.
        if (HasForbiddenAssetSegment(relativePath.Trim()))
            return false;

        try
        {
            string assetsRoot = Path.Combine(TrimmedFullPath(projectRoot), ImportPathRules.AssetsFolder);
            string candidate = Path.GetFullPath(Path.Combine(assetsRoot, relativePath.Trim().Replace('/', Path.DirectorySeparatorChar)));
            if (!IsInside(assetsRoot, candidate))
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

    /// <summary>
    /// Asset existente em 2-Assets: nenhum segmento entre a raiz e o arquivo pode ser reparse point (link, junção
    /// ou atalho). Falso se fora de 2-Assets, se houver link no caminho ou se o arquivo não existir.
    /// </summary>
    public static bool TryResolveAsset(string projectRoot, string? relativePath, out string fullPath)
    {
        return TryResolveAssetPath(projectRoot, relativePath, out fullPath)
               && !HasReparsePointBetween(projectRoot, fullPath)
               && File.Exists(fullPath);
    }

    /// <summary>
    /// Saída de render utilizável: arquivo existente, não vazio e sem reparse point no caminho desde o projeto.
    /// Usado tanto para validar sucesso do CLI quanto para decidir se um READY_REAL anterior pode ser reaproveitado.
    /// </summary>
    public static bool IsCompleteOutputFile(string projectRoot, string outputFileAbsolute)
    {
        try
        {
            if (HasReparsePointBetween(projectRoot, outputFileAbsolute))
                return false;

            return File.Exists(outputFileAbsolute) && new FileInfo(outputFileAbsolute).Length > 0;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>Verdadeiro se <paramref name="candidate"/> está estritamente dentro de <paramref name="root"/>.</summary>
    public static bool IsInside(string root, string candidate)
    {
        string r = TrimmedFullPath(root) + Path.DirectorySeparatorChar;
        string c = Path.GetFullPath(candidate);
        return c.Length > r.Length && c.StartsWith(r, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Verdadeiro se algum segmento entre <paramref name="root"/> (exclusivo) e <paramref name="target"/> (inclusivo)
    /// for reparse point (link simbólico, junção ou atalho). Cada segmento é inspecionado sem seguir o link.
    /// Segmentos inexistentes não são reparse points. Falha de leitura e alvo fora da raiz contam como bloqueio.
    /// </summary>
    public static bool HasReparsePointBetween(string root, string target)
    {
        try
        {
            string r = TrimmedFullPath(root);
            string t = TrimmedFullPath(target);
            if (!IsInside(r, t))
                return true;

            string current = r;
            foreach (string segment in Path.GetRelativePath(r, t).Split(Path.DirectorySeparatorChar))
            {
                if (IsReparseSegment(current, segment))
                    return true;

                current = Path.Combine(current, segment);
            }

            return false;
        }
        catch (ArgumentException)
        {
            return true;
        }
        catch (NotSupportedException)
        {
            return true;
        }
    }

    /// <summary>
    /// Verifica o segmento <paramref name="name"/> dentro de <paramref name="parent"/> lendo os atributos do próprio
    /// caminho, sem seguir o link (File.GetAttributes). Junções quebradas (alvo inexistente) continuam marcadas como
    /// ReparsePoint. Segmento inexistente = não é reparse point. Qualquer outra falha de leitura conta como bloqueio.
    /// Nomes 8.3 já chegam expandidos por Path.GetFullPath; a rejeição de "~N" em caminho relativo é defesa adicional.
    /// </summary>
    private static bool IsReparseSegment(string parent, string name)
    {
        try
        {
            FileAttributes attributes = File.GetAttributes(Path.Combine(parent, name));
            return (attributes & FileAttributes.ReparsePoint) != 0;
        }
        catch (FileNotFoundException)
        {
            return false;
        }
        catch (DirectoryNotFoundException)
        {
            return false;
        }
        catch (IOException)
        {
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return true;
        }
    }

    /// <summary>
    /// Segmentos de caminho relativo de asset que não são aceitos: vazio, "." (já tratados antes), curingas "*" e "?"
    /// e nomes curtos 8.3 (~ seguido de dígito, ex.: MYLINK~1), que podem apontar para link sem aparecer pelo nome longo.
    /// </summary>
    private static bool HasForbiddenAssetSegment(string relativePath)
    {
        foreach (string segment in relativePath.Split(Separators))
        {
            if (segment.IndexOfAny(WildcardChars) >= 0 || ShortNameMarker.IsMatch(segment))
                return true;
        }

        return false;
    }

    private static string TrimmedFullPath(string path) =>
        Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
}
