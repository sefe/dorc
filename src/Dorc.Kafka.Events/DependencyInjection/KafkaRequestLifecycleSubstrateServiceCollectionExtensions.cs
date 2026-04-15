using Dorc.Core.Events;
using Dorc.Kafka.Events.Publisher;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Dorc.Kafka.Events.DependencyInjection;

public static class KafkaRequestLifecycleSubstrateServiceCollectionExtensions
{
    /// <summary>
    /// SPEC-S-006 DI extension. After SPEC-S-009 the substrate-selector flag
    /// is gone and Kafka is unconditional: registers the latching
    /// <see cref="RequestPollSignal"/>, the
    /// <see cref="PollSignalRequestEventHandler"/>, and the
    /// <see cref="DeploymentRequestsKafkaConsumer"/> as a hosted service.
    /// Does NOT re-register <c>IDeploymentEventsPublisher</c> /
    /// <c>IFallbackDeploymentEventPublisher</c> — those remain owned by S-007's
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

        services.TryAddSingleton<IRequestPollSignal, RequestPollSignal>();
        services.TryAddSingleton<IRequestEventHandler, PollSignalRequestEventHandler>();

        services.AddSingleton<DeploymentRequestsKafkaConsumer>();
        services.AddHostedService(sp => sp.GetRequiredService<DeploymentRequestsKafkaConsumer>());

        return services;
    }

    internal sealed class DorcKafkaRequestLifecycleMarker { }
}
