namespace Dorc.Kafka.ErrorLog.Tests;

[TestClass]
public class KafkaErrorEnvelopeTests
{
    // The parameterless constructor exists for the Avro deserialiser; its
    // defaults must be replay-safe (no nulls where the schema says string).
    [TestMethod]
    public void ParameterlessConstructor_ProducesSchemaSafeDefaults()
    {
        var envelope = new KafkaErrorEnvelope();

        Assert.AreEqual(string.Empty, envelope.SourceTopic);
        Assert.AreEqual(0, envelope.SourcePartition);
        Assert.AreEqual(0L, envelope.SourceOffset);
        Assert.AreEqual(string.Empty, envelope.ConsumerGroup);
        Assert.IsNull(envelope.MessageKey);
        Assert.IsNull(envelope.RawPayload);
        Assert.IsFalse(envelope.PayloadTruncated);
        Assert.AreEqual(string.Empty, envelope.ExceptionType);
        Assert.AreEqual(string.Empty, envelope.ExceptionMessage);
        Assert.IsNull(envelope.ExceptionStack);
    }

    [TestMethod]
    public void PrimaryConstructor_RoundTripsAllFields()
    {
        var occurred = DateTimeOffset.Parse("2026-06-01T10:00:00Z");
        var logged = DateTimeOffset.Parse("2026-06-01T10:00:01Z");
        var payload = new byte[] { 1, 2, 3 };

        var envelope = new KafkaErrorEnvelope(
            "dorc.requests.new", 4, 1234L, "dorc-monitor", "42",
            payload, PayloadTruncated: true,
            "System.InvalidOperationException", "boom", "stack",
            occurred, logged);

        Assert.AreEqual("dorc.requests.new", envelope.SourceTopic);
        Assert.AreEqual(4, envelope.SourcePartition);
        Assert.AreEqual(1234L, envelope.SourceOffset);
        Assert.AreEqual("dorc-monitor", envelope.ConsumerGroup);
        Assert.AreEqual("42", envelope.MessageKey);
        CollectionAssert.AreEqual(payload, envelope.RawPayload);
        Assert.IsTrue(envelope.PayloadTruncated);
        Assert.AreEqual("System.InvalidOperationException", envelope.ExceptionType);
        Assert.AreEqual("boom", envelope.ExceptionMessage);
        Assert.AreEqual("stack", envelope.ExceptionStack);
        Assert.AreEqual(occurred, envelope.OccurredAt);
        Assert.AreEqual(logged, envelope.LoggedAt);
    }
}
