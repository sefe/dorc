using Confluent.Kafka;
using Confluent.SchemaRegistry;
using Dorc.Core.Events;
using Dorc.Kafka.Client.Configuration;
using Dorc.Kafka.Client.Connection;
using Dorc.Kafka.Client.Producers;
using Dorc.Kafka.ErrorLog;
using Dorc.Kafka.Events.Configuration;
using Dorc.Kafka.Events.Publisher;
using Dorc.Kafka.Events.Serialization;
using Dorc.PersistentData.Model;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Dorc.Kafka.Events.IntegrationTests.Publisher;

/// <summary>
/// Shared harness for the requests/results substrate integration tests
/// (<see cref="RequestsSubstrateAcceptanceIntegrationTests"/> and the
/// consumer-resilience suite). Builds a full Kafka client layer (producer +
/// consumer) against the local compose stack, wires it through the
/// Dorc.Kafka.Events production classes, and substitutes fakes only where
/// the spec explicitly permits (broadcaster, error log).
/// </summary>
internal sealed class RequestsSubstrateTestHarness : IAsyncDisposable
{
    private readonly List<IDisposable> _disposables = new();

    public RequestsSubstrateTestHarness()
    {
        Registry = AvroKafkaTestHarness.BuildRegistry();
        _disposables.Add(Registry);
        Factory = AvroKafkaTestHarness.BuildFactory(Registry);
        _disposables.Add(Factory);

        ClientOptions = new KafkaClientOptions
        {
            BootstrapServers = AvroKafkaTestHarness.BootstrapServers,
            SchemaRegistry = { Url = AvroKafkaTestHarness.SchemaRegistryUrl },
            SessionTimeoutMs = 10_000,
            HeartbeatIntervalMs = 3_000,
            MaxPollIntervalMs = 60_000
        };
        ConnectionProvider = new KafkaConnectionProvider(Options.Create(ClientOptions));

        ProducerBuilder = new KafkaProducerBuilder<string, DeploymentResultEventData>(
            ConnectionProvider, Factory,
            NullLogger<KafkaProducerBuilder<string, DeploymentResultEventData>>.Instance);

        Broadcaster = new RecordingBroadcaster();
        ErrorLog = new RecordingErrorLog();
    }

    public ISchemaRegistryClient Registry { get; }
    public AvroKafkaSerializerFactory Factory { get; }
    public KafkaClientOptions ClientOptions { get; }
    public IKafkaConnectionProvider ConnectionProvider { get; }
    public IKafkaProducerBuilder<string, DeploymentResultEventData> ProducerBuilder { get; }
    public RecordingBroadcaster Broadcaster { get; }
    public RecordingErrorLog ErrorLog { get; }

    public KafkaDeploymentEventPublisher BuildPublisher()
    {
        var producer = ProducerBuilder.Build("test-publisher");
        _disposables.Add(producer);
        // : publisher now also requires a request producer. The
        // harness exercises only result-status flows so a separate dedicated
        // request producer is built per harness instance for completeness.
        var requestsBuilder = new KafkaProducerBuilder<string, DeploymentRequestEventData>(
            ConnectionProvider, Factory,
            NullLogger<KafkaProducerBuilder<string, DeploymentRequestEventData>>.Instance);
        var requestsProducer = requestsBuilder.Build("test-requests-publisher");
        _disposables.Add(requestsProducer);
        var publisher = new KafkaDeploymentEventPublisher(
            producer,
            requestsProducer,
            new NoopFallback(),
            Options.Create(new KafkaTopicsOptions()),
            NullLogger<KafkaDeploymentEventPublisher>.Instance);
        _disposables.Add(publisher);
        return publisher;
    }

    public DeploymentResultsKafkaConsumer BuildConsumer(string? topic = null, string? groupId = null)
    {
        var topicsOptions = Options.Create(new KafkaTopicsOptions());
        var consumer = new DeploymentResultsKafkaConsumer(
            ConnectionProvider,
            Factory,
            Broadcaster,
            ErrorLog,
            topicsOptions,
            new Dorc.Kafka.Client.Observability.NoOpKafkaConsumerMetrics(),
            NullLogger<DeploymentResultsKafkaConsumer>.Instance)
        {
            TopicName = topic ?? topicsOptions.Value.ResultsStatus,
            ConsumerGroupId = groupId ?? $"{DeploymentResultsKafkaConsumer.ConsumerGroupPrefix}.{HostInstanceId.Value}"
        };
        _disposables.Add(consumer);
        return consumer;
    }

    public ValueTask DisposeAsync()
    {
        foreach (var d in _disposables.AsEnumerable().Reverse())
        {
            try { d.Dispose(); }
            catch (ObjectDisposedException) { /* best-effort: already disposed */ }
            catch (InvalidOperationException) { /* best-effort: client in invalid state */ }
            catch (KafkaException) { /* best-effort: client teardown raced with broker */ }
        }
        return ValueTask.CompletedTask;
    }

    internal sealed class RecordingBroadcaster : IDeploymentResultBroadcaster
    {
        private int _counter;
        private readonly object _lock = new();
        private readonly List<(int ArrivalIndex, DeploymentResultEventData Event)> _received = new();

        // Thread-safe accessors — consumer writes on its poll thread; tests read on the test thread.
        public int Count { get { lock (_lock) return _received.Count; } }
        public IReadOnlyList<(int ArrivalIndex, DeploymentResultEventData Event)> Snapshot()
        { lock (_lock) return _received.ToList(); }

        public Task BroadcastAsync(DeploymentResultEventData eventData, CancellationToken cancellationToken)
        {
            lock (_lock) _received.Add((Interlocked.Increment(ref _counter), eventData));
            return Task.CompletedTask;
        }
    }

    internal sealed class RecordingErrorLog : IKafkaErrorLog
    {
        private int _seq;
        private readonly object _lock = new();
        private readonly List<(int Sequence, KafkaErrorLogEntry Entry)> _recorded = new();

        // Thread-safe count for polling; Snapshot() for assertion reads.
        public int Count { get { lock (_lock) return _recorded.Count; } }
        public IReadOnlyList<KafkaErrorLogEntry> Snapshot()
        { lock (_lock) return _recorded.Select(r => r.Entry).ToList(); }
        // Ordered snapshot including sequence numbers (for ordering assertions).
        public IReadOnlyList<(int Sequence, KafkaErrorLogEntry Entry)> SnapshotWithSeq()
        { lock (_lock) return _recorded.ToList(); }

        public Func<KafkaErrorLogEntry, CancellationToken, Task>? InsertOverride { get; set; }

        public Task InsertAsync(KafkaErrorLogEntry entry, CancellationToken cancellationToken)
        {
            if (InsertOverride is not null) return InsertOverride(entry, cancellationToken);
            lock (_lock) _recorded.Add((Interlocked.Increment(ref _seq), entry));
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<KafkaErrorLogEntry>> QueryAsync(
            string? topic, string? consumerGroup, DateTimeOffset? sinceUtc, int maxRows, CancellationToken cancellationToken)
            => Task.FromResult(Snapshot());

        public Task<int> PurgeAsync(CancellationToken cancellationToken) => Task.FromResult(0);
    }

    internal sealed class NoopFallback : Dorc.Core.Interfaces.IFallbackDeploymentEventPublisher
    {
        public Task PublishNewRequestAsync(DeploymentRequestEventData eventData) => Task.CompletedTask;
        public Task PublishRequestStatusChangedAsync(DeploymentRequestEventData eventData) => Task.CompletedTask;
        public Task PublishResultStatusChangedAsync(DeploymentResultEventData eventData) => Task.CompletedTask;
    }
}
