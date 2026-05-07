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
/// <para>Resolution order:</para>
/// <list type="number">
/// <item><description><c>DORC_REPLICA_ID</c> environment variable, if
/// set. The recommended production path: in K8s, inject the pod UID via
/// the downward API (<c>metadata.uid</c>) — globally unique per pod and
/// stable for the pod's lifetime. On bare-metal, set a persisted host-
/// level identifier per replica process.</description></item>
/// <item><description>Otherwise, <c>{MachineName}-{ProcessId}</c>.
/// Unique across replicas because PID differs even on the same host,
/// but mints a fresh group on every restart. Acceptable for dev/test;
/// production should set <c>DORC_REPLICA_ID</c> and pair it with a
/// periodic orphan-group cleanup.</description></item>
/// </list>
/// </summary>
public static class HostInstanceId
{
    public const string EnvironmentVariable = "DORC_REPLICA_ID";

    private static readonly Lazy<string> _value = new(Resolve);

    public static string Value => _value.Value;

    private static string Resolve() => Resolve(Environment.GetEnvironmentVariable, Environment.MachineName, Environment.ProcessId);

    /// <summary>
    /// Pure resolver split out for testability — the static <see cref="Value"/>
    /// is latched at first access and can't observe env-var changes mid-process.
    /// </summary>
    internal static string Resolve(Func<string, string?> env, string machineName, int processId)
    {
        var configured = env(EnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(configured))
            return $"{machineName}-{configured.Trim()}";

        return $"{machineName}-{processId}";
    }
}
