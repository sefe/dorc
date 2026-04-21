using System.Text;
using Confluent.Kafka;

namespace Dorc.Kafka.Client.IntegrationTests;

[TestClass]
public class RoundTripTests
{
    // AT-2: producer/consumer round-trip + manual-commit resume

    [TestMethod]
    public async Task Produce_Then_Consume_RoundTripsKeyAndValue()
    {
        var topic = KafkaTestHarness.NewTopicName("at2-roundtrip");
        await KafkaTestHarness.CreateTopicAsync(topic, partitions: 1);
        try
        {
            var options = KafkaTestHarness.DefaultOptions();

            using (var producer = KafkaTestHarness.ProducerBuilder<string, byte[]>(options).Build("at2-producer"))
            {
                var result = await producer.ProduceAsync(topic, new Message<string, byte[]>
                {
                    Key = "order-42",
                    Value = Encoding.UTF8.GetBytes("hello-kafka")
                });
                Assert.AreEqual(PersistenceStatus.Persisted, result.Status);
            }

            using var consumer = KafkaTestHarness.ConsumerBuilder<string, byte[]>(options).Build("at2-consumer");
            consumer.Subscribe(topic);

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var consumed = consumer.Consume(cts.Token);

            Assert.AreEqual("order-42", consumed.Message.Key);
            Assert.AreEqual("hello-kafka", Encoding.UTF8.GetString(consumed.Message.Value));
            consumer.Commit(consumed);
        }
        finally
        {
            await KafkaTestHarness.DeleteTopicAsync(topic);
        }
    }

    [TestMethod]
    public async Task ManualCommit_ConsumerRestart_ResumesPastCommittedOffset()
    {
        var topic = KafkaTestHarness.NewTopicName("at2-resume");
        await KafkaTestHarness.CreateTopicAsync(topic, partitions: 1);
        try
        {
            var options = KafkaTestHarness.DefaultOptions();

            using (var producer = KafkaTestHarness.ProducerBuilder<string, byte[]>(options).Build("at2-producer"))
            {
                await producer.ProduceAsync(topic, new Message<string, byte[]> { Key = "k", Value = new byte[] { 1 } });
                await producer.ProduceAsync(topic, new Message<string, byte[]> { Key = "k", Value = new byte[] { 2 } });
                await producer.ProduceAsync(topic, new Message<string, byte[]> { Key = "k", Value = new byte[] { 3 } });
                producer.Flush(TimeSpan.FromSeconds(5));
            }

            var consumerBuilder = KafkaTestHarness.ConsumerBuilder<string, byte[]>(options);

            using (var consumer = consumerBuilder.Build("at2-consumer-phase1"))
            {
                consumer.Subscribe(topic);
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                var first = consumer.Consume(cts.Token);
                CollectionAssert.AreEqual(new byte[] { 1 }, first.Message.Value);
                consumer.Commit(first);
                consumer.Close();
            }

            using (var consumer = consumerBuilder.Build("at2-consumer-phase2"))
            {
                consumer.Subscribe(topic);
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                var next = consumer.Consume(cts.Token);
                CollectionAssert.AreEqual(new byte[] { 2 }, next.Message.Value);
                consumer.Commit(next);
            }
        }
        finally
        {
            await KafkaTestHarness.DeleteTopicAsync(topic);
        }
    }
}
