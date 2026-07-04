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
        // The interface forward MUST be singleton. A scoped factory
        // registration would make every resolving scope capture the returned
        // instance in its disposables list, so the first disposed request
        // scope would Dispose the shared singleton's Kafka producers and
        // every subsequent publish would throw ObjectDisposedException.
        // (A scoped registration also turns the singleton's scoped
        // resolution into a captive-dependency error under scope validation.)
        services.Replace(ServiceDescriptor.Singleton<IDeploymentEventsPublisher>(sp =>
            sp.GetRequiredService<KafkaDeploymentEventPublisher>()));

        return services;
    }

    /// <summary>
    /// Registers the API-side results-status consumer-substrate (S-007).
    /// Calls <see cref="AddDorcKafkaPublisher"/> for the publisher half then
    /// adds the topic provisioner and the Kafka→SignalR projection consumer.
    ///
    /// <para>The retained <see cref="IFallbackDeploymentEventPublisher"/>
    /// continues to provide the SignalR fan-out for UI continuity (request-
    /// lifecycle events; the API-side <see cref="DeploymentResultsKafkaConsumer"/>
    /// projects results-status events to SignalR via the broadcaster).
    /// Idempotent via marker singleton.</para>
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">Host configuration.</param>
    /// <param name="useSharedConsumerGroup">
    /// True when SignalR runs on a service-wide backplane (Azure SignalR
    /// Service): hub sends then reach ALL clients regardless of which replica
    /// sends, so the per-replica fan-out consumer-group design would deliver
    /// every results-status event N times per client. In shared mode all
    /// replicas join ONE competing consumer group and exactly one replica
    /// projects each event. False (default) keeps per-replica fan-out for
    /// in-process SignalR, where each replica reaches only its own clients.
    /// </param>
    public static IServiceCollection AddDorcKafkaResultsStatusSubstrate(
        this IServiceCollection services,
        IConfiguration configuration,
        bool useSharedConsumerGroup = false)
    {
        if (services.Any(sd => sd.ServiceType == typeof(DorcKafkaResultsStatusSubstrateMarker)))
            return services;

        services.AddSingleton<DorcKafkaResultsStatusSubstrateMarker>();

        services.AddDorcKafkaPublisher(configuration);

        // Hosted services start in registration order. The topic provisioner
        // must run before the consumer subscribes — otherwise on a fresh
        // deployment the consumer hits UnknownTopicOrPartition in a tight
        // loop until provisioning completes. Guarded: the request-lifecycle
        // substrate extension registers the same provisioner.
        if (!services.Any(sd => sd.ServiceType == typeof(IHostedService)
                && sd.ImplementationType == typeof(KafkaResultsStatusTopicProvisioner)))
        {
            services.AddHostedService<KafkaResultsStatusTopicProvisioner>();
        }

        if (useSharedConsumerGroup)
        {
            services.AddSingleton(sp => new DeploymentResultsKafkaConsumer(
                sp.GetRequiredService<Client.Connection.IKafkaConnectionProvider>(),
                sp.GetRequiredService<Client.Serialization.IKafkaSerializerFactory>(),
                sp.GetRequiredService<IDeploymentResultBroadcaster>(),
                sp.GetRequiredService<ErrorLog.IKafkaErrorLog>(),
                sp.GetRequiredService<IOptions<ErrorLog.KafkaErrorLogOptions>>(),
                sp.GetRequiredService<IOptions<KafkaTopicsOptions>>(),
                sp.GetRequiredService<Client.Observability.IKafkaConsumerMetrics>(),
                sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<DeploymentResultsKafkaConsumer>>(),
                sp.GetService<IOptions<Client.Configuration.KafkaClientOptions>>(),
                useSharedConsumerGroup: true));
        }
        else
        {
            services.AddSingleton<DeploymentResultsKafkaConsumer>();
        }
        services.AddHostedService(sp => sp.GetRequiredService<DeploymentResultsKafkaConsumer>());

        return services;
    }

    internal sealed class DorcKafkaPublisherMarker { }
    internal sealed class DorcKafkaResultsStatusSubstrateMarker { }
}
