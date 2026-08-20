using Dorc.Kafka.Events.Configuration;
using Dorc.Kafka.Events.Publisher;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Dorc.Kafka.Events.DependencyInjection;

/// <summary>
/// Shared registration blocks used by every Kafka.Events substrate
/// entry-point (<see cref="KafkaRequestLifecycleSubstrateServiceCollectionExtensions"/>
/// and <see cref="KafkaResultsStatusSubstrateServiceCollectionExtensions"/>).
/// Each entry-point must be self-sufficient — hosts call them independently
/// and in any order — so the blocks are idempotent and live here in ONE
/// place instead of drifting between hand-rolled copies.
/// </summary>
internal static class KafkaEventsOptionsRegistration
{
    /// <summary>
    /// Binds + start-validates <see cref="KafkaTopicsOptions"/> and registers
    /// its validator. Repeated Bind is harmless; TryAddEnumerable is
    /// idempotent by implementation type (ServiceCollection dedups on
    /// (ServiceType, ImplementationType)) so the validator registers exactly
    /// once even when multiple substrate entry-points run.
    /// </summary>
    internal static void AddKafkaEventOptions(IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<KafkaTopicsOptions>()
            .Bind(configuration.GetSection(KafkaTopicsOptions.SectionName))
            // Back-compat: the replication factor used to live at
            // Kafka:Substrate:ResultsStatusReplicationFactor (the deleted
            // KafkaSubstrateOptions). Environments that set the old key
            // out-of-repo (e.g. Kafka__Substrate__ResultsStatusReplicationFactor=1
            // against a single-broker dev stack) must not silently revert to
            // RF=3 — that makes topic creation fail warn-only and consumers
            // loop on UnknownTopicOrPartition. The new key wins when both set.
            .PostConfigure(options =>
            {
                if (configuration[$"{KafkaTopicsOptions.SectionName}:{nameof(KafkaTopicsOptions.ReplicationFactor)}"] is null
                    && short.TryParse(configuration["Kafka:Substrate:ResultsStatusReplicationFactor"], out var legacyRf))
                {
                    options.ReplicationFactor = legacyRf;
                }
            })
            .ValidateOnStart();

        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<KafkaTopicsOptions>, KafkaTopicsOptionsValidator>());
    }

    /// <summary>
    /// Registers <see cref="KafkaResultsStatusTopicProvisioner"/> as a hosted
    /// service exactly once. Hosted services start in registration order, so
    /// callers must invoke this BEFORE registering any consumer hosted
    /// service that subscribes to the provisioned topics — otherwise a fresh
    /// cluster leaves the consumer erroring in a loop (e.g.
    /// UnknownTopicOrPartition) until provisioning completes. Guarded because
    /// both substrate entry-points register the same provisioner.
    /// </summary>
    internal static void EnsureResultsStatusTopicProvisioner(IServiceCollection services)
    {
        if (!services.Any(sd => sd.ServiceType == typeof(IHostedService)
                && sd.ImplementationType == typeof(KafkaResultsStatusTopicProvisioner)))
        {
            services.AddHostedService<KafkaResultsStatusTopicProvisioner>();
        }
    }
}
