using TravelCut.Application.Editor;
using TravelCut.Application.Ports;
using TravelCut.Contracts.Dtos;
using TravelCut.Infrastructure.Locks;
using TravelCut.UnitTests.Planning;
using Xunit;

namespace TravelCut.UnitTests.Editor;

/// <summary>
/// Trava de aplicação no editor (editor-apply): entre processos (arquivo) e em processo (gate), antes da checagem de
/// idempotência e sem chamar o adaptador quando ocupada. Usa o EditorFixture (banco real, plano real, adaptador dublê).
/// </summary>
public sealed class EditorApplyLockTests : IDisposable
{
    private readonly EditorFixture _f = new();

    public void Dispose() => _f.Dispose();

    private static ApplyEditorPlanRequest ApplyRequest(EditorApplyPreviewDto preview, string key) =>
        new(TravelFixture.ProjectId, preview.Revision, preview.AssemblyFingerprint, false, key);

    private async Task<EditorApplyPreviewDto> PreparedAsync()
    {
        await _f.GeneratePlanAsync();
        var r = await _f.Editor.PrepareApplyAsync(new PrepareEditorApplyRequest(TravelFixture.ProjectId, null));
        Assert.True(r.Success, r.ErrorMessage);
        return r.Value!;
    }

    private static EditorAdapterApplyResult SimulatedOk() => new(
        Status: EditorResultStatus.Simulated,
        EditorVersion: null,
        Steps: [],
        ClipMappings: [],
        Rejected: [],
        Warnings: [],
        SnapshotAbsolutePath: null,
        ErrorCode: null,
        ErrorMessage: null);

    [Fact]
    public async Task TravaDeOutroProcessoAtiva_RetornaOperationInProgress_SemChamarAdaptador()
    {
        var preview = await PreparedAsync();
        var outroProcesso = new FileProjectOperationLock();
        await using var held = await outroProcesso.TryAcquireAsync(_f.Root, EditorUseCases.ApplyOperation, TimeSpan.Zero, CancellationToken.None);
        Assert.NotNull(held);

        var r = await _f.Editor.ApplyAsync(ApplyRequest(preview, "trava-outro-1"));

        Assert.False(r.Success);
        Assert.Equal("OPERATION_IN_PROGRESS", r.ErrorCode);
        Assert.Contains("outra aplicação deste projeto está em andamento", r.ErrorMessage);
        Assert.Equal(0, _f.Adapter.ApplyCalls);
    }

    [Fact]
    public async Task ApplyConcorrenteNoMesmoProjeto_UmExecutaOutroRecebeOperationInProgress_AdaptadorUmaVez()
    {
        var preview = await PreparedAsync();
        var entrou = new ManualResetEventSlim(false);
        var liberar = new ManualResetEventSlim(false);
        _f.Adapter.OnApply = _ =>
        {
            entrou.Set();
            liberar.Wait(TimeSpan.FromSeconds(10));
            return SimulatedOk();
        };

        var primeiro = Task.Run(() => _f.Editor.ApplyAsync(ApplyRequest(preview, "concorrente-a")));
        Assert.True(entrou.Wait(TimeSpan.FromSeconds(10)), "a primeira aplicação deveria chegar ao adaptador");

        var segundo = await _f.Editor.ApplyAsync(ApplyRequest(preview, "concorrente-b"));
        liberar.Set();
        var resultadoPrimeiro = await primeiro;

        Assert.False(segundo.Success);
        Assert.Equal("OPERATION_IN_PROGRESS", segundo.ErrorCode);
        Assert.True(resultadoPrimeiro.Success, resultadoPrimeiro.ErrorMessage);
        Assert.Equal(1, _f.Adapter.ApplyCalls);
    }

    [Fact]
    public async Task SegundoProcesso_DepoisDoPrimeiro_VeORegistroEReaproveitaPelaChave()
    {
        var preview = await PreparedAsync();
        var primeiro = await _f.Editor.ApplyAsync(ApplyRequest(preview, "registro-compartilhado"));
        Assert.True(primeiro.Success, primeiro.ErrorMessage);

        // Outra instância da aplicação (outro processo): trava e casos de uso próprios, mesmo banco e mesmo diretório.
        var outroProcesso = new EditorUseCases(
            _f.Projects, _f.Media, _f.Planning, _f.Adapter, _f.EditorStore, new TravelCut.Infrastructure.Repositories.FileSystemFileExistence(),
            new FileProjectOperationLock(), TimeProvider.System, Microsoft.Extensions.Logging.Abstractions.NullLogger<EditorUseCases>.Instance);

        var segundo = await outroProcesso.ApplyAsync(ApplyRequest(preview, "registro-compartilhado"));

        Assert.True(segundo.Success, segundo.ErrorMessage);
        Assert.True(segundo.Value!.ReusedPrevious);
        Assert.Equal(1, _f.Adapter.ApplyCalls);
    }

    [Fact]
    public async Task LiberaATravaDepoisDeExcecaoDoAdaptador()
    {
        var preview = await PreparedAsync();
        _f.Adapter.ThrowOnApply = new InvalidOperationException("falha simulada do adaptador");

        var r = await _f.Editor.ApplyAsync(ApplyRequest(preview, "excecao-adaptador"));

        // O caso de uso registra ERROR (EDITOR_BRIDGE_FAILED) em vez de propagar a exceção.
        Assert.True(r.Success);
        Assert.Equal(EditorResultStatus.Error, r.Value!.Status);

        var depois = await new FileProjectOperationLock()
            .TryAcquireAsync(_f.Root, EditorUseCases.ApplyOperation, TimeSpan.Zero, CancellationToken.None);
        Assert.NotNull(depois);
        await depois!.DisposeAsync();
    }

    [Fact]
    public async Task OperationInProgress_NaoEhGravadoComoRegistroNemConsomeIdempotencia()
    {
        var preview = await PreparedAsync();
        var outroProcesso = new FileProjectOperationLock();
        await using (await outroProcesso.TryAcquireAsync(_f.Root, EditorUseCases.ApplyOperation, TimeSpan.Zero, CancellationToken.None))
        {
            var ocupado = await _f.Editor.ApplyAsync(ApplyRequest(preview, "retry-chave"));
            Assert.Equal("OPERATION_IN_PROGRESS", ocupado.ErrorCode);
        }

        var depois = await _f.Editor.ApplyAsync(ApplyRequest(preview, "retry-chave"));

        Assert.True(depois.Success, depois.ErrorMessage);
        Assert.False(depois.Value!.ReusedPrevious);
        Assert.Equal(1, _f.Adapter.ApplyCalls);
    }
}
