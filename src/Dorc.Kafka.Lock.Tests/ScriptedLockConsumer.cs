using Confluent.Kafka;

namespace Dorc.Kafka.Lock.Tests;

/// <summary>
/// Scripted <see cref="IConsumer{TKey,TValue}"/> for driving the coordinator's
/// consume loop without a broker. Only the members the loop touches are
/// meaningful (Consume / Subscribe / Close / Dispose); everything else throws.
/// </summary>
internal sealed class ScriptedLockConsumer : IConsumer<byte[], byte[]>
{
    private readonly List<string> _subscribedTopics = new();

    /// <summary>Invoked for every Consume call; throw to script errors.</summary>
    public Func<TimeSpan, ConsumeResult<byte[], byte[]>?>? OnConsume { get; set; }

    /// <summary>
    /// Invoked for the coordinator's connectivity probe (Committed); throw to
    /// script probe failure (broker unreachable). Default: succeed, matching a
    /// healthy broker.
    /// </summary>
    public Func<List<TopicPartitionOffset>>? OnCommitted { get; set; }

    public bool Closed { get; private set; }
    public bool Disposed { get; private set; }
    public IReadOnlyList<string> SubscribedTopics => _subscribedTopics;

    public ConsumeResult<byte[], byte[]> Consume(int millisecondsTimeout)
        => Consume(TimeSpan.FromMilliseconds(millisecondsTimeout));

    public ConsumeResult<byte[], byte[]> Consume(CancellationToken cancellationToken = default)
    {
        // Honor the token so coordinator tests that cancel via stoppingToken
        // see OperationCanceledException on the Consume call, matching
        // librdkafka's real behaviour.
        cancellationToken.ThrowIfCancellationRequested();
        return Consume(TimeSpan.FromMilliseconds(100));
    }

    public ConsumeResult<byte[], byte[]> Consume(TimeSpan timeout)
    {
        if (OnConsume is not null) return OnConsume(timeout)!;
        Thread.Sleep(5);
        return null!;
    }

    public void Subscribe(IEnumerable<string> topics) => _subscribedTopics.AddRange(topics);

    public void Subscribe(string topic) => _subscribedTopics.Add(topic);

    public void Unsubscribe() => _subscribedTopics.Clear();

    public void Close() => Closed = true;

    public void Dispose() => Disposed = true;

    public List<string> Subscription => new(_subscribedTopics);

    public List<TopicPartition> Assignment => new();

    public string MemberId => "scripted-member";

    public IConsumerGroupMetadata ConsumerGroupMetadata => throw new NotSupportedException();

    public Handle Handle => throw new NotSupportedException();

    public string Name => "scripted-lock-consumer";

    public void Assign(TopicPartition partition) { }
    public void Assign(TopicPartitionOffset partition) { }
    public void Assign(IEnumerable<TopicPartitionOffset> partitions) { }
    public void Assign(IEnumerable<TopicPartition> partitions) { }
    public void IncrementalAssign(IEnumerable<TopicPartitionOffset> partitions) { }
    public void IncrementalAssign(IEnumerable<TopicPartition> partitions) { }
    public void IncrementalUnassign(IEnumerable<TopicPartition> partitions) { }
    public void Unassign() { }
    public void StoreOffset(ConsumeResult<byte[], byte[]> result) { }
    public void StoreOffset(TopicPartitionOffset offset) { }
    public List<TopicPartitionOffset> Committed(TimeSpan timeout) => new();
    public List<TopicPartitionOffset> Committed(IEnumerable<TopicPartition> partitions, TimeSpan timeout)
        => OnCommitted is not null ? OnCommitted() : new();
    public List<TopicPartitionOffset> Commit() => new();
    public void Commit(IEnumerable<TopicPartitionOffset> offsets) { }
    public void Commit(ConsumeResult<byte[], byte[]> result) { }
    public void Seek(TopicPartitionOffset tpo) { }
    public void Pause(IEnumerable<TopicPartition> partitions) { }
    public void Resume(IEnumerable<TopicPartition> partitions) { }
    public Offset Position(TopicPartition partition) => Offset.Unset;
    public List<TopicPartitionOffset> OffsetsForTimes(IEnumerable<TopicPartitionTimestamp> timestampsToSearch, TimeSpan timeout) => new();
    public WatermarkOffsets GetWatermarkOffsets(TopicPartition topicPartition) => new(Offset.Unset, Offset.Unset);
    public WatermarkOffsets QueryWatermarkOffsets(TopicPartition topicPartition, TimeSpan timeout) => new(Offset.Unset, Offset.Unset);
    public int AddBrokers(string brokers) => 0;
    public void SetSaslCredentials(string username, string password) { }
}
