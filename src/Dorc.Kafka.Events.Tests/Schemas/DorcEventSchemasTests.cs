using Dorc.Core.Events;
using Dorc.Kafka.Events;
using Dorc.Kafka.Events.Schemas;

namespace Dorc.Kafka.Client.Tests.Schemas;

[TestClass]
public class DorcEventSchemasTests
{
    [TestMethod]
    public void GenerateJsonFor_DeploymentRequestEventData_IsDeterministic()
    {
        var first = DorcEventSchemas.GenerateJsonFor<DeploymentRequestEventData>();
        var second = DorcEventSchemas.GenerateJsonFor<DeploymentRequestEventData>();

        Assert.AreEqual(first, second);
    }

    [TestMethod]
    public void GenerateJsonFor_DeploymentResultEventData_IsDeterministic()
    {
        var first = DorcEventSchemas.GenerateJsonFor<DeploymentResultEventData>();
        var second = DorcEventSchemas.GenerateJsonFor<DeploymentResultEventData>();

        Assert.AreEqual(first, second);
    }

    [TestMethod]
    public void Generated_RequestEvent_MatchesCheckedInCanonical()
    {
        var generated = DorcEventSchemas.GenerateRequestEventSchema();
        var canonical = File.ReadAllText(CanonicalPath(KafkaSubjectNames.RequestsNewValue));
        Assert.AreEqual(canonical, generated,
            $"Regenerated schema diverged from {KafkaSubjectNames.RequestsNewValue}.avsc. Run tools/generate-schemas to refresh and commit the change.");
    }

    [TestMethod]
    public void Generated_RequestStatus_MatchesCheckedInCanonical()
    {
        var generated = DorcEventSchemas.GenerateRequestEventSchema();
        var canonical = File.ReadAllText(CanonicalPath(KafkaSubjectNames.RequestsStatusValue));
        Assert.AreEqual(canonical, generated);
    }

    [TestMethod]
    public void Generated_ResultEvent_MatchesCheckedInCanonical()
    {
        var generated = DorcEventSchemas.GenerateResultEventSchema();
        var canonical = File.ReadAllText(CanonicalPath(KafkaSubjectNames.ResultsStatusValue));
        Assert.AreEqual(canonical, generated);
    }

    [TestMethod]
    public void KafkaSubjectNames_FollowConfluentValueSuffix()
    {
        Assert.AreEqual("dorc.requests.new-value", KafkaSubjectNames.RequestsNewValue);
        Assert.AreEqual("dorc.requests.status-value", KafkaSubjectNames.RequestsStatusValue);
        Assert.AreEqual("dorc.results.status-value", KafkaSubjectNames.ResultsStatusValue);
    }

    private static string CanonicalPath(string subject)
    {
        var repoRoot = FindRepoRoot();
        return Path.Combine(repoRoot, "docs", "kafka-migration", "schemas", "current", $"{subject}.avsc");
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "src", "Dorc.sln")))
            dir = dir.Parent;
        return dir?.FullName
            ?? throw new InvalidOperationException("Could not locate repo root containing src/Dorc.sln");
    }
}
