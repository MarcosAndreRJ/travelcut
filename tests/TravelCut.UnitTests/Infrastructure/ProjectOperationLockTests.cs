using TravelCut.Application.Ports;
using TravelCut.Infrastructure.Locks;
using Xunit;

namespace TravelCut.UnitTests.Infrastructure;

/// <summary>
/// Trava entre processos (<see cref="FileProjectOperationLock"/>). Dois "processos" são simulados por duas instâncias
/// independentes da implementação sobre o mesmo diretório de projeto: a exclusão vem do FileShare.None no arquivo.
/// </summary>
public sealed class ProjectOperationLockTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "tc_lock_" + Guid.NewGuid().ToString("N"));

    public ProjectOperationLockTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // Melhor esforço: o handle pode ainda estar em liberação.
        }
    }

    private string LockPath(string operation) => Path.Combine(_root, "3-Trabalho", ".locks", operation + ".lock");

    [Fact]
    public async Task DoisProcessos_SegundoNaoObtemEnquantoPrimeiroSegura_E_ObtemDepoisDeLiberar()
    {
        IProjectOperationLock processoA = new FileProjectOperationLock();
        IProjectOperationLock processoB = new FileProjectOperationLock();

        await using var primeiro = await processoA.TryAcquireAsync(_root, "editor-apply", TimeSpan.Zero, CancellationToken.None);
        Assert.NotNull(primeiro);

        var segundo = await processoB.TryAcquireAsync(_root, "editor-apply", TimeSpan.Zero, CancellationToken.None);
        Assert.Null(segundo);

        await primeiro!.DisposeAsync();

        await using var terceiro = await processoB.TryAcquireAsync(_root, "editor-apply", TimeSpan.Zero, CancellationToken.None);
        Assert.NotNull(terceiro);
    }

    [Fact]
    public async Task OperacoesDiferentes_NaoSeBloqueiam()
    {
        IProjectOperationLock a = new FileProjectOperationLock();
        IProjectOperationLock b = new FileProjectOperationLock();

        await using var editor = await a.TryAcquireAsync(_root, "editor-apply", TimeSpan.Zero, CancellationToken.None);
        await using var analise = await b.TryAcquireAsync(_root, "analysis", TimeSpan.Zero, CancellationToken.None);

        Assert.NotNull(editor);
        Assert.NotNull(analise);
    }

    [Fact]
    public async Task EsperaCurta_ObtemQuandoOOutroLibaDentroDoPrazo()
    {
        IProjectOperationLock a = new FileProjectOperationLock();
        IProjectOperationLock b = new FileProjectOperationLock();
        var primeiro = await a.TryAcquireAsync(_root, "analysis", TimeSpan.Zero, CancellationToken.None);
        Assert.NotNull(primeiro);

        var liberar = Task.Run(async () =>
        {
            await Task.Delay(150);
            await primeiro!.DisposeAsync();
        });

        await using var segundo = await b.TryAcquireAsync(_root, "analysis", TimeSpan.FromSeconds(3), CancellationToken.None);
        await liberar;

        Assert.NotNull(segundo);
    }

    [Fact]
    public async Task EsperaEsgotada_DevolveNulo()
    {
        IProjectOperationLock a = new FileProjectOperationLock();
        IProjectOperationLock b = new FileProjectOperationLock();
        await using var primeiro = await a.TryAcquireAsync(_root, "analysis", TimeSpan.Zero, CancellationToken.None);
        Assert.NotNull(primeiro);

        var started = DateTime.UtcNow;
        var segundo = await b.TryAcquireAsync(_root, "analysis", TimeSpan.FromMilliseconds(200), CancellationToken.None);

        Assert.Null(segundo);
        Assert.True(DateTime.UtcNow - started >= TimeSpan.FromMilliseconds(150));
    }

    [Fact]
    public async Task ArquivoDeTravaFicaNaPastaDeTrabalhoEEhRemovidoAoLiberar()
    {
        IProjectOperationLock a = new FileProjectOperationLock();
        var handle = await a.TryAcquireAsync(_root, "editor-apply", TimeSpan.Zero, CancellationToken.None);
        Assert.NotNull(handle);

        Assert.True(File.Exists(LockPath("editor-apply")));

        await handle!.DisposeAsync();

        Assert.False(File.Exists(LockPath("editor-apply")));
    }

    [Fact]
    public void TextoDoDonoTemPidOperacaoEHorarioSemCaminhos()
    {
        // O arquivo fica com FileShare.None enquanto a trava existe; o formato do registro é verificado pelo texto gerado.
        var text = FileProjectOperationLock.OwnerText("analysis");

        Assert.Contains($"pid={Environment.ProcessId}", text);
        Assert.Contains("operacao=analysis", text);
        Assert.Contains("utc=", text);
        Assert.DoesNotContain(_root, text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\\", text);
    }

    [Fact]
    public async Task LiberadoAposExcecao_NoBlocoDeUso()
    {
        IProjectOperationLock a = new FileProjectOperationLock();
        IProjectOperationLock b = new FileProjectOperationLock();

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await using var handle = await a.TryAcquireAsync(_root, "editor-apply", TimeSpan.Zero, CancellationToken.None);
            Assert.NotNull(handle);
            throw new InvalidOperationException("falha simulada no adaptador");
        });

        await using var depois = await b.TryAcquireAsync(_root, "editor-apply", TimeSpan.Zero, CancellationToken.None);
        Assert.NotNull(depois);
    }

    [Fact]
    public async Task Cancelamento_NaEspera_LancaOperationCanceled()
    {
        IProjectOperationLock a = new FileProjectOperationLock();
        IProjectOperationLock b = new FileProjectOperationLock();
        await using var primeiro = await a.TryAcquireAsync(_root, "analysis", TimeSpan.Zero, CancellationToken.None);
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            b.TryAcquireAsync(_root, "analysis", TimeSpan.FromSeconds(10), cts.Token));
    }

    [Theory]
    [InlineData("../fora")]
    [InlineData("Com-Maiuscula")]
    [InlineData("com espaco")]
    [InlineData("")]
    public async Task NomeDeOperacaoInvalido_NaoTocaDisco(string operation)
    {
        IProjectOperationLock a = new FileProjectOperationLock();

        await Assert.ThrowsAnyAsync<ArgumentException>(() =>
            a.TryAcquireAsync(_root, operation, TimeSpan.Zero, CancellationToken.None));
        Assert.False(Directory.Exists(Path.Combine(_root, "3-Trabalho", ".locks")));
    }
}
