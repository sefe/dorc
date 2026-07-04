using Confluent.Kafka;
using Confluent.Kafka.Admin;
using Dorc.Kafka.Client.Configuration;
using Dorc.Kafka.Client.Connection;
using Dorc.Kafka.Client.Provisioning;
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
/// service / fail API startup. Also pins the batching contract: each
/// provisioner makes ONE CreateTopicsAsync call carrying all its specs.
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

    /// <summary>
    /// Broker-faithful CreateTopicsException: a batched create fails as one
    /// exception whose Results carry a per-topic report for EVERY spec in the
    /// batch, so the scripted error must cover the whole batch too.
    /// </summary>
    private static CreateTopicsException BatchError(IReadOnlyList<TopicSpecification> batch, ErrorCode code)
        => new(batch
            .Select(s => new CreateTopicReport { Topic = s.Name, Error = new Error(code, "scripted") })
            .ToList());

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
        // All three topics must travel in ONE batched admin call — a broker
        // round-trip per topic triples the startup stall when the broker is slow.
        Assert.AreEqual(1, admin.CreateRequests.Count);
        CollectionAssert.AreEquivalent(
            new[] { Topics.ResultsStatus, Topics.RequestsNew, Topics.RequestsStatus },
            admin.CreateRequests.Single().Select(s => s.Name).ToArray());
    }

    [TestMethod]
    public async Task Results_TopicExists_VerifiesPartitionCount_LogsDriftWarning()
    {
        var (sut, log) = ResultsProvisioner();
        var admin = new ScriptedAdminClient
        {
            OnCreateTopics = batch => throw BatchError(batch, ErrorCode.TopicAlreadyExists),
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
            OnCreateTopics = batch => throw BatchError(batch, ErrorCode.TopicAlreadyExists),
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
            OnCreateTopics = batch => throw BatchError(batch, ErrorCode.TopicAuthorizationFailed)
        };

        await sut.ProvisionAsync(admin, CancellationToken.None);

        Assert.IsTrue(log.Entries.Any(e => e.Level == LogLevel.Error && e.Message.Contains("ACL")));
        Assert.AreEqual(1, admin.CreateRequests.Count);
        Assert.AreEqual(3, admin.CreateRequests.Single().Count);
    }

    [TestMethod]
    public async Task Results_SingleBrokerRfRejection_IsWarningNotError()
    {
        var (sut, log) = ResultsProvisioner();
        var admin = new ScriptedAdminClient
        {
            OnCreateTopics = batch => throw BatchError(batch, ErrorCode.InvalidReplicationFactor)
        };

        await sut.ProvisionAsync(admin, CancellationToken.None);

        Assert.IsTrue(log.Entries.Any(e => e.Level == LogLevel.Warning && e.Message.Contains("dev-style")));
        Assert.IsFalse(log.Entries.Any(e => e.Level == LogLevel.Error),
            "Expected single-broker dev noise must not page operators at ERROR.");
    }

    [TestMethod]
    public async Task Results_MixedBatchOutcome_TriagesEachTopicIndependently()
    {
        // The broker answers a batched create with per-topic results; one
        // topic already existing must not mask another's creation.
        var (sut, log) = ResultsProvisioner();
        var admin = new ScriptedAdminClient
        {
            OnCreateTopics = batch => throw new CreateTopicsException(batch
                .Select(s => new CreateTopicReport
                {
                    Topic = s.Name,
                    Error = new Error(s.Name == Topics.ResultsStatus ? ErrorCode.TopicAlreadyExists : ErrorCode.NoError, "scripted")
                })
                .ToList()),
            OnGetMetadata = (topic, _) => MetadataWithPartitions(topic, 12)
        };

        await sut.ProvisionAsync(admin, CancellationToken.None);

        Assert.IsTrue(log.Entries.Any(e => e.Level == LogLevel.Information && e.Message.Contains("already present")),
            "The existing topic must be verified, not treated as created.");
        Assert.AreEqual(2, log.Entries.Count(e => e.Level == LogLevel.Information && e.Message.Contains("topic created")),
            "The two NoError results must each be logged as created.");
        Assert.IsFalse(log.Entries.Any(e => e.Level >= LogLevel.Warning));
    }

    /// <summary>
    /// CreateTopicsAsync takes no CancellationToken, so the shared core bounds
    /// it with WaitAsync(timeout, ct). The resulting TimeoutException must be
    /// swallowed like broker-unreachable — an admin call that hangs (dead
    /// broker, black-holed TCP) must neither hang StartAsync nor crash it.
    /// </summary>
    [TestMethod]
    [Timeout(10_000)]
    public async Task ProvisionCore_AdminCallNeverCompletes_TimesOutLogsAndReturns()
    {
        var log = new ListLogger<KafkaResultsStatusTopicProvisioner>();
        var never = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var admin = new ScriptedAdminClient { OnCreateTopics = _ => never.Task };
        var specs = new[] { new TopicSpecification { Name = "dorc.hung", NumPartitions = 1, ReplicationFactor = 1 } };

        await IdempotentTopicProvisioner.ProvisionAsync(
            admin, specs, "Kafka", log, CancellationToken.None,
            adminCallTimeout: TimeSpan.FromMilliseconds(50));

        Assert.IsTrue(log.Entries.Any(e => e.Level == LogLevel.Error && e.Message.Contains("Startup continues")),
            $"A timed-out admin call must be logged as a skipped provision; got: {string.Join(" | ", log.Entries.Select(e => e.Message))}");
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

        var spec = admin.CreateRequests.Single().Single();
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
            OnCreateTopics = batch => throw BatchError(batch, ErrorCode.TopicAlreadyExists),
            OnGetMetadata = (topic, _) => MetadataWithPartitions(topic, 3)
        };

        await sut.ProvisionAsync(admin, CancellationToken.None);

        Assert.IsTrue(log.Entries.Any(e => e.Level == LogLevel.Information && e.Message.Contains("already present")));
        Assert.IsFalse(log.Entries.Any(e => e.Level >= LogLevel.Warning));
    }

    /// <summary>
    /// ProvisionAsync receives a ready admin client; touching the connection
    /// provider from inside the seam means broker coupling leaked back in.
    /// (GetAdminConfig's default implementation delegates to GetProducerConfig,
    /// so it throws too.)
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
