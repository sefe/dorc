namespace Dorc.Kafka.ErrorLog;

/// <summary>
/// Avro-serialised DLQ envelope. Carries the source-message metadata, the
/// raw payload (truncated if it exceeds <see cref="KafkaErrorLogOptions.MaxPayloadBytes"/>),
/// and the captured exception so an operator can replay or triage offline.
///
/// Chr.Avro picks up the property names directly; the record's parameterless
/// constructor exists for the deserialiser. Schema generation lives in
/// <c>DorcEventSchemas</c>; the registry-acknowledged snapshot is committed
/// under <c>docs/kafka-migration/schemas/latest/</c>.
/// </summary>
public record KafkaErrorEnvelope(
    string SourceTopic,
    int SourcePartition,
    long SourceOffset,
    string ConsumerGroup,
    string? MessageKey,
    byte[]? RawPayload,
    bool PayloadTruncated,
    string ExceptionType,
    string ExceptionMessage,
    string? ExceptionStack,
    DateTimeOffset OccurredAt,
    DateTimeOffset LoggedAt
)
{
    public KafkaErrorEnvelope() : this(
        string.Empty,
        0,
        0L,
        string.Empty,
        null,
        null,
        false,
        string.Empty,
        string.Empty,
        null,
        DateTimeOffset.UtcNow,
        DateTimeOffset.UtcNow
    )
    {
    }
}
