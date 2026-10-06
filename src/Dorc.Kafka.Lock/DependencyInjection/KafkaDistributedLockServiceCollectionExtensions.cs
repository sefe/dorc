using Dorc.Core.HighAvailability;
using Dorc.Kafka.Client.DependencyInjection;
using Dorc.Kafka.Events.Configuration;
using Dorc.Kafka.Lock.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Dorc.Kafka.Lock.DependencyInjection;

public static class KafkaDistributedLockServiceCollectionExtensions
{
    /// <summary>
    /// Registers the distributed-lock substrate. After  the
    /// substrate-selector flag is gone and Kafka is unconditional: the
    /// <see cref="KafkaLockCoordinator"/> hosted service, the
    /// <see cref="KafkaDistributedLockService"/> implementation of
    /// <see cref="IDistributedLockService"/>, and the lock-topic provisioner
    /// are all registered. Idempotent via marker singleton.
    /// </summary>
    public static IServiceCollection AddDorcKafkaDistributedLock(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        if (services.Any(sd => sd.ServiceType == typeof(DorcKafkaDistributedLockMarker)))
            return services;

        services.AddSingleton<DorcKafkaDistributedLockMarker>();

        services.AddOptions<KafkaLocksOptions>()
            .Bind(configuration.GetSection(KafkaLocksOptions.SectionName))
            .ValidateOnStart();

        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<KafkaLocksOptions>, KafkaLocksOptionsValidator>());

        services.AddOptions<KafkaTopicsOptions>()
            .Bind(configuration.GetSection(KafkaTopicsOptions.SectionName))
            .ValidateOnStart();

        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<KafkaTopicsOptions>, KafkaTopicsOptionsValidator>());

        services.AddDorcKafkaClient(configuration);

        // The provisioner MUST be registered before the coordinator: hosted
        // services start in registration order, and on first boot the
        // coordinator would otherwise subscribe to a not-yet-existing topic.
        services.AddHostedService<KafkaLocksTopicProvisioner>();

        services.AddSingleton<KafkaLockCoordinator>();
        services.AddHostedService(sp => sp.GetRequiredService<KafkaLockCoordinator>());

        services.Replace(ServiceDescriptor.Singleton<IDistributedLockService, KafkaDistributedLockService>());

        return services;
    }

    internal sealed class DorcKafkaDistributedLockMarker { }
}
