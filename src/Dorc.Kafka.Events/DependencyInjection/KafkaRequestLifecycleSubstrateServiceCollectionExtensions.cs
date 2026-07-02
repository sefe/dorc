using Dorc.Core.Events;
using Dorc.Kafka.Events.Configuration;
using Dorc.Kafka.Events.Publisher;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Dorc.Kafka.Events.DependencyInjection;

public static class KafkaRequestLifecycleSubstrateServiceCollectionExtensions
{
    /// <summary>
    /// DI extension. After  the substrate-selector flag
    /// is gone and Kafka is unconditional: registers the latching
    /// <see cref="RequestPollSignal"/>, the
    /// <see cref="PollSignalRequestEventHandler"/>, and the
    /// <see cref="DeploymentRequestsKafkaConsumer"/> as a hosted service.
    /// Does NOT re-register <c>IDeploymentEventsPublisher</c>
    /// <c>IFallbackDeploymentEventPublisher</c> — those remain owned by
    /// <see cref="KafkaResultsStatusSubstrateServiceCollectionExtensions"/>.
    /// Idempotent via marker singleton.
    /// </summary>
    public static IServiceCollection AddDorcKafkaRequestLifecycleSubstrate(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        if (services.Any(sd => sd.ServiceType == typeof(DorcKafkaRequestLifecycleMarker)))
            return services;

        services.AddSingleton<DorcKafkaRequestLifecycleMarker>();

        // KafkaTopicsOptions registration is idempotent — the consumer
        // resolves IOptions<KafkaTopicsOptions> in its constructor, so this
        // extension must register it independently of results-status
        // extension. TryAddEnumerable is idempotent by implementation type
        // (ServiceCollection dedup on (ServiceType, ImplementationType)) so
        // the validator registers exactly once even when both substrate
        // entry-points run.
        services.AddOptions<KafkaTopicsOptions>()
            .Bind(configuration.GetSection(KafkaTopicsOptions.SectionName))
            .ValidateOnStart();

        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<KafkaTopicsOptions>, KafkaTopicsOptionsValidator>());

        // The provisioner needs KafkaSubstrateOptions (replication factor);
        // register it here so this extension is self-sufficient — the
        // Monitor calls this entry-point before (or without) the publisher
        // extension that also registers it. Repeated Bind is harmless.
        services.AddOptions<KafkaSubstrateOptions>()
            .Bind(configuration.GetSection(KafkaSubstrateOptions.SectionName))
            .ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<KafkaSubstrateOptions>, KafkaSubstrateOptionsValidator>());

        services.TryAddSingleton<IRequestPollSignal, RequestPollSignal>();
        services.TryAddSingleton<IRequestEventHandler, PollSignalRequestEventHandler>();

        // The Monitor consumes dorc.requests.new / dorc.requests.status but
        // historically only the API host provisioned them — a fresh cluster
        // with Monitor-first start order left this consumer erroring in a
        // loop until an API came up. Hosted services start in registration
        // order, so the provisioner must precede the consumer below. Guarded:
        // AddDorcKafkaResultsStatusSubstrate registers the same provisioner.
        if (!services.Any(sd => sd.ServiceType == typeof(IHostedService)
                && sd.ImplementationType == typeof(KafkaResultsStatusTopicProvisioner)))
        {
            services.AddHostedService<KafkaResultsStatusTopicProvisioner>();
        }

        services.AddSingleton<DeploymentRequestsKafkaConsumer>();
        services.AddHostedService(sp => sp.GetRequiredService<DeploymentRequestsKafkaConsumer>());

        return services;
    }

    internal sealed class DorcKafkaRequestLifecycleMarker { }
}
