using Microsoft.Extensions.Logging;

namespace Dorc.Kafka.Events.Publisher;

/// <summary>
/// Identifier used to suffix the per-replica Kafka consumer group id (per
/// multi-replica fan-out design). Each replica gets its own
/// consumer group so every replica consumes every event and broadcasts to
/// its own locally-pinned SignalR clients — avoids the shared-group
/// sticky-session fan-out gap. The suffix has two requirements:
/// <list type="bullet">
/// <item><description><b>unique across replicas</b> — two replicas with
/// the same suffix would share a group, partitions split between them,
/// each event reaches only one of them and the SignalR fan-out
/// invariant breaks silently.</description></item>
/// <item><description><b>stable across restarts</b> — a fresh suffix on
/// every restart leaves orphan consumer groups in
/// <c>__consumer_offsets</c> indefinitely.</description></item>
/// </list>
///
/// <para>Resolution order (see <see cref="For"/>):</para>
/// <list type="number">
/// <item><description><c>DORC_REPLICA_ID</c> environment variable, if
/// set. The value must be <b>stable across restarts AND rollouts</b> —
/// in K8s use a rollout-stable identity such as the StatefulSet pod
/// name or a persisted per-replica identifier. Do NOT use the pod UID
/// (<c>metadata.uid</c>): it changes on every pod recreation, so each
/// rollout mints a brand-new consumer group, orphaning the previous
/// one in <c>__consumer_offsets</c> until offset retention expires.
/// <b>Deployments that run more than one SAME-TIER replica on the same
/// host MUST set this</b> — neither the config channel (tier-level) nor
/// the machine-name fallback can distinguish them.</description></item>
/// <item><description><c>Kafka:ReplicaId</c> configuration, the
/// installer-friendly channel: the MSI writes tier-distinct values
/// ("prod"/"nonprod") so co-hosted Prod/NonProd services never share a
/// per-replica group.</description></item>
/// <item><description>Otherwise, <c>{MachineName}</c> alone. Stable
/// across restarts — a PID-style suffix would mint a fresh consumer
/// group on every restart, accumulating orphan groups in
/// <c>__consumer_offsets</c> and replaying any messages from Latest
/// on the requests consumer. Unique across single-replica-per-host
/// deployments; NOT unique for multiple replicas on one host (set
/// <c>Kafka:ReplicaId</c> explicitly in that topology).</description></item>
/// </list>
/// </summary>
public static class HostInstanceId
{
    public const string EnvironmentVariable = "DORC_REPLICA_ID";

    private static readonly Lazy<string> _value = new(Resolve);

    public static string Value => _value.Value;

    private static string Resolve() => Resolve(Environment.GetEnvironmentVariable, Environment.MachineName);

    /// <summary>
    /// Effective per-replica suffix. Precedence:
    /// <c>DORC_REPLICA_ID</c> env var → <c>Kafka:ReplicaId</c> config →
    /// machine name. The env var outranks the config channel deliberately:
    /// the MSI writes tier-level config values ("prod"/"nonprod")
    /// unconditionally, and a site that already distinguishes same-tier
    /// co-hosted replicas via a per-replica <c>DORC_REPLICA_ID</c> must not
    /// have those identities silently collapsed onto the tier value by an
    /// upgrade. The config channel combines with the machine name
    /// (<c>{MachineName}-{ReplicaId}</c>) — unique per machine AND per
    /// co-hosted service tier.
    /// </summary>
    public static string For(string? configuredReplicaId)
    {
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(EnvironmentVariable)))
            return Value;

        return string.IsNullOrWhiteSpace(configuredReplicaId)
            ? Value
            : $"{Environment.MachineName}-{configuredReplicaId.Trim()}";
    }

    /// <summary>
    /// Pure resolver split out for testability — the static <see cref="Value"/>
    /// is latched at first access and can't observe env-var changes mid-process.
    /// </summary>
    internal static string Resolve(Func<string, string?> env, string machineName)
    {
        var configured = env(EnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(configured))
            return $"{machineName}-{configured.Trim()}";

        return machineName;
    }

    /// <summary>
    /// Emits a warning when neither <c>Kafka:ReplicaId</c> (config) nor
    /// <c>DORC_REPLICA_ID</c> (env var) is set. Call once at host startup
    /// (before consumers subscribe) so the misconfiguration is visible in
    /// structured logs before any fan-out invariant breaks silently.
    /// </summary>
    public static void WarnIfFallingBackToMachineName(ILogger logger, string? configuredReplicaId = null)
    {
        if (!string.IsNullOrWhiteSpace(configuredReplicaId))
            return;
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(EnvironmentVariable)))
            return;

        logger.LogWarning(
            "Neither Kafka:ReplicaId (config) nor DORC_REPLICA_ID (environment) is set. HostInstanceId is falling back to MachineName='{MachineName}'. " +
            "If more than one API or Monitor replica runs on this host, they will share a Kafka consumer group and the " +
            "fan-out invariant (every replica receives every event) will be silently broken. " +
            "Set Kafka:ReplicaId to a per-service value (the MSI writes prod/nonprod) or DORC_REPLICA_ID to a rollout-stable per-replica identifier.",
            Environment.MachineName);
    }
}
