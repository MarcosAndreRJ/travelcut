using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using TravelCut.Application.Ports;

namespace TravelCut.Infrastructure.Locks;

/// <summary>
/// Trava entre processos baseada em arquivo: <c>&lt;raiz&gt;/3-Trabalho/.locks/&lt;operação&gt;.lock</c>, aberto com
/// <see cref="FileShare.None"/> e <see cref="FileOptions.DeleteOnClose"/>. Enquanto o handle estiver aberto, outro
/// processo (ou outra instância no mesmo processo) não consegue abrir o arquivo; ao liberar, o arquivo some.
/// O conteúdo tem só pid e horário UTC (sem caminhos nem dados do projeto).
/// "Ocupada" devolve null; problemas estruturais (pasta do projeto ausente, pasta de travas inacessível, E/S) lançam
/// <see cref="ProjectLockException"/> com código estável.
/// Limitação: a exclusão depende do comportamento de FileShare.None; o alvo é Windows (SMB local). Em sistemas Unix a
/// semântica é de flock consultivo e não é o alvo deste projeto.
/// </summary>
public sealed class FileProjectOperationLock : IProjectOperationLock
{
    public const string LocksRelativeFolder = "3-Trabalho/.locks";
    public const string LockExtension = ".lock";

    private static readonly TimeSpan RetryInterval = ExclusiveFileLock.DefaultRetryInterval;
    private static readonly Regex OperationPattern = new(@"^[a-z][a-z0-9-]{0,40}\z", RegexOptions.CultureInvariant);

    public async Task<IAsyncDisposable?> TryAcquireAsync(string projectRoot, string operation, TimeSpan wait, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectRoot);
        ArgumentException.ThrowIfNullOrEmpty(operation);
        ArgumentOutOfRangeException.ThrowIfLessThan(wait, TimeSpan.Zero);
        if (!OperationPattern.IsMatch(operation))
        {
            throw new ArgumentException("Nome de operação inválido.", nameof(operation));
        }

        var root = Path.GetFullPath(projectRoot);
        if (!Directory.Exists(root))
        {
            throw new ProjectLockException(ProjectLockException.ProjectRootMissingCode,
                "A pasta do projeto não foi encontrada.");
        }

        var locksDir = Path.Combine(root, "3-Trabalho", ".locks");
        EnsureLocksDirectory(locksDir);
        var path = Path.Combine(locksDir, operation + LockExtension);

        var clock = Stopwatch.StartNew();
        while (true)
        {
            ct.ThrowIfCancellationRequested();

            var stream = ExclusiveFileLock.TryOpen(path, out var denied);
            if (stream is not null)
            {
                return await HoldAsync(stream, operation, ct);
            }

            var remaining = wait - clock.Elapsed;
            if (remaining <= TimeSpan.Zero)
            {
                // Acesso negado persistente com arquivo ausente é ACL real; com arquivo presente, outro processo segura.
                ExclusiveFileLock.ThrowIfPermanentlyDenied(path, denied);
                return null;
            }
            await Task.Delay(remaining < RetryInterval ? remaining : RetryInterval, ct);
        }
    }

    /// <summary>Cria a pasta de travas; arquivo no lugar da pasta ou sem permissão viram erro estruturado.</summary>
    private static void EnsureLocksDirectory(string locksDir)
    {
        try
        {
            if (File.Exists(locksDir))
            {
                throw new ProjectLockException(ProjectLockException.LocksFolderUnavailableCode,
                    "A pasta de travas do projeto está ocupada por um arquivo.");
            }
            Directory.CreateDirectory(locksDir);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new ProjectLockException(ProjectLockException.LocksFolderUnavailableCode,
                "A pasta de travas do projeto não está disponível.", ex);
        }
    }

    /// <summary>
    /// Escreve o registro do dono. Qualquer falha (inclusive cancelamento) descarta o handle, liberando a trava
    /// imediatamente: sem isso, um handle órfão bloquearia a operação até o fim do processo.
    /// </summary>
    private static async Task<IAsyncDisposable> HoldAsync(FileStream stream, string operation, CancellationToken ct)
    {
        try
        {
            await WriteOwnerAsync(stream, operation, ct);
            return new Held(stream);
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    /// <summary>Texto gravado no arquivo de trava: pid, operação e horário UTC. Nunca caminhos.</summary>
    public static string OwnerText(string operation) =>
        $"pid={Environment.ProcessId}\noperacao={operation}\nutc={DateTime.UtcNow:O}\n";

    private static async Task WriteOwnerAsync(FileStream stream, string operation, CancellationToken ct)
    {
        try
        {
            var bytes = Encoding.UTF8.GetBytes(OwnerText(operation));
            stream.SetLength(0);
            await stream.WriteAsync(bytes, ct);
            await stream.FlushAsync(ct);
        }
        catch (IOException)
        {
            // Sem o registro do dono a trava continua válida: o arquivo só informa quem segura.
        }
    }

    private sealed class Held(FileStream stream) : IAsyncDisposable
    {
        private int _released;

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
            {
                await stream.DisposeAsync();
            }
        }
    }
}
