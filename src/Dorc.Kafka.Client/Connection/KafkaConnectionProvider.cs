using Confluent.Kafka;
using Dorc.Kafka.Client.Configuration;
using Microsoft.Extensions.Options;

namespace Dorc.Kafka.Client.Connection;

public sealed class KafkaConnectionProvider : IKafkaConnectionProvider
{
    private readonly KafkaClientOptions _options;

    public KafkaConnectionProvider(IOptions<KafkaClientOptions> options)
    {
        _options = options.Value;
    }

    public ProducerConfig GetProducerConfig()
    {
        var config = new ProducerConfig
        {
            BootstrapServers = _options.BootstrapServers,
            EnableIdempotence = true,
            Acks = Acks.All
        };
        ApplySecurity(config);
        return config;
    }

    public ConsumerConfig GetConsumerConfig(string? groupIdOverride = null)
    {
        var groupId = groupIdOverride ?? _options.ConsumerGroupId
            ?? throw new InvalidOperationException(
                $"{KafkaClientOptions.SectionName}:{nameof(KafkaClientOptions.ConsumerGroupId)} is required to build a consumer config (no override supplied).");

        var config = new ConsumerConfig
        {
            BootstrapServers = _options.BootstrapServers,
            GroupId = groupId,
            EnableAutoCommit = _options.EnableAutoCommit,
            AutoOffsetReset = Map(_options.AutoOffsetReset),
            SessionTimeoutMs = _options.SessionTimeoutMs,
            HeartbeatIntervalMs = _options.HeartbeatIntervalMs,
            MaxPollIntervalMs = _options.MaxPollIntervalMs,
            PartitionAssignmentStrategy = PartitionAssignmentStrategy.CooperativeSticky
        };
        ApplySecurity(config);
        return config;
    }

    private void ApplySecurity(ClientConfig config)
    {
        if (_options.AuthMode != KafkaAuthMode.SaslSsl) return;

        config.SecurityProtocol = SecurityProtocol.SaslSsl;
        config.SaslMechanism = ParseMechanism(_options.Sasl.Mechanism);
        config.SaslUsername = _options.Sasl.Username;
        config.SaslPassword = _options.Sasl.Password;

        if (!string.IsNullOrWhiteSpace(_options.SslCaLocation))
            config.SslCaLocation = _options.SslCaLocation;
    }

    private static AutoOffsetReset Map(KafkaAutoOffsetReset reset) => reset switch
    {
        KafkaAutoOffsetReset.Earliest => AutoOffsetReset.Earliest,
        KafkaAutoOffsetReset.Latest => AutoOffsetReset.Latest,
        KafkaAutoOffsetReset.Error => AutoOffsetReset.Error,
        _ => AutoOffsetReset.Earliest
    };

    private static SaslMechanism ParseMechanism(string mechanism) => mechanism.ToUpperInvariant() switch
    {
        "SCRAM-SHA-256" => SaslMechanism.ScramSha256,
        "SCRAM-SHA-512" => SaslMechanism.ScramSha512,
        "PLAIN" => SaslMechanism.Plain,
        "GSSAPI" => SaslMechanism.Gssapi,
        "OAUTHBEARER" => SaslMechanism.OAuthBearer,
        _ => throw new InvalidOperationException(
            $"Unsupported {KafkaClientOptions.SectionName}:{nameof(KafkaClientOptions.Sasl)}:{nameof(KafkaSaslOptions.Mechanism)} value '{mechanism}'.")
    };
}
