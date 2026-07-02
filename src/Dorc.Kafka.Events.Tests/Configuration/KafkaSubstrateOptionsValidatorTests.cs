using Dorc.Kafka.Events.Publisher;

namespace Dorc.Kafka.Events.Tests.Configuration;

[TestClass]
public class KafkaSubstrateOptionsValidatorTests
{
    private static readonly KafkaSubstrateOptionsValidator Validator = new();

    [TestMethod]
    public void Validate_ReplicationFactorOne_Succeeds_ForSingleBrokerDev()
    {
        var result = Validator.Validate(null, new KafkaSubstrateOptions { ResultsStatusReplicationFactor = 1 });
        Assert.IsTrue(result.Succeeded);
    }

    [TestMethod]
    public void Validate_ReplicationFactorBelowOne_FailsNamingTheKey()
    {
        var result = Validator.Validate(null, new KafkaSubstrateOptions { ResultsStatusReplicationFactor = 0 });

        Assert.IsTrue(result.Failed);
        StringAssert.Contains(result.FailureMessage, nameof(KafkaSubstrateOptions.ResultsStatusReplicationFactor));
        StringAssert.Contains(result.FailureMessage, KafkaSubstrateOptions.SectionName);
    }
}
