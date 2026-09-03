using Dorc.Core.Events;

namespace Dorc.Kafka.Events.Tests.Publisher;

/// <summary>   — wake-up primitive semantics.</summary>
[TestClass]
public class RequestPollSignalTests
{
    [TestMethod]
    public async Task Signal_BeforeWait_LatchesAndReleasesNextWaitImmediately()
    {
        // : latch across no-waiter window.
        using var s = new RequestPollSignal();
        s.Signal();

        await s.WaitAsync(TimeSpan.FromSeconds(30), CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(5));
    }

    [TestMethod]
    public async Task Signal_AfterWaitStarts_ShortCircuitsTimeout()
    {
        // : signal short-circuits a pending wait.
        using var s = new RequestPollSignal();
        var waitTask = s.WaitAsync(TimeSpan.FromSeconds(10), CancellationToken.None);
        await Task.Delay(50);
        s.Signal();

        await waitTask.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [TestMethod]
    public async Task NoSignal_TimeoutElapsesNormally()
    {
        // Baseline: with no signal, wait elapses to its full timeout.
        using var s = new RequestPollSignal();
        var waitTask = s.WaitAsync(TimeSpan.FromMilliseconds(300), CancellationToken.None);

        await Task.Delay(50);
        Assert.IsFalse(waitTask.IsCompleted, "An unsignalled wait should not complete immediately.");
        await waitTask.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [TestMethod]
    public async Task Wait_HonoursCancellationToken()
    {
        using var s = new RequestPollSignal();
        using var cts = new CancellationTokenSource();
        var waitTask = s.WaitAsync(TimeSpan.FromSeconds(30), cts.Token);
        cts.CancelAfter(50);

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => waitTask.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [TestMethod]
    public void Signal_AfterDispose_IsNoOp()
    {
        // : disposed-signal must not throw.
        var s = new RequestPollSignal();
        s.Dispose();
        s.Signal(); // must not throw
    }

    [TestMethod]
    public async Task Signal_DuplicateCollapses_OnlyOneWaitReleased()
    {
        // duplicate-collapse semantic.
        using var s = new RequestPollSignal();
        s.Signal();
        s.Signal();
        s.Signal();

        // First wait — immediate.
        await s.WaitAsync(TimeSpan.FromMilliseconds(100), CancellationToken.None);

        // Second wait — should TIMEOUT (only one slot was latched).
        var secondWait = s.WaitAsync(TimeSpan.FromMilliseconds(300), CancellationToken.None);
        await Task.Delay(50);
        Assert.IsFalse(secondWait.IsCompleted, "Duplicate signals should release only one wait.");
        await secondWait.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [TestMethod]
    public async Task Wait_AfterDispose_FallsBackToPlainDelay_NoThrow()
    {
        // Disposed-wait contract: the consumer loop keeps its baseline
        // cadence via Task.Delay rather than crashing on a dead semaphore.
        var s = new RequestPollSignal();
        s.Dispose();

        var waitTask = s.WaitAsync(TimeSpan.FromMilliseconds(120), CancellationToken.None);

        await Task.Delay(25);
        Assert.IsFalse(waitTask.IsCompleted, "A disposed wait should retain the polling delay.");
        await waitTask.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [TestMethod]
    public async Task Wait_AfterDispose_CancellationIsSwallowed()
    {
        // Shutdown path: cancellation during the disposed-fallback delay is
        // the expected exit and must not surface TaskCanceledException.
        var s = new RequestPollSignal();
        s.Dispose();
        using var cts = new CancellationTokenSource(50);

        await s.WaitAsync(TimeSpan.FromSeconds(10), cts.Token); // must not throw
    }

    [TestMethod]
    public void Dispose_CalledTwice_IsIdempotent()
    {
        var s = new RequestPollSignal();
        s.Dispose();
        s.Dispose(); // must not throw
    }
}
