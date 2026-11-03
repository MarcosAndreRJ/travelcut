using Microsoft.Extensions.Logging;
using TravelCut.Application.Ports;

namespace TravelCut.Application.UseCases;

/// <summary>Resultado de system.openFolder: caminho resolvido e se foi aberto com seleção de arquivo.</summary>
public sealed record OpenFolderOutcome(string FullPath, bool SelectedFile);

/// <summary>
/// system.openFolder. Regras:
/// - caminho absoluto (Path.IsPathFullyQualified), sem UNC/rede;
/// - deve estar dentro (ou ser igual) à RootPath de um projeto existente no repositório;
/// - pasta existente é aberta; arquivo existente abre a pasta que o contém com o item selecionado;
/// - nunca executa shell com interpolação: a abertura é feita pela porta IShellFolderOpener.
/// </summary>
public sealed class OpenFolderUseCase
{
    private readonly IProjectRepository _projectRepo;
    private readonly IShellFolderOpener _opener;
    private readonly ILogger<OpenFolderUseCase> _logger;

    public OpenFolderUseCase(IProjectRepository projectRepo, IShellFolderOpener opener, ILogger<OpenFolderUseCase> logger)
    {
        _projectRepo = projectRepo;
        _opener = opener;
        _logger = logger;
    }

    public async Task<(bool Success, OpenFolderOutcome? Outcome, string? ErrorCode, string? ErrorMessage)> ExecuteAsync(
        string? requestedPath,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(requestedPath))
        {
            return Fail("INVALID_PATH", "Caminho não informado.");
        }

        string raw = requestedPath.Trim();
        if (raw.StartsWith(@"\\", StringComparison.Ordinal) || raw.StartsWith("//", StringComparison.Ordinal))
        {
            return Fail("INVALID_PATH", "Caminhos de rede (UNC) não são permitidos.");
        }

        if (!Path.IsPathFullyQualified(raw))
        {
            return Fail("INVALID_PATH", "O caminho deve ser absoluto.");
        }

        string full;
        try
        {
            full = Path.GetFullPath(raw);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return Fail("INVALID_PATH", "Caminho inválido.");
        }

        if (full.StartsWith(@"\\", StringComparison.Ordinal))
        {
            return Fail("INVALID_PATH", "Caminhos de rede (UNC) não são permitidos.");
        }

        // Projetos conhecidos: list-all via limite máximo (a lista de projetos é pequena e local).
        var projects = await _projectRepo.ListRecentAsync(int.MaxValue, ct);
        bool allowed = projects.Any(p => !string.IsNullOrWhiteSpace(p.RootPath) && IsSameOrInside(p.RootPath, full));
        if (!allowed)
        {
            _logger.LogWarning("system.openFolder recusado: caminho fora das pastas de projetos conhecidos");
            return Fail("PATH_NOT_ALLOWED", "A pasta não pertence a um projeto conhecido.");
        }

        bool isDirectory = Directory.Exists(full);
        bool isFile = !isDirectory && File.Exists(full);
        if (!isDirectory && !isFile)
        {
            return Fail("DIRECTORY_NOT_FOUND", "Pasta ou arquivo não encontrado.");
        }

        try
        {
            _opener.OpenInExplorer(full, selectFile: isFile);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Falha ao abrir o Explorer");
            return Fail("OPEN_FAILED", "Não foi possível abrir o Explorer.");
        }

        return (true, new OpenFolderOutcome(full, isFile), null, null);
    }

    /// <summary>True se <paramref name="candidate"/> for a própria raiz ou estiver dentro dela (sem diferenciar maiúsculas).</summary>
    public static bool IsSameOrInside(string rootPath, string candidate)
    {
        string root = Path.GetFullPath(rootPath);
        string target = Path.GetFullPath(candidate);
        if (string.Equals(Path.TrimEndingDirectorySeparator(root), Path.TrimEndingDirectorySeparator(target), StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // Prefixo com separador final: "C:\\proj" não contém "C:\\projeto-outro".
        string prefix = root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar;
        return target.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    private static (bool Success, OpenFolderOutcome? Outcome, string? ErrorCode, string? ErrorMessage) Fail(string code, string message)
        => (false, null, code, message);
}
