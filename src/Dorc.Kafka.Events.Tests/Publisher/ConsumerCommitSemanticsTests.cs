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
/// review (Sonnet+Haiku). Both consumers must enforce their commit-mode
/// invariants regardless of what an operator sets globally on
/// <see cref="KafkaClientOptions.EnableAutoCommit"/>; otherwise a crash
/// mid-broadcast / mid-handler can silently advance past an unprocessed
/// record. The tests exercise <c>BuildConsumerConfig()</c> directly so any
/// future refactor that drops the explicit override fails CI.
/// </summary>
[TestClass]
public class ConsumerCommitSemanticsTests
{
    [TestMethod]
    public void ResultsConsumer_OverridesGlobalEnableAutoCommitToFalse()
    {
        // Operator sets EnableAutoCommit=true globally — exactly the
        // misconfiguration that motivated the strong 2/3 finding.
        var options = Options.Create(new KafkaClientOptions
        {
            BootstrapServers = "127.0.0.1:1",
            ConsumerGroupId = "test-group",
            EnableAutoCommit = true
        });
        var sut = NewResultsConsumer(options);

        var config = sut.BuildConsumerConfig();

        Assert.IsFalse(config.EnableAutoCommit ?? true,
            "Results consumer must force EnableAutoCommit=false; otherwise a global override would silently auto-commit on a background timer between consume() and BroadcastAsync completion.");
    }

    [TestMethod]
    public void ResultsConsumer_AutoOffsetResetIsLatest()
    {
        // Status events are real-time; we don't replay history on restart.
        var options = Options.Create(new KafkaClientOptions
        {
            BootstrapServers = "127.0.0.1:1",
            ConsumerGroupId = "test-group",
            AutoOffsetReset = KafkaAutoOffsetReset.Earliest
        });
        var sut = NewResultsConsumer(options);

        var config = sut.BuildConsumerConfig();

        Assert.AreEqual(AutoOffsetReset.Latest, config.AutoOffsetReset);
    }

    [TestMethod]
    public void ResultsConsumer_GroupIdIsPerReplica_NotTheGlobalDefault()
    {
        var options = Options.Create(new KafkaClientOptions
        {
            BootstrapServers = "127.0.0.1:1",
            ConsumerGroupId = "global-shared-group" // wrong default for fan-out
        });
        var sut = NewResultsConsumer(options);

        var config = sut.BuildConsumerConfig();

        StringAssert.StartsWith(config.GroupId,
            DeploymentResultsKafkaConsumer.ConsumerGroupPrefix,
            "Per-replica fan-out (SPEC-S-007 R-2) requires a per-consumer group id, not the global Kafka:ConsumerGroupId.");
        Assert.AreNotEqual("global-shared-group", config.GroupId);
    }

    [TestMethod]
    public void RequestsConsumer_KeepsAutoCommit_ButDisablesOffsetStore()
    {
        var options = Options.Create(new KafkaClientOptions
        {
            BootstrapServers = "127.0.0.1:1",
            ConsumerGroupId = "test-group"
        });
        var sut = NewRequestsConsumer(options);

        var config = sut.BuildConsumerConfig();

        // The fix-branch's chosen at-least-once shape: timer keeps committing
        // *stored* offsets, but we control storage explicitly via
        // StoreOffset() after handler success. A crash mid-handler means the
        // offset was never stored, so the next replica picks the record up.
        Assert.IsTrue(config.EnableAutoCommit ?? false,
            "Requests consumer leaves the auto-commit timer enabled (low-overhead).");
        Assert.IsFalse(config.EnableAutoOffsetStore ?? true,
            "Requests consumer must disable auto-offset-store; storage is gated on handler success.");
    }

    [TestMethod]
    public void RequestsConsumer_AutoOffsetResetIsEarliest()
    {
        // Narrow the visibility gap on consumer restart; rebalance-replay
        // is harmless because the handler is idempotent.
        var options = Options.Create(new KafkaClientOptions
        {
            BootstrapServers = "127.0.0.1:1",
            ConsumerGroupId = "test-group",
            AutoOffsetReset = KafkaAutoOffsetReset.Latest
        });
        var sut = NewRequestsConsumer(options);

        var config = sut.BuildConsumerConfig();

        Assert.AreEqual(AutoOffsetReset.Earliest, config.AutoOffsetReset);
    }

    [TestMethod]
    public void RequestsConsumer_GroupIdIsPerReplica_NotTheGlobalDefault()
    {
        var options = Options.Create(new KafkaClientOptions
        {
            BootstrapServers = "127.0.0.1:1",
            ConsumerGroupId = "global-shared-group"
        });
        var sut = NewRequestsConsumer(options);

        var config = sut.BuildConsumerConfig();

        StringAssert.StartsWith(config.GroupId,
            DeploymentRequestsKafkaConsumer.ConsumerGroupPrefix,
            "Per-replica fan-out (SPEC-S-006 R-3) requires a per-consumer group id.");
    }

    private static DeploymentResultsKafkaConsumer NewResultsConsumer(IOptions<KafkaClientOptions> options)
    {
        var connection = new KafkaConnectionProvider(options);
        return new DeploymentResultsKafkaConsumer(
            connection,
            new DefaultKafkaSerializerFactory(),
            new StubBroadcaster(),
            new NoopErrorLog(),
            Options.Create(new KafkaErrorLogOptions()),
            Options.Create(new KafkaTopicsOptions()),
            new NoOpKafkaConsumerMetrics(),
            NullLogger<DeploymentResultsKafkaConsumer>.Instance);
    }

    private static DeploymentRequestsKafkaConsumer NewRequestsConsumer(IOptions<KafkaClientOptions> options)
    {
        var connection = new KafkaConnectionProvider(options);
        return new DeploymentRequestsKafkaConsumer(
            connection,
            new DefaultKafkaSerializerFactory(),
            new StubHandler(),
            new NoopErrorLog(),
            Options.Create(new KafkaTopicsOptions()),
            new NoOpKafkaConsumerMetrics(),
            NullLogger<DeploymentRequestsKafkaConsumer>.Instance);
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
