using Dorc.Kafka.Client.Configuration;
using Dorc.Kafka.Client.Connection;
using Dorc.Kafka.Client.Observability;
using Dorc.Kafka.Client.Producers;
using Dorc.Kafka.Client.Serialization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Dorc.Kafka.Client.DependencyInjection;

public static class KafkaClientServiceCollectionExtensions
{
    public static IServiceCollection AddDorcKafkaClient(this IServiceCollection services, IConfiguration configuration)
    {
        if (services.Any(sd => sd.ServiceType == typeof(DorcKafkaClientMarker)))
            return services;

        services.AddSingleton<DorcKafkaClientMarker>();

        services.AddOptions<KafkaClientOptions>()
            .Bind(configuration.GetSection(KafkaClientOptions.SectionName))
            .ValidateOnStart();

        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<KafkaClientOptions>, KafkaClientOptionsValidator>());

        services.TryAddSingleton<IKafkaConnectionProvider, KafkaConnectionProvider>();
        services.TryAddSingleton<IKafkaSerializerFactory, DefaultKafkaSerializerFactory>();
        // Real metrics sink that publishes a `Dorc.Kafka.Consumer` Meter.
        // Hosts that want OTLP/Prometheus export wire AddMeter(KafkaConsumerMetrics.MeterName)
        // into their OpenTelemetry pipeline; the meter is dormant otherwise.
        services.TryAddSingleton<IKafkaConsumerMetrics, KafkaConsumerMetrics>();
        services.TryAddSingleton(typeof(IKafkaProducerBuilder<,>), typeof(KafkaProducerBuilder<,>));

        return services;
    }

    internal sealed class DorcKafkaClientMarker { }
}
