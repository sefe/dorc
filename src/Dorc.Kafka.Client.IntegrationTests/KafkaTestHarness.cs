using Confluent.Kafka;
using Confluent.Kafka.Admin;
using Dorc.Kafka.Client.Configuration;
using Dorc.Kafka.Client.Connection;
using Dorc.Kafka.Client.Consumers;
using Dorc.Kafka.Client.Producers;
using Dorc.Kafka.Client.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Dorc.Kafka.Client.IntegrationTests;

internal static class KafkaTestHarness
{
    public static string BootstrapServers
        => Environment.GetEnvironmentVariable("KAFKA_BOOTSTRAP") ?? "localhost:9092";

    public static string NewTopicName(string prefix)
        => $"{prefix}-{Guid.NewGuid():N}";

    public static async Task CreateTopicAsync(string topic, int partitions)
    {
        using var admin = new AdminClientBuilder(new AdminClientConfig { BootstrapServers = BootstrapServers }).Build();
        await admin.CreateTopicsAsync(new[]
        {
            new TopicSpecification
            {
                Name = topic,
                NumPartitions = partitions,
                ReplicationFactor = 1
            }
        });
    }

    public static async Task DeleteTopicAsync(string topic)
    {
        try
        {
            using var admin = new AdminClientBuilder(new AdminClientConfig { BootstrapServers = BootstrapServers }).Build();
            await admin.DeleteTopicsAsync(new[] { topic });
        }
        catch
        {
            // best-effort cleanup
        }
    }

    public static IKafkaProducerBuilder<TKey, TValue> ProducerBuilder<TKey, TValue>(
        KafkaClientOptions? options = null,
        ILogger<KafkaProducerBuilder<TKey, TValue>>? logger = null)
    {
        var provider = Connection(options);
        return new KafkaProducerBuilder<TKey, TValue>(
            provider,
            new DefaultKafkaSerializerFactory(),
            logger ?? NullLogger<KafkaProducerBuilder<TKey, TValue>>.Instance);
    }

    public static IKafkaConsumerBuilder<TKey, TValue> ConsumerBuilder<TKey, TValue>(
        KafkaClientOptions? options = null,
        ILogger<KafkaConsumerBuilder<TKey, TValue>>? logger = null)
    {
        var provider = Connection(options);
        return new KafkaConsumerBuilder<TKey, TValue>(
            provider,
            new DefaultKafkaSerializerFactory(),
            logger ?? NullLogger<KafkaConsumerBuilder<TKey, TValue>>.Instance);
    }

    public static KafkaClientOptions DefaultOptions(string? groupId = null) => new()
    {
        BootstrapServers = BootstrapServers,
        ConsumerGroupId = groupId ?? $"it-{Guid.NewGuid():N}",
        EnableAutoCommit = false,
        SessionTimeoutMs = 10_000,
        HeartbeatIntervalMs = 3_000,
        MaxPollIntervalMs = 60_000
    };

    private static IKafkaConnectionProvider Connection(KafkaClientOptions? options)
        => new KafkaConnectionProvider(Options.Create(options ?? DefaultOptions()));
}
