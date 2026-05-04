using System.Globalization;
using System.Text;
using Confluent.Kafka;

namespace Dorc.Kafka.Client.Envelope;

/// <summary>
/// Call-site opt-in envelope helpers per SPEC-S-002 R-6. Producers choose
/// to call WithEnvelope; consumers choose to call AsEnvelope. Neither
/// producer nor consumer builders require envelope use to function.
/// </summary>
public static class KafkaEnvelopeExtensions
{
    public static Message<TKey, TValue> WithEnvelope<TKey, TValue>(
        this Message<TKey, TValue> message,
        string correlationId,
        string messageId,
        string source,
        DateTimeOffset? timestamp = null)
    {
        message.Headers ??= new Headers();
        message.Headers.Add(KafkaEnvelopeHeaderNames.CorrelationId, Encoding.UTF8.GetBytes(correlationId));
        message.Headers.Add(KafkaEnvelopeHeaderNames.MessageId, Encoding.UTF8.GetBytes(messageId));
        message.Headers.Add(KafkaEnvelopeHeaderNames.Source, Encoding.UTF8.GetBytes(source));
        message.Headers.Add(
            KafkaEnvelopeHeaderNames.Timestamp,
            Encoding.UTF8.GetBytes((timestamp ?? DateTimeOffset.UtcNow).ToString("O", CultureInfo.InvariantCulture)));
        return message;
    }

    public static KafkaEnvelope<TValue> AsEnvelope<TKey, TValue>(this ConsumeResult<TKey, TValue> result)
        => result.Message.AsEnvelope();

    public static KafkaEnvelope<TValue> AsEnvelope<TKey, TValue>(this Message<TKey, TValue> message)
    {
        var dict = new Dictionary<string, string?>(StringComparer.Ordinal);
        if (message.Headers is { Count: > 0 })
        {
            foreach (var header in message.Headers)
            {
                var bytes = header.GetValueBytes();
                dict[header.Key] = bytes is null ? null : Encoding.UTF8.GetString(bytes);
            }
        }
        return new KafkaEnvelope<TValue>(message.Value, dict);
    }
}
