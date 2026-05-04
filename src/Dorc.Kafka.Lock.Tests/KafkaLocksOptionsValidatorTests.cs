using Dorc.Kafka.Lock.Configuration;

namespace Dorc.Kafka.Lock.Tests;

/// <summary>AT-9 — options validator fails fast on invalid values.</summary>
[TestClass]
public class KafkaLocksOptionsValidatorTests
{
    private static KafkaLocksOptions Valid() => new()
    {
        PartitionCount = 12,
        ReplicationFactor = 3,
        ConsumerGroupId = "dorc.monitor.locks",
        LockWaitDefaultTimeoutMs = 30_000
    };

    [TestMethod]
    public void Validate_DefaultValid_Succeeds()
    {
        var r = new KafkaLocksOptionsValidator().Validate(null, Valid());
        Assert.IsTrue(r.Succeeded, string.Join("; ", r.Failures ?? Array.Empty<string>()));
    }

    [TestMethod]
    public void Validate_PartitionCountZero_Fails()
    {
        var opts = Valid();
        opts.PartitionCount = 0;
        var r = new KafkaLocksOptionsValidator().Validate(null, opts);
        Assert.IsTrue(r.Failed);
        StringAssert.Contains(string.Join("; ", r.Failures!), "PartitionCount");
    }

    [TestMethod]
    public void Validate_ReplicationFactorZero_Fails()
    {
        var opts = Valid();
        opts.ReplicationFactor = 0;
        var r = new KafkaLocksOptionsValidator().Validate(null, opts);
        Assert.IsTrue(r.Failed);
        StringAssert.Contains(string.Join("; ", r.Failures!), "ReplicationFactor");
    }

    [TestMethod]
    public void Validate_ConsumerGroupIdEmpty_Fails()
    {
        var opts = Valid();
        opts.ConsumerGroupId = " ";
        var r = new KafkaLocksOptionsValidator().Validate(null, opts);
        Assert.IsTrue(r.Failed);
        StringAssert.Contains(string.Join("; ", r.Failures!), "ConsumerGroupId");
    }

    [TestMethod]
    public void Validate_WaitTimeoutZero_Fails()
    {
        var opts = Valid();
        opts.LockWaitDefaultTimeoutMs = 0;
        var r = new KafkaLocksOptionsValidator().Validate(null, opts);
        Assert.IsTrue(r.Failed);
        StringAssert.Contains(string.Join("; ", r.Failures!), "LockWaitDefaultTimeoutMs");
    }
}
