namespace Dorc.Kafka.Client.Envelope;

public static class KafkaEnvelopeHeaderNames
{
    public const string CorrelationId = "dorc.correlation-id";
    public const string MessageId = "dorc.message-id";
    public const string Source = "dorc.source";
    public const string Timestamp = "dorc.timestamp";
}
