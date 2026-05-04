namespace Dorc.Kafka.Client.Observability;

/// <summary>
/// Sink for Confluent.Kafka's per-consumer statistics JSON blob. Concrete
/// implementations parse <c>consumer_lag</c> and consumer state out of the
/// payload and publish them as observable gauges through <c>System.Diagnostics.Metrics</c>;
/// operators wire those into OTLP / Prometheus / etc. The default no-op
/// implementation lets non-instrumented configurations skip metrics
/// without changing the consumer wiring.
/// </summary>
public interface IKafkaConsumerMetrics
{
    /// <summary>
    /// Called from each consumer's statistics callback (configured via
    /// <c>statistics.interval.ms</c>) with the raw librdkafka stats JSON.
    /// Implementations must not throw — failures during parsing should be
    /// swallowed or logged so a malformed payload doesn't crash the
    /// consumer's poll loop.
    /// </summary>
    /// <param name="consumerName">Logical consumer name; used as a metric
    /// dimension so multiple consumer types in the same process don't
    /// collide.</param>
    /// <param name="statsJson">Raw librdkafka statistics payload.</param>
    void RecordStatistics(string consumerName, string statsJson);
}

/// <summary>
/// Default sink — discards the payload. Used when the application doesn't
/// register a real <see cref="IKafkaConsumerMetrics"/>; consumers continue
/// to receive statistics callbacks but the metrics path is dormant.
/// </summary>
public sealed class NoOpKafkaConsumerMetrics : IKafkaConsumerMetrics
{
    public void RecordStatistics(string consumerName, string statsJson) { }
}
