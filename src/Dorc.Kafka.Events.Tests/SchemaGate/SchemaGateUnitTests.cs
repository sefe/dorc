using Dorc.Kafka.Events;
using Dorc.Kafka.Events.Schemas;
using Dorc.Kafka.Events.SchemaGate;

namespace Dorc.Kafka.Events.Tests.SchemaGate;

[TestClass]
public class SchemaGateUnitTests
{
    private string _tempRoot = null!;
    private string _canonicalDir = null!;
    private string _snapshotDir = null!;

    [TestInitialize]
    public void SetUp()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"schema-gate-{Guid.NewGuid():N}");
        _canonicalDir = Path.Combine(_tempRoot, "current");
        _snapshotDir = Path.Combine(_tempRoot, "latest");
        Directory.CreateDirectory(_canonicalDir);
        Directory.CreateDirectory(_snapshotDir);
    }

    [TestCleanup]
    public void TearDown()
    {
        if (Directory.Exists(_tempRoot)) Directory.Delete(_tempRoot, recursive: true);
    }

    [TestMethod]
    public async Task CanonicalMissing_FailsClosed()
    {
        var gate = new AvroSchemaGate(registryHttp: null, canonicalDir: _canonicalDir, snapshotDir: null);

        var report = await gate.CheckSubjectAsync(
            KafkaSubjectNames.RequestsNewValue,
            DorcEventSchemas.GenerateRequestEventSchema(),
            CancellationToken.None);

        Assert.AreEqual(GateOutcome.Fail, report.Outcome);
        StringAssert.Contains(report.Message, "Canonical schema file not found");
    }

    [TestMethod]
    public async Task CanonicalMismatch_FailsWithRefreshInstruction()
    {
        await WriteCanonical(KafkaSubjectNames.RequestsNewValue, """{"type":"record","name":"Stale","fields":[]}""");
        var gate = new AvroSchemaGate(registryHttp: null, canonicalDir: _canonicalDir, snapshotDir: null);

        var report = await gate.CheckSubjectAsync(
            KafkaSubjectNames.RequestsNewValue,
            DorcEventSchemas.GenerateRequestEventSchema(),
            CancellationToken.None);

        Assert.AreEqual(GateOutcome.Fail, report.Outcome);
        StringAssert.Contains(report.Message, "Regenerated schema does not match canonical");
        StringAssert.Contains(report.Message, "tools/generate-schemas");
    }

    [TestMethod]
    public async Task SnapshotPath_SchemaUnchanged_Passes()
    {
        var schema = DorcEventSchemas.GenerateRequestEventSchema();
        await WriteCanonical(KafkaSubjectNames.RequestsNewValue, schema);
        await WriteSnapshot(KafkaSubjectNames.RequestsNewValue, schema);
        var gate = new AvroSchemaGate(registryHttp: null, canonicalDir: _canonicalDir, snapshotDir: _snapshotDir);

        var report = await gate.CheckSubjectAsync(
            KafkaSubjectNames.RequestsNewValue,
            schema,
            CancellationToken.None);

        Assert.AreEqual(GateOutcome.Pass, report.Outcome);
        Assert.AreEqual("snapshot", report.Source);
    }

    [TestMethod]
    public async Task SnapshotPath_SchemaDifferent_FailsClosed()
    {
        var schema = DorcEventSchemas.GenerateRequestEventSchema();
        await WriteCanonical(KafkaSubjectNames.RequestsNewValue, schema);
        await WriteSnapshot(KafkaSubjectNames.RequestsNewValue, """{"type":"record","name":"Older","fields":[]}""");
        var gate = new AvroSchemaGate(registryHttp: null, canonicalDir: _canonicalDir, snapshotDir: _snapshotDir);

        var report = await gate.CheckSubjectAsync(
            KafkaSubjectNames.RequestsNewValue,
            schema,
            CancellationToken.None);

        Assert.AreEqual(GateOutcome.Fail, report.Outcome);
        Assert.AreEqual("snapshot", report.Source);
        StringAssert.Contains(report.Message, "differs from committed snapshot");
    }

    [TestMethod]
    public async Task NeitherSourceAvailable_FailsClosed()
    {
        await WriteCanonical(KafkaSubjectNames.RequestsNewValue, DorcEventSchemas.GenerateRequestEventSchema());
        var gate = new AvroSchemaGate(registryHttp: null, canonicalDir: _canonicalDir, snapshotDir: null);

        var report = await gate.CheckSubjectAsync(
            KafkaSubjectNames.RequestsNewValue,
            DorcEventSchemas.GenerateRequestEventSchema(),
            CancellationToken.None);

        Assert.AreEqual(GateOutcome.Fail, report.Outcome);
        StringAssert.Contains(report.Message, "Neither live schema registry nor committed snapshot");
    }

    [TestMethod]
    public async Task RunAsync_CoversAllThreeSubjects()
    {
        await WriteCanonical(KafkaSubjectNames.RequestsNewValue, DorcEventSchemas.GenerateRequestEventSchema());
        await WriteCanonical(KafkaSubjectNames.RequestsStatusValue, DorcEventSchemas.GenerateRequestEventSchema());
        await WriteCanonical(KafkaSubjectNames.ResultsStatusValue, DorcEventSchemas.GenerateResultEventSchema());
        await WriteSnapshot(KafkaSubjectNames.RequestsNewValue, DorcEventSchemas.GenerateRequestEventSchema());
        await WriteSnapshot(KafkaSubjectNames.RequestsStatusValue, DorcEventSchemas.GenerateRequestEventSchema());
        await WriteSnapshot(KafkaSubjectNames.ResultsStatusValue, DorcEventSchemas.GenerateResultEventSchema());
        var gate = new AvroSchemaGate(registryHttp: null, canonicalDir: _canonicalDir, snapshotDir: _snapshotDir);

        var reports = await gate.RunAsync();

        Assert.AreEqual(3, reports.Count);
        CollectionAssert.AreEquivalent(
            new[] { KafkaSubjectNames.RequestsNewValue, KafkaSubjectNames.RequestsStatusValue, KafkaSubjectNames.ResultsStatusValue },
            reports.Select(r => r.Subject).ToList());
        Assert.IsTrue(reports.All(r => r.Outcome == GateOutcome.Pass));
    }

    private Task WriteCanonical(string subject, string content)
        => File.WriteAllTextAsync(Path.Combine(_canonicalDir, $"{subject}.avsc"), content);

    private Task WriteSnapshot(string subject, string content)
        => File.WriteAllTextAsync(Path.Combine(_snapshotDir, $"{subject}.avsc"), content);
}
