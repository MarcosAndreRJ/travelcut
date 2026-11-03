using TravelCut.Application.Motion;
using TravelCut.Application.Ports;
using TravelCut.Contracts.Dtos;
using TravelCut.Infrastructure.Locks;
using TravelCut.Infrastructure.Motion;
using Xunit;

namespace TravelCut.Tests.Motion;

/// <summary>
/// Render entre processos (M6): duas instâncias de <see cref="MotionUseCases"/> com a mesma trava de arquivo simulam
/// dois processos. A operação <c>motion-render</c> é adquirida antes de qualquer escrita em 3-Trabalho/motion.
/// </summary>
public class MotionProjectLockTests : IDisposable
{
    private static readonly TimeSpan ShortWait = TimeSpan.FromMilliseconds(200);

    private readonly TempMotionProject _project = new();

    public void Dispose() => _project.Dispose();

    private static MotionRenderResultDto Simulated(MotionRenderContext ctx) => new(
        MotionRenderStatuses.Simulated, true, null, MotionPaths.ManifestRelative(ctx.RenderId),
        null, MockMotionRenderer.NoRenderMessage, DateTime.UtcNow, DateTime.UtcNow);

    private string LockFile => Path.Combine(_project.Root, "3-Trabalho", ".locks", MotionUseCases.RenderOperation + FileProjectOperationLock.LockExtension);

    [Fact]
    public async Task TwoInstances_SameFileLock_SecondGetsOperationInProgress_AndRendererCalledOnce()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var renderer = new CountingMotionRenderer("mock", async (_, ctx) =>
        {
            entered.TrySetResult();
            await release.Task;
            return Simulated(ctx);
        });
        var processA = new MotionUseCases(renderer, new FileProjectOperationLock(), lockWait: ShortWait);
        var processB = new MotionUseCases(renderer, new FileProjectOperationLock(), lockWait: ShortWait);
        var request = TempMotionProject.ValidRequest();

        Task<MotionRenderResultDto> firstCall = processA.RenderAsync(request, _project.Root);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));

        var second = await processB.RenderAsync(request, _project.Root);

        Assert.Equal(MotionRenderStatuses.Error, second.Status);
        Assert.Equal(MotionUseCases.OperationInProgressCode, second.ErrorCode);
        Assert.Equal(string.Empty, second.ManifestRelativePath);
        Assert.Equal(1, renderer.RenderCount);

        release.SetResult();
        var first = await firstCall;

        Assert.Equal(MotionRenderStatuses.Simulated, first.Status);
        Assert.Equal(1, renderer.RenderCount);
        Assert.False(File.Exists(LockFile), "a trava deve sumir ao ser liberada");
    }

    [Fact]
    public async Task HeldByAnotherOwner_RendererIsNotCalled_AndNothingIsWrittenInMotion()
    {
        var renderer = new CountingMotionRenderer("mock", (_, ctx) => Task.FromResult(Simulated(ctx)));
        var lockFromOtherProcess = new FileProjectOperationLock();
        await using var held = await lockFromOtherProcess.TryAcquireAsync(
            _project.Root, MotionUseCases.RenderOperation, TimeSpan.Zero, CancellationToken.None);
        Assert.NotNull(held);

        var useCases = new MotionUseCases(renderer, new FileProjectOperationLock(), lockWait: ShortWait);
        var result = await useCases.RenderAsync(TempMotionProject.ValidRequest(), _project.Root);

        Assert.Equal(MotionUseCases.OperationInProgressCode, result.ErrorCode);
        Assert.Equal(0, renderer.RenderCount);
        Assert.False(Directory.Exists(_project.WorkRoot), "nenhuma pasta de render pode ser criada com a trava ocupada");
    }

    [Fact]
    public async Task LockIsReleased_AfterRendererFailure_SoAnotherInstanceCanRender()
    {
        int calls = 0;
        var renderer = new CountingMotionRenderer("mock", (_, ctx) =>
        {
            calls++;
            if (calls == 1)
                throw new InvalidOperationException("falha simulada do renderer");
            return Task.FromResult(Simulated(ctx));
        });
        var request = TempMotionProject.ValidRequest();

        var failed = await new MotionUseCases(renderer, new FileProjectOperationLock(), lockWait: ShortWait)
            .RenderAsync(request, _project.Root);
        var next = await new MotionUseCases(renderer, new FileProjectOperationLock(), lockWait: ShortWait)
            .RenderAsync(request, _project.Root);

        Assert.Equal(MotionUseCases.RenderInternalErrorCode, failed.ErrorCode);
        Assert.Equal(MotionRenderStatuses.Simulated, next.Status);
        Assert.Equal(2, calls);
        Assert.False(File.Exists(LockFile), "a trava deve ser liberada mesmo quando o renderer falha");
    }

    [Fact]
    public async Task InvalidRequest_DoesNotTakeTheLock()
    {
        var renderer = new CountingMotionRenderer("mock", (_, ctx) => Task.FromResult(Simulated(ctx)));
        var useCases = new MotionUseCases(renderer, new FileProjectOperationLock(), lockWait: ShortWait);

        var result = await useCases.RenderAsync(TempMotionProject.ValidRequest(templateId: "nao_existe"), _project.Root);

        Assert.Equal(MotionRequestValidator.UnknownTemplateCode, result.ErrorCode);
        Assert.False(Directory.Exists(Path.Combine(_project.Root, "3-Trabalho", ".locks")));
    }

    [Fact]
    public void NegativeLockWait_IsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new MotionUseCases(new MockMotionRenderer(), new FileProjectOperationLock(), lockWait: TimeSpan.FromSeconds(-1)));
    }
}
