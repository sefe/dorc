using Confluent.Kafka;
using Dorc.Core.Events;
using Dorc.Kafka.Client.Configuration;
using Dorc.Kafka.Client.Connection;
using Dorc.Kafka.Client.Producers;
using Dorc.Kafka.ErrorLog;
using Dorc.Kafka.Events.Publisher;
using Dorc.PersistentData.Model;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Dorc.Kafka.Events.IntegrationTests.Publisher;

/// <summary>
/// SPEC-S-006 integration tests against the local compose stack.
/// AT-3 (consumer subscribes both topics, handler invoked per record),
/// AT-10 (E2E producer→consumer wake-up signal),
/// AT-11 (per-RequestId ordering preserved by key-partition FIFO).
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
                BuildConnection(groupId),
                factory,
                handler,
                new NoopErrorLog(),
                NullLogger<DeploymentRequestsKafkaConsumer>.Instance)
            {
                Topics = new[] { newTopic, statusTopic },
                ConsumerGroupId = groupId
            };
            await consumer.StartAsync(CancellationToken.None);

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

            await WaitForCount(() => handler.Received.Count, 2, TimeSpan.FromSeconds(20));
            await consumer.StopAsync(CancellationToken.None);

            Assert.AreEqual(2, handler.Received.Count);
            Assert.IsTrue(handler.Received.Any(r => r.Topic == newTopic && r.Event.RequestId == 1));
            Assert.IsTrue(handler.Received.Any(r => r.Topic == statusTopic && r.Event.RequestId == 2));
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
                BuildConnection(groupId),
                factory,
                new PollSignalRequestEventHandler(signal),
                new NoopErrorLog(),
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
                BuildConnection(groupId),
                factory,
                handler,
                new NoopErrorLog(),
                NullLogger<DeploymentRequestsKafkaConsumer>.Instance)
            {
                Topics = new[] { topic },
                ConsumerGroupId = groupId
            };
            await consumer.StartAsync(CancellationToken.None);

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

            await WaitForCount(() => handler.Received.Count, statuses.Length, TimeSpan.FromSeconds(20));
            await consumer.StopAsync(CancellationToken.None);

            var observed = handler.Received.Select(r => r.Event.Status).ToArray();
            CollectionAssert.AreEqual(statuses, observed,
                "Per-RequestId events keyed identically must arrive in produce order (single-partition FIFO).");
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

    private static IKafkaConnectionProvider BuildConnection(string groupId)
        => new KafkaConnectionProvider(Options.Create(new KafkaClientOptions
        {
            BootstrapServers = AvroKafkaTestHarness.BootstrapServers,
            ConsumerGroupId = groupId,
            SchemaRegistry = { Url = AvroKafkaTestHarness.SchemaRegistryUrl },
            SessionTimeoutMs = 10_000,
            HeartbeatIntervalMs = 3_000,
            MaxPollIntervalMs = 60_000
        }));

    private sealed class RecordingHandler : IRequestEventHandler
    {
        public List<(string Topic, DeploymentRequestEventData Event)> Received { get; } = new();
        private readonly object _lock = new();
        public Task HandleAsync(string topic, DeploymentRequestEventData eventData, CancellationToken cancellationToken)
        {
            lock (_lock) Received.Add((topic, eventData));
            return Task.CompletedTask;
        }
    }

    private sealed class NoopErrorLog : IKafkaErrorLog
    {
        public Task InsertAsync(KafkaErrorLogEntry entry, CancellationToken ct) => Task.CompletedTask;
        public Task<IReadOnlyList<KafkaErrorLogEntry>> QueryAsync(string? t, string? g, DateTimeOffset? s, int m, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<KafkaErrorLogEntry>>(Array.Empty<KafkaErrorLogEntry>());
        public Task<int> PurgeAsync(CancellationToken ct) => Task.FromResult(0);
    }
}
