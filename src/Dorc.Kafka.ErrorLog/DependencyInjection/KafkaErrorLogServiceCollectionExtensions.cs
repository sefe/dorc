using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Dorc.Kafka.ErrorLog.DependencyInjection;

public static class KafkaErrorLogServiceCollectionExtensions
{
    public static IServiceCollection AddDorcKafkaErrorLog(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        if (services.Any(sd => sd.ServiceType == typeof(DorcKafkaErrorLogMarker)))
            return services;

        services.AddSingleton<DorcKafkaErrorLogMarker>();

        services.AddOptions<KafkaErrorLogOptions>()
            .Bind(configuration.GetSection(KafkaErrorLogOptions.SectionName))
            .ValidateOnStart();

        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<KafkaErrorLogOptions>, KafkaErrorLogOptionsValidator>());

        services.TryAddScoped<IKafkaErrorLogContextFactory, DeploymentContextKafkaErrorLogContextFactory>();
        services.TryAddScoped<IKafkaErrorLog, KafkaErrorLog>();

        return services;
    }

    internal sealed class DorcKafkaErrorLogMarker { }
}
