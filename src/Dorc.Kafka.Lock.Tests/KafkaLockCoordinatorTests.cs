using System.Collections.Concurrent;
using System.Reflection;
using Confluent.Kafka;
using Dorc.Kafka.Events.Configuration;
using Dorc.Kafka.Lock.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Dorc.Kafka.Lock.Tests;

/// <summary>
/// In-process tests for the coordinator's partition-ownership state machine.
/// Does not touch Kafka — exercises the slot transitions directly via private
/// callbacks so the CTS/TCS semantics are unit-testable without a
/// broker. The HA suite covers the librdkafka integration end-to-end.
/// </summary>
[TestClass]
public class KafkaLockCoordinatorTests
{
    private static KafkaLockCoordinator NewCoordinator(
        int partitionCount = 12,
        int? livenessTimeoutMs = null,
        TimeProvider? timeProvider = null,
        int? sessionTimeoutMs = null)
    {
        var opts = Options.Create(new KafkaLocksOptions
        {
            Enabled = false, // disabled so no consumer is built in StartAsync
            PartitionCount = partitionCount,
            ConsumerGroupId = "test",
            LivenessTimeoutMs = livenessTimeoutMs
        });
        var topics = Options.Create(new KafkaTopicsOptions());
        return new KafkaLockCoordinator(
            new FakeConnectionProvider { SessionTimeoutMs = sessionTimeoutMs },
            opts,
            topics,
            NullLogger<KafkaLockCoordinator>.Instance,
            timeProvider);
    }

