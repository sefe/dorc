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
    /// Registers the S-007 results-status substrate. Behaviour is driven by
    /// <c>Kafka:Substrate:ResultsStatus</c>:
    /// <list type="bullet">
    ///   <item><c>Direct</c> (default): no registration changes — the host
    ///     retains whatever <see cref="IDeploymentEventsPublisher"/> was
    ///     registered upstream (typically <c>DirectDeploymentEventPublisher</c>).
    ///     Kafka consumer + topic provisioner are not registered.</item>
    ///   <item><c>Kafka</c>: registers <see cref="KafkaDeploymentEventPublisher"/>
    ///     as <c>IDeploymentEventsPublisher</c> (caller must have registered an
    ///     <see cref="IFallbackDeploymentEventPublisher"/> for request-lifecycle
    ///     delegation). Also registers <see cref="DeploymentResultsKafkaConsumer"/>
    ///     and <see cref="KafkaResultsStatusTopicProvisioner"/> as hosted services.</item>
    /// </list>
    /// Idempotent. The substrate mode is read from the supplied
    /// <paramref name="configuration"/> at registration time.
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

        // Read the flag from the supplied IConfiguration (not from the DI
        // graph) so the registration is deterministic at compile time.
        var mode = ReadResultsStatusMode(configuration);
        if (mode != KafkaSubstrateMode.Kafka)
            return services;

        // --- Kafka mode ---
        // The Confluent IProducer is a singleton built once from S-002's
        // builder; the publisher wrapping it is likewise a singleton.
        services.TryAddSingleton<IProducer<string, DeploymentResultEventData>>(sp =>
        {
            var builder = sp.GetRequiredService<IKafkaProducerBuilder<string, DeploymentResultEventData>>();
            return builder.Build("dorc-results-status-publisher");
        });
        // S-006 R-1 / R-2: KafkaDeploymentEventPublisher always carries a request
        // producer because the publisher class dual-publishes both flows (results
        // and request-lifecycle). Registered TryAdd here so the publisher resolves
        // even with substrate=Direct on the RequestLifecycle slot — in that mode
        // the consumer is absent (S-006 extension not called), so requests are
        // emitted onto a topic with no Monitor consumer, which is a benign no-op
        // until S-006 is also flipped.
        services.TryAddSingleton<IProducer<string, DeploymentRequestEventData>>(sp =>
        {
            var builder = sp.GetRequiredService<IKafkaProducerBuilder<string, DeploymentRequestEventData>>();
            return builder.Build("dorc-requests-publisher");
        });
        services.TryAddSingleton<KafkaDeploymentEventPublisher>();
        services.Replace(ServiceDescriptor.Scoped<IDeploymentEventsPublisher>(sp =>
            sp.GetRequiredService<KafkaDeploymentEventPublisher>()));

        // Consumer: hosted background service that subscribes to
        // dorc.results.status and projects to SignalR via the injected
        // IDeploymentResultBroadcaster.
        services.AddSingleton<DeploymentResultsKafkaConsumer>();
        services.AddHostedService(sp => sp.GetRequiredService<DeploymentResultsKafkaConsumer>());

        // Topic provisioning: one hosted service, runs StartAsync once.
        services.AddHostedService<KafkaResultsStatusTopicProvisioner>();

        return services;
    }

    private static KafkaSubstrateMode ReadResultsStatusMode(IConfiguration configuration)
    {
        var raw = configuration[$"{KafkaSubstrateOptions.SectionName}:{nameof(KafkaSubstrateOptions.ResultsStatus)}"];
        if (string.IsNullOrWhiteSpace(raw)) return KafkaSubstrateMode.Direct;
        if (!Enum.TryParse<KafkaSubstrateMode>(raw, ignoreCase: true, out var parsed)
            || !Enum.IsDefined(typeof(KafkaSubstrateMode), parsed))
        {
            // .NET's config binder silently falls back on invalid enum
            // strings; we don't — per SPEC-S-007 R-7 the substrate flag
            // must fail at host build with the failing key named, not
            // silently land on a default.
            throw new InvalidOperationException(
                $"{KafkaSubstrateOptions.SectionName}:{nameof(KafkaSubstrateOptions.ResultsStatus)}"
                + $" has invalid value '{raw}'. Allowed: {string.Join(", ", Enum.GetNames<KafkaSubstrateMode>())}.");
        }
        return parsed;
    }

    internal sealed class DorcKafkaResultsStatusSubstrateMarker { }
}
