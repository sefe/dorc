using Confluent.Kafka;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Dorc.Kafka.ErrorLog.Tests;

[TestClass]
public class KafkaErrorLogTests
{
    [TestMethod]
    public void TruncatePayload_NullPayload_ReturnsNullNotTruncated()
    {
        var (payload, truncated) = KafkaErrorLog.TruncatePayload(null, 100);
        Assert.IsNull(payload);
        Assert.IsFalse(truncated);
    }

    [TestMethod]
    public void TruncatePayload_UnderCap_ReturnsOriginalNotTruncated()
    {
        var raw = new byte[] { 1, 2, 3, 4, 5 };
        var (payload, truncated) = KafkaErrorLog.TruncatePayload(raw, 100);
        CollectionAssert.AreEqual(raw, payload);
        Assert.IsFalse(truncated);
    }

    [TestMethod]
    public void TruncatePayload_OverCap_ReturnsTruncatedFlagged()
    {
        var raw = new byte[200];
        for (var i = 0; i < raw.Length; i++) raw[i] = (byte)i;

        var (payload, truncated) = KafkaErrorLog.TruncatePayload(raw, 50);

        Assert.IsNotNull(payload);
        Assert.AreEqual(50, payload!.Length);
        Assert.IsTrue(truncated);
        for (var i = 0; i < 50; i++) Assert.AreEqual((byte)i, payload[i]);
    }

    [TestMethod]
    public async Task InsertAsync_UnmappedSourceTopic_ThrowsDlqNotConfigured()
    {
        var routes = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["dorc.requests.new"] = "dorc.requests.new.dlq"
        };
        var sut = new KafkaErrorLog(
            new NoopProducer(),
            Options.Create(new KafkaErrorLogOptions()),
            routes,
            NullLogger<KafkaErrorLog>.Instance);

        var entry = new KafkaErrorLogEntry { Topic = "dorc.results.status" };

        var ex = await Assert.ThrowsExactlyAsync<DlqNotConfiguredException>(
            () => sut.InsertAsync(entry, CancellationToken.None));
        Assert.AreEqual("dorc.results.status", ex.SourceTopic);
    }

    private sealed class NoopProducer : IProducer<string, KafkaErrorEnvelope>
    {
        public Handle Handle => throw new NotSupportedException();
        public string Name => "noop";
        public int AddBrokers(string brokers) => 0;
        public void SetSaslCredentials(string username, string password) { }
        public Task<DeliveryResult<string, KafkaErrorEnvelope>> ProduceAsync(string topic, Message<string, KafkaErrorEnvelope> message, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public Task<DeliveryResult<string, KafkaErrorEnvelope>> ProduceAsync(TopicPartition topicPartition, Message<string, KafkaErrorEnvelope> message, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public void Produce(string topic, Message<string, KafkaErrorEnvelope> message, Action<DeliveryReport<string, KafkaErrorEnvelope>>? deliveryHandler = null) => throw new NotSupportedException();
        public void Produce(TopicPartition topicPartition, Message<string, KafkaErrorEnvelope> message, Action<DeliveryReport<string, KafkaErrorEnvelope>>? deliveryHandler = null) => throw new NotSupportedException();
        public int Poll(TimeSpan timeout) => 0;
        public int Flush(TimeSpan timeout) => 0;
        public void Flush(CancellationToken cancellationToken = default) { }
        public void InitTransactions(TimeSpan timeout) { }
        public void BeginTransaction() { }
        public void CommitTransaction(TimeSpan timeout) { }
        public void CommitTransaction() { }
        public void AbortTransaction(TimeSpan timeout) { }
        public void AbortTransaction() { }
        public void SendOffsetsToTransaction(IEnumerable<TopicPartitionOffset> offsets, IConsumerGroupMetadata groupMetadata, TimeSpan timeout) { }
        public void Dispose() { }
    }
}
