using Confluent.Kafka;
using Dorc.Core.Events;
using Dorc.Kafka.Client.Configuration;
using Dorc.Kafka.Client.Connection;
using Dorc.Kafka.Client.Producers;
using Dorc.Kafka.ErrorLog;
using Dorc.Kafka.Events.Configuration;
using Dorc.Kafka.Events.Publisher;
using Dorc.PersistentData.Model;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Dorc.Kafka.Events.IntegrationTests.Publisher;

/// <summary>
/// Integration tests for consumer restart resilience against the local compose
/// stack (Kafka + Karapace).
///
/// Covers two gaps identified post-adversarial-review:
///
/// <list type="bullet">
///   <item><description>
///     AT1 — committed-offset restart: when a consumer stops cleanly after
///     committing offsets, a new consumer on the same group resumes from the
///     committed position and receives exactly the events produced after the
///     restart (no replay, no loss).
///   </description></item>
///   <item><description>
///     AT2 — pre-commit crash replay: when the consumer is stopped during
///     <see cref="IDeploymentResultBroadcaster.BroadcastAsync"/> (before
///     <c>consumer.StoreOffset(result)</c>), the offset is never stored
///     (auto-offset-store is disabled) and therefore never committed.
///     A new consumer on the same group replays that record — demonstrating
///     the at-least-once guarantee built into <see cref="DeploymentResultsKafkaConsumer"/>.
///   </description></item>
/// </list>
///
/// Note — runner crash (no cooperative close, lock expires via session
/// timeout): this scenario requires true process isolation (the broker cannot
/// be told "stop heartbeating" from within the same process) and is covered
/// at the architectural level by SC2a/SC2b/SC2c in Dorc.Kafka.Lock.HATests.
/// </summary>
[TestClass]
public class S009ConsumerResilienceIntegrationTests
{
    // ── AT1: committed-offset restart ──────────────────────────────────────

    [TestMethod]
    public async Task AT1_CleanRestart_SameGroup_ResumesFromCommittedOffset_NoDuplicates()
    {
        var topic = AvroKafkaTestHarness.NewTopic("s009-at1");
        await AvroKafkaTestHarness.CreateTopicAsync(topic, partitions: 1);

        try
        {
            await using var harness = new RequestsSubstrateTestHarness();

            var groupId = $"s009-at1-{Guid.NewGuid():N}";

            // ── Phase 1: consumer A processes 3 events and commits ──
            var broadcasterA = new RecordingBroadcaster();
            using var consumerA = harness.BuildConsumer(topic, groupId: groupId,
                broadcaster: broadcasterA);
            await consumerA.StartAsync(CancellationToken.None);
            await WaitUntilAssigned(TimeSpan.FromSeconds(15));

            for (var i = 1; i <= 3; i++)
                await ProduceResultEvent(topic, requestId: i);

            // Manual commit in DeploymentResultsKafkaConsumer happens
            // synchronously after each BroadcastAsync returns. Waiting for 3
            // broadcasts guarantees 3 committed offsets.
            await WaitUntil(() => broadcasterA.Count >= 3, TimeSpan.FromSeconds(20));
            await consumerA.StopAsync(CancellationToken.None);

            Assert.AreEqual(3, broadcasterA.Count,
                "Consumer A must see all 3 pre-restart events.");

            // ── Phase 2: produce 2 more events WHILE consumer is stopped ──
            await ProduceResultEvent(topic, requestId: 4);
            await ProduceResultEvent(topic, requestId: 5);

            // ── Phase 3: consumer B starts on same group, same topic ──
            var broadcasterB = new RecordingBroadcaster();
            using var consumerB = harness.BuildConsumer(topic, groupId: groupId,
                broadcaster: broadcasterB);
            await consumerB.StartAsync(CancellationToken.None);

            await WaitUntil(() => broadcasterB.Count >= 2, TimeSpan.FromSeconds(20));
            await consumerB.StopAsync(CancellationToken.None);

            // Consumer B must receive only the 2 events produced after the
            // restart (from the committed offset, not from Latest or Earliest).
            Assert.AreEqual(2, broadcasterB.Count,
                "Consumer B must not replay committed events: exactly 2 new events expected.");
            var ids = broadcasterB.Snapshot().Select(r => r.RequestId).OrderBy(x => x).ToArray();
            CollectionAssert.AreEqual(new[] { 4, 5 }, ids,
                "Consumer B must receive exactly requestIds 4 and 5.");
        }
        finally
        {
            await AvroKafkaTestHarness.DeleteTopicAsync(topic);
            await AvroKafkaTestHarness.DeleteSubjectAsync(topic + "-value");
        }
    }

    // ── AT2: pre-commit crash → at-least-once replay ───────────────────────

