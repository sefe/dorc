using Dorc.Core.HighAvailability;
using Dorc.Kafka.Client.DependencyInjection;
using Dorc.Kafka.Events.Publisher;
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
    /// Registers the S-005b distributed-lock substrate (SPEC-S-005b R-5).
    /// Behaviour is driven by <c>Kafka:Substrate:DistributedLock</c>:
    /// <list type="bullet">
    ///   <item><c>Direct</c> (default): no registration changes — the host retains
    ///     whatever <see cref="IDistributedLockService"/> was registered upstream
    ///     (typically <c>RabbitMqDistributedLockService</c>). The Kafka coordinator
    ///     and topic provisioner are not registered.</item>
    ///   <item><c>Kafka</c>: registers <see cref="KafkaLockCoordinator"/> as a
    ///     hosted-service singleton, <see cref="KafkaDistributedLockService"/> as
    ///     the active <see cref="IDistributedLockService"/> (replacing any prior
    ///     registration), and <see cref="KafkaLocksTopicProvisioner"/> as a hosted
    ///     service.</item>
    /// </list>
    /// Idempotent; the substrate mode is read from the supplied
    /// <paramref name="configuration"/> at registration time.
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

        var mode = ReadDistributedLockMode(configuration);
        if (mode != KafkaSubstrateMode.Kafka)
            return services;

        // Ensure the S-002 client layer (options, connection provider) is
        // present — safe no-op if the host already called it for S-007.
        services.AddDorcKafkaClient(configuration);

        services.AddSingleton<KafkaLockCoordinator>();
        services.AddHostedService(sp => sp.GetRequiredService<KafkaLockCoordinator>());

        // Replace any prior IDistributedLockService registration (e.g. the
        // RabbitMQ singleton from Monitor.Program.cs) so the Kafka impl wins
        // resolution. Kept as a singleton matching the Rabbit impl lifetime.
        services.Replace(ServiceDescriptor.Singleton<IDistributedLockService, KafkaDistributedLockService>());

        services.AddHostedService<KafkaLocksTopicProvisioner>();

        return services;
    }

    private static KafkaSubstrateMode ReadDistributedLockMode(IConfiguration configuration)
    {
        var raw = configuration[$"{KafkaSubstrateOptions.SectionName}:{nameof(KafkaSubstrateOptions.DistributedLock)}"];
        if (string.IsNullOrWhiteSpace(raw)) return KafkaSubstrateMode.Direct;
        if (!Enum.TryParse<KafkaSubstrateMode>(raw, ignoreCase: true, out var parsed)
            || !Enum.IsDefined(typeof(KafkaSubstrateMode), parsed))
        {
            throw new InvalidOperationException(
                $"{KafkaSubstrateOptions.SectionName}:{nameof(KafkaSubstrateOptions.DistributedLock)}"
                + $" has invalid value '{raw}'. Allowed: {string.Join(", ", Enum.GetNames<KafkaSubstrateMode>())}.");
        }
        return parsed;
    }

    internal sealed class DorcKafkaDistributedLockMarker { }
}
