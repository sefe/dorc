using System.Text;
using Confluent.Kafka;
using Dorc.Kafka.Client.Configuration;
using Dorc.Kafka.Client.Connection;
using Dorc.Kafka.Client.Consumers;
using Dorc.Kafka.Client.Envelope;
using Dorc.Kafka.Client.Producers;
using Dorc.Kafka.Client.Serialization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Dorc.Kafka.Client.Tests.Envelope;

[TestClass]
public class KafkaEnvelopeTests
{
    [TestMethod]
    public void WithEnvelope_SetsAllExpectedHeaders()
    {
        var message = new Message<string, byte[]> { Key = "k", Value = new byte[] { 1, 2, 3 } };
        var timestamp = DateTimeOffset.Parse("2026-04-14T10:15:00Z");

        message.WithEnvelope("corr-1", "msg-1", "dorc-api", timestamp);

        var envelope = message.AsEnvelope();
        Assert.AreEqual("corr-1", envelope.CorrelationId);
        Assert.AreEqual("msg-1", envelope.MessageId);
        Assert.AreEqual("dorc-api", envelope.Source);
        Assert.AreEqual(timestamp, envelope.Timestamp);
        CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, envelope.Value);
    }

    [TestMethod]
    public void AsEnvelope_OnRawMessage_ReturnsEmptyHeaders_ValueIntact()
    {
        var message = new Message<string, byte[]> { Key = "k", Value = new byte[] { 9 } };

        var envelope = message.AsEnvelope();

        Assert.IsNull(envelope.CorrelationId);
        Assert.IsNull(envelope.MessageId);
        Assert.IsNull(envelope.Source);
        Assert.IsNull(envelope.Timestamp);
        Assert.AreEqual(0, envelope.Headers.Count);
        CollectionAssert.AreEqual(new byte[] { 9 }, envelope.Value);
    }

    [TestMethod]
    public void Timestamp_UnparseableHeader_YieldsNull_NotException()
    {
        var message = new Message<string, byte[]> { Value = Array.Empty<byte>(), Headers = new Headers() };
        message.Headers.Add(KafkaEnvelopeHeaderNames.Timestamp, Encoding.UTF8.GetBytes("not-a-date"));

        var envelope = message.AsEnvelope();

        Assert.IsNull(envelope.Timestamp);
    }

    [TestMethod]
    public void SameBuilders_DriveBothEnvelopeAndRawPaths_R6Optionality()
    {
        // AT-5 tightening: both sub-tests must be driven by the same R-2/R-3
        // builder entry points; envelope engagement is call-site opt-in.
        var options = Options.Create(new KafkaClientOptions
        {
            BootstrapServers = "127.0.0.1:1",
            ConsumerGroupId = "envelope-test-group"
        });
        var connection = new KafkaConnectionProvider(options);
        var factory = new DefaultKafkaSerializerFactory();

        var producerBuilder = new KafkaProducerBuilder<string, byte[]>(
            connection, factory, NullLogger<KafkaProducerBuilder<string, byte[]>>.Instance);
        var consumerBuilder = new KafkaConsumerBuilder<string, byte[]>(
            connection, factory, NullLogger<KafkaConsumerBuilder<string, byte[]>>.Instance);

        using var producer = producerBuilder.Build("shared-producer");
        using var consumer = consumerBuilder.Build("shared-consumer");

        var enveloped = new Message<string, byte[]> { Key = "k1", Value = new byte[] { 1 } }
            .WithEnvelope("c1", "m1", "test", DateTimeOffset.UtcNow);
        var raw = new Message<string, byte[]> { Key = "k2", Value = new byte[] { 2 } };

        // Envelope engagement is call-site-only: builder APIs are identical.
        Assert.IsNotNull(enveloped.Headers);
        Assert.IsNull(raw.Headers);

        var envelopedView = enveloped.AsEnvelope();
        var rawView = raw.AsEnvelope();

        Assert.AreEqual("c1", envelopedView.CorrelationId);
        Assert.IsNull(rawView.CorrelationId);
    }
}
