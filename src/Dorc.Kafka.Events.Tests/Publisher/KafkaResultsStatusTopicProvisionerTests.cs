using Confluent.Kafka;
using Dorc.Kafka.Client.Connection;
using Dorc.Kafka.Events.Configuration;
using Dorc.Kafka.Events.Publisher;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Dorc.Kafka.Events.Tests.Publisher;

/// <summary>
/// Pins the per-topic creation spec, in particular the retention override:
/// the two requests topics are consumed with per-replica groups +
/// AutoOffsetReset.Earliest, so a fresh group replays the whole retained
/// topic — they get an explicit 7-day retention.ms; results.status keeps the
/// broker default (real-time signals, Latest reset).
/// </summary>
[TestClass]
public class KafkaResultsStatusTopicProvisionerTests
{
    private static readonly KafkaTopicsOptions Topics = new();

    [TestMethod]
    public void BuildTopicSpecification_RequestsNew_SetsSevenDayRetention()
    {
        var spec = NewProvisioner().BuildTopicSpecification(Topics.RequestsNew);

        Assert.AreEqual("604800000", spec.Configs["retention.ms"]);
        Assert.AreEqual(KafkaResultsStatusTopicProvisioner.RequestsTopicRetentionMs,
            long.Parse(spec.Configs["retention.ms"]));
    }

    [TestMethod]
    public void BuildTopicSpecification_RequestsStatus_SetsSevenDayRetention()
    {
        var spec = NewProvisioner().BuildTopicSpecification(Topics.RequestsStatus);

        Assert.AreEqual("604800000", spec.Configs["retention.ms"]);
    }

    [TestMethod]
    public void BuildTopicSpecification_ResultsStatus_KeepsBrokerDefaultRetention()
    {
        var spec = NewProvisioner().BuildTopicSpecification(Topics.ResultsStatus);

        Assert.IsFalse(spec.Configs.ContainsKey("retention.ms"),
            "results.status must inherit the broker default retention — no override");
    }

    [TestMethod]
    public void BuildTopicSpecification_RetentionFollowsConfiguredRequestsTopicNames()
    {
        // SEFE-style enterprise naming: the override must key off the
        // CONFIGURED names, not the dorc.* defaults.
        var deployed = new KafkaTopicsOptions
        {
            RequestsNew = "tr.dv.gbl.deploy.request.il2.dorc",
            RequestsStatus = "tr.dv.gbl.deploy.requeststatus.il2.dorc",
            ResultsStatus = "tr.dv.gbl.deploy.resultstatus.il2.dorc"
        };
        var sut = NewProvisioner(deployed);

        Assert.IsTrue(sut.BuildTopicSpecification(deployed.RequestsNew).Configs.ContainsKey("retention.ms"));
        Assert.IsTrue(sut.BuildTopicSpecification(deployed.RequestsStatus).Configs.ContainsKey("retention.ms"));
        Assert.IsFalse(sut.BuildTopicSpecification(deployed.ResultsStatus).Configs.ContainsKey("retention.ms"));
    }

    [TestMethod]
    public void BuildTopicSpecification_KeepsPartitionsReplicationAndMinIsr()
    {
        var spec = NewProvisioner().BuildTopicSpecification(Topics.RequestsNew);

        Assert.AreEqual(12, spec.NumPartitions);
        Assert.AreEqual((short)3, spec.ReplicationFactor);
        Assert.AreEqual("2", spec.Configs["min.insync.replicas"]);
    }

    [TestMethod]
    public void BuildTopicSpecification_SingleBrokerDev_MinIsrIsOne()
    {
        var sut = NewProvisioner(substrate: new KafkaSubstrateOptions { ResultsStatusReplicationFactor = 1 });

        var spec = sut.BuildTopicSpecification(Topics.ResultsStatus);

        Assert.AreEqual("1", spec.Configs["min.insync.replicas"]);
    }

    private static KafkaResultsStatusTopicProvisioner NewProvisioner(
        KafkaTopicsOptions? topics = null,
        KafkaSubstrateOptions? substrate = null)
        => new(
            new UnusedConnectionProvider(),
            Options.Create(substrate ?? new KafkaSubstrateOptions()),
            Options.Create(topics ?? new KafkaTopicsOptions()),
            NullLogger<KafkaResultsStatusTopicProvisioner>.Instance);

    /// <summary>
    /// BuildTopicSpecification never touches the connection provider; any
    /// call here means the seam grew unintended broker coupling.
    /// </summary>
    private sealed class UnusedConnectionProvider : IKafkaConnectionProvider
    {
        public ConsumerConfig GetConsumerConfig(string? groupIdOverride = null) => throw new NotSupportedException();
        public ProducerConfig GetProducerConfig() => throw new NotSupportedException();
    }
}
