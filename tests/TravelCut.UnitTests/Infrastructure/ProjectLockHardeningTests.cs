using System.Reflection;
using TravelCut.Application.Ports;
using TravelCut.Infrastructure.Locks;
using Xunit;

namespace TravelCut.UnitTests.Infrastructure;

/// <summary>
/// Endurecimento da trava entre processos (revisão de concorrência):
/// A1 (disputa sem exceção e sem duas posses), M1 (handle liberado se a escrita do dono falhar/for cancelada),
/// B1 (erros estruturados: pasta do projeto ausente, pasta de travas inacessível, ACL real).
/// </summary>
public sealed class ProjectLockHardeningTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "tc_lock_hard_" + Guid.NewGuid().ToString("N"));

    public ProjectLockHardeningTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // Melhor esforço: handles de teste podem estar liberando.
        }
    }

    private string LocksDir => Path.Combine(_root, "3-Trabalho", ".locks");
    private string LockPath(string operation) => Path.Combine(LocksDir, operation + ".lock");

    // ---- A1: disputa entre tarefas ----

    [Fact]
    public async Task OitoTarefasDisputandoATrava_200CiclosCada_SemExcecaoESemDuasPosses()
    {
        const int tasks = 8;
        const int cycles = 200;
        int holders = 0;
        int maxHolders = 0;
        int acquired = 0;
        var gate = new object();

        var workers = Enumerable.Range(0, tasks).Select(_ => Task.Run(async () =>
        {
            var lockImpl = new FileProjectOperationLock();
            for (int i = 0; i < cycles; i++)
            {
                await using var held = await lockImpl.TryAcquireAsync(_root, "stress-lock",
                    TimeSpan.FromSeconds(30), CancellationToken.None);
                Assert.NotNull(held);

                lock (gate)
                {
                    holders++;
                    maxHolders = Math.Max(maxHolders, holders);
                    acquired++;
                }
                await Task.Yield();
                lock (gate)
                {
                    holders--;
                }
            }
        })).ToArray();

        await Task.WhenAll(workers);

        Assert.Equal(tasks * cycles, acquired);
        Assert.Equal(1, maxHolders);
        Assert.False(File.Exists(LockPath("stress-lock")));
    }

    [Fact]
    public async Task OcupadaPorHandleExterno_DevolveNulo_SemDependerDeFileExists()
    {
        Directory.CreateDirectory(LocksDir);
        using var externo = new FileStream(LockPath("analysis"), FileMode.OpenOrCreate, FileAccess.ReadWrite,
            FileShare.None, 4096, FileOptions.DeleteOnClose);

        var segundo = await new FileProjectOperationLock().TryAcquireAsync(_root, "analysis", TimeSpan.Zero, CancellationToken.None);

        Assert.Null(segundo);
    }

    // ---- M1: escrita do dono cancelada ----

    [Fact]
    public async Task CancelamentoNaEscritaDoDono_LiberaOHandle_NovaAquisicaoFuncionaDeImediato()
    {
        Directory.CreateDirectory(LocksDir);
        var path = LockPath("owner-cancel");
        var stream = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 4096,
            FileOptions.DeleteOnClose);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // HoldAsync é o passo que grava o dono após a abertura (privado; invocado por reflexão para ser determinístico).
        var hold = typeof(FileProjectOperationLock).GetMethod("HoldAsync", BindingFlags.NonPublic | BindingFlags.Static)!;
        var pending = (Task<IAsyncDisposable>)hold.Invoke(null, [stream, "owner-cancel", cts.Token])!;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);

        await using var depois = await new FileProjectOperationLock()
            .TryAcquireAsync(_root, "owner-cancel", TimeSpan.Zero, CancellationToken.None);
        Assert.NotNull(depois);
    }

    // ---- B1: erros estruturados ----

    [Fact]
    public async Task PastaDoProjetoAusente_LancaProjectRootMissing_SemCriarNada()
    {
        var ausente = Path.Combine(_root, "nao-existe");

        var ex = await Assert.ThrowsAsync<ProjectLockException>(() =>
            new FileProjectOperationLock().TryAcquireAsync(ausente, "analysis", TimeSpan.Zero, CancellationToken.None));

        Assert.Equal(ProjectLockException.ProjectRootMissingCode, ex.Code);
        Assert.DoesNotContain(ausente, ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(Directory.Exists(ausente));
    }

    [Fact]
    public async Task PastaDeTravasOcupadaPorArquivo_LancaErroEstruturado_NaoIOExceptionCrua()
    {
        Directory.CreateDirectory(Path.Combine(_root, "3-Trabalho"));
        File.WriteAllText(LocksDir, "não é pasta");

        var ex = await Assert.ThrowsAsync<ProjectLockException>(() =>
            new FileProjectOperationLock().TryAcquireAsync(_root, "analysis", TimeSpan.Zero, CancellationToken.None));

        Assert.Equal(ProjectLockException.LocksFolderUnavailableCode, ex.Code);
        Assert.DoesNotContain(_root, ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AcessoNegadoPersistenteSemArquivo_LancaLockAccessDenied()
    {
        // Diretório no lugar do arquivo de trava: abrir como arquivo falha com acesso negado e File.Exists é falso (ACL real).
        Directory.CreateDirectory(LockPath("analysis"));

        var ex = await Assert.ThrowsAsync<ProjectLockException>(() =>
            new FileProjectOperationLock().TryAcquireAsync(_root, "analysis", TimeSpan.Zero, CancellationToken.None));

        Assert.Equal(ProjectLockException.LockAccessDeniedCode, ex.Code);
        Assert.IsAssignableFrom<UnauthorizedAccessException>(ex.InnerException);
    }
}
