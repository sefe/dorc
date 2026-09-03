using Dorc.Core.Events;
using Microsoft.Extensions.Logging;
using NSubstitute;
using System.Collections.Concurrent;

namespace Dorc.Monitor.Tests
{
    /// <summary>
    /// Pins the DeploymentEngine backpressure loop (UC3):
    /// <list type="bullet">
    ///   <item>Completed tasks are pruned each iteration, then — when
    ///   MaxConcurrentDeployments &gt; 0 and the in-flight count is at or over
    ///   the cap — the loop awaits <c>Task.WhenAny</c> before pulling new work,
    ///   so <c>ExecuteRequests</c> is NOT called again while at the cap.</item>
    ///   <item>Once one in-flight deployment completes, the loop resumes and
    ///   <c>ExecuteRequests</c> is called again (even if the post-prune count
    ///   still equals the cap — the guard is a single <c>if</c>, not a loop).</item>
    ///   <item>MaxConcurrentDeployments = 0 means unlimited: the loop never
    ///   blocks on in-flight tasks.</item>
    /// </list>
    /// Iteration timing is driven deterministically through a controllable
    /// IRequestPollSignal (the engine ends each iteration in
    /// <c>pollSignal.WaitAsync</c>) — no Thread.Sleep racing.
    /// </summary>
    [TestClass]
    public class DeploymentEngineBackpressureTests
    {
        private static readonly TimeSpan AssertTimeout = TimeSpan.FromSeconds(10);
        /// <summary>Bounded window for "did NOT happen" checks.</summary>
        private static readonly TimeSpan NegativeCheckWindow = TimeSpan.FromMilliseconds(250);

        private IDeploymentRequestStateProcessor mockProcessor = null!;
        private IMonitorConfiguration mockConfiguration = null!;
        private ControllablePollSignal pollSignal = null!;
        private DeploymentEngine sut = null!;

        [TestInitialize]
        public void Setup()
        {
            mockProcessor = Substitute.For<IDeploymentRequestStateProcessor>();
            mockConfiguration = Substitute.For<IMonitorConfiguration>();
            pollSignal = new ControllablePollSignal();
            sut = new DeploymentEngine(
                Substitute.For<ILogger<DeploymentEngine>>(),
                mockProcessor,
                mockConfiguration,
                pollSignal);
        }

        [TestMethod]
        public async Task ProcessDeploymentRequests_AtMaxConcurrent_DoesNotPullNewWorkUntilOneCompletes()
        {
            // Arrange: cap of 2, first ExecuteRequests hands back 3 long-running
            // tasks (the engine adds everything a single call returns; the cap
            // gates the NEXT pull, not the batch size).
            mockConfiguration.MaxConcurrentDeployments.Returns(2);

            var tcs1 = NewTcs();
            var tcs2 = NewTcs();
            var tcs3 = NewTcs();
            var firstExecuteCall = NewTcs();
            var secondExecuteCall = NewTcs();
            var executeCalls = 0;

            mockProcessor
                .ExecuteRequests(false, Arg.Any<ConcurrentDictionary<int, CancellationTokenSource>>(), Arg.Any<CancellationToken>())
                .Returns(_ =>
                {
                    var call = Interlocked.Increment(ref executeCalls);
                    if (call == 1)
                    {
                        firstExecuteCall.TrySetResult(true);
                        return new[] { tcs1.Task, tcs2.Task, tcs3.Task };
                    }
                    secondExecuteCall.TrySetResult(true);
                    return Array.Empty<Task>();
                });

            var secondIterationSweep = NewTcs();
            var sweepCalls = 0;
            mockProcessor
                .When(p => p.AbandonRequests(false, Arg.Any<ConcurrentDictionary<int, CancellationTokenSource>>(), Arg.Any<CancellationToken>()))
                .Do(_ =>
                {
                    if (Interlocked.Increment(ref sweepCalls) == 2)
                        secondIterationSweep.TrySetResult(true);
                });

            using var monitorCts = new CancellationTokenSource();
            var cancellationSources = new ConcurrentDictionary<int, CancellationTokenSource>();

            // Act: iteration 1 pulls the 3 tasks, then parks on the poll signal.
            var engineTask = sut.ProcessDeploymentRequestsAsync(false, cancellationSources, monitorCts.Token, iterationDelayMs: 60_000);
            await firstExecuteCall.Task.WaitAsync(AssertTimeout);

            // Release iteration 2: its sweeps run, then — at 3 in-flight >= cap 2 —
            // the loop must block in Task.WhenAny BEFORE calling ExecuteRequests.
            pollSignal.ReleaseIteration();
            await secondIterationSweep.Task.WaitAsync(AssertTimeout);

            var pulledWhileAtCap = await Task.WhenAny(secondExecuteCall.Task, Task.Delay(NegativeCheckWindow)) == secondExecuteCall.Task;
            Assert.IsFalse(pulledWhileAtCap,
                "ExecuteRequests must not be called again while the in-flight count is at MaxConcurrentDeployments.");
            Assert.AreEqual(1, Volatile.Read(ref executeCalls));

            // Complete one deployment: WhenAny returns, the completed task is
            // pruned, and the loop pulls new work again.
            tcs1.SetResult(true);
            await secondExecuteCall.Task.WaitAsync(AssertTimeout);
            Assert.AreEqual(2, Volatile.Read(ref executeCalls));

            await ShutDownAsync(engineTask, monitorCts, tcs2, tcs3);
        }

