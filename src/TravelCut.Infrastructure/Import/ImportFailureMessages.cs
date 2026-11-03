using System.ComponentModel;

namespace TravelCut.Infrastructure.Import;

/// <summary>Já existe ".tcpart" de outra execução no destino: a cópia não é feita e o arquivo temporário não é apagado.</summary>
public sealed class PartialFileConflictException : IOException
{
    public PartialFileConflictException() : base(ImportFailureMessages.PartialFileExists)
    {
    }
}

/// <summary>
/// Traduz exceções de cópia em mensagens curtas em PT-BR, sem caminhos e sem texto bruto de exceção.
/// O detalhe completo continua no log (ILogger), nunca no item enviado à UI.
/// </summary>
public static class ImportFailureMessages
{
    public const string AccessDenied = "Acesso negado ao copiar o arquivo. Verifique as permissões da pasta de destino.";
    public const string FileInUse = "O arquivo está em uso por outro programa. Feche-o e tente novamente.";
    public const string DiskFull = "Disco cheio no destino. Libere espaço e tente novamente.";
    public const string PartialFileExists = "Já existe um arquivo temporário (.tcpart) com este nome no destino. Remova-o manualmente e tente novamente.";
    public const string Generic = "Erro de leitura ou gravação ao copiar o arquivo. Detalhes no log.";

    public static string Describe(Exception ex)
    {
        if (ex is PartialFileConflictException)
            return PartialFileExists;

        return DescribeOs(ex);
    }

    // Códigos Win32 (parte baixa do HResult).
    private const int ErrorAccessDenied = 5;
    private const int ErrorHandleDiskFull = 39;
    private const int ErrorSharingViolation = 32;
    private const int ErrorLockViolation = 33;
    private const int ErrorDiskFull = 112;

    private static string DescribeOs(Exception ex)
    {
        if (ex is UnauthorizedAccessException)
            return AccessDenied;

        if (ex is Win32Exception win32)
        {
            return Describe(win32.NativeErrorCode);
        }

        if (ex is IOException io)
        {
            // No Windows, HResult de IOException carrega o código Win32 nos 16 bits inferiores (0x8007xxxx).
            int code = io.HResult & 0xFFFF;
            string fromCode = Describe(code);
            if (fromCode != Generic)
                return fromCode;
        }

        return Generic;
    }

    private static string Describe(int win32Code) => win32Code switch
    {
        ErrorAccessDenied => AccessDenied,
        ErrorSharingViolation or ErrorLockViolation => FileInUse,
        ErrorDiskFull or ErrorHandleDiskFull => DiskFull,
        _ => Generic
    };
}
