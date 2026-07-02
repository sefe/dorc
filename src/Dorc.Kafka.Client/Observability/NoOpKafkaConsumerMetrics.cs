namespace Dorc.Kafka.Client.Observability;

/// <summary>
/// Default sink — discards the payload. Used when the application doesn't
/// register a real <see cref="IKafkaConsumerMetrics"/>; consumers continue
/// to receive statistics callbacks but the metrics path is dormant.
/// </summary>
public sealed class NoOpKafkaConsumerMetrics : IKafkaConsumerMetrics
{
    public void RecordStatistics(string consumerName, string statsJson) { }
}
