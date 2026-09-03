using Confluent.Kafka;
using Confluent.Kafka.Admin;
using Microsoft.Extensions.Logging;

namespace Dorc.Kafka.Client.Provisioning;

/// <summary>
/// Shared warn-only core for idempotent, best-effort topic provisioning at
/// host startup. Used by the Events-side provisioners (results/requests +
/// DLQ); the lock topic provisioner deliberately does NOT use this core —
/// its partition-count-mismatch policy is fail-fast (mis-routed lock keys →
/// split-brain), the opposite of warn-only.
///
/// Policy (nothing here may escape StartAsync except cancellation):
/// <list type="bullet">
/// <item>All topics are created in ONE batched CreateTopicsAsync call;
/// broker responses are triaged per topic.</item>
/// <item>Existing topic with same partition count → Information, no-op.</item>
/// <item>Existing topic with different partition count → Warning, no throw.</item>
/// <item>ACL denial → Error (loud, operator actionable); RF/policy rejection
/// on single-broker dev → Warning (expected noise). Neither throws — the
/// producer fail-loud on first publish is the backstop.</item>
/// <item>Broker unreachable or admin call timed out → Error, no throw: a
/// startup crash-loop for an outage the substrate already tolerates.</item>
/// <item><see cref="OperationCanceledException"/> propagates — the host
/// start was genuinely cancelled.</item>
/// </list>
/// </summary>
public static class IdempotentTopicProvisioner
{
    /// <summary>
    /// Upper bound on the batched create call. librdkafka's admin default is
    /// ~60s per call and <c>CreateTopicsAsync</c> takes no CancellationToken,
    /// so <c>WaitAsync(timeout, ct)</c> is the only available bound — an
    /// unattended service start must not hang for minutes when the broker is
    /// down.
    /// </summary>
    public static readonly TimeSpan AdminCallTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Provisions <paramref name="specs"/> in a single batched admin call and
    /// triages the outcome per topic. Warn-only: never throws for broker-side
    /// failures; only cancellation propagates.
    /// </summary>
    /// <param name="adminCallTimeout">
    /// Test seam for the create-call bound; production callers pass nothing
    /// and get <see cref="AdminCallTimeout"/>.
    /// </param>
    public static async Task ProvisionAsync(
        IAdminClient admin,
        IReadOnlyList<TopicSpecification> specs,
        string logLabel,
        ILogger logger,
        CancellationToken cancellationToken,
        TimeSpan? adminCallTimeout = null)
    {
        try
        {
            // One batched call for the whole set — the admin API takes no
            // CancellationToken, so bound it with WaitAsync(timeout, ct).
            await admin.CreateTopicsAsync(specs)
                .WaitAsync(adminCallTimeout ?? AdminCallTimeout, cancellationToken)
                .ConfigureAwait(false);

            foreach (var spec in specs)
                LogCreated(logger, logLabel, spec);
        }
        catch (CreateTopicsException ex)
        {
            // Batched create fails as a whole even when only some topics are
            // rejected — triage each per-topic result independently so one
            // rejection can't mask the others' outcomes.
            var specsByName = specs.ToDictionary(s => s.Name);
            foreach (var result in ex.Results)
            {
                specsByName.TryGetValue(result.Topic, out var spec);
                var code = result.Error.Code;
                var reason = result.Error.Reason;

                if (code == ErrorCode.NoError && spec is not null)
                {
                    LogCreated(logger, logLabel, spec);
                }
                else if (code == ErrorCode.TopicAlreadyExists)
                {
                    await VerifyPartitionCountAsync(
                        admin, result.Topic, spec?.NumPartitions ?? -1, logLabel, logger, cancellationToken)
                        .ConfigureAwait(false);
                }
                else if (code is ErrorCode.TopicAuthorizationFailed or ErrorCode.ClusterAuthorizationFailed)
                {
                    // ACL misconfiguration is loud (Error) ...
                    logger.LogError(
                        "{Label} topic create denied by broker ACL: topic={Topic} code={Code} reason={Reason}. Check service-account permissions.",
                        logLabel, result.Topic, code, reason);
                }
                else if (code is ErrorCode.InvalidReplicationFactor or ErrorCode.PolicyViolation)
                {
                    // ... while RF-rejection on single-broker dev is expected noise.
                    logger.LogWarning(
                        "{Label} topic create rejected (dev-style config issue): topic={Topic} code={Code} reason={Reason}.",
                        logLabel, result.Topic, code, reason);
                }
                else
                {
                    logger.LogError(
                        "{Label} topic create failed: topic={Topic} code={Code} reason={Reason}.",
                        logLabel, result.Topic, code, reason);
                }
            }
        }
        // Broker unreachable (plain KafkaException) or the WaitAsync bound
        // expired (TimeoutException). Neither may escape StartAsync: that
        // aborts IHost.StartAsync — a crash-loop for the Monitor Windows
        // service and a failed app start for the API — for an outage the
        // consumers/producers already tolerate with retry.
        catch (Exception ex) when (ex is KafkaException or TimeoutException)
        {
            logger.LogError(ex,
                "{Label} topic provisioning skipped (broker unreachable or admin call timed out): topics={Topics}. Startup continues; consumers retry and the producer fails loud on first publish.",
                logLabel, string.Join(",", specs.Select(s => s.Name)));
        }
    }

