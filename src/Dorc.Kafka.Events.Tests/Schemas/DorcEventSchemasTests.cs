using Dorc.Core.Events;
using Dorc.Kafka.Events.Configuration;
using Dorc.Kafka.Events.Schemas;

namespace Dorc.Kafka.Events.Tests.Schemas;

[TestClass]
public class DorcEventSchemasTests
{
    private static readonly KafkaTopicsOptions Defaults = new();

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
        var canonical = File.ReadAllText(CanonicalPath($"{Defaults.RequestsNew}-value"));
        Assert.AreEqual(canonical, generated,
            $"Regenerated schema diverged from {Defaults.RequestsNew}-value.avsc. Run tools/generate-schemas to refresh and commit the change.");
    }

    [TestMethod]
    public void Generated_RequestStatus_MatchesCheckedInCanonical()
    {
        var generated = DorcEventSchemas.GenerateRequestEventSchema();
        var canonical = File.ReadAllText(CanonicalPath($"{Defaults.RequestsStatus}-value"));
        Assert.AreEqual(canonical, generated);
    }

    [TestMethod]
    public void Generated_ResultEvent_MatchesCheckedInCanonical()
    {
        var generated = DorcEventSchemas.GenerateResultEventSchema();
        var canonical = File.ReadAllText(CanonicalPath($"{Defaults.ResultsStatus}-value"));
        Assert.AreEqual(canonical, generated);
    }

    [TestMethod]
    public void DefaultDerivedSubjects_FollowConfluentValueSuffix()
    {
        Assert.AreEqual("dorc.requests.new-value", $"{Defaults.RequestsNew}-value");
        Assert.AreEqual("dorc.requests.status-value", $"{Defaults.RequestsStatus}-value");
        Assert.AreEqual("dorc.results.status-value", $"{Defaults.ResultsStatus}-value");
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
