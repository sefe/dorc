using System.Collections.Concurrent;
using Confluent.Kafka;
using Confluent.Kafka.Admin;
using Dorc.Kafka.Events.Configuration;
using Dorc.Kafka.Lock.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Dorc.Kafka.Lock.Tests;

/// <summary>
/// Pins the provisioner's startup error policy:
/// - partition-count mismatch → fail fast (host must not start: split-brain risk);
/// - broker unreachable (KafkaException) → log and continue (consumer loop retries);
/// - create rejected (ACL/policy/other) → log "locking may be unavailable" and continue;
/// - the StartAsync CancellationToken is honored.
/// </summary>
[TestClass]
public class KafkaLocksTopicProvisionerTests
{
    private const string Topic = "dorc.locks";

    private static (KafkaLocksTopicProvisioner provisioner, CapturingLogger logger) Build(int partitionCount = 12)
    {
        var opts = Options.Create(new KafkaLocksOptions
        {
            PartitionCount = partitionCount,
            ReplicationFactor = 1,
            ConsumerGroupId = "test"
        });
        var topics = Options.Create(new KafkaTopicsOptions());
        var logger = new CapturingLogger();
        var provisioner = new KafkaLocksTopicProvisioner(
            new FakeConnectionProvider(), opts, topics, logger);
        return (provisioner, logger);
    }

    private static CreateTopicsException TopicError(ErrorCode code, string reason = "scripted")
        => new(new List<CreateTopicReport>
        {
            new() { Topic = Topic, Error = new Error(code, reason) }
        });

    private static Metadata MetadataWithPartitions(int partitionCount)
    {
        var partitions = Enumerable.Range(0, partitionCount)
            .Select(i => new PartitionMetadata(i, 1, new[] { 1 }, new[] { 1 }, new Error(ErrorCode.NoError)))
            .ToList();
        return new Metadata(
            new List<BrokerMetadata> { new(1, "localhost", 9092) },
            new List<TopicMetadata> { new(Topic, partitions, new Error(ErrorCode.NoError)) },
            1, "localhost:9092");
    }

    [TestMethod]
    public async Task BrokerUnreachable_KafkaException_DoesNotEscapeStartup()
    {
        var (provisioner, logger) = Build();
        var admin = new ScriptedAdminClient
        {
            OnCreateTopics = _ => throw new KafkaException(new Error(ErrorCode.Local_TimedOut, "broker unreachable"))
        };

        // Old behavior: the KafkaException escaped StartAsync and crash-looped the host.
        await provisioner.ProvisionAsync(admin, CancellationToken.None);

        Assert.IsTrue(logger.Messages.Any(m => m.Contains("UNAVAILABLE", StringComparison.OrdinalIgnoreCase)),
            $"Must warn that distributed locking may be unavailable; got: {string.Join(" | ", logger.Messages)}");
    }

    [TestMethod]
    public async Task CreateDeniedByAcl_LogsLockingUnavailable_AndContinues()
    {
        var (provisioner, logger) = Build();
        var admin = new ScriptedAdminClient
        {
            OnCreateTopics = _ => throw TopicError(ErrorCode.TopicAuthorizationFailed)
        };

        await provisioner.ProvisionAsync(admin, CancellationToken.None);

        Assert.IsTrue(logger.Messages.Any(m => m.Contains("denied by ACL")));
        Assert.IsTrue(logger.Messages.Any(m => m.Contains("UNAVAILABLE", StringComparison.OrdinalIgnoreCase)),
            "Non-HA boot must be loudly flagged, not silently degraded.");
    }

    [TestMethod]
    public async Task TopicExists_PartitionCountMismatch_FailsFast()
    {
        var (provisioner, _) = Build(partitionCount: 12);
        var admin = new ScriptedAdminClient
        {
            OnCreateTopics = _ => throw TopicError(ErrorCode.TopicAlreadyExists),
            OnGetMetadata = (_, _) => MetadataWithPartitions(6) // wrong count → mis-routed lock keys
        };

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => provisioner.ProvisionAsync(admin, CancellationToken.None));
    }

    [TestMethod]
    public async Task TopicExists_MatchingPartitionCount_Continues()
    {
        var (provisioner, logger) = Build(partitionCount: 12);
        var admin = new ScriptedAdminClient
        {
            OnCreateTopics = _ => throw TopicError(ErrorCode.TopicAlreadyExists),
            OnGetMetadata = (_, _) => MetadataWithPartitions(12)
        };

        await provisioner.ProvisionAsync(admin, CancellationToken.None);
        Assert.IsTrue(logger.Messages.Any(m => m.Contains("expected partitions")));
    }

    [TestMethod]
    public async Task TopicExists_MetadataUnavailable_ContinuesStartup()
    {
        // Audit CR#3 (deliberately reverses the earlier "M1" fail-fast decision):
        // when the topic already exists but partition-count cannot be verified
        // because the broker is transiently unreachable during the metadata
        // fetch, the provisioner must LOG and CONTINUE rather than throw and
        // abort host startup. Crashing here on a transient blip during a routine
        // restart takes down the DB-poll fallback path too, and is asymmetric
        // with the topic-missing path (which already logs and continues). A
        // genuine partition-count MISMATCH still fails fast — that path throws
        // InvalidOperationException, not KafkaException (see
        // TopicExists_WrongPartitionCount_FailsFast).
        var (provisioner, logger) = Build();
        var admin = new ScriptedAdminClient
        {
            OnCreateTopics = _ => throw TopicError(ErrorCode.TopicAlreadyExists),
            OnGetMetadata = (_, _) => throw new KafkaException(new Error(ErrorCode.Local_Transport, "down"))
        };

        // Must not throw.
        await provisioner.ProvisionAsync(admin, CancellationToken.None);

        Assert.IsTrue(
            logger.Messages.Any(m => m.Contains("could not be verified")),
            "A transient broker outage during verification must be logged and tolerated, not fatal.");
    }

    [TestMethod]
    [Timeout(30_000)]
    public async Task ProvisionAsync_HonorsCancellation_WhileBrokerCallHangs()
    {
        var (provisioner, _) = Build();
        var never = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var admin = new ScriptedAdminClient { OnCreateTopics = _ => never.Task };

        using var cts = new CancellationTokenSource(100);
        // Old behavior: the token was ignored and a hung admin call hung StartAsync forever.
        await Assert.ThrowsExactlyAsync<TaskCanceledException>(
            () => provisioner.ProvisionAsync(admin, cts.Token));
    }

    private sealed class CapturingLogger : ILogger<KafkaLocksTopicProvisioner>
    {
        public ConcurrentQueue<string> Messages { get; } = new();
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Messages.Enqueue(formatter(state, exception));
    }

    private sealed class FakeConnectionProvider : Dorc.Kafka.Client.Connection.IKafkaConnectionProvider
    {
        public ProducerConfig GetProducerConfig() => new();
        public ConsumerConfig GetConsumerConfig(string groupId) => new() { GroupId = groupId };
    }
}
