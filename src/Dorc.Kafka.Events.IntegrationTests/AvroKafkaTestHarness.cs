using Confluent.Kafka;
using Confluent.Kafka.Admin;
using Confluent.SchemaRegistry;
using Dorc.Kafka.Client.Configuration;
using Dorc.Kafka.Client.Connection;
using Dorc.Kafka.Client.Consumers;
using Dorc.Kafka.Client.Producers;
using Dorc.Kafka.Events.Serialization;
using Dorc.Kafka.Events;
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
        catch (DeleteTopicsException) { /* best-effort: topic may not exist */ }
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
        catch (HttpRequestException) { /* best-effort: registry unreachable / 404 */ }
        catch (TaskCanceledException) { /* best-effort: request timed out */ }
    }

    public static ISchemaRegistryClient BuildRegistry()
        => new CachedSchemaRegistryClient(new SchemaRegistryConfig { Url = SchemaRegistryUrl });

    // Auto-registration is deliberately ON here, unlike the production
    // default (Never): every test produces to a fresh GUID-suffixed subject,
    // so first-produce registration IS the scenario under test (AT2) and the
    // precondition for the others. Production schema changes go through the
    // schema gate; AvroSchemaRegistrationBehaviorTests pins the default.
    public static AvroKafkaSerializerFactory BuildFactory(ISchemaRegistryClient registry)
        => new AvroKafkaSerializerFactory(
            registry,
            Options.Create(new KafkaAvroOptions { AllowAutomaticSchemaRegistration = true }));

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
            connection, factory, new Dorc.Kafka.Client.Observability.NoOpKafkaConsumerMetrics(),
            NullLogger<KafkaConsumerBuilder<string, TValue>>.Instance);
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
