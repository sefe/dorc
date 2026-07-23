using Confluent.Kafka;
using Dorc.Core.Events;
using Dorc.Kafka.Client.Configuration;
using Dorc.Kafka.Client.Connection;
using Dorc.Kafka.Client.Observability;
using Dorc.Kafka.Client.Producers;
using Dorc.Kafka.ErrorLog;
using Dorc.Kafka.Events.Configuration;
using Dorc.Kafka.Events.Publisher;
using Dorc.PersistentData.Model;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Dorc.Kafka.Events.IntegrationTests.Publisher;

/// <summary>
/// integration tests against the local compose stack.
/// (consumer subscribes both topics, handler invoked per record),
/// (E2E producer→consumer wake-up signal),
/// (per-RequestId ordering preserved by key-partition FIFO).
/// </summary>
[TestClass]
public class S006RequestLifecycleIntegrationTests
{
    [TestMethod]
    public async Task AT3_ConsumerSubscribesBothTopics_HandlerInvokedPerRecord()
    {
        var newTopic = AvroKafkaTestHarness.NewTopic("dorc.requests.new");
        var statusTopic = AvroKafkaTestHarness.NewTopic("dorc.requests.status");
        await AvroKafkaTestHarness.CreateTopicAsync(newTopic, partitions: 3);
        await AvroKafkaTestHarness.CreateTopicAsync(statusTopic, partitions: 3);

        try
        {
            using var registry = AvroKafkaTestHarness.BuildRegistry();
            var factory = AvroKafkaTestHarness.BuildFactory(registry);
            using var producer = AvroKafkaTestHarness.ProducerBuilder<DeploymentRequestEventData>(factory)
                .Build("s006-it-producer");

            var handler = new RecordingHandler();
            var groupId = $"s006-it-{Guid.NewGuid():N}";
            using var consumer = new DeploymentRequestsKafkaConsumer(
                BuildConnection(),
                factory,
                handler,
                new NoopErrorLog(),
                Options.Create(new KafkaTopicsOptions()),
                new NoOpKafkaConsumerMetrics(),
                NullLogger<DeploymentRequestsKafkaConsumer>.Instance)
            {
                Topics = new[] { newTopic, statusTopic },
                ConsumerGroupId = groupId
            };
            await consumer.StartAsync(CancellationToken.None);
            await Task.Delay(2_000); // allow initial rebalance to complete before producing

            // Produce 1 to each topic; expect 2 handler invocations.
            await producer.ProduceAsync(newTopic, new Message<string, DeploymentRequestEventData>
            {
                Key = "1",
                Value = new DeploymentRequestEventData(1, "Pending", null, null, DateTimeOffset.UtcNow)
            });
            await producer.ProduceAsync(statusTopic, new Message<string, DeploymentRequestEventData>
            {
                Key = "2",
                Value = new DeploymentRequestEventData(2, "Cancelled", null, null, DateTimeOffset.UtcNow)
            });

            await WaitForCount(() => handler.Count, 2, TimeSpan.FromSeconds(20));
            await consumer.StopAsync(CancellationToken.None);

            Assert.AreEqual(2, handler.Count);
            Assert.IsTrue(handler.Snapshot().Any(r => r.Topic == newTopic && r.Event.RequestId == 1));
            Assert.IsTrue(handler.Snapshot().Any(r => r.Topic == statusTopic && r.Event.RequestId == 2));
        }
        finally
        {
            await AvroKafkaTestHarness.DeleteTopicAsync(newTopic);
            await AvroKafkaTestHarness.DeleteTopicAsync(statusTopic);
            await AvroKafkaTestHarness.DeleteSubjectAsync(newTopic + "-value");
            await AvroKafkaTestHarness.DeleteSubjectAsync(statusTopic + "-value");
        }
    }

