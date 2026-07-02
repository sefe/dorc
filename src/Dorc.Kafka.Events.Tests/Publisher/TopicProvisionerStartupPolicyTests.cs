using Confluent.Kafka;
using Confluent.Kafka.Admin;
using Dorc.Kafka.Client.Configuration;
using Dorc.Kafka.Client.Connection;
using Dorc.Kafka.ErrorLog;
using Dorc.Kafka.Events.Configuration;
using Dorc.Kafka.Events.Publisher;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Dorc.Kafka.Events.Tests.Publisher;

/// <summary>
/// Pins the startup error policy of both Events-side topic provisioners
/// (results/requests + DLQ): provisioning is best-effort, so NOTHING here may
/// escape StartAsync — a broker that is down at boot used to throw a plain
/// KafkaException out of IHost.StartAsync and crash-loop the Monitor Windows
/// service / fail API startup.
/// </summary>
[TestClass]
public class TopicProvisionerStartupPolicyTests
{
    private static readonly KafkaTopicsOptions Topics = new();

    private static (KafkaResultsStatusTopicProvisioner sut, ListLogger<KafkaResultsStatusTopicProvisioner> log) ResultsProvisioner()
    {
        var log = new ListLogger<KafkaResultsStatusTopicProvisioner>();
        return (new KafkaResultsStatusTopicProvisioner(
            new ThrowingConnectionProvider(),
            Options.Create(new KafkaSubstrateOptions()),
            Options.Create(Topics),
            log), log);
    }

    private static (KafkaErrorLogDlqTopicProvisioner sut, ListLogger<KafkaErrorLogDlqTopicProvisioner> log) DlqProvisioner()
    {
        var log = new ListLogger<KafkaErrorLogDlqTopicProvisioner>();
        return (new KafkaErrorLogDlqTopicProvisioner(
            new ThrowingConnectionProvider(),
            Options.Create(new KafkaErrorLogOptions { PartitionCount = 3, ReplicationFactor = 3, RetentionMs = 1000, MaxPayloadBytes = 1, ProduceTimeoutMs = 1 }),
            Options.Create(Topics),
            log), log);
    }

    private static CreateTopicsException TopicError(string topic, ErrorCode code)
        => new(new List<CreateTopicReport> { new() { Topic = topic, Error = new Error(code, "scripted") } });

    private static Metadata MetadataWithPartitions(string topic, int partitionCount)
    {
        var partitions = Enumerable.Range(0, partitionCount)
            .Select(i => new PartitionMetadata(i, 1, new[] { 1 }, new[] { 1 }, new Error(ErrorCode.NoError)))
            .ToList();
        return new Metadata(
            new List<BrokerMetadata> { new(1, "localhost", 9092) },
            new List<TopicMetadata> { new(topic, partitions, new Error(ErrorCode.NoError)) },
            1, "localhost:9092");
    }

    [TestMethod]
    public async Task Results_BrokerUnreachable_DoesNotEscapeStartup()
    {
        var (sut, log) = ResultsProvisioner();
        var admin = new ScriptedAdminClient
        {
            OnCreateTopics = _ => throw new KafkaException(new Error(ErrorCode.Local_TimedOut, "down"))
        };

        await sut.ProvisionAsync(admin, CancellationToken.None);

        Assert.IsTrue(log.Entries.Any(e => e.Level == LogLevel.Error && e.Message.Contains("Startup continues")),
            "Broker-down must be logged as a skipped provision, not thrown.");
        // All three topics must still be attempted - one unreachable answer
        // must not short-circuit the rest of the provisioning pass.
        Assert.AreEqual(3, admin.CreateRequests.Count);
    }

    [TestMethod]
    public async Task Results_TopicExists_VerifiesPartitionCount_LogsDriftWarning()
    {
        var (sut, log) = ResultsProvisioner();
        var admin = new ScriptedAdminClient
        {
            OnCreateTopics = _ => throw TopicError(Topics.ResultsStatus, ErrorCode.TopicAlreadyExists),
            OnGetMetadata = (topic, _) => MetadataWithPartitions(topic, 6) // expected 12
        };

        await sut.ProvisionAsync(admin, CancellationToken.None);

        Assert.IsTrue(log.Entries.Any(e => e.Level == LogLevel.Warning && e.Message.Contains("DIFFERENT partition count")),
            $"Topology drift must be surfaced; got: {string.Join(" | ", log.Entries.Select(e => e.Message))}");
    }

