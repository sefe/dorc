using Confluent.Kafka;

namespace Dorc.Kafka.Events.Tests.Publisher;

/// <summary>
/// Minimal IProducer&lt;TKey,TValue&gt; implementation for AT-1 unit tests.
/// Only ProduceAsync (the single-arg overload S-007 uses) is meaningful;
/// any other call throws to surface unintended surface use.
/// </summary>
internal sealed class StubProducer<TKey, TValue> : IProducer<TKey, TValue>
{
    public List<(string Topic, Message<TKey, TValue> Message)> Produced { get; } = new();

    public Task<DeliveryResult<TKey, TValue>> ProduceAsync(
        string topic, Message<TKey, TValue> message, CancellationToken cancellationToken = default)
    {
        Produced.Add((topic, message));
        return Task.FromResult(new DeliveryResult<TKey, TValue>
        {
            Topic = topic,
            Partition = new Partition(0),
            Offset = new Offset(Produced.Count),
            Message = message,
            Status = PersistenceStatus.Persisted
        });
    }

    public Task<DeliveryResult<TKey, TValue>> ProduceAsync(
        TopicPartition topicPartition, Message<TKey, TValue> message, CancellationToken cancellationToken = default)
        => ProduceAsync(topicPartition.Topic, message, cancellationToken);

    public Handle Handle => throw new NotSupportedException();
    public string Name => "stub-producer";

    public int AddBrokers(string brokers) => 0;
    public void SetSaslCredentials(string username, string password) { }
    public void Produce(string topic, Message<TKey, TValue> message, Action<DeliveryReport<TKey, TValue>>? deliveryHandler = null) => throw new NotSupportedException();
    public void Produce(TopicPartition topicPartition, Message<TKey, TValue> message, Action<DeliveryReport<TKey, TValue>>? deliveryHandler = null) => throw new NotSupportedException();
    public int Poll(TimeSpan timeout) => 0;
    public int Flush(TimeSpan timeout) => 0;
    public void Flush(CancellationToken cancellationToken = default) { }
    public void InitTransactions(TimeSpan timeout) => throw new NotSupportedException();
    public void BeginTransaction() => throw new NotSupportedException();
    public void CommitTransaction(TimeSpan timeout) => throw new NotSupportedException();
    public void CommitTransaction() => throw new NotSupportedException();
    public void AbortTransaction(TimeSpan timeout) => throw new NotSupportedException();
    public void AbortTransaction() => throw new NotSupportedException();
    public void SendOffsetsToTransaction(IEnumerable<TopicPartitionOffset> offsets, IConsumerGroupMetadata groupMetadata, TimeSpan timeout) => throw new NotSupportedException();
    public void Dispose() { }
}
