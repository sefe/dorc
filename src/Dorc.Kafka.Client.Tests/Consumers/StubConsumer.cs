using Confluent.Kafka;

namespace Dorc.Kafka.Client.Tests.Consumers;

/// <summary>
/// Minimal IConsumer implementation for driving KafkaRebalanceHandlers in
/// isolation. Only the Assignment property is meaningful; any other call
/// indicates the tests are exercising unintended surface.
/// </summary>
internal sealed class StubConsumer<TKey, TValue> : IConsumer<TKey, TValue>
{
    public StubConsumer(IEnumerable<TopicPartition>? assignment = null)
    {
        Assignment = assignment?.ToList() ?? new List<TopicPartition>();
    }

    public List<TopicPartition> Assignment { get; set; }

    public Handle Handle => throw new NotSupportedException();
    public string Name => "stub";
    public string MemberId => "stub-member";
    public List<string> Subscription => new();
    public IConsumerGroupMetadata ConsumerGroupMetadata => throw new NotSupportedException();

    public int AddBrokers(string brokers) => 0;
    public void SetSaslCredentials(string username, string password) { }
    public void Assign(TopicPartition partition) { }
    public void Assign(TopicPartitionOffset partition) { }
    public void Assign(IEnumerable<TopicPartitionOffset> partitions) { }
    public void Assign(IEnumerable<TopicPartition> partitions) { }
    public void Close() { }
    public List<TopicPartitionOffset> Commit() => new();
    public void Commit(IEnumerable<TopicPartitionOffset> offsets) { }
    public void Commit(ConsumeResult<TKey, TValue> result) { }
    public List<TopicPartitionOffset> Committed(TimeSpan timeout) => new();
    public List<TopicPartitionOffset> Committed(IEnumerable<TopicPartition> partitions, TimeSpan timeout) => new();
    public ConsumeResult<TKey, TValue>? Consume(int millisecondsTimeout) => null;
    public ConsumeResult<TKey, TValue>? Consume(CancellationToken cancellationToken = default) => null;
    public ConsumeResult<TKey, TValue>? Consume(TimeSpan timeout) => null;
    public WatermarkOffsets GetWatermarkOffsets(TopicPartition topicPartition) => new(Offset.Unset, Offset.Unset);
    public void IncrementalAssign(IEnumerable<TopicPartitionOffset> partitions) { }
    public void IncrementalAssign(IEnumerable<TopicPartition> partitions) { }
    public void IncrementalUnassign(IEnumerable<TopicPartition> partitions) { }
    public List<TopicPartitionOffset> OffsetsForTimes(IEnumerable<TopicPartitionTimestamp> timestampsToSearch, TimeSpan timeout) => new();
    public void Pause(IEnumerable<TopicPartition> partitions) { }
    public Offset Position(TopicPartition partition) => Offset.Unset;
    public WatermarkOffsets QueryWatermarkOffsets(TopicPartition topicPartition, TimeSpan timeout) => new(Offset.Unset, Offset.Unset);
    public void Resume(IEnumerable<TopicPartition> partitions) { }
    public void Seek(TopicPartitionOffset tpo) { }
    public void StoreOffset(ConsumeResult<TKey, TValue> result) { }
    public void StoreOffset(TopicPartitionOffset offset) { }
    public void Subscribe(string topic) { }
    public void Subscribe(IEnumerable<string> topics) { }
    public void Unassign() { }
    public void Unsubscribe() { }

    public void Dispose() { }
}
