using Confluent.Kafka;
using Dorc.Core.Events;
using Dorc.Kafka.Client.Configuration;
using Dorc.Kafka.Client.Connection;
using Dorc.Kafka.Client.Observability;
using Dorc.Kafka.Client.Serialization;
using Dorc.Kafka.ErrorLog;
using Dorc.Kafka.Events.Configuration;
using Dorc.Kafka.Events.Publisher;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Dorc.Kafka.Events.Tests.Publisher;

/// <summary>
/// Pin the strong-2/3 commit-semantics finding from PR #611's adversarial
/// review (Sonnet+Haiku). Both consumers must enforce their offset-semantics
/// invariants regardless of what the connection provider (or a librdkafka
/// default) puts on the returned config; otherwise a crash mid-broadcast /
/// mid-handler can silently advance past an unprocessed record. Both
/// consumers use the same pattern: auto-commit timer ON, auto-offset-store
/// OFF, offsets stored via explicit StoreOffset only after
/// broadcast/handler success. The tests exercise <c>BuildConsumerConfig</c>
/// directly so any future refactor that drops the explicit override fails CI.
///
/// Connection-provider behaviour is stubbed out — the test plants the
/// "hostile" config directly on the returned ConsumerConfig and asserts the
/// consumer override flips it. That way a future refactor of
/// <see cref="KafkaConnectionProvider"/> can't make these tests pass for
/// the wrong reason.
/// </summary>
[TestClass]
public class ConsumerCommitSemanticsTests
{
    [TestMethod]
    public void ResultsConsumer_KeepsAutoCommit_ButDisablesOffsetStore_OverridingProvider()
    {
        // Hostile baseline: provider returns a config with auto-offset-store
        // ON (the librdkafka default) and the auto-commit timer OFF. The
        // consumer must flip both: storage gated on broadcast success, timer
        // enabled so stored offsets actually flush.
        var sut = NewResultsConsumer(new StubConnectionProvider(
            new ConsumerConfig
            {
                BootstrapServers = "127.0.0.1:1",
                GroupId = "ignored-by-consumer",
                EnableAutoCommit = false,
                EnableAutoOffsetStore = true
            }));

        var config = sut.BuildConsumerConfig();

        Assert.IsTrue(config.EnableAutoCommit ?? false,
            "Results consumer leaves the auto-commit timer enabled (low-overhead); StoreOffset-after-broadcast provides the delivery gate.");
        Assert.IsFalse(config.EnableAutoOffsetStore ?? true,
            "Results consumer must disable auto-offset-store; otherwise a crash between consume() and BroadcastAsync completion silently drops a SignalR projection.");
    }

    [TestMethod]
    public void ResultsConsumer_AutoOffsetResetIsLatest_EvenWhenProviderSaysEarliest()
    {
        var sut = NewResultsConsumer(new StubConnectionProvider(
            new ConsumerConfig
            {
                BootstrapServers = "127.0.0.1:1",
                GroupId = "ignored",
                AutoOffsetReset = AutoOffsetReset.Earliest
            }));

        var config = sut.BuildConsumerConfig();

        Assert.AreEqual(AutoOffsetReset.Latest, config.AutoOffsetReset);
    }

    [TestMethod]
    public void ResultsConsumer_GroupIdHasPerReplicaDiscriminatorAfterPrefix()
    {
        var sut = NewResultsConsumer(new StubConnectionProvider(
            new ConsumerConfig { BootstrapServers = "127.0.0.1:1", GroupId = "global-shared-group" }));

        var config = sut.BuildConsumerConfig();

        // Stronger than StartsWith: a refactor that hardcoded GroupId =
        // ConsumerGroupPrefix (no suffix) would still satisfy StartsWith
        // but silently collapses the per-replica fan-out invariant
        // . Require the prefix + a '.'-separated suffix
        // and an actual non-empty discriminator after it.
        var prefix = DeploymentResultsKafkaConsumer.ConsumerGroupPrefix + ".";
        StringAssert.StartsWith(config.GroupId, prefix);
        Assert.IsTrue(config.GroupId.Length > prefix.Length,
            $"GroupId '{config.GroupId}' must include a per-replica discriminator after the prefix.");
        Assert.AreNotEqual("global-shared-group", config.GroupId);
    }

