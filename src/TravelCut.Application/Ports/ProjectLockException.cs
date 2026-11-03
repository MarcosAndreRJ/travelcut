namespace TravelCut.Application.Ports;

/// <summary>
/// Falha estrutural da trava entre processos (pasta do projeto ausente, pasta de travas inacessível, erro de E/S).
/// Diferente de "ocupada" (que é só null): os chamadores devolvem <see cref="Code"/> como erro estruturado.
/// A mensagem nunca contém caminhos.
/// </summary>
public sealed class ProjectLockException(string code, string message, Exception? inner = null) : Exception(message, inner)
{
    public const string ProjectRootMissingCode = "PROJECT_ROOT_MISSING";
    public const string LocksFolderUnavailableCode = "LOCKS_FOLDER_UNAVAILABLE";
    public const string LockAccessDeniedCode = "LOCK_ACCESS_DENIED";
    public const string LockIoErrorCode = "LOCK_IO_ERROR";

    /// <summary>Código estável (um dos constantes desta classe).</summary>
    public string Code { get; } = code;
}