    /// <summary>
    /// Scenario: the same host (same DORC_REPLICA_ID / machine name) restarts
    /// after a crash that occurred before the manual offset commit fired.
    /// Because the group ID is stable across same-host restarts, the consumer
    /// resumes from the last committed offset and replays the uncommitted event.
    ///
    /// This does NOT cover K8s pod replacement, where a new pod gets a fresh
    /// DORC_REPLICA_ID → new group ID → AutoOffsetReset.Latest → the uncommitted
    /// event is lost by design (the results consumer is a real-time fan-out;
    /// the UI reconnects on pod restart and gets fresh state).
    /// </summary>
    [TestMethod]
    public async Task AT2_ConsumerStoppedDuringBroadcast_BeforeCommit_EventReplayedOnRestart()
    {
        var topic = AvroKafkaTestHarness.NewTopic("s009-at2");
        await AvroKafkaTestHarness.CreateTopicAsync(topic, partitions: 1);

        try
        {
            await using var harness = new RequestsSubstrateTestHarness();
            var groupId = $"s009-at2-{Guid.NewGuid():N}";

            // ── Phase 1: consumer A commits event 100 so the group has a
            //             committed offset — required so the group doesn't
            //             jump to Latest when restarting after the crash. ──
            var firstCycleReady = new SequencedBroadcaster(commitFirstNEvents: 1);
            using var consumerA = harness.BuildConsumer(topic, groupId: groupId,
                broadcaster: firstCycleReady);
            await consumerA.StartAsync(CancellationToken.None);
            await WaitUntilAssigned(TimeSpan.FromSeconds(15));

            await ProduceResultEvent(topic, requestId: 100); // will be committed
            await WaitUntil(() => firstCycleReady.BroadcastsCompleted >= 1, TimeSpan.FromSeconds(20));
            // Allow the consumer loop to execute consumer.StoreOffset(result) after
            // BroadcastAsync returns (the stored offset is flushed by the auto-commit
            // timer / on Close). The RunLoop is synchronous (.GetAwaiter().GetResult())
            // but runs on a separate thread; a short pause avoids a race where we produce
            // event 999 before the offset for event 100 has been stored.
            await Task.Delay(500);

            // ── Phase 2: produce event 999 and let consumer A poll it.
            //             SequencedBroadcaster blocks on the 2nd event until ct
            //             is cancelled → consumer exits WITHOUT committing. ──
            await ProduceResultEvent(topic, requestId: 999);
            await WaitUntil(() => firstCycleReady.BlockingCallStarted, TimeSpan.FromSeconds(20));

            // Stop consumer A. The OperationCanceledException path in
            // DeploymentResultsKafkaConsumer.RunLoop exits the loop before
            // consumer.StoreOffset(result) for event 999 — an unstored offset
            // is never committed, not even by the Close() flush.
            await consumerA.StopAsync(CancellationToken.None);

            // ── Phase 3: consumer B on the same group. Committed offset is
            //             after event 100, so event 999 (uncommitted) is
            //             replayed. ──
            var broadcasterB = new RecordingBroadcaster();
            using var consumerB = harness.BuildConsumer(topic, groupId: groupId,
                broadcaster: broadcasterB);
            await consumerB.StartAsync(CancellationToken.None);

            await WaitUntil(() => broadcasterB.Count >= 1, TimeSpan.FromSeconds(20));
            await consumerB.StopAsync(CancellationToken.None);

            Assert.AreEqual(1, broadcasterB.Count,
                "Consumer B must replay exactly one uncommitted event.");
            Assert.AreEqual(999, broadcasterB.Snapshot()[0].RequestId,
                "Replayed event must be requestId 999 — the one not committed before crash.");
        }
        finally
        {
            await AvroKafkaTestHarness.DeleteTopicAsync(topic);
            await AvroKafkaTestHarness.DeleteSubjectAsync(topic + "-value");
        }
    }

    // ── helpers ────────────────────────────────────────────────────────────

    private static async Task ProduceResultEvent(string topic, int requestId)
    {
        using var registry = AvroKafkaTestHarness.BuildRegistry();
        var factory = AvroKafkaTestHarness.BuildFactory(registry);
        var connection = new KafkaConnectionProvider(Options.Create(new KafkaClientOptions
        {
            BootstrapServers = AvroKafkaTestHarness.BootstrapServers,
            SchemaRegistry   = { Url = AvroKafkaTestHarness.SchemaRegistryUrl }
        }));
        var builder = new KafkaProducerBuilder<string, DeploymentResultEventData>(
            connection, factory,
            NullLogger<KafkaProducerBuilder<string, DeploymentResultEventData>>.Instance);
        using var producer = builder.Build($"s009-producer-{requestId}");
        await producer.ProduceAsync(topic, new Message<string, DeploymentResultEventData>
        {
            Key   = requestId.ToString(),
            Value = new DeploymentResultEventData(requestId, requestId, 0, "Running",
                        DateTimeOffset.UtcNow, null, DateTimeOffset.UtcNow)
        });
    }

