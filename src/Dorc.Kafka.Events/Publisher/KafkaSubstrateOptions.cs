namespace Dorc.Kafka.Events.Publisher;

/// <summary>
/// Kafka substrate operational options. Post-SPEC-S-009 the substrate-selector
/// enum + flag fields are removed; Kafka is unconditional. Only the
/// operational replication-factor knob remains, kept on this type for
/// backwards-compatible config-binding (existing appsettings that already set
/// <c>Kafka:Substrate:ResultsStatusReplicationFactor</c> continue to work).
/// </summary>
public sealed class KafkaSubstrateOptions
{
    public const string SectionName = "Kafka:Substrate";

    /// <summary>
    /// Replication factor for the <c>dorc.results.status</c> topic on first
    /// provisioning. Production = 3 (Aiven 3-broker cluster); single-broker
    /// dev compose overrides to 1.
    /// </summary>
    public short ResultsStatusReplicationFactor { get; set; } = 3;
}
