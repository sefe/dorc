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
}

public sealed class DefaultKafkaSerializerFactory : IKafkaSerializerFactory
{
    public ISerializer<T>? GetKeySerializer<T>() => null;

    public ISerializer<T>? GetValueSerializer<T>() => null;

    public IDeserializer<T>? GetKeyDeserializer<T>() => null;

    public IDeserializer<T>? GetValueDeserializer<T>() => null;
}
