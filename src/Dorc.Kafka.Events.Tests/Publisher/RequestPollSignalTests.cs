using System.Diagnostics;
using Dorc.Core.Events;

namespace Dorc.Kafka.Events.Tests.Publisher;

/// <summary>AT-5 / AT-12 / AT-13 — wake-up primitive semantics.</summary>
[TestClass]
public class RequestPollSignalTests
{
    [TestMethod]
    public async Task Signal_BeforeWait_LatchesAndReleasesNextWaitImmediately()
    {
        // AT-12: latch across no-waiter window.
        using var s = new RequestPollSignal();
        s.Signal();

        var sw = Stopwatch.StartNew();
        await s.WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None);
        sw.Stop();

        Assert.IsTrue(sw.ElapsedMilliseconds < 500, $"Pre-signalled wait should return immediately; took {sw.ElapsedMilliseconds}ms.");
    }

    [TestMethod]
    public async Task Signal_AfterWaitStarts_ShortCircuitsTimeout()
    {
        // AT-5: signal short-circuits a pending wait.
        using var s = new RequestPollSignal();
        var sw = Stopwatch.StartNew();
        var waitTask = s.WaitAsync(TimeSpan.FromSeconds(10), CancellationToken.None);
        await Task.Delay(50);
        s.Signal();
        await waitTask;
        sw.Stop();

        Assert.IsTrue(sw.ElapsedMilliseconds < 1_000, $"Signalled wait should short-circuit; took {sw.ElapsedMilliseconds}ms.");
    }

    [TestMethod]
    public async Task NoSignal_TimeoutElapsesNormally()
    {
        // Baseline: with no signal, wait elapses to its full timeout.
        using var s = new RequestPollSignal();
        var sw = Stopwatch.StartNew();
        await s.WaitAsync(TimeSpan.FromMilliseconds(300), CancellationToken.None);
        sw.Stop();

        Assert.IsTrue(sw.ElapsedMilliseconds >= 250, $"Wait should approximate the timeout; got {sw.ElapsedMilliseconds}ms.");
    }

    [TestMethod]
    public async Task Wait_HonoursCancellationToken()
    {
        using var s = new RequestPollSignal();
        using var cts = new CancellationTokenSource();
        var waitTask = s.WaitAsync(TimeSpan.FromSeconds(30), cts.Token);
        cts.CancelAfter(50);

        var sw = Stopwatch.StartNew();
        try { await waitTask; } catch (OperationCanceledException) { }
        sw.Stop();

        Assert.IsTrue(sw.ElapsedMilliseconds < 1_000, $"Wait should observe cancellation; took {sw.ElapsedMilliseconds}ms.");
    }

    [TestMethod]
    public void Signal_AfterDispose_IsNoOp()
    {
        // AT-13: disposed-signal must not throw.
        var s = new RequestPollSignal();
        s.Dispose();
        s.Signal(); // must not throw
    }

    [TestMethod]
    public async Task Signal_DuplicateCollapses_OnlyOneWaitReleased()
    {
        // R-4 duplicate-collapse semantic.
        using var s = new RequestPollSignal();
        s.Signal();
        s.Signal();
        s.Signal();

        // First wait — immediate.
        await s.WaitAsync(TimeSpan.FromMilliseconds(100), CancellationToken.None);

        // Second wait — should TIMEOUT (only one slot was latched).
        var sw = Stopwatch.StartNew();
        await s.WaitAsync(TimeSpan.FromMilliseconds(300), CancellationToken.None);
        sw.Stop();
        Assert.IsTrue(sw.ElapsedMilliseconds >= 250,
            $"Second wait should timeout (duplicate signals collapsed); got {sw.ElapsedMilliseconds}ms.");
    }

}
