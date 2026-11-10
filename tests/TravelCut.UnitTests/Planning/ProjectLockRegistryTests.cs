using TravelCut.Application.Planning;
using Xunit;

namespace TravelCut.UnitTests.Planning;

/// <summary>B-c: o registro de locks por projeto libera entradas ociosas sem quebrar a exclusao mutua.</summary>
public sealed class ProjectLockRegistryTests
{
    [Fact]
    public async Task IdleEntries_AreRemoved_AfterRelease()
    {
        var reg = new ProjectLockRegistry();
        for (int i = 0; i < 50; i++)
        {
            using (await reg.AcquireAsync(Guid.NewGuid(), CancellationToken.None)) Assert.Equal(1, reg.ActiveCount);
        }
        Assert.Equal(0, reg.ActiveCount);
    }

    [Fact]
    public async Task CancelledWaiter_ReleasesItsReference_AndDoesNotBreakTheHolder()
    {
        var reg = new ProjectLockRegistry();
        var id = Guid.NewGuid();
        var holder = await reg.AcquireAsync(id, CancellationToken.None);
        using var cts = new CancellationTokenSource();
        var waiter = reg.AcquireAsync(id, cts.Token);
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiter);
        Assert.Equal(1, reg.ActiveCount);   // o detentor ainda segura a entrada

        holder.Dispose();
        Assert.Equal(0, reg.ActiveCount);
        holder.Dispose();                   // duplo Dispose e inofensivo
        Assert.Equal(0, reg.ActiveCount);
    }

    [Fact]
    public async Task SameProject_StaysMutuallyExclusive_UnderContention_AndTableDrains()
    {
        var reg = new ProjectLockRegistry();
        var ids = new[] { Guid.NewGuid(), Guid.NewGuid() };
        var inside = new int[2];
        int violations = 0;

        await Task.WhenAll(Enumerable.Range(0, 200).Select(n => Task.Run(async () =>
        {
            int k = n % 2;
            using (await reg.AcquireAsync(ids[k], CancellationToken.None))
            {
                if (Interlocked.Increment(ref inside[k]) != 1) Interlocked.Increment(ref violations);
                await Task.Yield();
                Interlocked.Decrement(ref inside[k]);
            }
        })));

        Assert.Equal(0, violations);
        Assert.Equal(0, reg.ActiveCount);
    }
}
