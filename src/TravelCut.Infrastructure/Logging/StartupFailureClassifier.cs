namespace TravelCut.Infrastructure.Logging;

/// <summary>
/// Traduz a causa técnica de uma falha de inicialização (banco/migração) para uma frase curta em português.
/// Sem stack trace e sem caminhos: a mensagem vai para o usuário; o detalhe completo fica no log.
/// </summary>
public static class StartupFailureClassifier
{
    public const string SchemaTooNew = "SCHEMA_TOO_NEW";
    public const string SchemaMigrationInProgress = "SCHEMA_MIGRATION_IN_PROGRESS";

    private const int ErrorDiskFull = 0x70;                       // ERROR_DISK_FULL (Win32)
    private const int HResultDiskFull = unchecked((int)0x80070070); // HRESULT_FROM_WIN32(ERROR_DISK_FULL)

    /// <summary>Causa em português, sem ponto final. Percorre a cadeia de InnerException.</summary>
    public static string Describe(Exception? exception)
    {
        for (Exception? current = exception; current != null; current = current.InnerException)
        {
            string message = current.Message ?? string.Empty;

            if (message.Contains(SchemaTooNew, StringComparison.Ordinal))
                return "banco criado por versão mais nova do TravelCut";

            if (message.Contains(SchemaMigrationInProgress, StringComparison.Ordinal))
                return "outra instância (app ou MCP) está atualizando o banco";

            if (message.Contains("database is locked", StringComparison.OrdinalIgnoreCase))
                return "o banco está em uso por outro processo (database is locked)";

            if (message.Contains("database or disk is full", StringComparison.OrdinalIgnoreCase)
                || message.Contains("disk is full", StringComparison.OrdinalIgnoreCase)
                || IsDiskFull(current))
                return "disco cheio";
        }

        return "erro inesperado na inicialização";
    }

    /// <summary>Mensagem curta para o usuário: causa + onde está o log. Nunca inclui stack trace.</summary>
    public static string UserMessage(Exception? exception, string logDirectory)
        => "Não foi possível iniciar o TravelCut Studio.\n\n"
           + $"Causa: {Describe(exception)}.\n\n"
           + $"Detalhes técnicos estão em: {logDirectory}";

    private static bool IsDiskFull(Exception exception)
        => exception is IOException io && (io.HResult == ErrorDiskFull || io.HResult == HResultDiskFull);
}