        [TestMethod]
        public async Task ProcessDeploymentRequests_MaxConcurrentZero_IsUnlimited_NeverBlocksOnInFlightTasks()
        {
            // Arrange: cap disabled; 3 in-flight tasks must not stop the next pull.
            mockConfiguration.MaxConcurrentDeployments.Returns(0);

            var tcs1 = NewTcs();
            var tcs2 = NewTcs();
            var tcs3 = NewTcs();
            var firstExecuteCall = NewTcs();
            var secondExecuteCall = NewTcs();
            var executeCalls = 0;

            mockProcessor
                .ExecuteRequests(false, Arg.Any<ConcurrentDictionary<int, CancellationTokenSource>>(), Arg.Any<CancellationToken>())
                .Returns(_ =>
                {
                    var call = Interlocked.Increment(ref executeCalls);
                    if (call == 1)
                    {
                        firstExecuteCall.TrySetResult(true);
                        return new[] { tcs1.Task, tcs2.Task, tcs3.Task };
                    }
                    secondExecuteCall.TrySetResult(true);
                    return Array.Empty<Task>();
                });

            using var monitorCts = new CancellationTokenSource();
            var cancellationSources = new ConcurrentDictionary<int, CancellationTokenSource>();

            // Act
            var engineTask = sut.ProcessDeploymentRequestsAsync(false, cancellationSources, monitorCts.Token, iterationDelayMs: 60_000);
            await firstExecuteCall.Task.WaitAsync(AssertTimeout);

            // Iteration 2 with all 3 tasks still running must reach
            // ExecuteRequests without waiting for any completion.
            pollSignal.ReleaseIteration();
            await secondExecuteCall.Task.WaitAsync(AssertTimeout);
            Assert.AreEqual(2, Volatile.Read(ref executeCalls));

            await ShutDownAsync(engineTask, monitorCts, tcs1, tcs2, tcs3);
        }

        private static TaskCompletionSource<bool> NewTcs()
            => new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>
        /// Graceful-shutdown teardown: the engine's exit path awaits every
        /// in-flight task, so complete the remaining TCSs before cancelling.
        /// </summary>
        private async Task ShutDownAsync(
            Task engineTask,
            CancellationTokenSource monitorCts,
            params TaskCompletionSource<bool>[] remaining)
        {
            foreach (var tcs in remaining)
                tcs.TrySetResult(true);
            monitorCts.Cancel();
            pollSignal.ReleaseIteration(); // wake the loop if parked on the signal
            await engineTask.WaitAsync(AssertTimeout);
        }

        /// <summary>
        /// Poll signal whose WaitAsync completes only when the test releases an
        /// iteration (the configured timeout is deliberately ignored), making
        /// loop progress fully test-driven. Cancellation still unparks the loop
        /// for shutdown, mirroring the production RequestPollSignal contract.
        /// </summary>
        private sealed class ControllablePollSignal : IRequestPollSignal
        {
            private readonly SemaphoreSlim _iterations = new(initialCount: 0);

            public void Signal()
            {
                // The engine never signals itself; consumers do. Not used here.
            }

            public async Task WaitAsync(TimeSpan timeout, CancellationToken cancellationToken)
            {
                try { await _iterations.WaitAsync(cancellationToken); }
                catch (OperationCanceledException)
                {
                    // Engine swallows cancellation here and re-checks the loop
                    // condition; match RequestPollSignal by rethrowing nothing.
                }
            }

            public void ReleaseIteration() => _iterations.Release();
        }
    }
}