    /// <summary>
    /// Warn-only partition-count check for a topic the broker acked as
    /// already existing. Never throws — drift is surfaced for the operator,
    /// but per-key ordering still holds for any fixed count.
    /// </summary>
    private static Task VerifyPartitionCountAsync(
        IAdminClient admin,
        string topic,
        int expected,
        string logLabel,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Metadata metadata;
        try
        {
            metadata = admin.GetMetadata(topic, TimeSpan.FromSeconds(3));
        }
        catch (KafkaException ex)
        {
            logger.LogWarning(ex,
                "{Label} topic partition-count verification skipped (metadata fetch failed): topic={Topic} reason={Reason}.",
                logLabel, topic, ex.Error.Reason);
            return Task.CompletedTask;
        }

        var meta = metadata.Topics.FirstOrDefault(t => t.Topic == topic);
        if (meta is null)
        {
            logger.LogWarning(
                "{Label} topic existence ack conflict: topic={Topic} not present in metadata fetch.",
                logLabel, topic);
            return Task.CompletedTask;
        }

        var actual = meta.Partitions.Count;
        if (actual == expected)
        {
            logger.LogInformation(
                "{Label} topic already present with expected partition count: topic={Topic} partitions={Partitions}",
                logLabel, topic, actual);
        }
        else
        {
            logger.LogWarning(
                "{Label} topic present with DIFFERENT partition count: topic={Topic} expected={Expected} actual={Actual}. Per-key ordering still holds for a fixed count, but this is topology drift the operator should reconcile.",
                logLabel, topic, expected, actual);
        }

        return Task.CompletedTask;
    }

    private static void LogCreated(ILogger logger, string logLabel, TopicSpecification spec)
    {
        var minIsr = spec.Configs is not null && spec.Configs.TryGetValue("min.insync.replicas", out var isr)
            ? isr
            : "broker-default";

        if (spec.Configs is not null && spec.Configs.TryGetValue("retention.ms", out var retentionMs))
        {
            logger.LogInformation(
                "{Label} topic created: topic={Topic} partitions={Partitions} rf={Rf} minIsr={MinIsr} retentionMs={RetentionMs}",
                logLabel, spec.Name, spec.NumPartitions, spec.ReplicationFactor, minIsr, retentionMs);
        }
        else
        {
            logger.LogInformation(
                "{Label} topic created: topic={Topic} partitions={Partitions} rf={Rf} minIsr={MinIsr}",
                logLabel, spec.Name, spec.NumPartitions, spec.ReplicationFactor, minIsr);
        }
    }
}
