using System.Reflection;
using Confluent.Kafka;
using Dorc.Kafka.Events.Configuration;
using Dorc.Kafka.Lock.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Dorc.Kafka.Lock.Tests;

/// <summary>
/// Drives the coordinator's real consume loop against a
/// <see cref="ScriptedLockConsumer"/> (via the internal factory seam) to pin
/// the split-brain watchdog (finding: lost connectivity must cancel locks even
/// though librdkafka fires no revoke/lost callback) and the fatal-error
/// dispose-and-rebuild path (finding: a fatal error must not leave a
/// permanently dead consumer spinning while locks stay "held").
/// </summary>
[TestClass]
public class KafkaLockCoordinatorConsumeLoopTests
{
    private static KafkaLockCoordinator NewRunningCoordinatorCandidate(
        Func<IConsumer<byte[], byte[]>> consumerFactory,
        TimeProvider? timeProvider = null,
        int? livenessTimeoutMs = null)
    {
        var opts = Options.Create(new KafkaLocksOptions
        {
            Enabled = true,
            PartitionCount = 12,
            ConsumerGroupId = "test",
            LivenessTimeoutMs = livenessTimeoutMs
        });
        var topics = Options.Create(new KafkaTopicsOptions());
        var coordinator = new KafkaLockCoordinator(
            new FakeConnectionProvider(),
            opts,
            topics,
            NullLogger<KafkaLockCoordinator>.Instance,
            timeProvider)
        {
            ConsumerFactoryOverride = consumerFactory
        };
        return coordinator;
    }

    private static void InvokePrivate(object target, string name, params object[] args)
        => target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(target, args);

    private static bool WaitForCancellation(CancellationToken token, TimeSpan timeout)
    {
        using var fired = new ManualResetEventSlim();
        using var reg = token.Register(fired.Set);
        return fired.Wait(timeout);
    }

    private static ConsumeException NewConsumeException(ErrorCode code, string reason, bool isFatal)
        => new(new ConsumeResult<byte[], byte[]>(), new Error(code, reason, isFatal));

    [TestMethod]
    public async Task FatalConsumeError_CancelsLocks_DisposesAndRebuildsConsumer_LoopKeepsRunning()
    {
        var consumers = new List<ScriptedLockConsumer>();
        using var allowFatal = new ManualResetEventSlim();
        var factory = () =>
        {
            var consumer = new ScriptedLockConsumer();
            // First consumer: block until the test holds a lock, then die
            // fatally. Rebuilt consumers just idle.
            consumer.OnConsume = consumers.Count == 0
                ? _ =>
                {
                    allowFatal.Wait(TimeSpan.FromSeconds(30));
                    throw NewConsumeException(ErrorCode.Local_Fatal, "fenced", isFatal: true);
                }
                : _ => { Thread.Sleep(10); return null; };
            consumers.Add(consumer);
            return (IConsumer<byte[], byte[]>)consumer;
        };

        var c = NewRunningCoordinatorCandidate(factory);
        await using var _ = c;
        await c.StartAsync(CancellationToken.None);

        InvokePrivate(c, "OnAssigned", 0);
        var token = await c.WaitForPartitionOwnershipAsync(0, CancellationToken.None);
        Assert.IsFalse(token.IsCancellationRequested);

        allowFatal.Set(); // unleash the fatal error

        Assert.IsTrue(WaitForCancellation(token, TimeSpan.FromSeconds(15)),
            "A fatal consumer error must fire every held LockLostToken.");
        Assert.IsTrue(SpinWait.SpinUntil(() => consumers.Count >= 2, TimeSpan.FromSeconds(15)),
            "The dead consumer must be rebuilt (bounded backoff), not spun on forever.");
        Assert.IsTrue(consumers[0].Disposed, "The dead consumer must be disposed.");
        Assert.IsTrue(SpinWait.SpinUntil(() => consumers[1].SubscribedTopics.Count > 0, TimeSpan.FromSeconds(15)),
            "The rebuilt consumer must re-subscribe to the lock topic.");
        Assert.IsTrue(consumers[1].SubscribedTopics.Contains("dorc.locks"));
        Assert.IsTrue(SpinWait.SpinUntil(() => !c.IsConsumerRebuildRequested, TimeSpan.FromSeconds(15)),
            "Rebuild must complete and clear the rebuild request.");

        // Loop is still alive: reassignment establishes a fresh, live slot.
        InvokePrivate(c, "OnAssigned", 0);
        var fresh = await c.WaitForPartitionOwnershipAsync(0, CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(10));
        Assert.IsFalse(fresh.IsCancellationRequested);
    }

