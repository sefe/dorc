using Dorc.Kafka.Client.Configuration;
using Dorc.Kafka.Client.Connection;
using Dorc.Kafka.Client.Consumers;
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
        services.TryAddSingleton(typeof(IKafkaProducerBuilder<,>), typeof(KafkaProducerBuilder<,>));
        services.TryAddSingleton(typeof(IKafkaConsumerBuilder<,>), typeof(KafkaConsumerBuilder<,>));

        return services;
    }

    internal sealed class DorcKafkaClientMarker { }
}
