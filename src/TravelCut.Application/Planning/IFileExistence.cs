namespace TravelCut.Application.Planning;

/// <summary>
/// Porta de verificação de existência de arquivo de mídia (usada pela validação do plano: SOURCE_FILE_MISSING).
/// Implementação real em Infrastructure; testes usam dublê em memória.
/// </summary>
public interface IFileExistence
{
    /// <summary>Verdadeiro quando o arquivo existe no caminho completo informado.</summary>
    Task<bool> ExistsAsync(string fullPath, CancellationToken ct = default);
}
