using Confluent.Kafka;

[assembly: Parallelize(Workers = 1, Scope = ExecutionScope.MethodLevel)]

namespace Dorc.Kafka.SmokeTests;

// AT-2 smoke test: produces one message and consumes it back through the
// local docker-compose Kafka. Verifies bootstrap connectivity and basic
// producer/consumer round-trip against a PLAINTEXT broker.
//
// This is a throwaway harness. The S-002 client layer supersedes it.
[TestClass]
public class RoundTripTest
{
    private static readonly string BootstrapServers =
        Environment.GetEnvironmentVariable("KAFKA_BOOTSTRAP") ?? "localhost:9092";

    [TestMethod]
    [Timeout(60_000, CooperativeCancellation = true)]
    public async Task ProduceAndConsume_SingleMessage_RoundTrips()
    {
        var topic = $"dorc.smoketest.{Guid.NewGuid():N}";
        var key = Guid.NewGuid().ToString("N");
        var value = $"smoke-{DateTimeOffset.UtcNow:O}";

        var producerConfig = new ProducerConfig
        {
            BootstrapServers = BootstrapServers,
            Acks = Acks.All,
            EnableIdempotence = true,
        };

        using (var producer = new ProducerBuilder<string, string>(producerConfig).Build())
        {
            var deliveryResult = await producer.ProduceAsync(topic,
                new Message<string, string> { Key = key, Value = value });
            Assert.AreEqual(PersistenceStatus.Persisted, deliveryResult.Status);
        }

        var consumerConfig = new ConsumerConfig
        {
            BootstrapServers = BootstrapServers,
            GroupId = $"dorc-smoketest-{Guid.NewGuid():N}",
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false,
        };

        using var consumer = new ConsumerBuilder<string, string>(consumerConfig).Build();
        consumer.Subscribe(topic);

        var consumed = consumer.Consume(TimeSpan.FromSeconds(30));
        Assert.IsNotNull(consumed, "No message consumed within 30s");
        Assert.AreEqual(key, consumed.Message.Key);
        Assert.AreEqual(value, consumed.Message.Value);

        consumer.Close();
    }
}
