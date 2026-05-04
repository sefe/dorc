using System.Globalization;

namespace Dorc.Kafka.Client.Envelope;

public sealed record KafkaEnvelope<TValue>(TValue Value, IReadOnlyDictionary<string, string?> Headers)
{
    public string? CorrelationId => TryGet(KafkaEnvelopeHeaderNames.CorrelationId);

    public string? MessageId => TryGet(KafkaEnvelopeHeaderNames.MessageId);

    public string? Source => TryGet(KafkaEnvelopeHeaderNames.Source);

    public DateTimeOffset? Timestamp
    {
        get
        {
            var raw = TryGet(KafkaEnvelopeHeaderNames.Timestamp);
            return DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var value)
                ? value
                : null;
        }
    }

    private string? TryGet(string key) => Headers.TryGetValue(key, out var v) ? v : null;
}
