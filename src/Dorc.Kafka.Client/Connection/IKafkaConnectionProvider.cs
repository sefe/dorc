using Confluent.Kafka;

namespace Dorc.Kafka.Client.Connection;

public interface IKafkaConnectionProvider
{
    ProducerConfig GetProducerConfig();

    /// <summary>
    /// Consumer config carrying connection/security/timeout settings for the
    /// given (required) consumer group. Offset semantics (EnableAutoCommit,
    /// AutoOffsetReset) are deliberately NOT set here — they are per-consumer
    /// decisions that every consumer applies explicitly to the returned config.
    /// </summary>
    ConsumerConfig GetConsumerConfig(string groupId);

    /// <summary>
    /// Admin-client config sharing the same bootstrap + security settings as
    /// the producers/consumers, so topic provisioners are single-sourced and
    /// never hand-copy connection fields. Default implementation derives the
    /// config from <see cref="GetProducerConfig"/> so existing implementations
    /// (including test fakes) keep working without change;
    /// <see cref="KafkaConnectionProvider"/> overrides it to build directly
    /// from options, which picks up future security fields automatically.
    /// </summary>
    AdminClientConfig GetAdminConfig()
    {
        var producerConfig = GetProducerConfig();
        return new AdminClientConfig
        {
            BootstrapServers = producerConfig.BootstrapServers,
            SecurityProtocol = producerConfig.SecurityProtocol,
            SaslMechanism = producerConfig.SaslMechanism,
            SaslUsername = producerConfig.SaslUsername,
            SaslPassword = producerConfig.SaslPassword,
            SslCaLocation = producerConfig.SslCaLocation
        };
    }
}
