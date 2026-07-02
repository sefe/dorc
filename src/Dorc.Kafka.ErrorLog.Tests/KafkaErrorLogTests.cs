using Confluent.Kafka;
using Microsoft.Extensions.Logging;
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
    public async Task InsertAsync_MapsExceptionTypeFromEntryIntoEnvelope()
    {
        var producer = new CapturingProducer();
        var sut = NewErrorLog(producer);
        var entry = new KafkaErrorLogEntry
        {
            Topic = "dorc.requests.new",
            Partition = 3,
            Offset = 42,
            ConsumerGroup = "g",
            Error = "boom",
            ExceptionType = "System.FormatException",
            OccurredAt = DateTimeOffset.UtcNow
        };

        await sut.InsertAsync(entry, CancellationToken.None);

        var envelope = producer.Produced.Single().Message.Value;
        Assert.AreEqual("System.FormatException", envelope.ExceptionType,
            "envelope must carry the actual failure exception type, not string.Empty");
    }

    [TestMethod]
    public async Task InsertAsync_NullExceptionType_MapsToEmptyString()
    {
        // The Avro contract declares ExceptionType as non-nullable string —
        // a null entry value must coalesce, not NRE / fail serialization.
        var producer = new CapturingProducer();
        var sut = NewErrorLog(producer);
        var entry = new KafkaErrorLogEntry
        {
            Topic = "dorc.requests.new",
            Error = "boom",
            ExceptionType = null
        };

        await sut.InsertAsync(entry, CancellationToken.None);

        Assert.AreEqual(string.Empty, producer.Produced.Single().Message.Value.ExceptionType);
    }

    [TestMethod]
    public async Task InsertAsync_TimeoutAbandonsProduce_LateFailureIsObservedAndLogged()
    {
        // WaitAsync(timeout) abandons the in-flight ProduceAsync; its
        // eventual failure must be observed (logged) rather than becoming an
        // unobserved-task exception.
        var producer = new HangingProducer();
        var logger = new CapturingLogger();
        var sut = NewErrorLog(producer, logger, produceTimeoutMs: 50);
        var entry = new KafkaErrorLogEntry { Topic = "dorc.requests.new", Error = "boom" };

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => sut.InsertAsync(entry, CancellationToken.None));

        producer.Completion.SetException(new KafkaException(new Error(ErrorCode.Local_MsgTimedOut)));

        var late = await WaitForLogEntry(logger, "dlq-produce-late-failure");
        Assert.AreEqual(LogLevel.Warning, late.Level);
        Assert.IsNotNull(late.Exception, "late failure must be observed with the actual exception attached");
    }

    [TestMethod]
    public async Task InsertAsync_TimeoutAbandonsProduce_LateSuccessLogsDoubleAccountingWarning()
    {
        // A late SUCCESS means the DLQ write landed even though InsertAsync
        // threw — the caller's structured-log fallback then ALSO records the
        // record. Benign double-accounting, but it must be visible.
        var producer = new HangingProducer();
        var logger = new CapturingLogger();
        var sut = NewErrorLog(producer, logger, produceTimeoutMs: 50);
        var entry = new KafkaErrorLogEntry { Topic = "dorc.requests.new", Error = "boom" };

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => sut.InsertAsync(entry, CancellationToken.None));

        producer.Completion.SetResult(new DeliveryResult<string, KafkaErrorEnvelope>
        {
            Topic = "dorc.requests.new.dlq",
            Partition = new Partition(0),
            Offset = new Offset(1),
            Status = PersistenceStatus.Persisted
        });

        var late = await WaitForLogEntry(logger, "dlq-produce-late-success");
        Assert.AreEqual(LogLevel.Warning, late.Level);
        StringAssert.Contains(late.Message, "double-accounted");
    }

    private static KafkaErrorLog NewErrorLog(
        IProducer<string, KafkaErrorEnvelope> producer,
        Microsoft.Extensions.Logging.ILogger<KafkaErrorLog>? logger = null,
        int? produceTimeoutMs = null)
    {
        var options = new KafkaErrorLogOptions();
        if (produceTimeoutMs is int t) options.ProduceTimeoutMs = t;
        var routes = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["dorc.requests.new"] = "dorc.requests.new.dlq"
        };
        return new KafkaErrorLog(producer, Options.Create(options), routes,
            logger ?? NullLogger<KafkaErrorLog>.Instance);
    }

    private static async Task<(LogLevel Level, string Message, Exception? Exception)> WaitForLogEntry(
        CapturingLogger logger, string fragment)
    {
        // The abandoned-task continuation runs asynchronously after the
        // outcome lands; poll briefly rather than racing it.
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            var match = logger.Snapshot().FirstOrDefault(e => e.Message.Contains(fragment));
            if (match.Message is not null) return match;
            await Task.Delay(20);
        }
        Assert.Fail($"Timed out waiting for log entry containing '{fragment}'. Got: [{string.Join(" | ", logger.Snapshot().Select(e => e.Message))}]");
        throw new InvalidOperationException("unreachable");
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

    /// <summary>Records every produced message; succeeds immediately.</summary>
    private sealed class CapturingProducer : ProducerStub
    {
        public List<(string Topic, Message<string, KafkaErrorEnvelope> Message)> Produced { get; } = new();

        public override Task<DeliveryResult<string, KafkaErrorEnvelope>> ProduceAsync(
            string topic, Message<string, KafkaErrorEnvelope> message, CancellationToken cancellationToken = default)
        {
            Produced.Add((topic, message));
            return Task.FromResult(new DeliveryResult<string, KafkaErrorEnvelope>
            {
                Topic = topic,
                Partition = new Partition(0),
                Offset = new Offset(Produced.Count),
                Message = message,
                Status = PersistenceStatus.Persisted
            });
        }
    }

    /// <summary>
    /// ProduceAsync ignores the cancellation token (mirrors librdkafka's
    /// best-effort cancellation) and completes only when the test resolves
    /// <see cref="Completion"/> — lets tests force the abandoned-task path.
    /// </summary>
    private sealed class HangingProducer : ProducerStub
    {
        public TaskCompletionSource<DeliveryResult<string, KafkaErrorEnvelope>> Completion { get; }
            = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override Task<DeliveryResult<string, KafkaErrorEnvelope>> ProduceAsync(
            string topic, Message<string, KafkaErrorEnvelope> message, CancellationToken cancellationToken = default)
            => Completion.Task;
    }

    private sealed class CapturingLogger : ILogger<KafkaErrorLog>
    {
        private readonly object _lock = new();
        private readonly List<(LogLevel Level, string Message, Exception? Exception)> _entries = new();

        public IReadOnlyList<(LogLevel Level, string Message, Exception? Exception)> Snapshot()
        {
            lock (_lock) return _entries.ToList();
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            lock (_lock) _entries.Add((logLevel, formatter(state, exception), exception));
        }
    }

    private abstract class ProducerStub : IProducer<string, KafkaErrorEnvelope>
    {
        public Handle Handle => throw new NotSupportedException();
        public string Name => "stub";
        public int AddBrokers(string brokers) => 0;
        public void SetSaslCredentials(string username, string password) { }
        public abstract Task<DeliveryResult<string, KafkaErrorEnvelope>> ProduceAsync(string topic, Message<string, KafkaErrorEnvelope> message, CancellationToken cancellationToken = default);
        public Task<DeliveryResult<string, KafkaErrorEnvelope>> ProduceAsync(TopicPartition topicPartition, Message<string, KafkaErrorEnvelope> message, CancellationToken cancellationToken = default)
            => ProduceAsync(topicPartition.Topic, message, cancellationToken);
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
