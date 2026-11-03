using TravelCut.Application.Ports;
using TravelCut.Contracts.Dtos;
using TravelCut.Infrastructure.Audio;
using TravelCut.Infrastructure.Locks;
using Xunit;

namespace TravelCut.UnitTests.Audio;

/// <summary>
/// Trava "analysis" entre processos: mantida enquanto o job roda; outra instância com a trava ocupada recebe
/// PIPELINE_IN_PROGRESS no início, e a trava é liberada ao fim do job.
/// </summary>
public sealed class PipelineAnalysisLockTests : IDisposable
{
    private readonly PipelineHarness _h = new();

    public void Dispose() => _h.Dispose();

    [Fact]
    public async Task TravaDeOutroProcessoAtiva_StartRecusaComPipelineInProgress()
    {
        var project = _h.CreateProject();
        _h.AddMedia(project, "1-Bruto/a.mp4");
        var outroProcesso = new FileProjectOperationLock();
        await using var held = await outroProcesso.TryAcquireAsync(_h.Root, PipelineJobManager.AnalysisLockOperation, TimeSpan.Zero, CancellationToken.None);
        Assert.NotNull(held);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _h.Manager.StartAsync(new StartPipelineRequest(project.ProjectId, null, null, null, false)));

        Assert.Equal("PIPELINE_IN_PROGRESS", ex.Message);
        Assert.Equal(0, _h.Whisper.TranscribeCalls);
    }

    [Fact]
    public async Task TravaLiberadaAoFimDoJob_PermiteNovaAnalise()
    {
        var project = _h.CreateProject();
        _h.AddMedia(project, "1-Bruto/a.mp4");

        await _h.Manager.StartAsync(new StartPipelineRequest(project.ProjectId, null, null, null, false));
        await _h.WaitForFinishAsync(project.ProjectId);

        var depois = await new FileProjectOperationLock()
            .TryAcquireAsync(_h.Root, PipelineJobManager.AnalysisLockOperation, TimeSpan.Zero, CancellationToken.None);
        Assert.NotNull(depois);
        await depois!.DisposeAsync();
    }

    [Fact]
    public async Task EnquantoOJobRoda_TravaEstaOcupadaParaOutroProcesso()
    {
        // Whisper disponível e preso até o cancelamento: o job fica em execução durante a verificação.
        using var h = new PipelineHarness(new FakeWhisperService { WaitForCancellation = true });
        var project = h.CreateProject();
        h.AddMedia(project, "1-Bruto/a.mp4");

        var jobId = await h.Manager.StartAsync(new StartPipelineRequest(project.ProjectId, null, null, null, false));
        try
        {
            var outro = await new FileProjectOperationLock()
                .TryAcquireAsync(h.Root, PipelineJobManager.AnalysisLockOperation, TimeSpan.Zero, CancellationToken.None);
            Assert.Null(outro);
        }
        finally
        {
            h.Manager.Cancel(jobId);
            await h.WaitForFinishAsync(project.ProjectId);
        }
    }
}