    [TestMethod]
    public async Task AT10_ProducerToConsumer_RaisesPollSignal()
    {
        var topic = AvroKafkaTestHarness.NewTopic("dorc.requests.new");
        await AvroKafkaTestHarness.CreateTopicAsync(topic, partitions: 1);

        try
        {
            using var registry = AvroKafkaTestHarness.BuildRegistry();
            var factory = AvroKafkaTestHarness.BuildFactory(registry);
            using var producer = AvroKafkaTestHarness.ProducerBuilder<DeploymentRequestEventData>(factory)
                .Build("s006-it-producer-at10");

            using var signal = new RequestPollSignal();
            var groupId = $"s006-it-{Guid.NewGuid():N}";
            using var consumer = new DeploymentRequestsKafkaConsumer(
                BuildConnection(),
                factory,
                // The handler only signals for records from the RequestsNew
                // topic, so point it at the fixture topic under test.
                new PollSignalRequestEventHandler(signal,
                    Options.Create(new KafkaTopicsOptions { RequestsNew = topic })),
                new NoopErrorLog(),
                Options.Create(new KafkaTopicsOptions()),
                new NoOpKafkaConsumerMetrics(),
                NullLogger<DeploymentRequestsKafkaConsumer>.Instance)
            {
                Topics = new[] { topic },
                ConsumerGroupId = groupId
            };
            await consumer.StartAsync(CancellationToken.None);
            await Task.Delay(2_000); // initial rebalance settle

            // Wait should fire well within the timeout once a record arrives.
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var waitTask = signal.WaitAsync(TimeSpan.FromSeconds(15), CancellationToken.None);
            await producer.ProduceAsync(topic, new Message<string, DeploymentRequestEventData>
            {
                Key = "1",
                Value = new DeploymentRequestEventData(1, "Pending", null, null, DateTimeOffset.UtcNow)
            });
            await waitTask;
            sw.Stop();
            await consumer.StopAsync(CancellationToken.None);

            Assert.IsTrue(sw.ElapsedMilliseconds < 10_000,
                $"Producer→consumer→signal acceleration should fire in <10s; took {sw.ElapsedMilliseconds}ms.");
        }
        finally
        {
            await AvroKafkaTestHarness.DeleteTopicAsync(topic);
            await AvroKafkaTestHarness.DeleteSubjectAsync(topic + "-value");
        }
    }

    [TestMethod]
    public async Task AT11_PerRequestIdOrdering_PreservedByKeyPartitionFifo()
    {
        var topic = AvroKafkaTestHarness.NewTopic("dorc.requests.status");
        await AvroKafkaTestHarness.CreateTopicAsync(topic, partitions: 12);

        try
        {
            using var registry = AvroKafkaTestHarness.BuildRegistry();
            var factory = AvroKafkaTestHarness.BuildFactory(registry);
            using var producer = AvroKafkaTestHarness.ProducerBuilder<DeploymentRequestEventData>(factory)
                .Build("s006-it-producer-at11");

            var handler = new RecordingHandler();
            var groupId = $"s006-it-{Guid.NewGuid():N}";
            using var consumer = new DeploymentRequestsKafkaConsumer(
                BuildConnection(),
                factory,
                handler,
                new NoopErrorLog(),
                Options.Create(new KafkaTopicsOptions()),
                new NoOpKafkaConsumerMetrics(),
                NullLogger<DeploymentRequestsKafkaConsumer>.Instance)
            {
                Topics = new[] { topic },
                ConsumerGroupId = groupId
            };
            await consumer.StartAsync(CancellationToken.None);
            await Task.Delay(2_000); // allow initial rebalance to complete before producing

            const int requestId = 4242;
            var statuses = new[] { "Pending", "Confirmed", "Requesting", "Running", "Completed" };
            foreach (var s in statuses)
            {
                await producer.ProduceAsync(topic, new Message<string, DeploymentRequestEventData>
                {
                    Key = requestId.ToString(),
                    Value = new DeploymentRequestEventData(requestId, s, null, null, DateTimeOffset.UtcNow)
                });
            }

            await WaitForCount(() => handler.Count, statuses.Length, TimeSpan.FromSeconds(20));
            await consumer.StopAsync(CancellationToken.None);

            var observed = handler.Snapshot().Select(r => r.Event.Status).ToArray();
            CollectionAssert.AreEqual(statuses, observed,
                "Per-RequestId events keyed identically must arrive in produce order (single-partition FIFO).");
        }
        finally
        {
            await AvroKafkaTestHarness.DeleteTopicAsync(topic);
            await AvroKafkaTestHarness.DeleteSubjectAsync(topic + "-value");
        }
    }

