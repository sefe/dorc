using Confluent.Kafka;
using Dorc.Core.Events;
using Dorc.Kafka.Events.Schemas;

namespace Dorc.Kafka.Events.IntegrationTests;

[TestClass]
public class AvroRoundTripTests
{
    [TestMethod]
    public async Task AT2_ProduceRegistersSubject_SchemaMatchesCanonical_AndSecondBuildIsIdempotent()
    {
        var topic = AvroKafkaTestHarness.NewTopic("at2-avro");
        var subject = topic + "-value";

        await AvroKafkaTestHarness.CreateTopicAsync(topic);
        try
        {
            using var registry = AvroKafkaTestHarness.BuildRegistry();
            using var http = AvroKafkaTestHarness.BuildRegistryHttpClient();

            // First build + produce
            var factory1 = AvroKafkaTestHarness.BuildFactory(registry);
            using (var producer = AvroKafkaTestHarness.ProducerBuilder<DeploymentRequestEventData>(factory1).Build("at2-p1"))
            {
                var result = await producer.ProduceAsync(topic, new Message<string, DeploymentRequestEventData>
                {
                    Key = "42",
                    Value = new DeploymentRequestEventData(42, "Running", DateTimeOffset.UtcNow, null, DateTimeOffset.UtcNow)
                });
                Assert.AreEqual(PersistenceStatus.Persisted, result.Status);
            }

            // Subject appears
            var subjects = await http.GetStringAsync("/subjects");
            StringAssert.Contains(subjects, subject);

            // Registered schema is semantically equal to canonical. Karapace may
            // reorder JSON properties on storage, so compare via parsed object
            // equivalence rather than byte-equality.
            var latestJson = await http.GetStringAsync($"/subjects/{subject}/versions/latest");
            var doc = System.Text.Json.JsonDocument.Parse(latestJson);
            var registeredSchema = doc.RootElement.GetProperty("schema").GetString()!;
            AssertSchemasEquivalent(DorcEventSchemas.GenerateRequestEventSchema(), registeredSchema);

            var versionsAfterFirst = await GetVersionCountAsync(http, subject);
            Assert.AreEqual(1, versionsAfterFirst);

            // Second build + produce — must not create a new version
            var factory2 = AvroKafkaTestHarness.BuildFactory(registry);
            using (var producer = AvroKafkaTestHarness.ProducerBuilder<DeploymentRequestEventData>(factory2).Build("at2-p2"))
            {
                await producer.ProduceAsync(topic, new Message<string, DeploymentRequestEventData>
                {
                    Key = "43",
                    Value = new DeploymentRequestEventData(43, "Completed", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow)
                });
            }

            var versionsAfterSecond = await GetVersionCountAsync(http, subject);
            Assert.AreEqual(1, versionsAfterSecond);
        }
        finally
        {
            await AvroKafkaTestHarness.DeleteSubjectAsync(subject);
            await AvroKafkaTestHarness.DeleteTopicAsync(topic);
        }
    }

    [TestMethod]
    public async Task AT6_ConsumerRoundTripsAvroPayload_PropertyEqual()
    {
        var topic = AvroKafkaTestHarness.NewTopic("at6-avro");
        var subject = topic + "-value";

        await AvroKafkaTestHarness.CreateTopicAsync(topic);
        try
        {
            using var registry = AvroKafkaTestHarness.BuildRegistry();
            var factory = AvroKafkaTestHarness.BuildFactory(registry);

            var sent = new DeploymentRequestEventData(
                RequestId: 4242,
                Status: "Succeeded",
                StartedTime: DateTimeOffset.Parse("2026-04-14T10:00:00Z"),
                CompletedTime: DateTimeOffset.Parse("2026-04-14T10:02:00Z"),
                Timestamp: DateTimeOffset.Parse("2026-04-14T10:02:01Z"));

            using (var producer = AvroKafkaTestHarness.ProducerBuilder<DeploymentRequestEventData>(factory).Build("at6-producer"))
            {
                await producer.ProduceAsync(topic, new Message<string, DeploymentRequestEventData>
                {
                    Key = "4242",
                    Value = sent
                });
            }

            var groupId = $"at6-group-{Guid.NewGuid():N}";
            using var consumer = AvroKafkaTestHarness.ConsumerBuilder<DeploymentRequestEventData>(factory, groupId).Build("at6-consumer");
            consumer.Subscribe(topic);

            var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var received = consumer.Consume(cts.Token);

            Assert.AreEqual(sent.RequestId, received.Message.Value.RequestId);
            Assert.AreEqual(sent.Status, received.Message.Value.Status);
            Assert.AreEqual(sent.StartedTime, received.Message.Value.StartedTime);
            Assert.AreEqual(sent.CompletedTime, received.Message.Value.CompletedTime);
            Assert.AreEqual(sent.Timestamp, received.Message.Value.Timestamp);
        }
        finally
        {
            await AvroKafkaTestHarness.DeleteSubjectAsync(subject);
            await AvroKafkaTestHarness.DeleteTopicAsync(topic);
        }
    }