    [TestMethod]
    public async Task TransportConsumeErrors_TripLivenessWatchdog_RebuildConsumer_LocksReEstablish()
    {
        // Scenario: broker connectivity lost (no revoked/lost callback fired by librdkafka),
        // Consume throws Local_Transport errors. The watchdog must:
        // 1. Cancel all held locks (split-brain guard — broker may have reassigned partitions).
        // 2. Force a consumer rebuild so the group re-joins and OnAssigned fires with certainty.
        //    Without the rebuild, a brief disconnect that recovers before session.timeout.ms
        //    triggers no broker-side eviction and no rebalance — _connectivitySuspect stays
        //    true forever and new slot tasks are never resolved.
        var time = new ManualTimeProvider();
        var consumers = new List<ScriptedLockConsumer>();
        var factory = () =>
        {
            var c2 = new ScriptedLockConsumer();
            // First consumer: always throws transport error (simulates persistent outage).
            // Subsequent consumers: idle (simulates post-rebuild healthy state).
            c2.OnConsume = consumers.Count == 0
                ? _ =>
                {
                    time.Advance(TimeSpan.FromMilliseconds(600));
                    Thread.Sleep(5);
                    throw NewConsumeException(ErrorCode.Local_Transport, "broker down", isFatal: false);
                }
                : _ => { Thread.Sleep(10); return null; };
            if (consumers.Count == 0)
            {
                // Persistent outage: the connectivity probe must fail too, or
                // it would (correctly) clear the suspicion and avert the trip.
                c2.OnCommitted = () => throw new KafkaException(
                    new Error(ErrorCode.Local_Transport, "broker down"));
            }
            consumers.Add(c2);
            return (IConsumer<byte[], byte[]>)c2;
        };

        var c = NewRunningCoordinatorCandidate(factory, time, livenessTimeoutMs: 1_000);
        await using var _ = c;
        await c.StartAsync(CancellationToken.None);

        InvokePrivate(c, "OnAssigned", 0);
        var token = await c.WaitForPartitionOwnershipAsync(0, CancellationToken.None);

        // Watchdog fires after liveness timeout → lock cancelled.
        Assert.IsTrue(WaitForCancellation(token, TimeSpan.FromSeconds(30)),
            "Transport errors beyond the liveness timeout must cancel held locks.");

        // Watchdog must also have requested a consumer rebuild (to guarantee a fresh
        // OnAssigned that clears _connectivitySuspect on reconnect).
        Assert.IsTrue(SpinWait.SpinUntil(() => consumers.Count >= 2, TimeSpan.FromSeconds(15)),
            "Watchdog trip must trigger consumer rebuild to guarantee re-assignment on reconnect.");
        Assert.IsTrue(consumers[0].Disposed, "The dead consumer must be disposed after rebuild.");

        // Simulate re-assignment after rebuild: broker grants partition to new consumer.
        InvokePrivate(c, "OnAssigned", 0);
        var fresh = await c.WaitForPartitionOwnershipAsync(0, CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(10));
        Assert.IsFalse(fresh.IsCancellationRequested,
            "After rebuild and OnAssigned, new lock slots must resolve successfully.");
    }

    [TestMethod]
    public async Task EmptyPolls_WhenHealthy_ResetWatchdog_DoNotTripLiveness()
    {
        // On an idle lock topic (no records by design), empty polls from a
        // healthy broker must count as broker contact and prevent false watchdog
        // trips. _connectivitySuspect defaults to false; empty polls refresh the
        // timestamp and the watchdog never fires.
        var time = new ManualTimeProvider();
        var consumer = new ScriptedLockConsumer
        {
            OnConsume = _ =>
            {
                time.Advance(TimeSpan.FromMilliseconds(600));
                Thread.Sleep(5);
                return null;
            }
        };

        var c = NewRunningCoordinatorCandidate(() => consumer, time, livenessTimeoutMs: 1_000);
        await using var _ = c;
        await c.StartAsync(CancellationToken.None);

        InvokePrivate(c, "OnAssigned", 0);
        var token = await c.WaitForPartitionOwnershipAsync(0, CancellationToken.None);

        // Allow ~100ms real time (~20 poll iterations, >10,000ms fake time = 10×
        // the liveness window). If empty polls don't count, the watchdog trips
        // after the 2nd poll (1200ms fake > 1000ms liveness). With the fix, the
        // token must remain live throughout.
        Thread.Sleep(TimeSpan.FromMilliseconds(200));

        Assert.IsFalse(token.IsCancellationRequested,
            "Empty polls on a healthy broker must keep the watchdog re-armed; " +
            "the lock must NOT be falsely cancelled on an idle topic.");
    }

