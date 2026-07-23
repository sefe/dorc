using Dorc.Core.Events;
using Dorc.Kafka.Events.Publisher;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

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

        // KafkaTopicsOptions registration is idempotent — the consumer and
        // the provisioner resolve IOptions<KafkaTopicsOptions> in their
        // constructors, so this extension must register it independently of
        // the results-status extension (the Monitor calls this entry-point
        // before, or without, the publisher extension).
        KafkaEventsOptionsRegistration.AddKafkaEventOptions(services, configuration);

        services.TryAddSingleton<IRequestPollSignal, RequestPollSignal>();
        services.TryAddSingleton<IRequestEventHandler, PollSignalRequestEventHandler>();

        // The Monitor consumes dorc.requests.new / dorc.requests.status but
        // historically only the API host provisioned them — a fresh cluster
        // with Monitor-first start order left this consumer erroring in a
        // loop until an API came up. Hosted services start in registration
        // order, so the provisioner must precede the consumer below. Guarded:
        // AddDorcKafkaResultsStatusSubstrate registers the same provisioner.
        KafkaEventsOptionsRegistration.EnsureResultsStatusTopicProvisioner(services);

        services.AddSingleton<DeploymentRequestsKafkaConsumer>();
        services.AddHostedService(sp => sp.GetRequiredService<DeploymentRequestsKafkaConsumer>());

        return services;
    }

    internal sealed class DorcKafkaRequestLifecycleMarker { }
}
