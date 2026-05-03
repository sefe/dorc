namespace Dorc.Kafka.Events.Publisher;

/// <summary>
/// Restart-stable identifier used to suffix the per-replica Kafka consumer
/// group id (per SPEC-S-007 R-2 multi-replica fan-out design). Each replica
/// gets its own consumer group so every replica consumes every event and
/// broadcasts to its own locally-pinned SignalR clients — avoids the
/// shared-group / sticky-session fan-out gap.
///
/// MachineName-only (no PID): MachineName is the per-replica identity in
/// every supported deployment topology (Windows service hostnames, Linux
/// container hostnames, K8s pod names). Including PID would mint a fresh
/// consumer group on every process restart, leaving orphan groups behind in
/// __consumer_offsets indefinitely.
/// </summary>
public static class HostInstanceId
{
    private static readonly Lazy<string> _value = new(() => Environment.MachineName);

    public static string Value => _value.Value;
}