    [TestMethod]
    public async Task EmptyPolls_AfterConnectivitySuspect_DoNotResetWatchdog_WatchdogFires()
    {
        // Once _connectivitySuspect is true (set by OnConsumerError on a
        // transport-class error), empty polls must no longer count as broker
        // contact. librdkafka can return empty polls during a half-open TCP
        // connection; counting those would defeat the split-brain guard.
        var time = new ManualTimeProvider();
        var consumer = new ScriptedLockConsumer
        {
            OnConsume = _ =>
            {
                time.Advance(TimeSpan.FromMilliseconds(600));
                Thread.Sleep(5);
                return null;
            },
            // Genuine outage: the probe must fail, or it would (correctly)
            // clear the suspicion and this trip test would never fire.
            OnCommitted = () => throw new KafkaException(
                new Error(ErrorCode.Local_Transport, "broker unreachable"))
        };

        var c = NewRunningCoordinatorCandidate(() => consumer, time, livenessTimeoutMs: 1_000);
        await using var _ = c;
        await c.StartAsync(CancellationToken.None);

        InvokePrivate(c, "OnAssigned", 0);
        var token = await c.WaitForPartitionOwnershipAsync(0, CancellationToken.None);

        // Mark connectivity as suspect — subsequent empty polls must NOT record
        // broker contact. Only a rebalance callback (OnAssigned / OnRevoked),
        // a real record delivery, or a successful probe re-clears the flag.
        InvokePrivate(c, "OnConsumerError", new Error(ErrorCode.Local_Transport, "broker unreachable", false));

        // After 2 empty polls (1200ms fake > 1000ms liveness), the watchdog must
        // trip and cancel all lock slots.
        Assert.IsTrue(WaitForCancellation(token, TimeSpan.FromSeconds(15)),
            "After a transport-class error, empty polls must NOT reset the watchdog; " +
            "the lock must be cancelled when the liveness window elapses.");
    }

    [TestMethod]
    public async Task BenignTransportError_ProbeSucceeds_SuspicionCleared_NoFalseTrip()
    {
        // Scenario: a single transport-class error from a routine broker
        // idle-connection reap on a HEALTHY cluster. The locks topic is idle
        // by design, so no record can ever clear the suspicion passively —
        // before the active probe existed, this false-tripped the watchdog and
        // cancelled every held lock ~LivenessTimeout later. The probe (a
        // group-coordinator round-trip) succeeds against the healthy broker,
        // clears the suspicion, and re-arms the watchdog: no trip, lock lives.
        var time = new ManualTimeProvider();
        var consumer = new ScriptedLockConsumer
        {
            OnConsume = _ =>
            {
                time.Advance(TimeSpan.FromMilliseconds(600));
                Thread.Sleep(5);
                return null;
            }
            // OnCommitted deliberately unset: default probe behaviour = success.
        };

        var c = NewRunningCoordinatorCandidate(() => consumer, time, livenessTimeoutMs: 1_000);
        await using var _ = c;
        await c.StartAsync(CancellationToken.None);

        InvokePrivate(c, "OnAssigned", 0);
        var token = await c.WaitForPartitionOwnershipAsync(0, CancellationToken.None);

        // Benign disconnect: suspicion set exactly as a real idle reap would.
        InvokePrivate(c, "OnConsumerError", new Error(ErrorCode.Local_Transport, "idle connection reaped", false));

        // ~40 poll iterations advance fake time ~24s — 24× the liveness window.
        // Without the probe the watchdog trips on the second post-error poll.
        Thread.Sleep(TimeSpan.FromMilliseconds(200));

        Assert.IsFalse(token.IsCancellationRequested,
            "A transport-class error on a healthy cluster must be cleared by the " +
            "connectivity probe; held locks must NOT be cancelled.");
    }

    private sealed class FakeConnectionProvider : Dorc.Kafka.Client.Connection.IKafkaConnectionProvider
    {
        public ProducerConfig GetProducerConfig() => new();
        public ConsumerConfig GetConsumerConfig(string groupId) => new() { GroupId = groupId };
    }
}
