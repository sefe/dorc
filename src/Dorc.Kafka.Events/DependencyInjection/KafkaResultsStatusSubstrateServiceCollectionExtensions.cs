using Confluent.Kafka;
using Dorc.Core.Events;
using Dorc.Core.Interfaces;
using Dorc.Kafka.Client.Producers;
using Dorc.Kafka.Events.Publisher;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Dorc.Kafka.Events.DependencyInjection;

public static class KafkaResultsStatusSubstrateServiceCollectionExtensions
{
    /// <summary>
    /// Registers the results-status Kafka substrate (S-007). After SPEC-S-009
    /// the substrate-selector flag is gone and Kafka is unconditional: the
    /// publisher, both producers, the consumer, and the topic provisioner
    /// are always registered. The retained <see cref="IFallbackDeploymentEventPublisher"/>
    /// continues to provide the SignalR fan-out for UI continuity (request-
    /// lifecycle events; the API-side <see cref="DeploymentResultsKafkaConsumer"/>
    /// projects results-status events to SignalR via the broadcaster).
    /// Idempotent via marker singleton.
    /// </summary>
    public static IServiceCollection AddDorcKafkaResultsStatusSubstrate(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        if (services.Any(sd => sd.ServiceType == typeof(DorcKafkaResultsStatusSubstrateMarker)))
            return services;

        services.AddSingleton<DorcKafkaResultsStatusSubstrateMarker>();

        services.AddOptions<KafkaSubstrateOptions>()
            .Bind(configuration.GetSection(KafkaSubstrateOptions.SectionName))
            .ValidateOnStart();

        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<KafkaSubstrateOptions>, KafkaSubstrateOptionsValidator>());

        services.TryAddSingleton<IProducer<string, DeploymentResultEventData>>(sp =>
        {
            var builder = sp.GetRequiredService<IKafkaProducerBuilder<string, DeploymentResultEventData>>();
            return builder.Build("dorc-results-status-publisher");
        });
        services.TryAddSingleton<IProducer<string, DeploymentRequestEventData>>(sp =>
        {
            var builder = sp.GetRequiredService<IKafkaProducerBuilder<string, DeploymentRequestEventData>>();
            return builder.Build("dorc-requests-publisher");
        });
        services.TryAddSingleton<KafkaDeploymentEventPublisher>();
        services.Replace(ServiceDescriptor.Scoped<IDeploymentEventsPublisher>(sp =>
            sp.GetRequiredService<KafkaDeploymentEventPublisher>()));

        services.AddSingleton<DeploymentResultsKafkaConsumer>();
        services.AddHostedService(sp => sp.GetRequiredService<DeploymentResultsKafkaConsumer>());

        services.AddHostedService<KafkaResultsStatusTopicProvisioner>();

        return services;
    }

    internal sealed class DorcKafkaResultsStatusSubstrateMarker { }
}
