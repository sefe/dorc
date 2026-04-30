using Microsoft.Extensions.Options;

namespace Dorc.Kafka.ErrorLog;

public sealed class KafkaErrorLogOptionsValidator : IValidateOptions<KafkaErrorLogOptions>
{
    public ValidateOptionsResult Validate(string? name, KafkaErrorLogOptions options)
    {
        var errors = new List<string>();

        if (options.MaxPayloadBytes <= 0)
            errors.Add($"{KafkaErrorLogOptions.SectionName}:{nameof(KafkaErrorLogOptions.MaxPayloadBytes)} must be > 0.");
        if (options.ProduceTimeoutMs <= 0)
            errors.Add($"{KafkaErrorLogOptions.SectionName}:{nameof(KafkaErrorLogOptions.ProduceTimeoutMs)} must be > 0.");
        if (options.PartitionCount <= 0)
            errors.Add($"{KafkaErrorLogOptions.SectionName}:{nameof(KafkaErrorLogOptions.PartitionCount)} must be > 0.");
        if (options.ReplicationFactor <= 0)
            errors.Add($"{KafkaErrorLogOptions.SectionName}:{nameof(KafkaErrorLogOptions.ReplicationFactor)} must be > 0.");
        if (options.RetentionMs <= 0)
            errors.Add($"{KafkaErrorLogOptions.SectionName}:{nameof(KafkaErrorLogOptions.RetentionMs)} must be > 0.");

        return errors.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(errors);
    }
}