    [TestMethod]
    public void RequestsConsumer_KeepsAutoCommit_ButDisablesOffsetStore_OverridingProvider()
    {
        // Hostile baseline: provider returns a config with auto-offset-store
        // ON (the librdkafka default). The consumer must turn it OFF so
        // offsets only advance via explicit StoreOffset after handler
        // success.
        var sut = NewRequestsConsumer(new StubConnectionProvider(
            new ConsumerConfig
            {
                BootstrapServers = "127.0.0.1:1",
                GroupId = "ignored",
                EnableAutoCommit = false,
                EnableAutoOffsetStore = true
            }));

        var config = sut.BuildConsumerConfig();

        Assert.IsTrue(config.EnableAutoCommit ?? false,
            "Requests consumer leaves the auto-commit timer enabled (low-overhead).");
        Assert.IsFalse(config.EnableAutoOffsetStore ?? true,
            "Requests consumer must disable auto-offset-store; storage is gated on handler success.");
    }

    [TestMethod]
    public void RequestsConsumer_AutoOffsetResetIsLatest_EvenWhenProviderSaysEarliest()
    {
        // The requests consumer is a per-replica fan-out (unique group id per instance).
        // AutoOffsetReset.Latest prevents K8s rollout replay storms: on restart the
        // DB-poll baseline catches up any missed events (C6 fix).
        var sut = NewRequestsConsumer(new StubConnectionProvider(
            new ConsumerConfig
            {
                BootstrapServers = "127.0.0.1:1",
                GroupId = "ignored",
                AutoOffsetReset = AutoOffsetReset.Earliest
            }));

        var config = sut.BuildConsumerConfig();

        Assert.AreEqual(AutoOffsetReset.Latest, config.AutoOffsetReset);
    }

    [TestMethod]
    public void RequestsConsumer_GroupIdHasPerReplicaDiscriminatorAfterPrefix()
    {
        var sut = NewRequestsConsumer(new StubConnectionProvider(
            new ConsumerConfig { BootstrapServers = "127.0.0.1:1", GroupId = "global-shared-group" }));

        var config = sut.BuildConsumerConfig();

        var prefix = DeploymentRequestsKafkaConsumer.ConsumerGroupPrefix + ".";
        StringAssert.StartsWith(config.GroupId, prefix);
        Assert.IsTrue(config.GroupId.Length > prefix.Length,
            $"GroupId '{config.GroupId}' must include a per-replica discriminator after the prefix.");
    }

    private static DeploymentResultsKafkaConsumer NewResultsConsumer(IKafkaConnectionProvider provider)
        => new DeploymentResultsKafkaConsumer(
            provider,
            new DefaultKafkaSerializerFactory(),
            new StubBroadcaster(),
            new NoopErrorLog(),
            Options.Create(new KafkaTopicsOptions()),
            new NoOpKafkaConsumerMetrics(),
            NullLogger<DeploymentResultsKafkaConsumer>.Instance);

    private static DeploymentRequestsKafkaConsumer NewRequestsConsumer(IKafkaConnectionProvider provider)
        => new DeploymentRequestsKafkaConsumer(
            provider,
            new DefaultKafkaSerializerFactory(),
            new StubHandler(),
            new NoopErrorLog(),
            Options.Create(new KafkaTopicsOptions()),
            new NoOpKafkaConsumerMetrics(),
            NullLogger<DeploymentRequestsKafkaConsumer>.Instance);

    /// <summary>
    /// Returns a caller-controlled <see cref="ConsumerConfig"/> verbatim,
    /// ignoring any group-id override. Lets each test plant the "hostile"
    /// baseline the consumer's BuildConsumerConfig is supposed to override.
    /// </summary>
    private sealed class StubConnectionProvider : IKafkaConnectionProvider
    {
        private readonly ConsumerConfig _consumerConfig;

        public StubConnectionProvider(ConsumerConfig consumerConfig) => _consumerConfig = consumerConfig;

        public ConsumerConfig GetConsumerConfig(string groupId)
        {
            // Honour the per-replica group id the consumer passes in (so the
            // GroupId tests can observe it) but preserve every other field
            // exactly as the test planted it.
            var copy = new ConsumerConfig();
            foreach (var kv in _consumerConfig)
                copy.Set(kv.Key, kv.Value);
            copy.GroupId = groupId;
            return copy;
        }

        public ProducerConfig GetProducerConfig() => throw new NotImplementedException();
    }

    private sealed class StubBroadcaster : IDeploymentResultBroadcaster
    {
        public Task BroadcastAsync(DeploymentResultEventData @event, CancellationToken cancellationToken)
            => Task.CompletedTask;
    }

    private sealed class StubHandler : IRequestEventHandler
    {
        public Task HandleAsync(string topic, DeploymentRequestEventData @event, CancellationToken cancellationToken)
            => Task.CompletedTask;
    }

    private sealed class NoopErrorLog : IKafkaErrorLog
    {
        public Task InsertAsync(KafkaErrorLogEntry entry, CancellationToken cancellationToken)
            => Task.CompletedTask;
    }
}
