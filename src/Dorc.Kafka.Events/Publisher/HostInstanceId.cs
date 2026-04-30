namespace Dorc.Kafka.Events.Publisher;

/// <summary>
/// Process-stable identifier used to suffix the per-replica Kafka consumer
/// group id (per SPEC-S-007 R-2 multi-replica fan-out design). Each replica
/// gets its own consumer group so every replica consumes every event and
/// broadcasts to its own locally-pinned SignalR clients — avoids the
/// shared-group / sticky-session fan-out gap.
/// </summary>
public static class HostInstanceId
{
    private static readonly Lazy<string> _value = new(() =>
        $"{Environment.MachineName}-{Environment.ProcessId}");

    public static string Value => _value.Value;
}
