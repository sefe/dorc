using Microsoft.Extensions.Options;

namespace Dorc.Kafka.Events.Configuration;

public sealed class KafkaTopicsOptionsValidator : IValidateOptions<KafkaTopicsOptions>
{
    public ValidateOptionsResult Validate(string? name, KafkaTopicsOptions options)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(options.Locks))
            errors.Add($"{KafkaTopicsOptions.SectionName}:{nameof(KafkaTopicsOptions.Locks)} is required.");

        if (string.IsNullOrWhiteSpace(options.RequestsNew))
            errors.Add($"{KafkaTopicsOptions.SectionName}:{nameof(KafkaTopicsOptions.RequestsNew)} is required.");

        if (string.IsNullOrWhiteSpace(options.RequestsStatus))
            errors.Add($"{KafkaTopicsOptions.SectionName}:{nameof(KafkaTopicsOptions.RequestsStatus)} is required.");

        if (string.IsNullOrWhiteSpace(options.ResultsStatus))
            errors.Add($"{KafkaTopicsOptions.SectionName}:{nameof(KafkaTopicsOptions.ResultsStatus)} is required.");

        return errors.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(errors);
    }
}
