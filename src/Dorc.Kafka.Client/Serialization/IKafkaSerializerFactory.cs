using Confluent.Kafka;

namespace Dorc.Kafka.Client.Serialization;

/// <summary>
/// Extension point for message-type-aware serialisation. S-002 ships a
/// no-op default that lets Confluent.Kafka's built-in primitive serialisers
/// take over (string, int, byte[], etc). S-003 replaces this with an
/// Avro/schema-registry-backed factory via DI override.
/// </summary>
public interface IKafkaSerializerFactory
{
    ISerializer<T>? GetKeySerializer<T>();

    ISerializer<T>? GetValueSerializer<T>();

    IDeserializer<T>? GetKeyDeserializer<T>();

    IDeserializer<T>? GetValueDeserializer<T>();

    /// <summary>
    /// Pre-resolve the (topic, type) → serializer mapping for the supplied
    /// topics so the first publish doesn't block on a schema-registry
    /// round-trip. Implementations that don't talk to a registry are no-ops.
    /// </summary>
    void WarmupSerializer<T>(IEnumerable<string> topics) { }

    /// <summary>
    /// Pre-resolve the (topic, type) → deserializer mapping for the supplied
    /// topics so the first <c>Consume()</c> call doesn't block on a schema-
    /// registry round-trip (which would risk exceeding <c>max.poll.interval.ms</c>
    /// and fencing the consumer). Implementations that don't talk to a
    /// registry are no-ops.
    /// </summary>
    void WarmupDeserializer<T>(IEnumerable<string> topics) { }
}

public sealed class DefaultKafkaSerializerFactory : IKafkaSerializerFactory
{
    public ISerializer<T>? GetKeySerializer<T>() => null;

    public ISerializer<T>? GetValueSerializer<T>() => null;

    public IDeserializer<T>? GetKeyDeserializer<T>() => null;

    public IDeserializer<T>? GetValueDeserializer<T>() => null;
}
