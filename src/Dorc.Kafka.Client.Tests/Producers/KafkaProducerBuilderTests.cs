using Confluent.Kafka;
using Dorc.Kafka.Client.Configuration;
using Dorc.Kafka.Client.Connection;
using Dorc.Kafka.Client.Producers;
using Dorc.Kafka.Client.Serialization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Dorc.Kafka.Client.Tests.Producers;

[TestClass]
public class KafkaProducerBuilderTests
{
    [TestMethod]
    public void Build_StringKeyByteValue_SucceedsWithDefaultFactory()
    {
        var producer = BuildProducer<string, byte[]>();

        Assert.IsNotNull(producer);
        producer.Dispose();
    }

    [TestMethod]
    public void Build_NonStringKeyType_AlsoSucceeds_ConfirmingGenericity()
    {
        // R-2: builder must not hard-code string keys.
        var producer = BuildProducer<long, byte[]>();

        Assert.IsNotNull(producer);
        producer.Dispose();
    }

    [TestMethod]
    public void Build_HonoursCustomSerializerFromFactory()
    {
        var custom = new CountingSerializer();
        var factory = new TestFactory { KeySerializer = custom };

        using var producer = BuildProducer<string, byte[]>(factory);

        // Producing to a non-existent broker: we don't await the delivery — we only prove the
        // custom serializer was wired. Kafka serialises key before enqueueing.
        _ = producer.ProduceAsync("topic-x", new Message<string, byte[]> { Key = "k1", Value = new byte[] { 1 } });

        Assert.IsTrue(custom.Invoked, "Custom key serializer was not invoked by the produced pipeline.");
    }

    private static IProducer<TKey, TValue> BuildProducer<TKey, TValue>(IKafkaSerializerFactory? factory = null)
    {
        var options = Options.Create(new KafkaClientOptions
        {
            BootstrapServers = "127.0.0.1:1"  // deliberately unreachable; Build() is offline
        });
        var connection = new KafkaConnectionProvider(options);
        var builder = new KafkaProducerBuilder<TKey, TValue>(
            connection,
            factory ?? new DefaultKafkaSerializerFactory(),
            NullLogger<KafkaProducerBuilder<TKey, TValue>>.Instance);

        return builder.Build("test-producer");
    }

    private sealed class TestFactory : IKafkaSerializerFactory
    {
        public ISerializer<string>? KeySerializer { get; set; }

        public ISerializer<T>? GetKeySerializer<T>()
            => typeof(T) == typeof(string) ? (ISerializer<T>?)(object?)KeySerializer : null;

        public ISerializer<T>? GetValueSerializer<T>() => null;

        public IDeserializer<T>? GetKeyDeserializer<T>() => null;

        public IDeserializer<T>? GetValueDeserializer<T>() => null;
    }

    private sealed class CountingSerializer : ISerializer<string>
    {
        public bool Invoked { get; private set; }

        public byte[] Serialize(string data, SerializationContext context)
        {
            Invoked = true;
            return System.Text.Encoding.UTF8.GetBytes(data ?? string.Empty);
        }
    }
}
