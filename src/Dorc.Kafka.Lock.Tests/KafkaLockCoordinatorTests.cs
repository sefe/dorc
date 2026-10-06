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
        int? sessionTimeoutMs = null,
        int? lockSessionTimeoutMs = null,
        bool useStaticGroupMembership = true,
        string? replicaId = null)
    {
        var opts = Options.Create(new KafkaLocksOptions
        {
            Enabled = false, // disabled so no consumer is built in StartAsync
            PartitionCount = partitionCount,
            ConsumerGroupId = "test",
            LivenessTimeoutMs = livenessTimeoutMs,
            SessionTimeoutMs = lockSessionTimeoutMs,
            UseStaticGroupMembership = useStaticGroupMembership
        });
        var topics = Options.Create(new KafkaTopicsOptions());
        return new KafkaLockCoordinator(
            new FakeConnectionProvider { SessionTimeoutMs = sessionTimeoutMs },
            opts,
            topics,
            NullLogger<KafkaLockCoordinator>.Instance,
            timeProvider,
            replicaId is null
                ? null
                : Options.Create(new Dorc.Kafka.Client.Configuration.KafkaClientOptions { ReplicaId = replicaId }));
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

    // ---- C2 fix: slot CTS must be cancelled synchronously in OnRevokedOrLost,
    //             outside _slotLock and before returning from the rebalance callback ----

    [TestMethod]
    public async Task Revoke_CancelsLockLostToken_SynchronouslyOutsideSlotLock_BeforeCallbackReturns()
    {
        // The CTS MUST be cancelled synchronously before OnRevokedOrLost returns.
        // In a cooperative rebalance the broker does not send OnAssigned to the peer
        // until after our callback returns, so cancelling before return ensures the
        // old holder's LockLostToken fires before any peer can acquire the lock.
        // Callbacks running on the revoking thread is acceptable: production callbacks
        // (linked CTS cancellation, event signalling) are quick and do not block.
        await using var c = NewCoordinator();
        InvokePrivate(c, "OnAssigned", 4);
        var token = await c.WaitForPartitionOwnershipAsync(4, CancellationToken.None);

        var slotLock = GetPrivateField(c, "_slotLock");
        bool? slotLockHeldInCallback = null;
        bool cancelledBeforeReturn = false;
        using var reg = token.Register(() =>
        {
            slotLockHeldInCallback = System.Threading.Monitor.IsEntered(slotLock);
            cancelledBeforeReturn = true;
        });

        InvokePrivate(c, "OnRevokedOrLost", 4, false);

        // The callback must have fired BEFORE OnRevokedOrLost returned (synchronous).
        Assert.IsTrue(cancelledBeforeReturn, "LockLostToken callback must fire synchronously during OnRevokedOrLost.");
        Assert.IsTrue(token.IsCancellationRequested, "LockLostToken must be cancelled before OnRevokedOrLost returns.");
        Assert.IsFalse(slotLockHeldInCallback!.Value,
            "Lock-lost callbacks must not run under _slotLock (deadlock risk).");
    }

    // Regression: the revoked slot's LockLostToken has escaped to lock holders,
    // who may block on token.WaitHandle or call token.Register to observe loss.
    // Disposing the CTS makes both throw ObjectDisposedException on the holder's
    // side, so the coordinator must cancel but never dispose an escaped CTS (a
    // cancelled, timerless CTS is plain managed memory for the GC).
    [TestMethod]
    public async Task Revoke_ReplacedSlotToken_RemainsObservableByHolders()
    {
        await using var c = NewCoordinator();
        InvokePrivate(c, "OnAssigned", 6);
        await c.WaitForPartitionOwnershipAsync(6, CancellationToken.None);

        var slots = (ConcurrentDictionary<int, KafkaLockCoordinator.PartitionSlot>)GetPrivateField(c, "_slots");
        var escapedToken = slots[6].Cts.Token;

        InvokePrivate(c, "OnRevokedOrLost", 6, false);

        // CTS is now cancelled synchronously; WaitHandle and Register must still work.
        Assert.IsTrue(escapedToken.WaitHandle.WaitOne(TimeSpan.Zero),
            "Holder must observe cancellation via WaitHandle after revoke.");
        Assert.IsTrue(escapedToken.IsCancellationRequested);
        // Both observation paths must stay usable after teardown completes.
        using var reg = escapedToken.Register(() => { });
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
    public async Task Liveness_Trip_RequestsConsumerRebuild_ToGuaranteeReassignment()
    {
        // Audit CR#5: forcing a rebuild on a liveness trip is deliberate and
        // load-bearing. Without it, a transient disconnect that recovers before
        // session.timeout.ms leaves the group with no rebalance to clear
        // _connectivitySuspect, wedging slot re-establishment until process
        // restart. The churn this can cause on a flapping broker is bounded by
        // the rebuild backoff and is the lesser evil. Pin the intent so a future
        // change can't silently drop the rebuild.
        var time = new ManualTimeProvider();
        await using var c = NewCoordinator(livenessTimeoutMs: 1_000, timeProvider: time);
        c.RecordBrokerContact();

        Assert.IsFalse(c.IsConsumerRebuildRequested, "No rebuild requested before any liveness trip.");

        time.Advance(TimeSpan.FromSeconds(2));
        Assert.IsTrue(c.EvaluateLiveness(), "Watchdog must trip once the liveness timeout elapses without contact.");
        Assert.IsTrue(c.IsConsumerRebuildRequested,
            "A liveness trip must request a consumer rebuild so the group re-joins and OnAssigned re-fires.");
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
    public async Task ResolveLivenessTimeout_HalfSessionTimeout_ClampedBelowSessionMinus2s()
    {
        // Low session timeout: floor at 10s, clamped below session-2s (8s).
        await using (var low = NewCoordinator(sessionTimeoutMs: 10_000))
            Assert.AreEqual(TimeSpan.FromSeconds(8), low.ResolveLivenessTimeout());

        // High session timeout: 60s×0.5=30s, clamped below 58s → 30s.
        await using (var high = NewCoordinator(sessionTimeoutMs: 60_000))
            Assert.AreEqual(TimeSpan.FromSeconds(30), high.ResolveLivenessTimeout());

        // Default session timeout (30s): 30s×0.5=15s, floor=10s → 15s, clamped below 28s → 15s.
        await using (var def = NewCoordinator(sessionTimeoutMs: 30_000))
            Assert.AreEqual(TimeSpan.FromSeconds(15), def.ResolveLivenessTimeout());

        // Explicit configuration always wins.
        await using (var configured = NewCoordinator(livenessTimeoutMs: 1_234, sessionTimeoutMs: 60_000))
            Assert.AreEqual(TimeSpan.FromMilliseconds(1_234), configured.ResolveLivenessTimeout());

        // The lock-specific session timeout (outage-grace budget) overrides
        // the shared client session timeout: 150s × 0.5 = 75s liveness. This
        // is the shipped Monitor configuration — a broker blip shorter than
        // ~75s must not cancel held locks.
        await using (var locks = NewCoordinator(sessionTimeoutMs: 30_000, lockSessionTimeoutMs: 150_000))
            Assert.AreEqual(TimeSpan.FromSeconds(75), locks.ResolveLivenessTimeout());
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

    // ---- Round 3 C1-revoke fix: cooperative revoke clears _connectivitySuspect ----

    [TestMethod]
    public async Task CooperativeRevoke_ClearsConnectivitySuspect()
    {
        // A transport-class error sets _connectivitySuspect=true, causing empty
        // polls to stop counting as broker contact. A subsequent cooperative revoke
        // (OnRevokedOrLost(lost:false)) proves the broker is reachable — the
        // coordinator received a rebalance callback, which requires a healthy
        // broker session. It must clear the flag so the consumer can resume
        // normal empty-poll liveness behaviour after reconnect.
        await using var c = NewCoordinator();
        InvokePrivate(c, "OnAssigned", 0);

        // Transport error: marks connectivity suspect.
        c.OnConsumerError(new Error(ErrorCode.Local_Transport, "transient outage", false));
        Assert.IsTrue((bool)GetPrivateField(c, "_connectivitySuspect"),
            "Transport-class error must set _connectivitySuspect=true.");

        // Cooperative revoke: broker-driven rebalance proves contact.
        InvokePrivate(c, "OnRevokedOrLost", 0, false);
        Assert.IsFalse((bool)GetPrivateField(c, "_connectivitySuspect"),
            "Cooperative revoke (lost=false) must clear _connectivitySuspect — " +
            "a rebalance round-trip with the broker proves connectivity is restored.");
    }

    [TestMethod]
    public async Task LostRevoke_DoesNotClearConnectivitySuspect()
    {
        // A "lost" revoke (max.poll.interval exceeded, or local session expiry)
        // is determined locally by librdkafka without a round-trip to the broker.
        // It must NOT clear _connectivitySuspect — it provides no evidence the
        // broker is actually reachable.
        await using var c = NewCoordinator();
        InvokePrivate(c, "OnAssigned", 1);

        c.OnConsumerError(new Error(ErrorCode.Local_Transport, "transient", false));
        InvokePrivate(c, "OnRevokedOrLost", 1, true); // lost=true

        Assert.IsTrue((bool)GetPrivateField(c, "_connectivitySuspect"),
            "A 'lost' revoke (local timeout, no broker round-trip) must NOT clear " +
            "_connectivitySuspect.");
    }

    // ---- BuildConsumerConfig seam: lock-consumer config invariants ----

    [TestMethod]
    public async Task BuildConsumerConfig_PinsAutoCommitAndOffsetReset()
    {
        await using var c = NewCoordinator();
        var config = c.BuildConsumerConfig();

        // Lock semantics don't use committed offsets: auto-commit on, join at Latest.
        Assert.IsTrue(config.EnableAutoCommit);
        Assert.AreEqual(AutoOffsetReset.Latest, config.AutoOffsetReset);
    }

    [TestMethod]
    public async Task BuildConsumerConfig_LockSessionTimeoutOverride_WinsOverSharedClientValue()
    {
        await using var c = NewCoordinator(sessionTimeoutMs: 30_000, lockSessionTimeoutMs: 150_000);
        Assert.AreEqual(150_000, c.BuildConsumerConfig().SessionTimeoutMs);
    }

    [TestMethod]
    public async Task BuildConsumerConfig_NoLockSessionTimeout_InheritsSharedClientValue()
    {
        await using var c = NewCoordinator(sessionTimeoutMs: 30_000, lockSessionTimeoutMs: null);
        Assert.AreEqual(30_000, c.BuildConsumerConfig().SessionTimeoutMs);
    }

    [TestMethod]
    public async Task BuildConsumerConfig_StaticMembership_SetsGroupScopedHostInstanceId()
    {
        // group.instance.id = "{group}.{HostInstanceId.For(replicaId)}". The
        // expectation is computed through HostInstanceId.For itself — the
        // honest pin, since DORC_REPLICA_ID in the test environment outranks
        // any configured replica id and would change the raw string.
        await using var c = NewCoordinator(useStaticGroupMembership: true);
        Assert.AreEqual(
            $"test.{Dorc.Kafka.Events.Publisher.HostInstanceId.For(null)}",
            c.BuildConsumerConfig().GroupInstanceId);
    }

    [TestMethod]
    public async Task BuildConsumerConfig_StaticMembershipDisabled_LeavesGroupInstanceIdNull()
    {
        await using var c = NewCoordinator(useStaticGroupMembership: false);
        Assert.IsNull(c.BuildConsumerConfig().GroupInstanceId);
    }

    [TestMethod]
    public async Task BuildConsumerConfig_ConfiguredReplicaId_FlowsIntoGroupInstanceId()
    {
        // Kafka:ReplicaId (constructor-injected KafkaClientOptions) must reach
        // the static-membership id via the same HostInstanceId channel the
        // event consumers use.
        await using var c = NewCoordinator(replicaId: "tier1");
        var groupInstanceId = c.BuildConsumerConfig().GroupInstanceId;

        Assert.AreEqual(
            $"test.{Dorc.Kafka.Events.Publisher.HostInstanceId.For("tier1")}",
            groupInstanceId);

        // When the env var is not set, the config channel combines with the
        // machine name — pin the exact "{MachineName}-{ReplicaId}" suffix.
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(
                Dorc.Kafka.Events.Publisher.HostInstanceId.EnvironmentVariable)))
        {
            Assert.AreEqual($"test.{Environment.MachineName}-tier1", groupInstanceId);
        }
    }

    private sealed class FakeConnectionProvider : Dorc.Kafka.Client.Connection.IKafkaConnectionProvider
    {
        public int? SessionTimeoutMs { get; set; }
        public Confluent.Kafka.ProducerConfig GetProducerConfig() => new();
        public Confluent.Kafka.ConsumerConfig GetConsumerConfig(string groupId) =>
            new() { GroupId = groupId, SessionTimeoutMs = SessionTimeoutMs };
    }
}
