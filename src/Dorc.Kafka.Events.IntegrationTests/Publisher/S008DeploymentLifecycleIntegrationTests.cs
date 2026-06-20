using Confluent.Kafka;
using Dorc.Core.Events;
using Dorc.Core.Interfaces;
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
/// Integration tests: user-facing deployment lifecycle operations
/// (New, Cancelling, Cancelled, Paused, Restarting) are published via
/// <see cref="KafkaDeploymentEventPublisher"/> and received by
/// <see cref="DeploymentRequestsKafkaConsumer"/> (the Monitor-side consumer).
///
/// This test class fills the gap identified in the pre-merge review:
/// the API's RequestController calls PublishRequestStatusChangedAsync /
/// PublishNewRequestAsync on every lifecycle transition, but no test
/// exercised the end-to-end path from publisher → Kafka → Monitor consumer.
/// </summary>
[TestClass]
public class S008DeploymentLifecycleIntegrationTests
{
    /// <summary>
    /// Five lifecycle status strings that map to the RequestController operations
    /// Cancel (→ Cancelling / Cancelled), Pause (→ Paused), Restart (→ Restarting),
    /// and a fresh NewRequest (→ Pending).
    /// </summary>
    [TestMethod]
    public async Task AT1_AllLifecycleTransitions_PublishedViaKafka_MonitorConsumerReceivesAll()
    {
        var newTopic    = AvroKafkaTestHarness.NewTopic("dorc.requests.new");
        var statusTopic = AvroKafkaTestHarness.NewTopic("dorc.requests.status");
        await AvroKafkaTestHarness.CreateTopicAsync(newTopic,    partitions: 3);
        await AvroKafkaTestHarness.CreateTopicAsync(statusTopic, partitions: 3);

        // KafkaTopicsOptions drives both publisher topic selection and
        // consumer DLQ routing; all five fields must be distinct.
        var topicsOptions = Options.Create(new KafkaTopicsOptions
        {
            RequestsNew    = newTopic,
            RequestsStatus = statusTopic,
            ResultsStatus  = $"dorc.results.unused-{Guid.NewGuid():N}",
            RequestsNewDlq = $"{newTopic}.dlq",
            Locks          = $"dorc.locks.unused-{Guid.NewGuid():N}"
        });

        try
        {
            using var registry = AvroKafkaTestHarness.BuildRegistry();
            var factory = AvroKafkaTestHarness.BuildFactory(registry);

            // Build publisher with both request producers.
            var pubConn = BuildConnection();
            using var resultsProducer = new KafkaProducerBuilder<string, DeploymentResultEventData>(
                pubConn, factory,
                NullLogger<KafkaProducerBuilder<string, DeploymentResultEventData>>.Instance)
                .Build("s008-results-producer");
            using var requestsProducer = new KafkaProducerBuilder<string, DeploymentRequestEventData>(
                pubConn, factory,
                NullLogger<KafkaProducerBuilder<string, DeploymentRequestEventData>>.Instance)
                .Build("s008-requests-producer");
            using var publisher = new KafkaDeploymentEventPublisher(
                resultsProducer, requestsProducer,
                new NoopFallback(), topicsOptions,
                NullLogger<KafkaDeploymentEventPublisher>.Instance);

            // Build the monitor-side consumer subscribed to both test topics.
            var handler = new RecordingHandler();
            var groupId = $"s008-at1-{Guid.NewGuid():N}";
            using var consumer = new DeploymentRequestsKafkaConsumer(
                BuildConnection(groupId), factory, handler,
                new NoopErrorLog(), topicsOptions,
                new Dorc.Kafka.Client.Observability.NoOpKafkaConsumerMetrics(),
                NullLogger<DeploymentRequestsKafkaConsumer>.Instance)
            {
                // Override the Topics set by the constructor (which reads from
                // topicsOptions.RequestsNew / RequestsStatus) with explicit
                // values so the intent is unambiguous in the test.
                Topics = new[] { newTopic, statusTopic },
                ConsumerGroupId = groupId
            };
            await consumer.StartAsync(CancellationToken.None);
            await Task.Delay(2_000); // allow initial partition assignment to complete

            var now = DateTimeOffset.UtcNow;

            // Simulate the five RequestController lifecycle operations:
            //   POST /Request          → PublishNewRequestAsync
            //   PUT  /Request/cancel   → PublishRequestStatusChangedAsync (Cancelling)
            //   PUT  /Request/cancel   → PublishRequestStatusChangedAsync (Cancelled — immediate)
            //   PUT  /Request/pause    → PublishRequestStatusChangedAsync (Paused)
            //   POST /Request/restart  → PublishRequestStatusChangedAsync (Restarting)
            await publisher.PublishNewRequestAsync(
                new DeploymentRequestEventData(1, "Pending",    null, null, now));
            await publisher.PublishRequestStatusChangedAsync(
                new DeploymentRequestEventData(2, "Cancelling", null, null, now));
            await publisher.PublishRequestStatusChangedAsync(
                new DeploymentRequestEventData(3, "Cancelled",  null, null, now));
            await publisher.PublishRequestStatusChangedAsync(
                new DeploymentRequestEventData(4, "Paused",     null, null, now));
            await publisher.PublishRequestStatusChangedAsync(
                new DeploymentRequestEventData(5, "Restarting", null, null, now));

            await WaitForCount(() => handler.Count, 5, TimeSpan.FromSeconds(20));
            await consumer.StopAsync(CancellationToken.None);

            Assert.AreEqual(5, handler.Count,
                "Monitor consumer must receive all 5 lifecycle events.");

            // New-request route goes to RequestsNew topic.
            AssertContains(handler, newTopic,    requestId: 1, status: "Pending");
            // All status-change routes go to RequestsStatus topic.
            AssertContains(handler, statusTopic, requestId: 2, status: "Cancelling");
            AssertContains(handler, statusTopic, requestId: 3, status: "Cancelled");
            AssertContains(handler, statusTopic, requestId: 4, status: "Paused");
            AssertContains(handler, statusTopic, requestId: 5, status: "Restarting");
        }
        finally
        {
            await AvroKafkaTestHarness.DeleteTopicAsync(newTopic);
            await AvroKafkaTestHarness.DeleteTopicAsync(statusTopic);
            await AvroKafkaTestHarness.DeleteSubjectAsync(newTopic    + "-value");
            await AvroKafkaTestHarness.DeleteSubjectAsync(statusTopic + "-value");
        }
    }

