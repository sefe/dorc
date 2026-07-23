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
        else if (options.ConsumerGroupId.Equals(KafkaLocksOptions.SharedDefaultConsumerGroupId, StringComparison.OrdinalIgnoreCase))
            errors.Add(
                $"{KafkaLocksOptions.SectionName}:{nameof(KafkaLocksOptions.ConsumerGroupId)} is still set to the shared default value '{KafkaLocksOptions.SharedDefaultConsumerGroupId}'. " +
                "Every deployment tier MUST use a distinct, explicitly configured group ID — e.g. 'dorc.monitor.locks.nonprod' for NonProd, " +
                "'dorc.monitor.locks.prod' for Prod. Sharing a group ID across tiers causes lock partitions to be assigned across tiers, " +
                "stalling deployments in whichever tier doesn't own the partition. " +
                "IMPORTANT: ensure the Prod configuration does NOT inherit a '.nonprod' suffixed group ID from a base appsettings file.");

        if (options.AcquireWaitMs <= 0)
            errors.Add($"{KafkaLocksOptions.SectionName}:{nameof(KafkaLocksOptions.AcquireWaitMs)} must be > 0.");

        if (options.LivenessTimeoutMs is <= 0)
            errors.Add($"{KafkaLocksOptions.SectionName}:{nameof(KafkaLocksOptions.LivenessTimeoutMs)} must be > 0 when specified (omit for the default of half the effective session timeout).");

        // Broker default floor is group.min.session.timeout.ms = 6000; below
        // that the group join is rejected and the lock substrate never starts.
        if (options.SessionTimeoutMs is < 6_000)
            errors.Add($"{KafkaLocksOptions.SectionName}:{nameof(KafkaLocksOptions.SessionTimeoutMs)} must be >= 6000 when specified (omit to inherit Kafka:SessionTimeoutMs).");

        return errors.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(errors);
    }
}