    /// <summary>
    /// AT4 — poison message on the requests consumer (DeploymentRequestsKafkaConsumer):
    /// non-Avro bytes → ConsumeException → KafkaErrorLog entry written →
    /// StoreOffset(offset+1) staged → on consumer.Close() the autocommit is flushed →
    /// consumer B (same group) resumes from the committed offset and handles a valid
    /// follow-up that was produced DURING THE DOWNTIME GAP (while consumer A was stopped).
    ///
    /// Proof structure (mirrors S009 AT1):
    ///   Offset 0: poison (processed by A → StoreOffset(1) staged)
    ///   [A stopped — Close() commits offset 1 to broker]
    ///   Offset 1: valid follow-up (produced while B does not exist yet)
    ///   [B starts on same group]
    ///
    ///   If StoreOffset(1) WAS called → committed offset = 1 → B resumes from 1
    ///     → reads follow-up → handler.Count = 1 ✓
    ///
    ///   If StoreOffset(1) was NOT called (bug) → no committed offset →
    ///     AutoOffsetReset.Latest → B jumps to HWM = 2 (past follow-up) →
    ///     handler stays 0 → WaitForCount times out → assertion fails ✓
    ///
    /// Producing the follow-up AFTER consumer B starts would neutralise the test:
    /// with Latest, an uncommitted B and a committed B both start at the log tail
    /// and read the follow-up — the test would pass with or without the StoreOffset.
    ///
    /// The requests consumer uses EnableAutoCommit=true + EnableAutoOffsetStore=false
    /// (StoreOffset path) — the same pattern the results consumer uses (see
    /// RequestsSubstrateAcceptanceIntegrationTests).
    /// </summary>
    [TestMethod]
    public async Task AT4_PoisonMessage_OffsetCommittedOnRestart_PoisonNotReplayed_FollowUpHandled()
    {
        var topic = AvroKafkaTestHarness.NewTopic("dorc.requests.new");
        await AvroKafkaTestHarness.CreateTopicAsync(topic, partitions: 1);

        try
        {
            var handler  = new RecordingHandler();
            var errorLog = new RecordingErrorLog();
            var groupId  = $"s006-at4-{Guid.NewGuid():N}";

            // ── Phase 1: consumer A processes the poison ─────────────────────
            // StoreOffset(offset+1) is staged; consumer.Close() (invoked during
            // StopAsync) commits it to the broker synchronously.
            {
                using var registry = AvroKafkaTestHarness.BuildRegistry();
                var factory = AvroKafkaTestHarness.BuildFactory(registry);
                using var consumerA = new DeploymentRequestsKafkaConsumer(
                    BuildConnection(), factory, handler, errorLog,
                    Options.Create(new KafkaTopicsOptions()),
                    new NoOpKafkaConsumerMetrics(),
                    NullLogger<DeploymentRequestsKafkaConsumer>.Instance)
                {
                    Topics          = new[] { topic },
                    ConsumerGroupId = groupId
                };
                await consumerA.StartAsync(CancellationToken.None);
                await Task.Delay(2_000); // rebalance settle

                using (var rawProducer = new ProducerBuilder<string, byte[]>(
                    new ProducerConfig { BootstrapServers = AvroKafkaTestHarness.BootstrapServers }).Build())
                {
                    await rawProducer.ProduceAsync(topic, new Message<string, byte[]>
                    {
                        Key   = "poison-key",
                        Value = System.Text.Encoding.UTF8.GetBytes("not-valid-avro")
                    });
                }

                await WaitForCount(() => errorLog.Count, 1, TimeSpan.FromSeconds(15));
                await consumerA.StopAsync(CancellationToken.None);
                // Close() commits offset 1 (StoreOffset staged by HandleFailure).
            }

            // ── DOWNTIME GAP: produce follow-up while no consumer is running ─
            // The follow-up lands at offset 1 (after the poison at offset 0).
            // A committed B resumes from offset 1 and reads it.
            // An uncommitted B uses AutoOffsetReset.Latest → starts at HWM=2 →
            // misses the follow-up → handler stays 0 → assertion fails.
            {
                using var registry = AvroKafkaTestHarness.BuildRegistry();
                var factory = AvroKafkaTestHarness.BuildFactory(registry);
                using var followUpProducer =
                    new KafkaProducerBuilder<string, DeploymentRequestEventData>(
                        BuildConnection(), factory,
                        NullLogger<KafkaProducerBuilder<string, DeploymentRequestEventData>>.Instance)
                    .Build("s006-at4-followup");

                await followUpProducer.ProduceAsync(topic, new Message<string, DeploymentRequestEventData>
                {
                    Key   = "42",
                    Value = new DeploymentRequestEventData(42, "Pending", null, null, DateTimeOffset.UtcNow)
                });
            }

            // ── Phase 2: consumer B on the same group ─────────────────────────
            {
                using var registry = AvroKafkaTestHarness.BuildRegistry();
                var factory = AvroKafkaTestHarness.BuildFactory(registry);
                using var consumerB = new DeploymentRequestsKafkaConsumer(
                    BuildConnection(), factory, handler, errorLog,
                    Options.Create(new KafkaTopicsOptions()),
                    new NoOpKafkaConsumerMetrics(),
                    NullLogger<DeploymentRequestsKafkaConsumer>.Instance)
                {
                    Topics          = new[] { topic },
                    ConsumerGroupId = groupId
                };
                await consumerB.StartAsync(CancellationToken.None);

                await WaitForCount(() => handler.Count, 1, TimeSpan.FromSeconds(20));
                await consumerB.StopAsync(CancellationToken.None);
            }

            // Exactly one error-log entry (from consumer A only — poison not replayed by B).
            var entries = errorLog.Snapshot();
            Assert.AreEqual(1, entries.Count,
                "Poison must not be replayed: error log must have exactly 1 entry (from consumer A only).");
            Assert.AreEqual(topic, entries[0].Topic);
            Assert.IsFalse(string.IsNullOrWhiteSpace(entries[0].Error));

            // Consumer B delivered exactly the follow-up produced during downtime.
            var received = handler.Snapshot();
            Assert.AreEqual(1, received.Count,
                "Consumer B must handle exactly one event — the follow-up produced during downtime.");
            Assert.AreEqual(42, received[0].Event.RequestId);
        }
        finally
        {
            await AvroKafkaTestHarness.DeleteTopicAsync(topic);
            await AvroKafkaTestHarness.DeleteSubjectAsync(topic + "-value");
        }
    }

