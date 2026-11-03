using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using TravelCut.Application.Audio;
using TravelCut.Application.Ports;
using TravelCut.Contracts.Dtos;
using TravelCut.Infrastructure.Audio;
using Xunit;

namespace TravelCut.UnitTests.Audio;

/// <summary>
/// B6: falha ao liberar a trava entre processos ao fim do job é registrada em log e não marca a análise como failed
/// (o trabalho da análise já terminou; a falha é só da liberação).
/// </summary>
public sealed class PipelineLockReleaseFailureTests : IDisposable
{
    private readonly PipelineHarness _h = new();

    public void Dispose() => _h.Dispose();

    [Fact]
    public async Task FalhaAoLiberarATrava_NaoMarcaOJobComoFailed()
    {
        var trava = new ThrowingReleaseLock();
        var manager = new PipelineJobManager(
            NullLogger<PipelineJobManager>.Instance,
            _h.Services.GetRequiredService<IServiceScopeFactory>(),
            _h.Whisper,
            _h.Silence,
            new SceneClassifierService(NullLogger<SceneClassifierService>.Instance),
            new CutCandidateService(NullLogger<CutCandidateService>.Instance),
            trava);
        var project = _h.CreateProject();
        _h.AddMedia(project, "1-Bruto/a.mp4");

        await manager.StartAsync(new StartPipelineRequest(project.ProjectId, null, null, null, false));
        var status = await EsperarFimAsync(manager, project.ProjectId);

        Assert.NotEqual("failed", status.State);
        Assert.Equal(1, trava.ReleaseAttempts);
    }

    private static async Task<PipelineStatusDto> EsperarFimAsync(PipelineJobManager manager, Guid projectId)
    {
        var limite = DateTime.UtcNow + TimeSpan.FromSeconds(20);
        while (true)
        {
            var status = await manager.GetStatusAsync(projectId);
            if (status.State is "completed" or "completed_with_issues" or "failed" or "canceled")
            {
                return status;
            }
            if (DateTime.UtcNow > limite)
            {
                throw new TimeoutException($"Análise não terminou; estado atual: {status.State}");
            }
            await Task.Delay(20);
        }
    }

    /// <summary>Trava dublê: adquire sempre, e falha ao liberar (como um handle já fechado por outro motivo).</summary>
    private sealed class ThrowingReleaseLock : IProjectOperationLock
    {
        public int ReleaseAttempts;

        public Task<IAsyncDisposable?> TryAcquireAsync(string projectRoot, string operation, TimeSpan wait, CancellationToken ct) =>
            Task.FromResult<IAsyncDisposable?>(new Held(this));

        private sealed class Held(ThrowingReleaseLock owner) : IAsyncDisposable
        {
            public ValueTask DisposeAsync()
            {
                Interlocked.Increment(ref owner.ReleaseAttempts);
                throw new IOException("falha simulada ao liberar a trava");
            }
        }
    }
}
