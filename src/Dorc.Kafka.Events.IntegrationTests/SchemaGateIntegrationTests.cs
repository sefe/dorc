using System.Net.Http.Json;
using Confluent.Kafka;
using Dorc.Core.Events;
using Dorc.Kafka.Events.Schemas;
using Dorc.Kafka.Events.SchemaGate;

namespace Dorc.Kafka.Events.IntegrationTests;

[TestClass]
public class SchemaGateIntegrationTests
{
    private string _tempRoot = null!;
    private string _canonicalDir = null!;

    [TestInitialize]
    public void SetUp()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"schema-gate-it-{Guid.NewGuid():N}");
        _canonicalDir = Path.Combine(_tempRoot, "current");
        Directory.CreateDirectory(_canonicalDir);
    }

    [TestCleanup]
    public void TearDown()
    {
        if (Directory.Exists(_tempRoot)) Directory.Delete(_tempRoot, recursive: true);
    }

    [TestMethod]
    public async Task AT5_LivePath_CompatibleChange_Passes()
    {
        var topic = AvroKafkaTestHarness.NewTopic("gate-compat");
        var subject = topic + "-value";
        await AvroKafkaTestHarness.CreateTopicAsync(topic);
        try
        {
            // Register a baseline via a produce (first version = current canonical).
            await ProduceBaselineAsync(topic);

            // Check that the same canonical passes the BACKWARD gate against itself.
            var schema = DorcEventSchemas.GenerateRequestEventSchema();
            await File.WriteAllTextAsync(Path.Combine(_canonicalDir, $"{subject}.avsc"), schema);

            using var http = AvroKafkaTestHarness.BuildRegistryHttpClient();
            var gate = new AvroSchemaGate(http, _canonicalDir, snapshotDir: null);
            var report = await gate.CheckSubjectAsync(subject, schema, CancellationToken.None);

            Assert.AreEqual(GateOutcome.Pass, report.Outcome, report.Message);
            Assert.AreEqual("live", report.Source);
        }
        finally
        {
            await AvroKafkaTestHarness.DeleteSubjectAsync(subject);
            await AvroKafkaTestHarness.DeleteTopicAsync(topic);
        }
    }

    [TestMethod]
    public async Task AT5_LivePath_IncompatibleChange_FailsWithSubjectAndReason()
    {
        var topic = AvroKafkaTestHarness.NewTopic("gate-incompat");
        var subject = topic + "-value";
        await AvroKafkaTestHarness.CreateTopicAsync(topic);
        try
        {
            await ProduceBaselineAsync(topic);

            // Construct a candidate that adds a required field without a
            // default. BACKWARD-incompatible: a new reader must have the
            // field populated, but old data doesn't carry it.
            var breaking = """
            {"name":"Dorc.Core.Events.DeploymentRequestEventData","type":"record","fields":[{"name":"CompletedTime","type":["null","string"]},{"name":"RequestId","type":"int"},{"name":"NewRequired","type":"string"},{"name":"StartedTime","type":["null","string"]},{"name":"Status","type":["null","string"]},{"name":"Timestamp","type":"string"}]}
            """.Trim();
            await File.WriteAllTextAsync(Path.Combine(_canonicalDir, $"{subject}.avsc"), breaking);

            using var http = AvroKafkaTestHarness.BuildRegistryHttpClient();
            var gate = new AvroSchemaGate(http, _canonicalDir, snapshotDir: null);
            var report = await gate.CheckSubjectAsync(subject, breaking, CancellationToken.None);

            Assert.AreEqual(GateOutcome.Fail, report.Outcome);
            Assert.AreEqual("live", report.Source);
            Assert.AreEqual(subject, report.Subject);
            StringAssert.Contains(report.Message, "BACKWARD incompatibility");
        }
        finally
        {
            await AvroKafkaTestHarness.DeleteSubjectAsync(subject);
            await AvroKafkaTestHarness.DeleteTopicAsync(topic);
        }
    }

    [TestMethod]
    public async Task AT5_LivePath_CompatibleAdditionWithDefault_Passes()
    {
        var topic = AvroKafkaTestHarness.NewTopic("gate-additive");
        var subject = topic + "-value";
        await AvroKafkaTestHarness.CreateTopicAsync(topic);
        try
        {
            await ProduceBaselineAsync(topic);

            // Candidate adds a new optional field with default null — BACKWARD-
            // compatible per Avro rules.
            var additive = """
            {"name":"Dorc.Core.Events.DeploymentRequestEventData","type":"record","fields":[{"name":"CompletedTime","type":["null","string"]},{"name":"RequestId","type":"int"},{"name":"StartedTime","type":["null","string"]},{"name":"Status","type":["null","string"]},{"name":"Timestamp","type":"string"},{"name":"Note","type":["null","string"],"default":null}]}
            """.Trim();
            await File.WriteAllTextAsync(Path.Combine(_canonicalDir, $"{subject}.avsc"), additive);

            using var http = AvroKafkaTestHarness.BuildRegistryHttpClient();
            var gate = new AvroSchemaGate(http, _canonicalDir, snapshotDir: null);
            var report = await gate.CheckSubjectAsync(subject, additive, CancellationToken.None);

            Assert.AreEqual(GateOutcome.Pass, report.Outcome, report.Message);
            Assert.AreEqual("live", report.Source);
        }
        finally
        {
            await AvroKafkaTestHarness.DeleteSubjectAsync(subject);
            await AvroKafkaTestHarness.DeleteTopicAsync(topic);
        }
    }

    private static async Task ProduceBaselineAsync(string topic)
    {
        using var registry = AvroKafkaTestHarness.BuildRegistry();
        var factory = AvroKafkaTestHarness.BuildFactory(registry);
        using var producer = AvroKafkaTestHarness.ProducerBuilder<DeploymentRequestEventData>(factory).Build("gate-baseline");
        await producer.ProduceAsync(topic, new Message<string, DeploymentRequestEventData>
        {
            Key = "1",
            Value = new DeploymentRequestEventData(1, null, null, null, DateTimeOffset.UtcNow)
        });
    }
}
