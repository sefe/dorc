namespace Dorc.Kafka.Events.Publisher;

public enum KafkaSubstrateMode
{
    /// <summary>
    /// Today's behaviour: SignalR is the only substrate; Kafka producer +
    /// consumer are not registered. Safe default for production until the
    /// S-011 cutover flips it.
    /// </summary>
    Direct = 0,

    /// <summary>
    /// Kafka is the authoritative substrate; producer publishes to Kafka,
    /// the API-side consumer projects to SignalR for UI continuity.
    /// </summary>
    Kafka = 1
}

public sealed class KafkaSubstrateOptions
{
    public const string SectionName = "Kafka:Substrate";

    /// <summary>
    /// Substrate selector for <c>DeploymentResultEventData</c> events
    /// (Monitor → UI). S-007 owns this slot.
    /// </summary>
    public KafkaSubstrateMode ResultsStatus { get; set; } = KafkaSubstrateMode.Direct;

    /// <summary>
    /// Substrate selector for Request-lifecycle events (PublishNewRequest,
    /// PublishRequestStatusChanged). S-006 owns this slot; declared here for
    /// forward-compat. S-007 does not act on it.
    /// </summary>
    public KafkaSubstrateMode RequestLifecycle { get; set; } = KafkaSubstrateMode.Direct;

    /// <summary>
    /// Replication factor for the dorc.results.status topic on first
    /// provisioning. Production = 3 (Aiven 3-broker cluster); single-broker
    /// dev compose overrides to 1.
    /// </summary>
    public short ResultsStatusReplicationFactor { get; set; } = 3;

    /// <summary>
    /// Substrate selector for the distributed-lock service used by Dorc.Monitor.
    /// S-005b owns this slot. Default <see cref="KafkaSubstrateMode.Direct"/>
    /// retains the upstream-registered implementation (RabbitMQ); <see cref="KafkaSubstrateMode.Kafka"/>
    /// replaces it with the consumer-group-partition-ownership implementation.
    /// </summary>
    public KafkaSubstrateMode DistributedLock { get; set; } = KafkaSubstrateMode.Direct;
}
