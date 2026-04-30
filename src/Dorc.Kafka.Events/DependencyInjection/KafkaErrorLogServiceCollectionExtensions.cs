using Confluent.Kafka;
using Dorc.Kafka.Client.Producers;
using Dorc.Kafka.ErrorLog;
using Dorc.Kafka.Events.Configuration;
using Dorc.Kafka.Events.Publisher;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Dorc.Kafka.ErrorLog.DependencyInjection;

/// <summary>
/// DI registration for the post-K-2 Kafka-topic-backed error-log substrate.
/// Lives in the <c>Dorc.Kafka.Events</c> assembly (not <c>Dorc.Kafka.ErrorLog</c>)
/// because it bridges <see cref="KafkaTopicsOptions"/> from
/// <c>Dorc.Kafka.Events.Configuration</c> into the producer impl — putting it
/// in <c>Dorc.Kafka.ErrorLog</c> would create a circular project reference.
/// Namespace is preserved so existing call-sites
/// (<c>using Dorc.Kafka.ErrorLog.DependencyInjection;</c>) keep compiling.
/// </summary>
public static class KafkaErrorLogServiceCollectionExtensions
{
    public static IServiceCollection AddDorcKafkaErrorLog(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        if (services.Any(sd => sd.ServiceType == typeof(DorcKafkaErrorLogMarker)))
            return services;

        services.AddSingleton<DorcKafkaErrorLogMarker>();

        services.AddOptions<KafkaErrorLogOptions>()
            .Bind(configuration.GetSection(KafkaErrorLogOptions.SectionName))
            .ValidateOnStart();

        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<KafkaErrorLogOptions>, KafkaErrorLogOptionsValidator>());

        services.AddOptions<KafkaTopicsOptions>()
            .Bind(configuration.GetSection(KafkaTopicsOptions.SectionName))
            .ValidateOnStart();

        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<KafkaTopicsOptions>, KafkaTopicsOptionsValidator>());

        services.TryAddSingleton<IProducer<string, KafkaErrorEnvelope>>(sp =>
        {
            var builder = sp.GetRequiredService<IKafkaProducerBuilder<string, KafkaErrorEnvelope>>();
            return builder.Build("dorc-dlq-publisher");
        });

        services.TryAddSingleton<IKafkaErrorLog>(sp =>
        {
            var producer = sp.GetRequiredService<IProducer<string, KafkaErrorEnvelope>>();
            var errorLogOptions = sp.GetRequiredService<IOptions<KafkaErrorLogOptions>>();
            var topics = sp.GetRequiredService<IOptions<KafkaTopicsOptions>>().Value;
            var logger = sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<KafkaErrorLog>>();

            // Per K-2: DLQ tier enabled only for RequestsNew. Other source
            // topics fall through to the structured-log tier via
            // DlqNotConfiguredException. Extend by adding more entries here.
            var routes = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [topics.RequestsNew] = topics.RequestsNewDlq
            };

            return new KafkaErrorLog(producer, errorLogOptions, routes, logger);
        });

        services.AddHostedService<KafkaErrorLogDlqTopicProvisioner>();

        return services;
    }

    internal sealed class DorcKafkaErrorLogMarker { }
}
