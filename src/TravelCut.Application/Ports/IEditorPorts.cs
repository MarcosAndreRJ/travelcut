using TravelCut.Contracts.Dtos;

namespace TravelCut.Application.Ports;

/// <summary>
/// Armazenamento dos artefatos do editor (F08) sob &lt;projeto&gt;/3-Trabalho/editor/&lt;planId&gt;/.
/// Caminhos relativos usam '/'. Implementação em Infrastructure (escrita atômica; nada fora de 3-Trabalho/editor).
/// </summary>
public interface IEditorArtifactStore
{
    /// <summary>
    /// Grava (ou substitui) o manifesto r&lt;NNNN&gt;.assembly.json: é um artefato DERIVADO do plano e pode ser regerado.
    /// Retorna o caminho relativo à raiz do projeto.
    /// </summary>
    Task<string> WriteAssemblyAsync(string projectRoot, EditorAssemblyDto assembly, CancellationToken ct = default);

    /// <summary>
    /// Grava o registro de aplicação applications/&lt;applicationId&gt;.json. NUNCA sobrescreve: registro existente → <see cref="EditorArtifactConflictException"/>.
    /// Retorna o caminho relativo (igual a <see cref="EditorStoragePaths.RelativeApplicationPath"/>).
    /// </summary>
    Task<string> WriteApplicationAsync(string projectRoot, EditorApplyResultDto application, CancellationToken ct = default);

    /// <summary>Lê todos os registros de aplicação de todas as pastas de plano. Arquivos corrompidos são ignorados (com log).</summary>
    Task<IReadOnlyList<EditorApplyResultDto>> ListApplicationsAsync(string projectRoot, CancellationToken ct = default);

    /// <summary>Caminho ABSOLUTO do .drp (3-Trabalho/editor/&lt;planId&gt;/snapshots/&lt;nome&gt;.drp). Não cria arquivos.</summary>
    string GetSnapshotAbsolutePath(string projectRoot, Guid planId, string editorProjectName);

    /// <summary>Diretório ABSOLUTO para prévias MP4 (3-Trabalho/editor/&lt;planId&gt;/previews/&lt;nome&gt;). Não cria diretórios.</summary>
    string GetPreviewDirectory(string projectRoot, Guid planId, string editorProjectName);

    /// <summary>Caminho relativo ('/') de um caminho absoluto SE estiver dentro de 3-Trabalho/editor; senão null.</summary>
    string? TryGetRelativePath(string projectRoot, string absolutePath);
}

/// <summary>Registro de aplicação já existente (não sobrescrito).</summary>
public sealed class EditorArtifactConflictException : IOException
{
    public EditorArtifactConflictException(string message) : base(message)
    {
    }
}

/// <summary>Constantes de caminhos do editor (relativas à raiz do projeto, separador '/').</summary>
public static class EditorStoragePaths
{
    public const string EditorFolder = "3-Trabalho/editor";

    public static string PlanDirectory(Guid planId) => $"{EditorFolder}/{planId:D}";

    public static string RelativeAssemblyPath(Guid planId, int revision) =>
        $"{PlanDirectory(planId)}/r{revision:D4}.assembly.json";

    public static string RelativeApplicationPath(Guid planId, Guid applicationId) =>
        $"{PlanDirectory(planId)}/applications/{applicationId:D}.json";

    /// <summary>Nome de projeto/timeline do DaVinci usado em arquivos: só [A-Za-z0-9_-], 1–120 caracteres.</summary>
    public static bool IsSafeArtifactName(string? name) =>
        !string.IsNullOrEmpty(name) && name.Length <= 120 && name.All(c => c is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z') or (>= '0' and <= '9') or '_' or '-');
}
