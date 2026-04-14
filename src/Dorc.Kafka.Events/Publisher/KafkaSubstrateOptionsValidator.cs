using Microsoft.Extensions.Options;

namespace Dorc.Kafka.Events.Publisher;

public sealed class KafkaSubstrateOptionsValidator : IValidateOptions<KafkaSubstrateOptions>
{
    public ValidateOptionsResult Validate(string? name, KafkaSubstrateOptions options)
    {
        var errors = new List<string>();

        if (!Enum.IsDefined(typeof(KafkaSubstrateMode), options.ResultsStatus))
            errors.Add($"{KafkaSubstrateOptions.SectionName}:{nameof(KafkaSubstrateOptions.ResultsStatus)} must be a defined {nameof(KafkaSubstrateMode)} value.");

        if (!Enum.IsDefined(typeof(KafkaSubstrateMode), options.RequestLifecycle))
            errors.Add($"{KafkaSubstrateOptions.SectionName}:{nameof(KafkaSubstrateOptions.RequestLifecycle)} must be a defined {nameof(KafkaSubstrateMode)} value.");

        if (!Enum.IsDefined(typeof(KafkaSubstrateMode), options.DistributedLock))
            errors.Add($"{KafkaSubstrateOptions.SectionName}:{nameof(KafkaSubstrateOptions.DistributedLock)} must be a defined {nameof(KafkaSubstrateMode)} value.");

        if (options.ResultsStatusReplicationFactor < 1)
            errors.Add($"{KafkaSubstrateOptions.SectionName}:{nameof(KafkaSubstrateOptions.ResultsStatusReplicationFactor)} must be >= 1.");

        return errors.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(errors);
    }
}