    // ---- helpers ----

    private static IKafkaConnectionProvider BuildConnection(string? groupId = null)
        => new KafkaConnectionProvider(Options.Create(new KafkaClientOptions
        {
            BootstrapServers  = AvroKafkaTestHarness.BootstrapServers,
            ConsumerGroupId   = groupId ?? $"s008-pub-{Guid.NewGuid():N}",
            SchemaRegistry    = { Url = AvroKafkaTestHarness.SchemaRegistryUrl },
            SessionTimeoutMs  = 10_000,
            HeartbeatIntervalMs = 3_000,
            MaxPollIntervalMs = 60_000
        }));

    private static void AssertContains(
        RecordingHandler handler, string expectedTopic, int requestId, string status)
    {
        Assert.IsTrue(
            handler.Snapshot().Any(r => r.Topic == expectedTopic
                                   && r.Event.RequestId == requestId
                                   && r.Event.Status    == status),
            $"Expected event requestId={requestId} status={status} on topic={expectedTopic} not found.");
    }

    private static async Task WaitForCount(Func<int> getter, int target, TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (getter() < target && DateTimeOffset.UtcNow < deadline)
            await Task.Delay(100);
    }

    private sealed class RecordingHandler : IRequestEventHandler
    {
        private readonly List<(string Topic, DeploymentRequestEventData Event)> _received = new();
        private readonly object _lock = new();
        // Thread-safe count for WaitForCount polling (consumer writes on its poll thread).
        public int Count { get { lock (_lock) return _received.Count; } }
        public IReadOnlyList<(string Topic, DeploymentRequestEventData Event)> Snapshot()
        { lock (_lock) return _received.ToList(); }
        public Task HandleAsync(string topic, DeploymentRequestEventData eventData, CancellationToken ct)
        {
            lock (_lock) _received.Add((topic, eventData));
            return Task.CompletedTask;
        }
    }

    private sealed class NoopErrorLog : IKafkaErrorLog
    {
        public Task InsertAsync(KafkaErrorLogEntry e, CancellationToken ct) => Task.CompletedTask;
        public Task<IReadOnlyList<KafkaErrorLogEntry>> QueryAsync(
            string? t, string? g, DateTimeOffset? s, int m, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<KafkaErrorLogEntry>>(Array.Empty<KafkaErrorLogEntry>());
        public Task<int> PurgeAsync(CancellationToken ct) => Task.FromResult(0);
    }

    private sealed class NoopFallback : IFallbackDeploymentEventPublisher
    {
        public Task PublishNewRequestAsync(DeploymentRequestEventData e)      => Task.CompletedTask;
        public Task PublishRequestStatusChangedAsync(DeploymentRequestEventData e) => Task.CompletedTask;
        public Task PublishResultStatusChangedAsync(DeploymentResultEventData e)   => Task.CompletedTask;
    }
}
