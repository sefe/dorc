using Dorc.Kafka.Lock.Configuration;

namespace Dorc.Kafka.Lock.Tests;

/// <summary> — options validator fails fast on invalid values.</summary>
[TestClass]
public class KafkaLocksOptionsValidatorTests
{
    private static KafkaLocksOptions Valid() => new()
    {
        PartitionCount = 12,
        ReplicationFactor = 3,
        ConsumerGroupId = "dorc.monitor.locks.test",
        AcquireWaitMs = 5_000
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
    public void Validate_AcquireWaitZero_Fails()
    {
        var opts = Valid();
        opts.AcquireWaitMs = 0;
        var r = new KafkaLocksOptionsValidator().Validate(null, opts);
        Assert.IsTrue(r.Failed);
        StringAssert.Contains(string.Join("; ", r.Failures!), "AcquireWaitMs");
    }

    [TestMethod]
    public void Validate_LivenessTimeoutZero_Fails()
    {
        var opts = Valid();
        opts.LivenessTimeoutMs = 0;
        var r = new KafkaLocksOptionsValidator().Validate(null, opts);
        Assert.IsTrue(r.Failed);
        StringAssert.Contains(string.Join("; ", r.Failures!), "LivenessTimeoutMs");
    }

    [TestMethod]
    public void Validate_LivenessTimeoutUnset_Succeeds()
    {
        var opts = Valid();
        opts.LivenessTimeoutMs = null; // auto: max(session.timeout.ms, 30s)
        var r = new KafkaLocksOptionsValidator().Validate(null, opts);
        Assert.IsTrue(r.Succeeded);
    }

    /// <summary>
    /// The shared default "dorc.monitor.locks" must be rejected to prevent
    /// Prod and NonProd Monitor tiers from fighting over the same consumer-group
    /// partition assignment, which would stall whichever tier doesn't own the
    /// partition. Deleting lines 19-25 of KafkaLocksOptionsValidator must
    /// cause this test to fail — it pins the split-brain guard.
    /// </summary>
    [TestMethod]
    public void Validate_ConsumerGroupIdIsSharedDefault_ExactMatch_Fails()
    {
        var opts = Valid();
        opts.ConsumerGroupId = "dorc.monitor.locks";
        var r = new KafkaLocksOptionsValidator().Validate(null, opts);
        Assert.IsTrue(r.Failed, "Exact shared default must be rejected.");
        StringAssert.Contains(string.Join("; ", r.Failures!), "ConsumerGroupId");
    }

    [TestMethod]
    public void Validate_ConsumerGroupIdIsSharedDefault_CaseInsensitive_Fails()
    {
        var opts = Valid();
        opts.ConsumerGroupId = "DORC.MONITOR.LOCKS";
        var r = new KafkaLocksOptionsValidator().Validate(null, opts);
        Assert.IsTrue(r.Failed, "Case-insensitive match of shared default must be rejected.");
        StringAssert.Contains(string.Join("; ", r.Failures!), "ConsumerGroupId");
    }
}
