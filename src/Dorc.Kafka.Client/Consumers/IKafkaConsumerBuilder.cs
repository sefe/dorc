using Confluent.Kafka;

namespace Dorc.Kafka.Client.Consumers;

public interface IKafkaConsumerBuilder<TKey, TValue>
{
    IConsumer<TKey, TValue> Build(string name, string? groupIdOverride = null);
}
