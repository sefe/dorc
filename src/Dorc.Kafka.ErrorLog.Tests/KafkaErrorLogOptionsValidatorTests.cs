using Microsoft.Extensions.Options;

namespace Dorc.Kafka.ErrorLog.Tests;

[TestClass]
public class KafkaErrorLogOptionsValidatorTests
{
    private static readonly KafkaErrorLogOptionsValidator Validator = new();

    private static KafkaErrorLogOptions ValidOptions() => new()
    {
        MaxPayloadBytes = 900_000,
        ProduceTimeoutMs = 5_000,
        PartitionCount = 3,
        ReplicationFactor = 3,
        RetentionMs = 2_592_000_000
    };

    [TestMethod]
    public void Validate_DefaultsShippedInAppSettings_Succeed()
    {
        Assert.IsTrue(Validator.Validate(null, ValidOptions()).Succeeded);
    }

    [DataTestMethod]
    [DataRow(nameof(KafkaErrorLogOptions.MaxPayloadBytes))]
    [DataRow(nameof(KafkaErrorLogOptions.ProduceTimeoutMs))]
    [DataRow(nameof(KafkaErrorLogOptions.PartitionCount))]
    [DataRow(nameof(KafkaErrorLogOptions.ReplicationFactor))]
    [DataRow(nameof(KafkaErrorLogOptions.RetentionMs))]
    public void Validate_NonPositiveValue_FailsNamingTheKey(string property)
    {
        var options = ValidOptions();
        options.GetType().GetProperty(property)!.SetValue(
            options, Convert.ChangeType(0, options.GetType().GetProperty(property)!.PropertyType));

        var result = Validator.Validate(null, options);

        Assert.IsTrue(result.Failed);
        StringAssert.Contains(result.FailureMessage, property,
            "Failure message must name the offending config key for the operator.");
        StringAssert.Contains(result.FailureMessage, KafkaErrorLogOptions.SectionName);
    }

    [TestMethod]
    public void Validate_MultipleInvalidValues_ReportsEveryFailure()
    {
        var options = new KafkaErrorLogOptions
        {
            MaxPayloadBytes = 0,
            ProduceTimeoutMs = -1,
            PartitionCount = 0,
            ReplicationFactor = 0,
            RetentionMs = 0
        };

        var result = Validator.Validate(null, options);

        Assert.IsTrue(result.Failed);
        foreach (var property in new[]
                 {
                     nameof(KafkaErrorLogOptions.MaxPayloadBytes),
                     nameof(KafkaErrorLogOptions.ProduceTimeoutMs),
                     nameof(KafkaErrorLogOptions.PartitionCount),
                     nameof(KafkaErrorLogOptions.ReplicationFactor),
                     nameof(KafkaErrorLogOptions.RetentionMs)
                 })
        {
            StringAssert.Contains(result.FailureMessage, property);
        }
    }
}
