using Confluent.Kafka;
using Dorc.Kafka.Client.Connection;
using Dorc.Kafka.Client.Serialization;
using Microsoft.Extensions.Logging;

namespace Dorc.Kafka.Client.Producers;

public sealed class KafkaProducerBuilder<TKey, TValue> : IKafkaProducerBuilder<TKey, TValue>
{
    private readonly IKafkaConnectionProvider _connectionProvider;
    private readonly IKafkaSerializerFactory _serializerFactory;
    private readonly ILogger<KafkaProducerBuilder<TKey, TValue>> _logger;

    public KafkaProducerBuilder(
        IKafkaConnectionProvider connectionProvider,
        IKafkaSerializerFactory serializerFactory,
        ILogger<KafkaProducerBuilder<TKey, TValue>> logger)
    {
        _connectionProvider = connectionProvider;
        _serializerFactory = serializerFactory;
        _logger = logger;
    }

    public IProducer<TKey, TValue> Build(string name)
    {
        var config = _connectionProvider.GetProducerConfig();
        var builder = new ProducerBuilder<TKey, TValue>(config)
            .SetErrorHandler((_, error) =>
                _logger.LogError(
                    "Kafka producer '{ProducerName}' error: {Reason} (Code={Code}, Fatal={Fatal})",
                    name, error.Reason, error.Code, error.IsFatal));

        var keySerializer = _serializerFactory.GetKeySerializer<TKey>();
        if (keySerializer is not null) builder.SetKeySerializer(keySerializer);

        var valueSerializer = _serializerFactory.GetValueSerializer<TValue>();
        if (valueSerializer is not null) builder.SetValueSerializer(valueSerializer);

        return builder.Build();
    }
}
