using Confluent.Kafka;

namespace Dorc.Kafka.Client.Connection;

public interface IKafkaConnectionProvider
{
    ProducerConfig GetProducerConfig();

    ConsumerConfig GetConsumerConfig(string? groupIdOverride = null);
}
