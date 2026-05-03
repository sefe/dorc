using Confluent.Kafka;
using Dorc.Core.Events;
using Dorc.Core.Interfaces;
using Dorc.Kafka.Client.Producers;
using Dorc.Kafka.Events.Configuration;
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
    /// Registers the dual-publish <see cref="KafkaDeploymentEventPublisher"/>
    /// and its two producers, replacing whatever <see cref="IDeploymentEventsPublisher"/>
    /// was already in the container. Both API (request-lifecycle events) and
    /// Monitor (result-status events) call this so every publish lands on
    /// the authoritative Kafka substrate. The caller is expected to have
    /// already registered an <see cref="IFallbackDeploymentEventPublisher"/>
    /// suitable for its host (API: SignalR-direct; Monitor: SignalR client).
    /// Idempotent via marker singleton.
    /// </summary>
    public static IServiceCollection AddDorcKafkaPublisher(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        if (services.Any(sd => sd.ServiceType == typeof(DorcKafkaPublisherMarker)))
            return services;

        services.AddSingleton<DorcKafkaPublisherMarker>();

        // KafkaTopicsOptions / KafkaSubstrateOptions are registered by every
        // Kafka.Events entry-point that needs them — see the long-running
        // idempotency comments on the consumer extensions. Repeating here
        // means publisher-only callers (Monitor) get a fully-validated
        // options graph without taking on the consumer wiring.
        services.AddOptions<KafkaSubstrateOptions>()
            .Bind(configuration.GetSection(KafkaSubstrateOptions.SectionName))
            .ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<KafkaSubstrateOptions>, KafkaSubstrateOptionsValidator>());

        services.AddOptions<KafkaTopicsOptions>()
            .Bind(configuration.GetSection(KafkaTopicsOptions.SectionName))
            .ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<KafkaTopicsOptions>, KafkaTopicsOptionsValidator>());

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

        return services;
    }

    /// <summary>
    /// Registers the API-side results-status consumer-substrate (S-007).
    /// Calls <see cref="AddDorcKafkaPublisher"/> for the publisher half then
    /// adds the topic provisioner, the Kafka→SignalR projection consumer,
    /// and an Avro warmup hosted service.
    ///
    /// <para>The retained <see cref="IFallbackDeploymentEventPublisher"/>
    /// continues to provide the SignalR fan-out for UI continuity (request-
    /// lifecycle events; the API-side <see cref="DeploymentResultsKafkaConsumer"/>
    /// projects results-status events to SignalR via the broadcaster).
    /// Idempotent via marker singleton.</para>
    /// </summary>
    public static IServiceCollection AddDorcKafkaResultsStatusSubstrate(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        if (services.Any(sd => sd.ServiceType == typeof(DorcKafkaResultsStatusSubstrateMarker)))
            return services;

        services.AddSingleton<DorcKafkaResultsStatusSubstrateMarker>();

        services.AddDorcKafkaPublisher(configuration);

        // Hosted services start in registration order. The topic provisioner
        // must run before the consumer subscribes — otherwise on a fresh
        // deployment the consumer hits UnknownTopicOrPartition in a tight
        // loop until provisioning completes.
        services.AddHostedService<KafkaResultsStatusTopicProvisioner>();

        services.AddSingleton<DeploymentResultsKafkaConsumer>();
        services.AddHostedService(sp => sp.GetRequiredService<DeploymentResultsKafkaConsumer>());

        return services;
    }

    internal sealed class DorcKafkaPublisherMarker { }
    internal sealed class DorcKafkaResultsStatusSubstrateMarker { }
}
