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

    [TestMethod]
    public void ScriptGroupProcessor_WhenPipeReturnsNullOrUninitializedGroup_SetsRequestIdAndThrowsDescriptiveException()
    {
        var logger = Substitute.For<Dorc.Runner.Logger.IRunnerLogger>();
        var pipeClient = Substitute.For<Dorc.Runner.Pipes.IScriptGroupPipeClient>();
        pipeClient.GetScriptGroupProperties("pipe-null").Returns((ScriptGroup)null!);

        var processor = new Dorc.Runner.ScriptGroupProcessor(logger, pipeClient);

        var ex = Assert.Throws<Exception>(() => processor.Process("pipe-null", 777));
        Assert.AreEqual("ScriptGroup is not initialized.", ex.Message);
        logger.Received(1).SetRequestId(777);
        logger.DidNotReceiveWithAnyArgs().SetDeploymentResultId(default);
    }

    [TestMethod]
    public void PowerShellScriptRunner_ArrayVariables_InjectsStringArraysAndRedactsSecretArrayElements()
    {
        var tempDir = Path.Join(AppContext.BaseDirectory, "ps-array-runner-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var scriptPath = Path.Join(tempDir, "test-array.ps1");
            File.WriteAllText(scriptPath, @"
if ($PlainArray -isnot [string[]] -or $PlainArray.Count -ne 2 -or $PlainArray[0] -ne 'item-alpha' -or $PlainArray[1] -ne 'item-beta') {
    throw ""PlainArray type/value mismatch: $($PlainArray.GetType().FullName)""
}
if ($SecretArray -isnot [string[]] -or $SecretArray.Count -lt 2) {
    throw ""SecretArray type/count mismatch""
}
Write-Output ""PlainJoin=$($PlainArray -join '|') SecretLeak=$($SecretArray[0]) and $($SecretArray[1]) Explicit=$($ExplicitSecretArray -join '|')""
");

            var logger = Substitute.For<Dorc.Runner.Logger.IRunnerLogger>();
            logger.FileLogger.Returns(Substitute.For<Microsoft.Extensions.Logging.ILogger>());
            var loggedMessages = new List<string>();
            logger.When(l => l.Information(Arg.Any<string>()))
                .Do(ci => loggedMessages.Add(ci.Arg<string>()));

            var contract1 = Group();
            File.WriteAllText(Path.Join(contract1.PowerShellEvidenceDirectory, "0.json"), JsonSerializer.Serialize(new PowerShellEvidence
            {
                RequestId = contract1.DefinitionRequestId, ResultId = contract1.DeployResultId,
                DefinitionRevision = contract1.DefinitionRevision, ScriptOrdinal = 0,
                Phase = "Allocated", SourcePath = scriptPath
            }));
            var runner = new Dorc.PowerShell.PowerShellScriptRunner(logger, contract1);
            var scriptProps = new Dictionary<string, Dorc.ApiModel.MonitorRunnerApi.VariableValue>
            {
                ["PlainArray"] = new() { Value = new[] { "item-alpha", "item-beta" }, Type = typeof(string[]) },
                ["SecretArray"] = new() { Value = new[] { "SuperSecretOne", "SuperSecretTwo", "123", "ab", "" }, Type = typeof(string[]) },
                ["ScalarSecret"] = new() { Value = "ScalarSecretVal", Type = typeof(string) }
            };

            var exitCode = runner.Run(tempDir, scriptPath, scriptProps, new Dictionary<string, Dorc.ApiModel.MonitorRunnerApi.VariableValue>());
            Directory.Delete(contract1.PowerShellEvidenceDirectory, true);

            Assert.AreEqual(0, exitCode);
            var combinedOutput = string.Join("\n", loggedMessages);
            StringAssert.Contains(combinedOutput, "PlainJoin=item-alpha|item-beta");
            StringAssert.Contains(combinedOutput, "[REDACTED]");
            Assert.IsFalse(combinedOutput.Contains("SuperSecretOne", StringComparison.Ordinal));
            Assert.IsFalse(combinedOutput.Contains("SuperSecretTwo", StringComparison.Ordinal));

            var explicitContract = Group();
            explicitContract.SensitivePropertyNames = new List<string> { "ExplicitSecretArray" };
            File.WriteAllText(Path.Join(explicitContract.PowerShellEvidenceDirectory, "0.json"), JsonSerializer.Serialize(new PowerShellEvidence
            {
                RequestId = explicitContract.DefinitionRequestId, ResultId = explicitContract.DeployResultId,
                DefinitionRevision = explicitContract.DefinitionRevision, ScriptOrdinal = 0,
                Phase = "Allocated", SourcePath = scriptPath
            }));
            var explicitRunner = new Dorc.PowerShell.PowerShellScriptRunner(logger, explicitContract);
            var explicitProps = new Dictionary<string, Dorc.ApiModel.MonitorRunnerApi.VariableValue>
            {
                ["PlainArray"] = new() { Value = new[] { "item-alpha", "item-beta" }, Type = typeof(string[]) },
                ["SecretArray"] = new() { Value = new[] { "NonSecretOne", "NonSecretTwo", "NonSecretThree" }, Type = typeof(string[]) },
                ["ExplicitSecretArray"] = new() { Value = new[] { "ExplicitSecretElem" }, Type = typeof(string[]) }
            };
            loggedMessages.Clear();
            Assert.AreEqual(0, explicitRunner.Run(tempDir, scriptPath, explicitProps, new Dictionary<string, Dorc.ApiModel.MonitorRunnerApi.VariableValue>()));
            var explicitOutput = string.Join("\n", loggedMessages);
            Assert.IsFalse(explicitOutput.Contains("ExplicitSecretElem", StringComparison.Ordinal));
            StringAssert.Contains(explicitOutput, "Explicit=[REDACTED]");
            Directory.Delete(explicitContract.PowerShellEvidenceDirectory, true);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }
}
