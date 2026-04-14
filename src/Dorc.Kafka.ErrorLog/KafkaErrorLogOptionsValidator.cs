using Microsoft.Extensions.Options;

namespace Dorc.Kafka.ErrorLog;

public sealed class KafkaErrorLogOptionsValidator : IValidateOptions<KafkaErrorLogOptions>
{
    public ValidateOptionsResult Validate(string? name, KafkaErrorLogOptions options)
    {
        var errors = new List<string>();

        if (options.RetentionDays <= 0)
            errors.Add($"{KafkaErrorLogOptions.SectionName}:{nameof(KafkaErrorLogOptions.RetentionDays)} must be > 0.");
        if (options.MaxPayloadBytes <= 0)
            errors.Add($"{KafkaErrorLogOptions.SectionName}:{nameof(KafkaErrorLogOptions.MaxPayloadBytes)} must be > 0.");
        if (options.PurgeBatchSize <= 0)
            errors.Add($"{KafkaErrorLogOptions.SectionName}:{nameof(KafkaErrorLogOptions.PurgeBatchSize)} must be > 0.");
        if (options.QueryMaxRowsCap <= 0)
            errors.Add($"{KafkaErrorLogOptions.SectionName}:{nameof(KafkaErrorLogOptions.QueryMaxRowsCap)} must be > 0.");

        return errors.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(errors);
    }
}