    private static void InvokePrivate(object target, string name, params object[] args)
    {
        var m = target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic);
        m!.Invoke(target, args);
    }

    private static object GetPrivateField(object target, string name) =>
        target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(target)!;

    /// <summary>
    /// Slot cancellation is dispatched to the thread pool (never inline on the
    /// revoking thread), so tests must wait for the token rather than assert
    /// synchronously.
    /// </summary>
    private static bool WaitForCancellation(CancellationToken token, TimeSpan? timeout = null)
    {
        using var fired = new ManualResetEventSlim();
        using var reg = token.Register(fired.Set);
        return fired.Wait(timeout ?? TimeSpan.FromSeconds(10));
    }

    [TestMethod]
    public async Task Wait_CompletesWhenPartitionAssigned()
    {
        await using var c = NewCoordinator();
        var waitTask = c.WaitForPartitionOwnershipAsync(3, CancellationToken.None);
        Assert.IsFalse(waitTask.IsCompleted);
        InvokePrivate(c, "OnAssigned", 3);
        var token = await waitTask;
        Assert.IsFalse(token.IsCancellationRequested);
    }

    [TestMethod]
    public async Task Revoke_CancelsPriorToken_AndNewAcquireGetsFreshToken()
    {
        await using var c = NewCoordinator();
        InvokePrivate(c, "OnAssigned", 5);
        var firstToken = await c.WaitForPartitionOwnershipAsync(5, CancellationToken.None);

        InvokePrivate(c, "OnRevokedOrLost", 5, false);
        Assert.IsTrue(WaitForCancellation(firstToken), "Revoke must fire the prior LockLostToken.");

        var reacquire = c.WaitForPartitionOwnershipAsync(5, CancellationToken.None);
        Assert.IsFalse(reacquire.IsCompleted, "Fresh slot should block until reassigned.");
        InvokePrivate(c, "OnAssigned", 5);
        var secondToken = await reacquire;
        Assert.IsFalse(secondToken.IsCancellationRequested, "Fresh cycle must yield an uncancelled token.");
    }

    [TestMethod]
    public async Task Lost_FiresLockLostToken()
    {
        await using var c = NewCoordinator();
        InvokePrivate(c, "OnAssigned", 7);
        var t = await c.WaitForPartitionOwnershipAsync(7, CancellationToken.None);
        InvokePrivate(c, "OnRevokedOrLost", 7, true);
        Assert.IsTrue(WaitForCancellation(t), "Lost must fire the LockLostToken.");
    }

    [TestMethod]
    public async Task RepeatAssign_NoOpRebalance_DoesNotRecycleCts()
    {
        await using var c = NewCoordinator();
        InvokePrivate(c, "OnAssigned", 2);
        var first = await c.WaitForPartitionOwnershipAsync(2, CancellationToken.None);
        InvokePrivate(c, "OnAssigned", 2); // duplicate
        var second = await c.WaitForPartitionOwnershipAsync(2, CancellationToken.None);
        Assert.AreEqual(first, second, "No-op reassignment must preserve the live LockLostToken.");
    }

    [TestMethod]
    public async Task Wait_Cancellation_ThrowsOperationCanceled()
    {
        await using var c = NewCoordinator();
        using var cts = new CancellationTokenSource();
        var task = c.WaitForPartitionOwnershipAsync(11, cts.Token);
        cts.Cancel();
        await Assert.ThrowsExactlyAsync<TaskCanceledException>(async () => await task);
    }

    [TestMethod]
    public async Task GetPartitionFor_IsStable()
    {
        await using var c = NewCoordinator();
        var p1 = c.GetPartitionFor("env:Production");
        var p2 = c.GetPartitionFor("env:Production");
        Assert.AreEqual(p1, p2);
        Assert.IsTrue(p1 >= 0 && p1 < 12);
    }

    [TestMethod]
    public async Task GetPartitionFor_MatchesJavaClientToPositiveMapping()
    {
        // Apache Kafka UtilsTest.testMurmur2: murmur2("foobar") == -790332482.
        // The partition mapping is toPositive(hash) % partitions with
        // toPositive = hash & 0x7fffffff — pin the coordinator against it.
        await using var c = NewCoordinator(partitionCount: 12);
        var expected = (-790332482 & 0x7fffffff) % 12;
        Assert.AreEqual(expected, c.GetPartitionFor("foobar"));
    }

    // ---- Finding 8 / 11a: slot teardown runs off-thread, outside _slotLock, and disposes the CTS ----

    [TestMethod]
    public async Task Revoke_RunsLockLostCallbacks_OffRevokingThread_AndOutsideSlotLock()
    {
        await using var c = NewCoordinator();
        InvokePrivate(c, "OnAssigned", 4);
        var token = await c.WaitForPartitionOwnershipAsync(4, CancellationToken.None);

        var slotLock = GetPrivateField(c, "_slotLock");
        bool? slotLockHeldInCallback = null;
        var callbackThreadId = -1;
        using var done = new ManualResetEventSlim();
        using var reg = token.Register(() =>
        {
            slotLockHeldInCallback = System.Threading.Monitor.IsEntered(slotLock);
            callbackThreadId = Environment.CurrentManagedThreadId;
            done.Set();
        });

        var revokingThreadId = Environment.CurrentManagedThreadId;
        InvokePrivate(c, "OnRevokedOrLost", 4, false);

        Assert.IsTrue(done.Wait(TimeSpan.FromSeconds(10)), "LockLostToken callback must fire.");
        Assert.IsFalse(slotLockHeldInCallback!.Value,
            "Lock-lost callbacks must not run under _slotLock (deadlock risk).");
        Assert.AreNotEqual(revokingThreadId, callbackThreadId,
            "Lock-lost callbacks must not run inline on the revoking (consume/rebalance) thread.");
    }

    [TestMethod]
    public async Task Revoke_DisposesReplacedSlotCts()
    {
        await using var c = NewCoordinator();
        InvokePrivate(c, "OnAssigned", 6);
        await c.WaitForPartitionOwnershipAsync(6, CancellationToken.None);

        var slots = (ConcurrentDictionary<int, KafkaLockCoordinator.PartitionSlot>)GetPrivateField(c, "_slots");
        var replacedSlot = slots[6];

        InvokePrivate(c, "OnRevokedOrLost", 6, false);

        // Teardown (cancel → callbacks → dispose) is asynchronous; poll for dispose.
        var disposed = SpinWait.SpinUntil(() =>
        {
            try { _ = replacedSlot.Cts.Token; return false; }
            catch (ObjectDisposedException) { return true; }
        }, TimeSpan.FromSeconds(10));
        Assert.IsTrue(disposed, "Replaced slot CTSs must be disposed after cancellation (leak per revoke cycle).");
    }

    // ---- Finding 1: connectivity watchdog (split-brain guard) ----

    [TestMethod]
    public async Task Liveness_NoBrokerContact_CancelsAllSlots_OnceUntilContactRestored()
    {
        var time = new ManualTimeProvider();
        await using var c = NewCoordinator(livenessTimeoutMs: 1_000, timeProvider: time);
        c.RecordBrokerContact();

        InvokePrivate(c, "OnAssigned", 1);
        InvokePrivate(c, "OnAssigned", 2);
        var t1 = await c.WaitForPartitionOwnershipAsync(1, CancellationToken.None);
        var t2 = await c.WaitForPartitionOwnershipAsync(2, CancellationToken.None);

        Assert.IsFalse(c.EvaluateLiveness(), "Watchdog must not trip within the liveness window.");
        Assert.IsFalse(t1.IsCancellationRequested);

        time.Advance(TimeSpan.FromSeconds(2));
        Assert.IsTrue(c.EvaluateLiveness(), "Watchdog must trip once the liveness timeout elapses without contact.");
        Assert.IsTrue(WaitForCancellation(t1), "All held locks must report lost on liveness trip.");
        Assert.IsTrue(WaitForCancellation(t2), "All held locks must report lost on liveness trip.");

        // One-shot per outage: no repeated trip while contact is still absent.
        Assert.IsFalse(c.EvaluateLiveness());
    }

    [TestMethod]
    public async Task Liveness_AfterTrip_SlotsReestablishOnReassign_AndWatchdogRearms()
    {
        var time = new ManualTimeProvider();
        await using var c = NewCoordinator(livenessTimeoutMs: 1_000, timeProvider: time);
        c.RecordBrokerContact();

        InvokePrivate(c, "OnAssigned", 3);
        var lostToken = await c.WaitForPartitionOwnershipAsync(3, CancellationToken.None);
        time.Advance(TimeSpan.FromSeconds(2));
        Assert.IsTrue(c.EvaluateLiveness());
        Assert.IsTrue(WaitForCancellation(lostToken));

        // Reconnect: rebalance reassigns the partition → fresh live slot.
        InvokePrivate(c, "OnAssigned", 3);
        var freshToken = await c.WaitForPartitionOwnershipAsync(3, CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(10));
        Assert.IsFalse(freshToken.IsCancellationRequested, "Slots must re-establish after reconnect.");

        // OnAssigned records contact, so the watchdog re-arms for the next outage.
        time.Advance(TimeSpan.FromSeconds(2));
        Assert.IsTrue(c.EvaluateLiveness(), "Watchdog must re-arm after contact is restored.");
        Assert.IsTrue(WaitForCancellation(freshToken));
    }

    [TestMethod]
    public async Task ResolveLivenessTimeout_DefaultsToSessionTimeoutWithThirtySecondFloor()
    {
        await using (var low = NewCoordinator(sessionTimeoutMs: 10_000))
            Assert.AreEqual(TimeSpan.FromSeconds(30), low.ResolveLivenessTimeout());

        await using (var high = NewCoordinator(sessionTimeoutMs: 60_000))
            Assert.AreEqual(TimeSpan.FromSeconds(60), high.ResolveLivenessTimeout());

        await using (var configured = NewCoordinator(livenessTimeoutMs: 1_234, sessionTimeoutMs: 60_000))
            Assert.AreEqual(TimeSpan.FromMilliseconds(1_234), configured.ResolveLivenessTimeout());
    }

    // ---- Finding 2: fatal consumer errors ----

    [TestMethod]
    public async Task FatalConsumerError_CancelsAllSlots_AndRequestsRebuild()
    {
        await using var c = NewCoordinator();
        InvokePrivate(c, "OnAssigned", 9);
        var token = await c.WaitForPartitionOwnershipAsync(9, CancellationToken.None);

        c.OnFatalConsumerError(new Error(ErrorCode.Local_Fatal, "librdkafka fatal", true));

        Assert.IsTrue(WaitForCancellation(token), "Fatal errors must fire every held LockLostToken.");
        Assert.IsTrue(c.IsConsumerRebuildRequested, "Fatal errors must request a consumer rebuild.");
    }

    [TestMethod]
    public async Task ErrorHandler_FatalError_RoutesToFatalHandling()
    {
        await using var c = NewCoordinator();
        InvokePrivate(c, "OnAssigned", 10);
        var token = await c.WaitForPartitionOwnershipAsync(10, CancellationToken.None);

        c.OnConsumerError(new Error(ErrorCode.Local_Fatal, "fatal via error handler", true));

        Assert.IsTrue(WaitForCancellation(token));
        Assert.IsTrue(c.IsConsumerRebuildRequested);
    }

    private sealed class FakeConnectionProvider : Dorc.Kafka.Client.Connection.IKafkaConnectionProvider
    {
        public int? SessionTimeoutMs { get; set; }
        public Confluent.Kafka.ProducerConfig GetProducerConfig() => new();
        public Confluent.Kafka.ConsumerConfig GetConsumerConfig(string? groupIdOverride = null) =>
            new() { GroupId = groupIdOverride ?? "test", SessionTimeoutMs = SessionTimeoutMs };
    }
}