    [TestMethod]
    public async Task Results_TopicExists_MetadataFetchFails_LogsAndContinues()
    {
        var (sut, log) = ResultsProvisioner();
        var admin = new ScriptedAdminClient
        {
            OnCreateTopics = _ => throw TopicError(Topics.ResultsStatus, ErrorCode.TopicAlreadyExists),
            OnGetMetadata = (_, _) => throw new KafkaException(new Error(ErrorCode.Local_Transport, "gone"))
        };

        await sut.ProvisionAsync(admin, CancellationToken.None);

        Assert.IsTrue(log.Entries.Any(e => e.Level == LogLevel.Warning && e.Message.Contains("verification skipped")));
    }

    [TestMethod]
    public async Task Results_AclDenied_LogsErrorAndContinues()
    {
        var (sut, log) = ResultsProvisioner();
        var admin = new ScriptedAdminClient
        {
            OnCreateTopics = _ => throw TopicError(Topics.ResultsStatus, ErrorCode.TopicAuthorizationFailed)
        };

        await sut.ProvisionAsync(admin, CancellationToken.None);

        Assert.IsTrue(log.Entries.Any(e => e.Level == LogLevel.Error && e.Message.Contains("ACL")));
        Assert.AreEqual(3, admin.CreateRequests.Count);
    }

    [TestMethod]
    public async Task Results_SingleBrokerRfRejection_IsWarningNotError()
    {
        var (sut, log) = ResultsProvisioner();
        var admin = new ScriptedAdminClient
        {
            OnCreateTopics = _ => throw TopicError(Topics.ResultsStatus, ErrorCode.InvalidReplicationFactor)
        };

        await sut.ProvisionAsync(admin, CancellationToken.None);

        Assert.IsTrue(log.Entries.Any(e => e.Level == LogLevel.Warning && e.Message.Contains("dev-style")));
        Assert.IsFalse(log.Entries.Any(e => e.Level == LogLevel.Error),
            "Expected single-broker dev noise must not page operators at ERROR.");
    }

    [TestMethod]
    public async Task Dlq_BrokerUnreachable_DoesNotEscapeStartup()
    {
        var (sut, log) = DlqProvisioner();
        var admin = new ScriptedAdminClient
        {
            OnCreateTopics = _ => throw new KafkaException(new Error(ErrorCode.Local_TimedOut, "down"))
        };

        await sut.ProvisionAsync(admin, CancellationToken.None);

        Assert.IsTrue(log.Entries.Any(e => e.Level == LogLevel.Error && e.Message.Contains("Startup continues")));
    }

    [TestMethod]
    public async Task Dlq_CreateSucceeds_RequestsSpecCarriesRetentionAndMinIsr()
    {
        var (sut, _) = DlqProvisioner();
        var admin = new ScriptedAdminClient();

        await sut.ProvisionAsync(admin, CancellationToken.None);

        var spec = admin.CreateRequests.Single();
        Assert.AreEqual(Topics.RequestsNewDlq, spec.Name);
        Assert.AreEqual(3, spec.NumPartitions);
        Assert.AreEqual("1000", spec.Configs["retention.ms"]);
        Assert.AreEqual("2", spec.Configs["min.insync.replicas"]);
    }

    [TestMethod]
    public async Task Dlq_TopicExists_MatchingPartitionCount_LogsInformationOnly()
    {
        var (sut, log) = DlqProvisioner();
        var admin = new ScriptedAdminClient
        {
            OnCreateTopics = _ => throw TopicError(Topics.RequestsNewDlq, ErrorCode.TopicAlreadyExists),
            OnGetMetadata = (topic, _) => MetadataWithPartitions(topic, 3)
        };

        await sut.ProvisionAsync(admin, CancellationToken.None);

        Assert.IsTrue(log.Entries.Any(e => e.Level == LogLevel.Information && e.Message.Contains("already present")));
        Assert.IsFalse(log.Entries.Any(e => e.Level >= LogLevel.Warning));
    }

    /// <summary>
    /// ProvisionAsync receives a ready admin client; touching the connection
    /// provider from inside the seam means broker coupling leaked back in.
    /// </summary>
    private sealed class ThrowingConnectionProvider : IKafkaConnectionProvider
    {
        public ProducerConfig GetProducerConfig() => throw new AssertFailedException("ProvisionAsync must not touch the connection provider.");
        public ConsumerConfig GetConsumerConfig(string groupId) => throw new AssertFailedException("ProvisionAsync must not touch the connection provider.");
    }

    private sealed class ListLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = new();
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Entries.Add((logLevel, formatter(state, exception)));
    }
}
