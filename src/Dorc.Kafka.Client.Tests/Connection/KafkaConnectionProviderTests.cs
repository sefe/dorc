using Confluent.Kafka;
using Dorc.Kafka.Client.Configuration;
using Dorc.Kafka.Client.Connection;
using Microsoft.Extensions.Options;

namespace Dorc.Kafka.Client.Tests.Connection;

[TestClass]
public class KafkaConnectionProviderTests
{
    [TestMethod]
    public void ProducerConfig_HasIdempotenceAndAcksAll()
    {
        var provider = Build(new KafkaClientOptions { BootstrapServers = "b:9092" });

        var cfg = provider.GetProducerConfig();

        Assert.AreEqual("b:9092", cfg.BootstrapServers);
        Assert.IsTrue(cfg.EnableIdempotence);
        Assert.AreEqual(Acks.All, cfg.Acks);
    }

    [TestMethod]
    public void ProducerConfig_AppliesSaslSslWhenConfigured()
    {
        var provider = Build(new KafkaClientOptions
        {
            BootstrapServers = "aiven:9092",
            AuthMode = KafkaAuthMode.SaslSsl,
            Sasl = { Mechanism = "SCRAM-SHA-256", Username = "svc", Password = "pw" }
        });

        var cfg = provider.GetProducerConfig();

        Assert.AreEqual(SecurityProtocol.SaslSsl, cfg.SecurityProtocol);
        Assert.AreEqual(SaslMechanism.ScramSha256, cfg.SaslMechanism);
        Assert.AreEqual("svc", cfg.SaslUsername);
        Assert.AreEqual("pw", cfg.SaslPassword);
    }

    [TestMethod]
    public void ProducerConfig_PlaintextLeavesSecurityUnset()
    {
        var provider = Build(new KafkaClientOptions { BootstrapServers = "local:9092" });

        var cfg = provider.GetProducerConfig();

        Assert.IsNull(cfg.SecurityProtocol);
    }

    [TestMethod]
    public void ConsumerConfig_AppliesTimeoutsAndCooperativeSticky()
    {
        var provider = Build(new KafkaClientOptions
        {
            BootstrapServers = "b:9092",
            ConsumerGroupId = "dorc-monitor",
            SessionTimeoutMs = 40_000,
            HeartbeatIntervalMs = 13_000,
            MaxPollIntervalMs = 400_000
        });

        var cfg = provider.GetConsumerConfig();

        Assert.AreEqual("dorc-monitor", cfg.GroupId);
        Assert.IsFalse(cfg.EnableAutoCommit);
        Assert.AreEqual(AutoOffsetReset.Earliest, cfg.AutoOffsetReset);
        Assert.AreEqual(40_000, cfg.SessionTimeoutMs);
        Assert.AreEqual(13_000, cfg.HeartbeatIntervalMs);
        Assert.AreEqual(400_000, cfg.MaxPollIntervalMs);
        Assert.AreEqual(PartitionAssignmentStrategy.CooperativeSticky, cfg.PartitionAssignmentStrategy);
    }

    [TestMethod]
    public void ConsumerConfig_GroupIdOverride_WinsOverConfigured()
    {
        var provider = Build(new KafkaClientOptions
        {
            BootstrapServers = "b:9092",
            ConsumerGroupId = "default-group"
        });

        var cfg = provider.GetConsumerConfig(groupIdOverride: "per-component-group");

        Assert.AreEqual("per-component-group", cfg.GroupId);
    }

    [TestMethod]
    public void ConsumerConfig_NoGroupIdAnywhere_Throws()
    {
        var provider = Build(new KafkaClientOptions { BootstrapServers = "b:9092" });

        var ex = Assert.ThrowsExactly<InvalidOperationException>(() => _ = provider.GetConsumerConfig());
        StringAssert.Contains(ex.Message, nameof(KafkaClientOptions.ConsumerGroupId));
    }

    [TestMethod]
    public void ProducerConfig_UnsupportedSaslMechanism_Throws()
    {
        var provider = Build(new KafkaClientOptions
        {
            BootstrapServers = "b:9092",
            AuthMode = KafkaAuthMode.SaslSsl,
            Sasl = { Mechanism = "BOGUS", Username = "u", Password = "p" }
        });

        var ex = Assert.ThrowsExactly<InvalidOperationException>(() => _ = provider.GetProducerConfig());
        StringAssert.Contains(ex.Message, "Mechanism");
    }

    private static KafkaConnectionProvider Build(KafkaClientOptions options)
        => new(Options.Create(options));
}
