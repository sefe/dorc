using Microsoft.Extensions.Options;

namespace Dorc.Kafka.Client.Configuration;

public sealed class KafkaClientOptionsValidator : IValidateOptions<KafkaClientOptions>
{
    /// <summary>
    /// Floor for <see cref="KafkaClientOptions.SessionTimeoutMs"/>, matching the
    /// Kafka broker default <c>group.min.session.timeout.ms</c>. Values below
    /// this are rejected by the broker anyway, and would push the lock
    /// coordinator's liveness watchdog into a sub-second firing range (audit F3).
    /// </summary>
    internal const int MinSessionTimeoutMs = 6_000;

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
            else
            {
                // Validate the mechanism value eagerly so a typo (e.g. "SCRAM-SHA-265")
                // is caught at startup rather than throwing InvalidOperationException
                // the first time a producer or consumer is constructed.
                if (!KafkaSaslOptions.SupportedMechanisms.Contains(options.Sasl.Mechanism))
                    errors.Add($"{KafkaClientOptions.SectionName}:{nameof(KafkaClientOptions.Sasl)}:{nameof(KafkaSaslOptions.Mechanism)} value '{options.Sasl.Mechanism}' is not supported. Valid values: {string.Join(", ", KafkaSaslOptions.SupportedMechanisms)}.");
            }
        }

        if (!string.IsNullOrWhiteSpace(options.SchemaRegistry.Url)
            && !Uri.TryCreate(options.SchemaRegistry.Url, UriKind.Absolute, out _))
        {
            errors.Add($"{KafkaClientOptions.SectionName}:{nameof(KafkaClientOptions.SchemaRegistry)}:{nameof(KafkaSchemaRegistryOptions.Url)} must be an absolute URI.");
        }

        if (options.SessionTimeoutMs < MinSessionTimeoutMs)
            // Enforce the Kafka broker's default group.min.session.timeout.ms (6s)
            // as a floor. Beyond matching the broker minimum, this keeps the
            // KafkaLockCoordinator connectivity watchdog out of its degenerate
            // range: ResolveLivenessTimeout derives the watchdog from
            // session.timeout.ms, and a sub-second session timeout would make it
            // fire ~every second, churning lock slots. See audit finding F3.
            errors.Add($"{KafkaClientOptions.SectionName}:{nameof(KafkaClientOptions.SessionTimeoutMs)} must be >= {MinSessionTimeoutMs} (Kafka broker default group.min.session.timeout.ms).");

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
