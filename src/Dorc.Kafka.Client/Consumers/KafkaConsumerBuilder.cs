using Confluent.Kafka;
using Dorc.Kafka.Client.Connection;
using Dorc.Kafka.Client.Serialization;
using Microsoft.Extensions.Logging;

namespace Dorc.Kafka.Client.Consumers;

public sealed class KafkaConsumerBuilder<TKey, TValue> : IKafkaConsumerBuilder<TKey, TValue>
{
    private readonly IKafkaConnectionProvider _connectionProvider;
    private readonly IKafkaSerializerFactory _serializerFactory;
    private readonly ILogger<KafkaConsumerBuilder<TKey, TValue>> _logger;

    public KafkaConsumerBuilder(
        IKafkaConnectionProvider connectionProvider,
        IKafkaSerializerFactory serializerFactory,
        ILogger<KafkaConsumerBuilder<TKey, TValue>> logger)
    {
        _connectionProvider = connectionProvider;
        _serializerFactory = serializerFactory;
        _logger = logger;
    }

    public IConsumer<TKey, TValue> Build(string name, string? groupIdOverride = null)
    {
        var config = _connectionProvider.GetConsumerConfig(groupIdOverride);
        var handlers = new KafkaRebalanceHandlers<TKey, TValue>(_logger, name);

        var builder = new ConsumerBuilder<TKey, TValue>(config)
            .SetErrorHandler(handlers.OnError)
            .SetStatisticsHandler(handlers.OnStatistics)
            .SetPartitionsAssignedHandler(handlers.OnPartitionsAssigned)
            .SetPartitionsRevokedHandler(handlers.OnPartitionsRevoked)
            .SetPartitionsLostHandler(handlers.OnPartitionsLost);

        var keyDeserializer = _serializerFactory.GetKeyDeserializer<TKey>();
        if (keyDeserializer is not null) builder.SetKeyDeserializer(keyDeserializer);

        var valueDeserializer = _serializerFactory.GetValueDeserializer<TValue>();
        if (valueDeserializer is not null) builder.SetValueDeserializer(valueDeserializer);

        return builder.Build();
    }
}
