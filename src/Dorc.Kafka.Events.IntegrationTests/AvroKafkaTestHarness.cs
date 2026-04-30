using Confluent.Kafka;
using Confluent.Kafka.Admin;
using Confluent.SchemaRegistry;
using Dorc.Kafka.Client.Configuration;
using Dorc.Kafka.Client.Connection;
using Dorc.Kafka.Client.Consumers;
using Dorc.Kafka.Client.Producers;
using Dorc.Kafka.Events.Serialization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Dorc.Kafka.Events.IntegrationTests;

internal static class AvroKafkaTestHarness
{
    public static string BootstrapServers
        => Environment.GetEnvironmentVariable("KAFKA_BOOTSTRAP") ?? "localhost:9092";

    public static string SchemaRegistryUrl
        => Environment.GetEnvironmentVariable("KAFKA_SCHEMA_REGISTRY") ?? "http://localhost:8081";

    public static string NewTopic(string prefix) => $"{prefix}-{Guid.NewGuid():N}";

    public static async Task CreateTopicAsync(string topic, int partitions = 1)
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
        catch { /* best-effort */ }
    }

    public static async Task DeleteSubjectAsync(string subject)
    {
        try
        {
            using var http = new HttpClient { BaseAddress = new Uri(SchemaRegistryUrl) };
            // Soft delete + hard delete so a rerun starts clean.
            await http.DeleteAsync($"/subjects/{subject}");
            await http.DeleteAsync($"/subjects/{subject}?permanent=true");
        }
        catch { /* best-effort */ }
    }

    public static ISchemaRegistryClient BuildRegistry()
        => new CachedSchemaRegistryClient(new SchemaRegistryConfig { Url = SchemaRegistryUrl });

    public static AvroKafkaSerializerFactory BuildFactory(ISchemaRegistryClient registry)
        => new AvroKafkaSerializerFactory(registry);

    public static HttpClient BuildRegistryHttpClient()
        => new HttpClient { BaseAddress = new Uri(SchemaRegistryUrl) };

    public static KafkaProducerBuilder<string, TValue> ProducerBuilder<TValue>(AvroKafkaSerializerFactory factory)
    {
        var connection = BuildConnection();
        return new KafkaProducerBuilder<string, TValue>(
            connection, factory, NullLogger<KafkaProducerBuilder<string, TValue>>.Instance);
    }

    public static KafkaConsumerBuilder<string, TValue> ConsumerBuilder<TValue>(
        AvroKafkaSerializerFactory factory,
        string groupId)
    {
        var connection = BuildConnection(groupId);
        return new KafkaConsumerBuilder<string, TValue>(
            connection, factory, NullLogger<KafkaConsumerBuilder<string, TValue>>.Instance);
    }

    private static IKafkaConnectionProvider BuildConnection(string? groupId = null)
    {
        var options = new KafkaClientOptions
        {
            BootstrapServers = BootstrapServers,
            ConsumerGroupId = groupId ?? $"it-{Guid.NewGuid():N}",
            SchemaRegistry = { Url = SchemaRegistryUrl },
            SessionTimeoutMs = 10_000,
            HeartbeatIntervalMs = 3_000,
            MaxPollIntervalMs = 60_000
        };
        return new KafkaConnectionProvider(Options.Create(options));
    }
}
