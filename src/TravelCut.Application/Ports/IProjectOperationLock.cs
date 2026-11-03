namespace TravelCut.Application.Ports;

/// <summary>
/// Trava de operação por projeto, válida ENTRE PROCESSOS (Desktop e servidor MCP podem rodar juntos sobre o mesmo banco e projeto).
/// Operações conhecidas: <c>editor-apply</c> (aplicação no editor) e <c>analysis</c> (job de análise).
/// </summary>
public interface IProjectOperationLock
{
    /// <summary>
    /// Tenta obter a trava da operação no projeto, aguardando até <paramref name="wait"/> (zero = uma tentativa).
    /// Devolve null quando a trava continua ocupada. Liberar = dispor o objeto retornado.
    /// </summary>
    Task<IAsyncDisposable?> TryAcquireAsync(string projectRoot, string operation, TimeSpan wait, CancellationToken ct);
}