    [TestMethod]
    public async Task AvroWireFormat_StartsWithConfluentMagicByte()
    {
        var topic = AvroKafkaTestHarness.NewTopic("at4-wire");
        var subject = topic + "-value";

        await AvroKafkaTestHarness.CreateTopicAsync(topic);
        try
        {
            using var registry = AvroKafkaTestHarness.BuildRegistry();
            var factory = AvroKafkaTestHarness.BuildFactory(registry);

            using var producer = AvroKafkaTestHarness.ProducerBuilder<DeploymentRequestEventData>(factory).Build("wire-p");
            await producer.ProduceAsync(topic, new Message<string, DeploymentRequestEventData>
            {
                Key = "1",
                Value = new DeploymentRequestEventData(1, null, null, null, DateTimeOffset.UtcNow)
            });

            // Byte-inspection: use a byte-array consumer on the same topic to read the raw wire format.
            var consumerConfig = new ConsumerConfig
            {
                BootstrapServers = AvroKafkaTestHarness.BootstrapServers,
                GroupId = $"wire-{Guid.NewGuid():N}",
                AutoOffsetReset = AutoOffsetReset.Earliest,
                EnableAutoCommit = false
            };
            using var rawConsumer = new ConsumerBuilder<string, byte[]>(consumerConfig).Build();
            rawConsumer.Subscribe(topic);

            var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var raw = rawConsumer.Consume(cts.Token);

            Assert.AreEqual((byte)0x00, raw.Message.Value[0]);  // Confluent magic byte
            Assert.AreEqual(5, Math.Min(raw.Message.Value.Length, 5));  // at least magic + 4-byte schema id
        }
        finally
        {
            await AvroKafkaTestHarness.DeleteSubjectAsync(subject);
            await AvroKafkaTestHarness.DeleteTopicAsync(topic);
        }
    }

    private static async Task<int> GetVersionCountAsync(HttpClient http, string subject)
    {
        var body = await http.GetStringAsync($"/subjects/{subject}/versions");
        var doc = System.Text.Json.JsonDocument.Parse(body);
        return doc.RootElement.GetArrayLength();
    }

    private static void AssertSchemasEquivalent(string expected, string actual)
    {
        var expectedNode = System.Text.Json.Nodes.JsonNode.Parse(expected);
        var actualNode = System.Text.Json.Nodes.JsonNode.Parse(actual);
        var expectedCanon = Canonicalise(expectedNode).ToJsonString();
        var actualCanon = Canonicalise(actualNode).ToJsonString();
        Assert.AreEqual(expectedCanon, actualCanon);
    }

    private static System.Text.Json.Nodes.JsonNode? Canonicalise(System.Text.Json.Nodes.JsonNode? node)
    {
        switch (node)
        {
            case System.Text.Json.Nodes.JsonObject obj:
                var ordered = new System.Text.Json.Nodes.JsonObject();
                foreach (var kv in obj.OrderBy(p => p.Key, StringComparer.Ordinal))
                    ordered[kv.Key] = Canonicalise(kv.Value?.DeepClone());
                return ordered;
            case System.Text.Json.Nodes.JsonArray arr:
                var newArr = new System.Text.Json.Nodes.JsonArray();
                foreach (var item in arr)
                    newArr.Add(Canonicalise(item?.DeepClone()));
                return newArr;
            default:
                return node;
        }
    }
}
