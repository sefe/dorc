using Confluent.Kafka;
using Microsoft.Extensions.Logging;

namespace Dorc.Kafka.Client.Consumers;

/// <summary>
/// Structured logging for Kafka consumer lifecycle callbacks. The log-shape
/// contract is the R-4 requirement from SPEC-S-002 §4.3: operators must be
/// able to tell what changed and what the new steady state is from a single
/// entry. Exposed as a standalone class so AT-3's lost-partition-path test
/// can invoke the same handler R-3 production consumers use (per the spec's
/// documented substitution), not a test double.
/// </summary>
public sealed class KafkaRebalanceHandlers<TKey, TValue>
{
    private readonly ILogger _logger;
    private readonly string _consumerName;

    public KafkaRebalanceHandlers(ILogger logger, string consumerName)
    {
        _logger = logger;
        _consumerName = consumerName;
    }

    public void OnPartitionsAssigned(IConsumer<TKey, TValue> consumer, List<TopicPartition> partitions)
    {
        // Use Union so the rendered post-assignment set is correct regardless of whether
        // librdkafka has merged the incoming partitions into Assignment by the time the
        // callback fires (the behaviour is version-sensitive).
        var all = consumer.Assignment
            .Union(partitions)
            .Select(p => p.Partition.Value)
            .OrderBy(v => v);
        _logger.LogInformation(
            "Kafka consumer '{ConsumerName}' partitions incrementally assigned: [{AssignedPartitions}], all: [{AllPartitions}]",
            _consumerName,
            string.Join(",", partitions.Select(p => p.Partition.Value)),
            string.Join(",", all));
    }

    public void OnPartitionsRevoked(IConsumer<TKey, TValue> consumer, List<TopicPartitionOffset> partitions)
    {
        var remaining = consumer.Assignment.Where(atp => partitions.All(rtp => rtp.TopicPartition != atp));
        _logger.LogInformation(
            "Kafka consumer '{ConsumerName}' partitions incrementally revoked: [{RevokedPartitions}], remaining: [{RemainingPartitions}]",
            _consumerName,
            string.Join(",", partitions.Select(p => p.Partition.Value)),
            string.Join(",", remaining.Select(p => p.Partition.Value)));
    }

    public void OnPartitionsLost(IConsumer<TKey, TValue> consumer, List<TopicPartitionOffset> partitions)
    {
        _logger.LogWarning(
            "Kafka consumer '{ConsumerName}' partitions were lost: [{LostPartitions}]",
            _consumerName,
            string.Join(",", partitions.Select(p => p.Partition.Value)));
    }

    public void OnError(IConsumer<TKey, TValue> consumer, Error error)
    {
        _logger.LogError(
            "Kafka consumer '{ConsumerName}' error: {Reason} (Code={Code}, Fatal={Fatal})",
            _consumerName, error.Reason, error.Code, error.IsFatal);
    }

    public void OnStatistics(IConsumer<TKey, TValue> consumer, string json)
    {
        _logger.LogDebug("Kafka consumer '{ConsumerName}' statistics: {Statistics}", _consumerName, json);
    }
}
