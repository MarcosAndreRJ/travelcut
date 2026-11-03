using System.Diagnostics;
using TravelCut.Application.Ports;

namespace TravelCut.Infrastructure.Locks;

/// <summary>
/// Abertura exclusiva de arquivo (<see cref="FileShare.None"/>) usada pelas travas entre processos (projeto e migração de schema).
/// Classificação do resultado SEM depender de File.Exists:
/// <list type="bullet">
/// <item>Sharing/lock violation (HRESULT 32/33, ou seja <c>0x80070020</c>/<c>0x80070021</c>): "ocupado".</item>
/// <item><see cref="UnauthorizedAccessException"/>: transitório enquanto outro handle com DeleteOnClose ainda não sumiu no Windows.
/// Só vira erro (<see cref="ProjectLockException"/>) no fim do prazo, quando o arquivo não existe (ACL real).</item>
/// <item>Outras <see cref="IOException"/>: erro de E/S, encapsulado em <see cref="ProjectLockException"/>.</item>
/// </list>
/// </summary>
internal static class ExclusiveFileLock
{
    internal const int ErrorSharingViolation = 32;
    internal const int ErrorLockViolation = 33;

    public static readonly TimeSpan DefaultRetryInterval = TimeSpan.FromMilliseconds(50);

    /// <summary>Uma tentativa. Null = ocupado (ou acesso negado, ver <paramref name="denied"/>).</summary>
    public static FileStream? TryOpen(string path, out Exception? denied)
    {
        denied = null;
        try
        {
            return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 4096,
                FileOptions.DeleteOnClose);
        }
        catch (IOException ex) when (IsSharingOrLockViolation(ex))
        {
            return null;
        }
        catch (UnauthorizedAccessException ex)
        {
            denied = ex;
            return null;
        }
        catch (IOException ex)
        {
            throw new ProjectLockException(ProjectLockException.LockIoErrorCode,
                "Falha de E/S ao abrir a trava.", ex);
        }
    }

    /// <summary>Sharing/lock violation pelo código Win32 do HRESULT (os 16 bits baixos).</summary>
    public static bool IsSharingOrLockViolation(IOException ex) =>
        (ex.HResult & 0xFFFF) is ErrorSharingViolation or ErrorLockViolation;

    /// <summary>
    /// Decisão no fim do prazo: acesso negado com arquivo ausente é erro de ACL (relançado como estruturado);
    /// qualquer outro caso é "ocupado" (null).
    /// </summary>
    public static void ThrowIfPermanentlyDenied(string path, Exception? denied)
    {
        if (denied is not null && !File.Exists(path))
        {
            throw new ProjectLockException(ProjectLockException.LockAccessDeniedCode,
                "Sem permissão para criar a trava na pasta de travas do projeto.", denied);
        }
    }

    /// <summary>
    /// Versão síncrona com espera (usada na migração do schema, que já é síncrona). Devolve null se o prazo acabar com a trava ocupada.
    /// </summary>
    public static FileStream? WaitExclusive(string path, TimeSpan wait)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentOutOfRangeException.ThrowIfLessThan(wait, TimeSpan.Zero);

        var clock = Stopwatch.StartNew();
        while (true)
        {
            var stream = TryOpen(path, out var denied);
            if (stream is not null)
            {
                return stream;
            }

            var remaining = wait - clock.Elapsed;
            if (remaining <= TimeSpan.Zero)
            {
                ThrowIfPermanentlyDenied(path, denied);
                return null;
            }
            Thread.Sleep(remaining < DefaultRetryInterval ? remaining : DefaultRetryInterval);
        }
    }
}
