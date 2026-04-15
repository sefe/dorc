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
    /// SPEC-S-006 DI extension. Behaviour is driven by
    /// <c>Kafka:Substrate:RequestLifecycle</c>:
    /// <list type="bullet">
    ///   <item><c>Direct</c> (default): no registration changes — the host
    ///     retains whatever <see cref="IRequestPollSignal"/> was registered
    ///     upstream (typically <see cref="NoOpRequestPollSignal"/>) and the
    ///     Kafka request consumer is not registered.</item>
    ///   <item><c>Kafka</c>: registers the latching <see cref="RequestPollSignal"/>,
    ///     the <see cref="PollSignalRequestEventHandler"/>, and the
    ///     <see cref="DeploymentRequestsKafkaConsumer"/> as a hosted service.
    ///     <b>Does NOT re-register</b> <c>IDeploymentEventsPublisher</c> or
    ///     <c>IFallbackDeploymentEventPublisher</c> per SPEC-S-006 R-2 GPT-F7
    ///     guard — those remain owned by S-007's
    ///     <c>AddDorcKafkaResultsStatusSubstrate</c>. Hosting S-006 standalone
    ///     therefore presumes S-007 is also registered (the producer side
    ///     lives on <see cref="KafkaDeploymentEventPublisher"/>).</item>
    /// </list>
    /// Idempotent via marker-singleton; substrate mode read at registration
    /// time per the S-007 pattern.
    /// </summary>
    public static IServiceCollection AddDorcKafkaRequestLifecycleSubstrate(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        if (services.Any(sd => sd.ServiceType == typeof(DorcKafkaRequestLifecycleMarker)))
            return services;

        services.AddSingleton<DorcKafkaRequestLifecycleMarker>();

        var mode = ReadRequestLifecycleMode(configuration);
        if (mode != KafkaSubstrateMode.Kafka)
            return services;

        // Replace the upstream-registered signal with the latching one (no-op
        // upstream is the production default; tests may inject something else).
        services.Replace(ServiceDescriptor.Singleton<IRequestPollSignal, RequestPollSignal>());

        services.TryAddSingleton<IRequestEventHandler, PollSignalRequestEventHandler>();

        services.AddSingleton<DeploymentRequestsKafkaConsumer>();
        services.AddHostedService(sp => sp.GetRequiredService<DeploymentRequestsKafkaConsumer>());

        return services;
    }

    private static KafkaSubstrateMode ReadRequestLifecycleMode(IConfiguration configuration)
    {
        var raw = configuration[$"{KafkaSubstrateOptions.SectionName}:{nameof(KafkaSubstrateOptions.RequestLifecycle)}"];
        if (string.IsNullOrWhiteSpace(raw)) return KafkaSubstrateMode.Direct;
        if (!Enum.TryParse<KafkaSubstrateMode>(raw, ignoreCase: true, out var parsed)
            || !Enum.IsDefined(typeof(KafkaSubstrateMode), parsed))
        {
            throw new InvalidOperationException(
                $"{KafkaSubstrateOptions.SectionName}:{nameof(KafkaSubstrateOptions.RequestLifecycle)}"
                + $" has invalid value '{raw}'. Allowed: {string.Join(", ", Enum.GetNames<KafkaSubstrateMode>())}.");
        }
        return parsed;
    }

    internal sealed class DorcKafkaRequestLifecycleMarker { }
}
