using Confluent.Kafka;

namespace Dorc.Kafka.Client.Connection;

public interface IKafkaConnectionProvider
{
    ProducerConfig GetProducerConfig();

    ConsumerConfig GetConsumerConfig(string? groupIdOverride = null);

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
