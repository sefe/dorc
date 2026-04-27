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
/// Shared harness for S-007 integration tests (AT-2..AT-6). Builds a full
/// Kafka client layer (producer + consumer) against the local compose
/// stack, wires it through the Dorc.Kafka.Events production classes, and
/// substitutes fakes only where the spec explicitly permits (broadcaster,
/// error log).
/// </summary>
internal sealed class S007TestHarness : IAsyncDisposable
{
    private readonly List<IDisposable> _disposables = new();

    public S007TestHarness()
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
        // S-006 R-1: publisher now also requires a request producer. The S-007
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
            Options.Create(new KafkaErrorLogOptions()),
            topicsOptions,
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
            try { d.Dispose(); } catch { /* best-effort */ }
        }
        return ValueTask.CompletedTask;
    }

    internal sealed class RecordingBroadcaster : IDeploymentResultBroadcaster
    {
        private int _counter;
        private readonly object _lock = new();

        public List<(int ArrivalIndex, DeploymentResultEventData Event)> Received { get; } = new();

        public Task BroadcastAsync(DeploymentResultEventData eventData, CancellationToken cancellationToken)
        {
            lock (_lock) Received.Add((Interlocked.Increment(ref _counter), eventData));
            return Task.CompletedTask;
        }
    }

    internal sealed class RecordingErrorLog : IKafkaErrorLog
    {
        private int _seq;
        private readonly object _lock = new();
        public List<(int Sequence, KafkaErrorLogEntry Entry)> Recorded { get; } = new();
        public List<KafkaErrorLogEntry> Entries => Recorded.Select(r => r.Entry).ToList();
        public Func<KafkaErrorLogEntry, CancellationToken, Task>? InsertOverride { get; set; }

        public Task InsertAsync(KafkaErrorLogEntry entry, CancellationToken cancellationToken)
        {
            if (InsertOverride is not null) return InsertOverride(entry, cancellationToken);
            lock (_lock) Recorded.Add((Interlocked.Increment(ref _seq), entry));
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<KafkaErrorLogEntry>> QueryAsync(
            string? topic, string? consumerGroup, DateTimeOffset? sinceUtc, int maxRows, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<KafkaErrorLogEntry>>(Entries);

        public Task<int> PurgeAsync(CancellationToken cancellationToken) => Task.FromResult(0);
    }

    internal sealed class NoopFallback : Dorc.Core.Interfaces.IFallbackDeploymentEventPublisher
    {
        public Task PublishNewRequestAsync(DeploymentRequestEventData eventData) => Task.CompletedTask;
        public Task PublishRequestStatusChangedAsync(DeploymentRequestEventData eventData) => Task.CompletedTask;
        public Task PublishResultStatusChangedAsync(DeploymentResultEventData eventData) => Task.CompletedTask;
    }
}
