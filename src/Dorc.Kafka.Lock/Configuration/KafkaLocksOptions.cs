namespace Dorc.Kafka.Lock.Configuration;

/// <summary>
/// Options for the Kafka-based distributed lock substrate (SPEC-S-005b R-4).
/// Topic name moved to <c>KafkaTopicsOptions.Locks</c> per SPEC-S-017.
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
    /// Partition count. Immutable post-cutover (ADR-S-005 §4 #2) — the validator
    /// does not enforce that because it's an operational constraint, but the
    /// value is logged at startup and the S-010 runbook flags it.
    /// </summary>
    public int PartitionCount { get; set; } = 12;

    /// <summary>Replication factor for first provisioning. Dev compose overrides to 1.</summary>
    public short ReplicationFactor { get; set; } = 3;

    /// <summary>
    /// All Monitor replicas share this group id so partitions split across the
    /// fleet (mutual-exclusion model), distinct from S-007's per-replica group
    /// id (fan-out model).
    /// </summary>
    public string ConsumerGroupId { get; set; } = "dorc.monitor.locks";

    /// <summary>
    /// Default wait-cap (ms) when the caller passes <c>leaseTimeMs &lt;= 0</c>.
    /// </summary>
    public int LockWaitDefaultTimeoutMs { get; set; } = 30_000;
}
