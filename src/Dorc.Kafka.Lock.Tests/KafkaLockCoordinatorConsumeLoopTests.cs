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
    public async Task TransportConsumeErrors_TripLivenessWatchdog_WithoutRevokeCallback()
    {
        // Scenario from the review: broker connectivity lost, librdkafka fires
        // no revoked/lost callback, Consume throws local/transport errors. The
        // broker reassigns our partitions after session.timeout.ms — the
        // watchdog must cancel locks here or two holders exist (split-brain).
        var time = new ManualTimeProvider();
        var consumer = new ScriptedLockConsumer
        {
            OnConsume = _ =>
            {
                time.Advance(TimeSpan.FromMilliseconds(600));
                Thread.Sleep(5);
                throw NewConsumeException(ErrorCode.Local_Transport, "broker down", isFatal: false);
            }
        };

        var rebuilds = 0;
        var c = NewRunningCoordinatorCandidate(
            () => { rebuilds++; return consumer; }, time, livenessTimeoutMs: 1_000);
        await using var _ = c;
        await c.StartAsync(CancellationToken.None);

        InvokePrivate(c, "OnAssigned", 0);
        var token = await c.WaitForPartitionOwnershipAsync(0, CancellationToken.None);

        Assert.IsTrue(WaitForCancellation(token, TimeSpan.FromSeconds(30)),
            "With no broker contact beyond the liveness timeout, held locks must report lost.");
        Assert.IsFalse(c.IsConsumerRebuildRequested,
            "Transport errors are transient — the loop must keep running, not rebuild.");
        Assert.AreEqual(1, rebuilds, "The consumer must not be torn down on transport errors.");

        // Reconnect: a rebalance reassignment re-establishes the slot.
        InvokePrivate(c, "OnAssigned", 0);
        var fresh = await c.WaitForPartitionOwnershipAsync(0, CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(10));
        Assert.IsFalse(fresh.IsCancellationRequested);
    }

    [TestMethod]
    public async Task SilentDisconnect_EmptyPollsPlusTransportErrorEvent_TripsWatchdog()
    {
        // librdkafka can keep returning empty polls while disconnected,
        // surfacing the outage only through the error handler. Empty polls
        // count as contact ONLY while connectivity is not suspect.
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

        // Healthy: empty polls refresh contact, watchdog never trips even
        // though far more fake-time than the liveness timeout elapses.
        await Task.Delay(300);
        Assert.IsFalse(token.IsCancellationRequested,
            "Empty polls must count as contact while connectivity is healthy.");

        // Error handler reports a transport failure → polls stop counting.
        c.OnConsumerError(new Error(ErrorCode.Local_Transport, "connection refused", false));

        Assert.IsTrue(WaitForCancellation(token, TimeSpan.FromSeconds(30)),
            "Once connectivity is suspect, silence beyond the liveness timeout must cancel held locks.");
    }

    private sealed class FakeConnectionProvider : Dorc.Kafka.Client.Connection.IKafkaConnectionProvider
    {
        public ProducerConfig GetProducerConfig() => new();
        public ConsumerConfig GetConsumerConfig(string? groupIdOverride = null) => new() { GroupId = groupIdOverride ?? "test" };
    }
}
