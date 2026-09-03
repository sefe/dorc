using Confluent.Kafka;

namespace Dorc.Kafka.Client.Producers;

public interface IKafkaProducerBuilder<TKey, TValue>
{
    IProducer<TKey, TValue> Build(string name);
}