    private static async Task WaitForCount(Func<int> getter, int target, TimeSpan timeout)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (getter() < target && sw.Elapsed < timeout)
            await Task.Delay(100);
    }

    private static IKafkaConnectionProvider BuildConnection()
        => new KafkaConnectionProvider(Options.Create(new KafkaClientOptions
        {
            BootstrapServers = AvroKafkaTestHarness.BootstrapServers,
            SchemaRegistry = { Url = AvroKafkaTestHarness.SchemaRegistryUrl },
            SessionTimeoutMs = 10_000,
            HeartbeatIntervalMs = 3_000,
            MaxPollIntervalMs = 60_000
        }));

    private sealed class RecordingHandler : IRequestEventHandler
    {
        private readonly List<(string Topic, DeploymentRequestEventData Event)> _received = new();
        private readonly object _lock = new();
        // Thread-safe count for polling in WaitForCount without exposing the raw List.
        public int Count { get { lock (_lock) return _received.Count; } }
        public IReadOnlyList<(string Topic, DeploymentRequestEventData Event)> Snapshot()
        { lock (_lock) return _received.ToList(); }
        public Task HandleAsync(string topic, DeploymentRequestEventData eventData, CancellationToken cancellationToken)
        {
            lock (_lock) _received.Add((topic, eventData));
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingErrorLog : IKafkaErrorLog
    {
        private readonly List<KafkaErrorLogEntry> _entries = new();
        private readonly object _lock = new();
        public int Count { get { lock (_lock) return _entries.Count; } }
        public IReadOnlyList<KafkaErrorLogEntry> Snapshot() { lock (_lock) return _entries.ToList(); }
        public Task InsertAsync(KafkaErrorLogEntry entry, CancellationToken ct)
        { lock (_lock) _entries.Add(entry); return Task.CompletedTask; }
        public Task<IReadOnlyList<KafkaErrorLogEntry>> QueryAsync(string? t, string? g, DateTimeOffset? s, int m, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<KafkaErrorLogEntry>>(Snapshot());
        public Task<int> PurgeAsync(CancellationToken ct) => Task.FromResult(0);
    }

    private sealed class NoopErrorLog : IKafkaErrorLog
    {
        public Task InsertAsync(KafkaErrorLogEntry entry, CancellationToken ct) => Task.CompletedTask;
        public Task<IReadOnlyList<KafkaErrorLogEntry>> QueryAsync(string? t, string? g, DateTimeOffset? s, int m, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<KafkaErrorLogEntry>>(Array.Empty<KafkaErrorLogEntry>());
        public Task<int> PurgeAsync(CancellationToken ct) => Task.FromResult(0);
    }
}