    private static Task WaitUntilAssigned(TimeSpan timeout)
        => Task.Delay(TimeSpan.FromSeconds(5)); // grace period for initial rebalance

    private static async Task WaitUntil(Func<bool> predicate, TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (predicate()) return;
            await Task.Delay(100);
        }
        throw new TimeoutException($"Predicate did not become true within {timeout}.");
    }

    // ── inner fakes ────────────────────────────────────────────────────────

    /// <summary>
    /// Broadcaster that records all received events.
    /// Thread-safe: written on the consumer poll thread, read on the test thread.
    /// </summary>
    private sealed class RecordingBroadcaster : IDeploymentResultBroadcaster
    {
        private readonly object _lock = new();
        private readonly List<DeploymentResultEventData> _received = new();

        public int Count { get { lock (_lock) return _received.Count; } }
        public IReadOnlyList<DeploymentResultEventData> Snapshot() { lock (_lock) return _received.ToList(); }

        public Task BroadcastAsync(DeploymentResultEventData data, CancellationToken ct)
        {
            lock (_lock) _received.Add(data);
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// Broadcaster that completes the first <paramref name="commitFirstNEvents"/>
    /// events normally (allowing manual commit), then blocks on the next event
    /// until <c>stoppingToken</c> is cancelled — simulating a consumer killed
    /// mid-broadcast before the offset commit.
    ///
    /// Fields use <c>volatile</c> / <c>Interlocked</c> because they are written
    /// on the consumer poll thread and read by the test's polling loop on a
    /// separate thread.
    /// </summary>
    private sealed class SequencedBroadcaster : IDeploymentResultBroadcaster
    {
        private readonly int _commitThreshold;
        private int _callCount;
        // Tracks BroadcastAsync completions for the "will be committed" events.
        // Named BroadcastsCompleted (not CommittedCount) because completion of
        // BroadcastAsync precedes — but does not guarantee — the broker commit;
        // callers should add a short delay after observing this value to allow
        // the consumer loop's consumer.StoreOffset(result) to execute (the
        // auto-commit timer / Close() then flushes the stored offset).
        private volatile int _broadcastsCompleted;
        private volatile bool _blockingCallStarted;

        public int  BroadcastsCompleted  => _broadcastsCompleted;
        public bool BlockingCallStarted  => _blockingCallStarted;

        public SequencedBroadcaster(int commitFirstNEvents) => _commitThreshold = commitFirstNEvents;

        public async Task BroadcastAsync(DeploymentResultEventData data, CancellationToken ct)
        {
            var n = Interlocked.Increment(ref _callCount);
            if (n <= _commitThreshold)
            {
                // Complete normally → DeploymentResultsKafkaConsumer.RunLoop
                // proceeds to consumer.StoreOffset(result) for this record.
                Interlocked.Increment(ref _broadcastsCompleted);
                return;
            }

            // n > threshold: signal and block until ct is cancelled.
            _blockingCallStarted = true;
            // Propagate OCE so the RunLoop's
            // catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            // path exits before consumer.StoreOffset(result).
            await Task.Delay(Timeout.Infinite, ct);
        }
    }
}

// ── S009-local RequestsSubstrateTestHarness extension ──────────────────────────────────
// RequestsSubstrateTestHarness.BuildConsumer() is hard-wired to the harness's own
// RecordingBroadcaster.  We need to inject our own broadcaster in S009,
// so we extend with a local helper that accepts an arbitrary broadcaster.
// KafkaConnectionProvider is a config-factory (not a connection holder), so
// sharing the harness.ConnectionProvider across multiple consumer instances
// is safe.

internal static class RequestsSubstrateTestHarnessExtensions
{
    internal static DeploymentResultsKafkaConsumer BuildConsumer(
        this RequestsSubstrateTestHarness harness,
        string topic,
        string groupId,
        IDeploymentResultBroadcaster broadcaster)
        => new DeploymentResultsKafkaConsumer(
            harness.ConnectionProvider,
            harness.Factory,
            broadcaster,
            harness.ErrorLog,
            Options.Create(new KafkaTopicsOptions()),
            new Dorc.Kafka.Client.Observability.NoOpKafkaConsumerMetrics(),
            NullLogger<DeploymentResultsKafkaConsumer>.Instance)
        {
            TopicName       = topic,
            ConsumerGroupId = groupId
        };
}
