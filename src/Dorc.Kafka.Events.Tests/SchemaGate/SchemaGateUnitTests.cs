using Dorc.Kafka.Events.Configuration;
using Dorc.Kafka.Events.Schemas;
using Dorc.Kafka.Events.SchemaGate;

namespace Dorc.Kafka.Events.Tests.SchemaGate;

[TestClass]
public class SchemaGateUnitTests
{
    private static readonly KafkaTopicsOptions Defaults = new();

    private static string DefaultRequestsNewSubject => $"{Defaults.RequestsNew}-value";
    private static string DefaultRequestsStatusSubject => $"{Defaults.RequestsStatus}-value";
    private static string DefaultResultsStatusSubject => $"{Defaults.ResultsStatus}-value";
    private static string DefaultRequestsNewDlqSubject => $"{Defaults.RequestsNewDlq}-value";

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
            DefaultRequestsNewSubject,
            DefaultRequestsNewSubject,
            DorcEventSchemas.GenerateRequestEventSchema(),
            CancellationToken.None);

        Assert.AreEqual(GateOutcome.Fail, report.Outcome);
        StringAssert.Contains(report.Message, "Canonical schema file not found");
    }

    [TestMethod]
    public async Task CanonicalMismatch_FailsWithRefreshInstruction()
    {
        await WriteCanonical(DefaultRequestsNewSubject, """{"type":"record","name":"Stale","fields":[]}""");
        var gate = new AvroSchemaGate(registryHttp: null, canonicalDir: _canonicalDir, snapshotDir: null);

        var report = await gate.CheckSubjectAsync(
            DefaultRequestsNewSubject,
            DefaultRequestsNewSubject,
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
        await WriteCanonical(DefaultRequestsNewSubject, schema);
        await WriteSnapshot(DefaultRequestsNewSubject, schema);
        var gate = new AvroSchemaGate(registryHttp: null, canonicalDir: _canonicalDir, snapshotDir: _snapshotDir);

        var report = await gate.CheckSubjectAsync(
            DefaultRequestsNewSubject,
            DefaultRequestsNewSubject,
            schema,
            CancellationToken.None);

        Assert.AreEqual(GateOutcome.Pass, report.Outcome);
        Assert.AreEqual("snapshot", report.Source);
    }

    [TestMethod]
    public async Task SnapshotPath_SchemaDifferent_FailsClosed()
    {
        var schema = DorcEventSchemas.GenerateRequestEventSchema();
        await WriteCanonical(DefaultRequestsNewSubject, schema);
        await WriteSnapshot(DefaultRequestsNewSubject, """{"type":"record","name":"Older","fields":[]}""");
        var gate = new AvroSchemaGate(registryHttp: null, canonicalDir: _canonicalDir, snapshotDir: _snapshotDir);

        var report = await gate.CheckSubjectAsync(
            DefaultRequestsNewSubject,
            DefaultRequestsNewSubject,
            schema,
            CancellationToken.None);

        Assert.AreEqual(GateOutcome.Fail, report.Outcome);
        Assert.AreEqual("snapshot", report.Source);
        StringAssert.Contains(report.Message, "differs from committed snapshot");
    }

    [TestMethod]
    public async Task NeitherSourceAvailable_FailsClosed()
    {
        await WriteCanonical(DefaultRequestsNewSubject, DorcEventSchemas.GenerateRequestEventSchema());
        var gate = new AvroSchemaGate(registryHttp: null, canonicalDir: _canonicalDir, snapshotDir: null);

        var report = await gate.CheckSubjectAsync(
            DefaultRequestsNewSubject,
            DefaultRequestsNewSubject,
            DorcEventSchemas.GenerateRequestEventSchema(),
            CancellationToken.None);

        Assert.AreEqual(GateOutcome.Fail, report.Outcome);
        StringAssert.Contains(report.Message, "Neither live schema registry nor committed snapshot");
    }

    [TestMethod]
    public async Task RunAsync_CoversAllSubjects()
    {
        await WriteCanonical(DefaultRequestsNewSubject, DorcEventSchemas.GenerateRequestEventSchema());
        await WriteCanonical(DefaultRequestsStatusSubject, DorcEventSchemas.GenerateRequestEventSchema());
        await WriteCanonical(DefaultResultsStatusSubject, DorcEventSchemas.GenerateResultEventSchema());
        await WriteCanonical(DefaultRequestsNewDlqSubject, DorcEventSchemas.GenerateErrorEnvelopeSchema());
        await WriteSnapshot(DefaultRequestsNewSubject, DorcEventSchemas.GenerateRequestEventSchema());
        await WriteSnapshot(DefaultRequestsStatusSubject, DorcEventSchemas.GenerateRequestEventSchema());
        await WriteSnapshot(DefaultResultsStatusSubject, DorcEventSchemas.GenerateResultEventSchema());
        await WriteSnapshot(DefaultRequestsNewDlqSubject, DorcEventSchemas.GenerateErrorEnvelopeSchema());
        var gate = new AvroSchemaGate(registryHttp: null, canonicalDir: _canonicalDir, snapshotDir: _snapshotDir);

        var reports = await gate.RunAsync();

        Assert.AreEqual(4, reports.Count);
        CollectionAssert.AreEquivalent(
            new[] { DefaultRequestsNewSubject, DefaultRequestsStatusSubject, DefaultResultsStatusSubject, DefaultRequestsNewDlqSubject },
            reports.Select(r => r.Subject).ToList());
        Assert.IsTrue(reports.All(r => r.Outcome == GateOutcome.Pass));
    }

    // SPEC-S-017 AC-13 — diverged-name path: deployed topic differs from default.
    // Verifies (a) the gate reads canonical/snapshot files under the
    // *default*-derived filename, and (b) the resulting GateReport.Subject
    // reflects the *deployed* live subject (so PR-gate output is keyed to the
    // registry's view, not the developer's local default).
    [TestMethod]
    public async Task DivergedNames_CanonicalReadFromDefaultPath_LiveSubjectInReport()
    {
        var deployed = new KafkaTopicsOptions
        {
            RequestsNew = "tr.dv.gbl.deploy.request.il2.dorc",
            RequestsStatus = "tr.dv.gbl.deploy.requeststatus.il2.dorc",
            ResultsStatus = "tr.dv.gbl.deploy.resultstatus.il2.dorc"
        };
        var defaultCanonicalKey = $"{Defaults.RequestsNew}-value"; // dorc.requests.new-value
        var deployedLiveSubject = $"{deployed.RequestsNew}-value"; // tr.dv.gbl....-value
        Assert.AreNotEqual(defaultCanonicalKey, deployedLiveSubject, "fixture must actually diverge");

        var schema = DorcEventSchemas.GenerateRequestEventSchema();
        // Write the canonical at the DEFAULT path only — if the gate keys file
        // lookup off liveSubject, it will fail to find the file.
        await WriteCanonical(defaultCanonicalKey, schema);
        await WriteSnapshot(defaultCanonicalKey, schema);

        var gate = new AvroSchemaGate(deployed, registryHttp: null, canonicalDir: _canonicalDir, snapshotDir: _snapshotDir);

        var report = await gate.CheckSubjectAsync(
            canonicalKey: defaultCanonicalKey,
            liveSubject: deployedLiveSubject,
            schema,
            CancellationToken.None);

        Assert.AreEqual(GateOutcome.Pass, report.Outcome,
            "Gate must read canonical from the default-derived path even when deployed name differs.");
        Assert.AreEqual(deployedLiveSubject, report.Subject,
            "GateReport.Subject must surface the *deployed* liveSubject so PR-gate output matches the registry's view.");
    }

    [TestMethod]
    public void InScopeSchemas_DefaultDeploy_CanonicalKeyEqualsLiveSubject()
    {
        var gate = new AvroSchemaGate(registryHttp: null, canonicalDir: _canonicalDir, snapshotDir: null);

        var triples = gate.InScopeSchemas();

        Assert.AreEqual(4, triples.Count);
        foreach (var (canonicalKey, liveSubject, _) in triples)
            Assert.AreEqual(canonicalKey, liveSubject,
                "When deployed topics match defaults, canonicalKey == liveSubject (production-default deploy).");
    }

    [TestMethod]
    public void InScopeSchemas_DivergedDeploy_CanonicalKeyKeepsDefaults_LiveSubjectFollowsDeployed()
    {
        var deployed = new KafkaTopicsOptions { RequestsNew = "custom.requests.new" };
        var gate = new AvroSchemaGate(deployed, registryHttp: null, canonicalDir: _canonicalDir, snapshotDir: null);

        var triples = gate.InScopeSchemas();

        var requestsNewTriple = triples[0];
        Assert.AreEqual($"{Defaults.RequestsNew}-value", requestsNewTriple.CanonicalKey);
        Assert.AreEqual("custom.requests.new-value", requestsNewTriple.LiveSubject);
    }

    private Task WriteCanonical(string subject, string content)
        => File.WriteAllTextAsync(Path.Combine(_canonicalDir, $"{subject}.avsc"), content);

    private Task WriteSnapshot(string subject, string content)
        => File.WriteAllTextAsync(Path.Combine(_snapshotDir, $"{subject}.avsc"), content);
}
