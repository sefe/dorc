namespace Dorc.Kafka.Lock.Configuration;

/// <summary>
/// Options for the Kafka-based distributed lock substrate.
/// Topic name moved to <c>KafkaTopicsOptions.Locks</c>.
/// </summary>
public sealed class KafkaLocksOptions
{
    public const string SectionName = "Kafka:Locks";

    /// <summary>
    /// The shared default lock <see cref="ConsumerGroupId"/>. This value is
    /// deliberately REJECTED by <c>KafkaLocksOptionsValidator</c> at startup:
    /// every deployment tier must set a distinct, explicit group id (see
    /// <see cref="ConsumerGroupId"/>). Kept as a single const so the default and
    /// the validator guard can never drift apart (audit CR#4/#8).
    /// </summary>
    public const string SharedDefaultConsumerGroupId = "dorc.monitor.locks";

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
    /// All Monitor replicas for a given deployment tier share this group id so
    /// partitions split across the fleet (mutual-exclusion model), distinct from
    /// per-replica group id (fan-out model).
    ///
    /// <b>IMPORTANT:</b> This value MUST differ between Prod and NonProd monitor
    /// deployments. If both share the same group id, Kafka assigns lock partitions
    /// across all monitors regardless of tier — a NonProd lock partition may land
    /// on the Prod monitor, which never processes NonProd work, silently stalling
    /// NonProd deployments. Set this to a tier-specific value in each environment's
    /// configuration (e.g. <c>dorc.monitor.locks.prod</c> / <c>dorc.monitor.locks.nonprod</c>).
    /// </summary>
    public string ConsumerGroupId { get; set; } = SharedDefaultConsumerGroupId;

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
