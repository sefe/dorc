using Confluent.Kafka;
using Dorc.Core.Events;

namespace Dorc.Kafka.Events.IntegrationTests;

[TestClass]
public class CompatibilityModeTests
{
    // AT-3: each registered subject's effective compatibility mode is BACKWARD.
    // Karapace may return 40408 for subjects with no per-subject config, in which
    // case they inherit the registry-global mode (compose sets this to BACKWARD).

    [TestMethod]
    public async Task AT3_EffectiveCompatibilityMode_IsBackward()
    {
        var topic = AvroKafkaTestHarness.NewTopic("at3-compat");
        var subject = topic + "-value";

        await AvroKafkaTestHarness.CreateTopicAsync(topic);
        try
        {
            using var registry = AvroKafkaTestHarness.BuildRegistry();
            var factory = AvroKafkaTestHarness.BuildFactory(registry);

            using (var producer = AvroKafkaTestHarness.ProducerBuilder<DeploymentRequestEventData>(factory).Build("at3-p"))
            {
                await producer.ProduceAsync(topic, new Message<string, DeploymentRequestEventData>
                {
                    Key = "1",
                    Value = new DeploymentRequestEventData(1, null, null, null, DateTimeOffset.UtcNow)
                });
            }

            using var http = AvroKafkaTestHarness.BuildRegistryHttpClient();
            var effectiveMode = await ReadEffectiveCompatibilityAsync(http, subject);

            Assert.AreEqual("BACKWARD", effectiveMode);
        }
        finally
        {
            await AvroKafkaTestHarness.DeleteSubjectAsync(subject);
            await AvroKafkaTestHarness.DeleteTopicAsync(topic);
        }
    }

    private static async Task<string> ReadEffectiveCompatibilityAsync(HttpClient http, string subject)
    {
        // Try per-subject first; fall back to global.
        var perSubject = await http.GetAsync($"/config/{subject}");
        if (perSubject.IsSuccessStatusCode)
        {
            var body = await perSubject.Content.ReadAsStringAsync();
            return System.Text.Json.JsonDocument.Parse(body)
                .RootElement.GetProperty("compatibilityLevel").GetString()!;
        }

        var global = await http.GetStringAsync("/config");
        return System.Text.Json.JsonDocument.Parse(global)
            .RootElement.GetProperty("compatibilityLevel").GetString()!;
    }
}
