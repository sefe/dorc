namespace Dorc.Kafka.Lock.Configuration;

/// <summary>
/// Options for the Kafka-based distributed lock substrate.
/// Topic name moved to <c>KafkaTopicsOptions.Locks</c>.
/// </summary>
public sealed class KafkaLocksOptions
{
    public const string SectionName = "Kafka:Locks";

    /// <summary>
    /// Master enable flag surfaced via <c>IDistributedLockService.IsEnabled</c>.
    /// Independent of the substrate-selector flag: the substrate decides which
    /// type is registered; this decides whether the active type reports
    /// enabled.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Partition count. Immutable post-cutover — the validator
    /// does not enforce that because it's an operational constraint, but the
    /// value is logged at startup and the  runbook flags it.
    /// </summary>
    public int PartitionCount { get; set; } = 12;

    /// <summary>Replication factor for first provisioning. Dev compose overrides to 1.</summary>
    public short ReplicationFactor { get; set; } = 3;

    /// <summary>
    /// All Monitor replicas share this group id so partitions split across the
    /// fleet (mutual-exclusion model), distinct from per-replica group
    /// id (fan-out model).
    /// </summary>
    public string ConsumerGroupId { get; set; } = "dorc.monitor.locks";

    /// <summary>
    /// Upper bound (ms) a single <c>TryAcquireLockAsync</c> call waits for
    /// partition ownership before returning null. Deliberately short: callers
    /// (the Monitor) poll, so a contested resource should fail fast and back
    /// off rather than park a task for the caller's lease duration.
    /// </summary>
    public int AcquireWaitMs { get; set; } = 5_000;

    /// <summary>
    /// Connectivity watchdog (split-brain guard). If the coordinator observes
    /// no successful broker contact for this long, every held lock reports
    /// lost (LockLostToken fires), because the broker may already have
    /// reassigned our partitions to a peer after <c>session.timeout.ms</c>
    /// without librdkafka surfacing a revoke/lost callback locally.
    /// Null (default) resolves to <c>max(session.timeout.ms, 30s)</c>.
    /// </summary>
    public int? LivenessTimeoutMs { get; set; }
}
