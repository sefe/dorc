using Microsoft.Extensions.Options;

namespace Dorc.Kafka.Events.Publisher;

public sealed class KafkaSubstrateOptionsValidator : IValidateOptions<KafkaSubstrateOptions>
{
    public ValidateOptionsResult Validate(string? name, KafkaSubstrateOptions options)
    {
        if (options.ResultsStatusReplicationFactor < 1)
            return ValidateOptionsResult.Fail(
                $"{KafkaSubstrateOptions.SectionName}:{nameof(KafkaSubstrateOptions.ResultsStatusReplicationFactor)} must be >= 1.");

        return ValidateOptionsResult.Success;
    }
}
