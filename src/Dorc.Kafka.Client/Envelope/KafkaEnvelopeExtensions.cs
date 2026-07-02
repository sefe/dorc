using System.Globalization;
using System.Text;
using Confluent.Kafka;

namespace Dorc.Kafka.Client.Envelope;

/// <summary>
/// Call-site opt-in envelope helpers. Producers choose
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
        // Headers.Add appends, so a repeated WithEnvelope call (e.g. a retry
        // path reusing the Message instance) would produce duplicate header
        // keys; Kafka consumers conventionally read last-wins but other
        // tooling surfaces all duplicates. Remove-then-add keeps exactly one.
        SetHeader(message.Headers, KafkaEnvelopeHeaderNames.CorrelationId, correlationId);
        SetHeader(message.Headers, KafkaEnvelopeHeaderNames.MessageId, messageId);
        SetHeader(message.Headers, KafkaEnvelopeHeaderNames.Source, source);
        SetHeader(
            message.Headers,
            KafkaEnvelopeHeaderNames.Timestamp,
            (timestamp ?? DateTimeOffset.UtcNow).ToString("O", CultureInfo.InvariantCulture));
        return message;
    }

    private static void SetHeader(Headers headers, string key, string value)
    {
        headers.Remove(key);
        headers.Add(key, Encoding.UTF8.GetBytes(value));
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
