using Confluent.Kafka;
using Dorc.Kafka.Client.Connection;
using Dorc.Kafka.Client.Observability;
using Dorc.Kafka.Client.Serialization;
using Microsoft.Extensions.Logging;

namespace Dorc.Kafka.Client.Consumers;

/// <summary>
/// Builds fully-wired consumers (connection config, rebalance/error/statistics
/// handlers, serializer-factory deserializers) for test harnesses. NOT
/// registered in DI: the production consumers each shape their own
/// ConsumerConfig (group identity, offset semantics) and build their consumer
/// directly; the integration-test harnesses construct this builder themselves
/// (see Dorc.Kafka.Client.IntegrationTests.KafkaTestHarness and
/// Dorc.Kafka.Events.IntegrationTests.AvroKafkaTestHarness).
/// </summary>
public sealed class KafkaConsumerBuilder<TKey, TValue> : IKafkaConsumerBuilder<TKey, TValue>
{
    private readonly IKafkaConnectionProvider _connectionProvider;
    private readonly IKafkaSerializerFactory _serializerFactory;
    private readonly IKafkaConsumerMetrics _metrics;
    private readonly ILogger<KafkaConsumerBuilder<TKey, TValue>> _logger;

    public KafkaConsumerBuilder(
        IKafkaConnectionProvider connectionProvider,
        IKafkaSerializerFactory serializerFactory,
        IKafkaConsumerMetrics metrics,
        ILogger<KafkaConsumerBuilder<TKey, TValue>> logger)
    {
        _connectionProvider = connectionProvider;
        _serializerFactory = serializerFactory;
        _metrics = metrics;
        _logger = logger;
    }

    public IConsumer<TKey, TValue> Build(string name, string groupId)
    {
        var config = _connectionProvider.GetConsumerConfig(groupId);
        // Harness-oriented offset semantics, matching the historical global
        // defaults: manual commit (tests drive Commit explicitly) and
        // Earliest so a consumer subscribing after the produce still sees
        // the records under test.
        config.EnableAutoCommit = false;
        config.AutoOffsetReset = AutoOffsetReset.Earliest;
        var handlers = new KafkaRebalanceHandlers<TKey, TValue>(_logger, name, _metrics);

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
