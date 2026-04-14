using Microsoft.Extensions.Options;

namespace Dorc.Kafka.Client.Configuration;

public sealed class KafkaClientOptionsValidator : IValidateOptions<KafkaClientOptions>
{
    public ValidateOptionsResult Validate(string? name, KafkaClientOptions options)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(options.BootstrapServers))
            errors.Add($"{KafkaClientOptions.SectionName}:{nameof(KafkaClientOptions.BootstrapServers)} is required.");

        if (options.AuthMode == KafkaAuthMode.SaslSsl)
        {
            if (string.IsNullOrWhiteSpace(options.Sasl.Username))
                errors.Add($"{KafkaClientOptions.SectionName}:{nameof(KafkaClientOptions.Sasl)}:{nameof(KafkaSaslOptions.Username)} is required when AuthMode=SaslSsl.");
            if (string.IsNullOrWhiteSpace(options.Sasl.Password))
                errors.Add($"{KafkaClientOptions.SectionName}:{nameof(KafkaClientOptions.Sasl)}:{nameof(KafkaSaslOptions.Password)} is required when AuthMode=SaslSsl.");
            if (string.IsNullOrWhiteSpace(options.Sasl.Mechanism))
                errors.Add($"{KafkaClientOptions.SectionName}:{nameof(KafkaClientOptions.Sasl)}:{nameof(KafkaSaslOptions.Mechanism)} is required when AuthMode=SaslSsl.");
        }

        if (!string.IsNullOrWhiteSpace(options.SchemaRegistry.Url)
            && !Uri.TryCreate(options.SchemaRegistry.Url, UriKind.Absolute, out _))
        {
            errors.Add($"{KafkaClientOptions.SectionName}:{nameof(KafkaClientOptions.SchemaRegistry)}:{nameof(KafkaSchemaRegistryOptions.Url)} must be an absolute URI.");
        }

        if (options.SessionTimeoutMs <= 0)
            errors.Add($"{KafkaClientOptions.SectionName}:{nameof(KafkaClientOptions.SessionTimeoutMs)} must be > 0.");

        if (options.HeartbeatIntervalMs <= 0)
            errors.Add($"{KafkaClientOptions.SectionName}:{nameof(KafkaClientOptions.HeartbeatIntervalMs)} must be > 0.");

        if (options.HeartbeatIntervalMs >= options.SessionTimeoutMs)
            errors.Add($"{KafkaClientOptions.SectionName}:{nameof(KafkaClientOptions.HeartbeatIntervalMs)} must be < {nameof(KafkaClientOptions.SessionTimeoutMs)}.");

        if (options.MaxPollIntervalMs <= options.SessionTimeoutMs)
            errors.Add($"{KafkaClientOptions.SectionName}:{nameof(KafkaClientOptions.MaxPollIntervalMs)} must be > {nameof(KafkaClientOptions.SessionTimeoutMs)}.");

        return errors.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(errors);
    }
}
