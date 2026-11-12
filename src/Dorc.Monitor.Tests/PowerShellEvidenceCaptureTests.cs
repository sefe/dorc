using System.Text.Json;
using Dorc.ApiModel;
using Dorc.Monitor;
using Dorc.PersistentData.Sources.Interfaces;
using NSubstitute;

namespace Dorc.Monitor.Tests;

[TestClass]
public class PowerShellEvidenceCaptureTests
{
    private static ScriptGroup Group()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "definition-evidence-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return new ScriptGroup
        {
            DefinitionRequestId = 4, DeployResultId = 8, DefinitionRevision = "revision",
            PowerShellEvidenceDirectory = directory, ScriptProperties = new List<ScriptProperties> { new() }
        };
    }

    [TestMethod]
    public void CorruptEvidence_IsUnknownNotSuccessfulOrPrepared()
    {
        var group = Group();
        var requests = Substitute.For<IRequestsPersistentSource>();
        File.WriteAllText(Path.Combine(group.PowerShellEvidenceDirectory, "0.json"), "{corrupt");
        Assert.IsFalse(new PowerShellEvidenceCapture().Ingest(group, requests));
        requests.Received(1).SavePowerShellEvidence(Arg.Is<PowerShellEvidence>(e =>
            e.Phase == "Unknown" && e.PreparedAt == null && e.CompletedAt == null && e.TextSha256 == null));
        Assert.IsFalse(Directory.Exists(group.PowerShellEvidenceDirectory));
    }

    [TestMethod]
    [DataRow("missing-hash")]
    [DataRow("missing-runtime")]
    [DataRow("missing-prepared")]
    [DataRow("missing-invocation")]
    [DataRow("missing-return")]
    [DataRow("hash-newline")]
    [DataRow("wrong-requested-runtime")]
    public void IncompleteReturnedEvidence_IsUnknown(string invalidField)
    {
        var group = Group();
        var evidence = ReturnedEvidence();
        switch (invalidField)
        {
            case "missing-hash": evidence.TextSha256 = null; break;
            case "missing-runtime": evidence.RuntimeVersion = null; break;
            case "missing-prepared": evidence.PreparedAt = null; break;
            case "missing-invocation": evidence.InvocationObservedAt = null; break;
            case "missing-return": evidence.CompletedAt = null; break;
            case "hash-newline": evidence.TextSha256 += "\n"; break;
            case "wrong-requested-runtime": evidence.RequestedRuntime = "different"; break;
        }
        File.WriteAllText(Path.Combine(group.PowerShellEvidenceDirectory, "0.json"), JsonSerializer.Serialize(evidence));
        var requests = Substitute.For<IRequestsPersistentSource>();
        Assert.IsFalse(new PowerShellEvidenceCapture().Ingest(group, requests));
        requests.Received(1).SavePowerShellEvidence(Arg.Is<PowerShellEvidence>(e =>
            e.Phase == "Unknown" && e.PreparedAt == null && e.TextSha256 == null));
    }

    [TestMethod]
    public void ValidReturnedEvidence_PreservesExactInputObservations()
    {
        var group = Group();
        var evidence = ReturnedEvidence();
        File.WriteAllText(Path.Combine(group.PowerShellEvidenceDirectory, "0.json"), JsonSerializer.Serialize(evidence));
        var requests = Substitute.For<IRequestsPersistentSource>();
        Assert.IsTrue(new PowerShellEvidenceCapture().Ingest(group, requests));
        requests.Received(1).SavePowerShellEvidence(Arg.Is<PowerShellEvidence>(e =>
            e.Phase == "InvocationReturned" && e.TextSha256 == evidence.TextSha256 &&
            e.PreparedAt == evidence.PreparedAt && e.InvocationObservedAt == evidence.InvocationObservedAt &&
            e.CompletedAt == evidence.CompletedAt));
    }

    private static PowerShellEvidence ReturnedEvidence()
    {
        var now = DateTimeOffset.UtcNow;
        return new PowerShellEvidence
        {
            RequestId = 4, ResultId = 8, DefinitionRevision = "revision", ScriptOrdinal = 0,
            Phase = "InvocationReturned", TextSha256 = new string('A', 64), RuntimeVersion = "7.4.0",
            PreparedAt = now.AddSeconds(-2), InvocationObservedAt = now.AddSeconds(-1), CompletedAt = now
        };
    }

    [TestMethod]
    public void PersistenceFailure_RetainsEvidenceAndDoesNotClaimCompletion()
    {
        var group = Group();
        try
        {
            var evidence = new PowerShellEvidence
            {
                RequestId = 4, ResultId = 8, DefinitionRevision = "revision", ScriptOrdinal = 0,
                Phase = "Prepared", TextSha256 = new string('A', 64), PreparedAt = DateTimeOffset.UtcNow,
                RuntimeVersion = "7.4.0"
            };
            File.WriteAllText(Path.Combine(group.PowerShellEvidenceDirectory, "0.json"), JsonSerializer.Serialize(evidence));
            var requests = Substitute.For<IRequestsPersistentSource>();
            requests.When(r => r.SavePowerShellEvidence(Arg.Any<PowerShellEvidence>()))
                .Do(_ => throw new InvalidOperationException("store unavailable"));
            Assert.Throws<InvalidOperationException>(() => new PowerShellEvidenceCapture().Ingest(group, requests));
            Assert.IsTrue(File.Exists(Path.Combine(group.PowerShellEvidenceDirectory, "0.json")));
            requests.Received(1).SavePowerShellEvidence(Arg.Is<PowerShellEvidence>(e => e.Phase == "Prepared"));
        }
        finally { Directory.Delete(group.PowerShellEvidenceDirectory, true); }
    }
}
