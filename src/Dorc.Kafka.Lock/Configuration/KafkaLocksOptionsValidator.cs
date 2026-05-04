using Microsoft.Extensions.Options;

namespace Dorc.Kafka.Lock.Configuration;

public sealed class KafkaLocksOptionsValidator : IValidateOptions<KafkaLocksOptions>
{
    public ValidateOptionsResult Validate(string? name, KafkaLocksOptions options)
    {
        var errors = new List<string>();

        if (options.PartitionCount < 1)
            errors.Add($"{KafkaLocksOptions.SectionName}:{nameof(KafkaLocksOptions.PartitionCount)} must be >= 1.");

        if (options.ReplicationFactor < 1)
            errors.Add($"{KafkaLocksOptions.SectionName}:{nameof(KafkaLocksOptions.ReplicationFactor)} must be >= 1.");

        if (string.IsNullOrWhiteSpace(options.ConsumerGroupId))
            errors.Add($"{KafkaLocksOptions.SectionName}:{nameof(KafkaLocksOptions.ConsumerGroupId)} is required.");

        if (options.LockWaitDefaultTimeoutMs <= 0)
            errors.Add($"{KafkaLocksOptions.SectionName}:{nameof(KafkaLocksOptions.LockWaitDefaultTimeoutMs)} must be > 0.");

        return errors.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(errors);
    }
}
