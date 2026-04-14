using Confluent.SchemaRegistry;
using Dorc.Kafka.Client.Configuration;
using Dorc.Kafka.Client.Serialization;
using Dorc.Kafka.Events.Serialization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Dorc.Kafka.Events.DependencyInjection;

public static class KafkaAvroServiceCollectionExtensions
{
    /// <summary>
    /// Register the Avro-backed serializer factory, overriding the default
    /// (no-op) factory from <c>AddDorcKafkaClient</c>. Idempotent; tolerates
    /// either call order relative to <c>AddDorcKafkaClient</c>.
    /// </summary>
    public static IServiceCollection AddDorcKafkaAvro(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        if (services.Any(sd => sd.ServiceType == typeof(DorcKafkaAvroMarker)))
            return services;

        services.AddSingleton<DorcKafkaAvroMarker>();

        services.AddOptions<KafkaAvroOptions>()
            .Bind(configuration.GetSection(KafkaAvroOptions.SectionName));

        services.TryAddSingleton<ISchemaRegistryClient>(sp =>
        {
            var kafkaOptions = sp.GetRequiredService<IOptions<KafkaClientOptions>>().Value;
            var url = kafkaOptions.SchemaRegistry.Url
                ?? throw new InvalidOperationException(
                    $"{KafkaClientOptions.SectionName}:{nameof(KafkaClientOptions.SchemaRegistry)}:{nameof(KafkaSchemaRegistryOptions.Url)} is required for Avro serialization.");

            var schemaRegistryConfig = new SchemaRegistryConfig { Url = url };
            if (!string.IsNullOrEmpty(kafkaOptions.SchemaRegistry.BasicAuthUsername))
            {
                schemaRegistryConfig.BasicAuthUserInfo =
                    $"{kafkaOptions.SchemaRegistry.BasicAuthUsername}:{kafkaOptions.SchemaRegistry.BasicAuthPassword}";
                schemaRegistryConfig.BasicAuthCredentialsSource = AuthCredentialsSource.UserInfo;
            }
            return new CachedSchemaRegistryClient(schemaRegistryConfig);
        });

        // Replace the default IKafkaSerializerFactory (registered by
        // AddDorcKafkaClient) with the Avro one. RemoveAll + AddSingleton
        // ensures correctness regardless of which extension is called first.
        services.RemoveAll<IKafkaSerializerFactory>();
        services.AddSingleton<IKafkaSerializerFactory, AvroKafkaSerializerFactory>();

        return services;
    }

    internal sealed class DorcKafkaAvroMarker { }
}
