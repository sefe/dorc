using System.Text;
using System.Text.Json;
using Dorc.ApiModel;
using Dorc.ApiModel.MonitorRunnerApi;
using Dorc.PersistentData.Sources.Interfaces;
using Dorc.PowerShell;
using Dorc.Runner.Logger;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Dorc.Monitor.Tests;

[TestClass]
public class PowerShellScriptRunnerVerificationTests
{
    private const string OtherBaseline =
        "9f86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a08";

    private string _workDirectory = null!;
    private string _evidenceRoot = null!;

    [TestInitialize]
    public void Setup()
    {
        _workDirectory = Path.Join(
            AppContext.BaseDirectory,
            "powershell-runner-verification-tests",
            Guid.NewGuid().ToString("N"));
        _evidenceRoot = Path.Join(_workDirectory, "evidence");
        Directory.CreateDirectory(_workDirectory);
        Directory.CreateDirectory(_evidenceRoot);
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_workDirectory))
        {
            Directory.Delete(_workDirectory, recursive: true);
        }
    }

    private static IRunnerLogger CreateRunnerLogger()
    {
        var runnerLogger = Substitute.For<IRunnerLogger>();
        runnerLogger.FileLogger.Returns(Substitute.For<ILogger>());
        return runnerLogger;
    }

    private void AllocateEvidence(ScriptGroup group, string revision, int requestId)
    {
        Directory.CreateDirectory(_evidenceRoot);
        group.DefinitionRequestId = requestId;
        group.DefinitionRevision = revision;
        group.EvidenceOrdinalOffset = 0;
        group.PowerShellEvidenceDirectory = _evidenceRoot;

        for (var index = 0; index < group.ScriptProperties.Count; index++)
        {
            var evidence = new PowerShellEvidence
            {
                RequestId = requestId,
                ResultId = group.DeployResultId,
                DefinitionRevision = revision,
                ScriptOrdinal = index,
                Phase = "Allocated",
                RequestedRuntime = group.PowerShellVersionNumber,
                SourcePath = group.ScriptProperties[index].ScriptPath,
                BuildIdentity = group.SourceBuildIdentity
            };

            File.WriteAllText(
                Path.Join(_evidenceRoot, $"{index}.json"),
                JsonSerializer.Serialize(evidence));
        }
    }

    [TestMethod]
    public void EnforceMismatch_RefusesBeforeAddScriptAndLeavesInvocationUnobservedWhileRecordingReceipt()
    {
        var scriptPath = Path.Join(_workDirectory, "Tampered.ps1");
        var markerPath = Path.Join(_workDirectory, "executed-marker.txt");
        var tamperedBody = $"[System.IO.File]::WriteAllText('{markerPath.Replace("'", "''")}', 'PAYLOAD_EXECUTED')";
        File.WriteAllText(scriptPath, tamperedBody, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

        var actualHash = ScriptContentHash.ComputeFromFile(scriptPath);
        var group = new ScriptGroup
        {
            DeployResultId = 501,
            ScriptsLocation = _workDirectory,
            PowerShellVersionNumber = "7.4",
            ContentVerification = ScriptContentVerificationMode.Enforce,
            CommonProperties = new Dictionary<string, VariableValue>(),
            ScriptProperties = new List<ScriptProperties>
            {
                new()
                {
                    ScriptPath = scriptPath,
                    ExpectedContentHash = OtherBaseline,
                    Properties = new Dictionary<string, VariableValue>()
                }
            }
        };

        AllocateEvidence(group, "rev-enforce", requestId: 101);
        var capture = new PowerShellEvidenceCapture();

        var logger = CreateRunnerLogger();
        var runner = new PowerShellScriptRunner(logger, group);

        Assert.ThrowsExactly<ScriptContentVerificationException>(() =>
            runner.Run(
                group.ScriptsLocation,
                scriptPath,
                new Dictionary<string, VariableValue>(),
                new Dictionary<string, VariableValue>(),
                group.ContentVerification,
                OtherBaseline));

        Assert.IsFalse(File.Exists(markerPath), "Untrusted PowerShell script must never execute on Enforce refusal.");
        logger.Received(1).Error(Arg.Is<string>(msg =>
            msg.Contains("has not been executed", StringComparison.Ordinal)
            && msg.Contains(OtherBaseline, StringComparison.Ordinal)
            && msg.Contains(actualHash, StringComparison.Ordinal)
            && !msg.Contains("PAYLOAD_EXECUTED", StringComparison.Ordinal)));
        logger.Received(1).FlushLogMessages();

        PowerShellEvidence? persistedReceipt = null;
        var requestsSource = Substitute.For<IRequestsPersistentSource>();
        requestsSource
            .When(source => source.SavePowerShellEvidence(Arg.Any<PowerShellEvidence>()))
            .Do(call => persistedReceipt = call.ArgAt<PowerShellEvidence>(0));

        var complete = capture.Ingest(group, requestsSource);

        Assert.IsFalse(complete);
        Assert.IsNotNull(persistedReceipt);
        Assert.AreEqual("Prepared", persistedReceipt.Phase);
        Assert.IsNull(persistedReceipt.InvocationObservedAt);
        Assert.IsNull(persistedReceipt.CompletedAt);
        Assert.AreEqual(actualHash.ToUpperInvariant(), persistedReceipt.TextSha256);
    }

    [TestMethod]
    public void ReportMismatch_LogsWarningAndExecutesSingleReadScriptWithMatchingReceipt()
    {
        var scriptPath = Path.Join(_workDirectory, "ModifiedReport.ps1");
        var markerPath = Path.Join(_workDirectory, "report-marker.txt");
        var scriptBody = $"[System.IO.File]::WriteAllText('{markerPath.Replace("'", "''")}', 'ran-in-report-mode')";
        File.WriteAllText(scriptPath, scriptBody, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

        var actualHash = ScriptContentHash.ComputeFromFile(scriptPath);
        var group = new ScriptGroup
        {
            DeployResultId = 502,
            ScriptsLocation = _workDirectory,
            PowerShellVersionNumber = "7.4",
            ContentVerification = ScriptContentVerificationMode.Report,
            CommonProperties = new Dictionary<string, VariableValue>(),
            ScriptProperties = new List<ScriptProperties>
            {
                new()
                {
                    ScriptPath = scriptPath,
                    ExpectedContentHash = OtherBaseline,
                    Properties = new Dictionary<string, VariableValue>()
                }
            }
        };

        AllocateEvidence(group, "rev-report", requestId: 102);
        var capture = new PowerShellEvidenceCapture();

        var logger = CreateRunnerLogger();
        var runner = new PowerShellScriptRunner(logger, group);

        var exitCode = runner.Run(
            group.ScriptsLocation,
            scriptPath,
            new Dictionary<string, VariableValue>(),
            new Dictionary<string, VariableValue>(),
            group.ContentVerification,
            OtherBaseline);

        Assert.AreEqual(0, exitCode);
        Assert.AreEqual("ran-in-report-mode", File.ReadAllText(markerPath));
        logger.Received(1).Warning(Arg.Is<string>(msg =>
            msg.Contains("Execution is permitted in report mode", StringComparison.Ordinal)
            && msg.Contains("report rather than enforce", StringComparison.Ordinal)
            && !msg.Contains("has been executed", StringComparison.Ordinal)
            && msg.Contains(OtherBaseline, StringComparison.Ordinal)
            && msg.Contains(actualHash, StringComparison.Ordinal)));

        PowerShellEvidence? persistedReceipt = null;
        var requestsSource = Substitute.For<IRequestsPersistentSource>();
        requestsSource
            .When(source => source.SavePowerShellEvidence(Arg.Any<PowerShellEvidence>()))
            .Do(call => persistedReceipt = call.ArgAt<PowerShellEvidence>(0));

        var complete = capture.Ingest(group, requestsSource);

        Assert.IsTrue(complete);
        Assert.IsNotNull(persistedReceipt);
        Assert.AreEqual("InvocationReturned", persistedReceipt.Phase);
        Assert.IsNotNull(persistedReceipt.InvocationObservedAt);
        Assert.IsNotNull(persistedReceipt.CompletedAt);
        Assert.AreEqual(actualHash.ToUpperInvariant(), persistedReceipt.TextSha256);
    }

    [TestMethod]
    public void MatchingBaseline_ExecutesCleanlyAndMatchesExecutionReceiptDigest()
    {
        var scriptPath = Path.Join(_workDirectory, "Verified.ps1");
        var markerPath = Path.Join(_workDirectory, "verified-marker.txt");
        var scriptBody = $"[System.IO.File]::WriteAllText('{markerPath.Replace("'", "''")}', 'verified')";
        File.WriteAllText(scriptPath, scriptBody, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

        var expectedHash = ScriptContentHash.ComputeFromFile(scriptPath);
        var group = new ScriptGroup
        {
            DeployResultId = 503,
            ScriptsLocation = _workDirectory,
            PowerShellVersionNumber = "7.4",
            ContentVerification = ScriptContentVerificationMode.Enforce,
            CommonProperties = new Dictionary<string, VariableValue>(),
            ScriptProperties = new List<ScriptProperties>
            {
                new()
                {
                    ScriptPath = scriptPath,
                    ExpectedContentHash = expectedHash,
                    Properties = new Dictionary<string, VariableValue>()
                }
            }
        };

        AllocateEvidence(group, "rev-matched", requestId: 103);
        var capture = new PowerShellEvidenceCapture();

        var logger = CreateRunnerLogger();
        var runner = new PowerShellScriptRunner(logger, group);

        var exitCode = runner.Run(
            group.ScriptsLocation,
            scriptPath,
            new Dictionary<string, VariableValue>(),
            new Dictionary<string, VariableValue>(),
            group.ContentVerification,
            expectedHash);

        Assert.AreEqual(0, exitCode);
        Assert.AreEqual("verified", File.ReadAllText(markerPath));
        logger.DidNotReceive().Warning(Arg.Any<string>());
        logger.DidNotReceive().Error(Arg.Any<string>());

        PowerShellEvidence? persistedReceipt = null;
        var requestsSource = Substitute.For<IRequestsPersistentSource>();
        requestsSource
            .When(source => source.SavePowerShellEvidence(Arg.Any<PowerShellEvidence>()))
            .Do(call => persistedReceipt = call.ArgAt<PowerShellEvidence>(0));

        var complete = capture.Ingest(group, requestsSource);

        Assert.IsTrue(complete);
        Assert.IsNotNull(persistedReceipt);
        Assert.AreEqual("InvocationReturned", persistedReceipt.Phase);
        Assert.IsTrue(ScriptContentHash.Matches(expectedHash, persistedReceipt.TextSha256));
    }

    [TestMethod]
    public void DoubleDotScriptName_ExecutesAndVerifiesCleanlyAgainstRecordedBaseline()
    {
        var subDir = Path.Join(_workDirectory, "sub");
        Directory.CreateDirectory(subDir);
        var scriptPath = Path.Join(subDir, "Deploy..ps1");
        var markerPath = Path.Join(_workDirectory, "double-dot-marker.txt");
        var scriptBody = $"[System.IO.File]::WriteAllText('{markerPath.Replace("'", "''")}', 'double-dot-ok')";
        File.WriteAllText(scriptPath, scriptBody, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

        var expectedHash = ScriptContentHash.ComputeFromFile(scriptPath);
        var group = new ScriptGroup
        {
            DeployResultId = 506,
            ScriptsLocation = _workDirectory,
            PowerShellVersionNumber = "7.4",
            ContentVerification = ScriptContentVerificationMode.Enforce,
            CommonProperties = new Dictionary<string, VariableValue>(),
            ScriptProperties = new List<ScriptProperties>
            {
                new()
                {
                    ScriptPath = scriptPath,
                    ExpectedContentHash = expectedHash,
                    Properties = new Dictionary<string, VariableValue>()
                }
            }
        };

        AllocateEvidence(group, "rev-double-dot", requestId: 106);
        var capture = new PowerShellEvidenceCapture();
        var logger = CreateRunnerLogger();
        var runner = new PowerShellScriptRunner(logger, group);

        var exitCode = runner.Run(
            group.ScriptsLocation,
            scriptPath,
            new Dictionary<string, VariableValue>(),
            new Dictionary<string, VariableValue>(),
            group.ContentVerification,
            expectedHash);

        Assert.AreEqual(0, exitCode);
        Assert.AreEqual("double-dot-ok", File.ReadAllText(markerPath));
        logger.DidNotReceive().Warning(Arg.Any<string>());
        logger.DidNotReceive().Error(Arg.Any<string>());

        PowerShellEvidence? persistedReceipt = null;
        var requestsSource = Substitute.For<IRequestsPersistentSource>();
        requestsSource
            .When(source => source.SavePowerShellEvidence(Arg.Any<PowerShellEvidence>()))
            .Do(call => persistedReceipt = call.ArgAt<PowerShellEvidence>(0));

        Assert.IsTrue(capture.Ingest(group, requestsSource));
        Assert.IsNotNull(persistedReceipt);
        Assert.AreEqual("InvocationReturned", persistedReceipt.Phase);
        Assert.IsTrue(ScriptContentHash.Matches(expectedHash, persistedReceipt.TextSha256));
    }

    [TestMethod]
    [DataRow(ScriptContentVerificationMode.Off)]
    [DataRow(ScriptContentVerificationMode.Report)]
    public void ParentTraversalSegmentPath_IsRejectedBeforeExecutionEvenWhenVerificationIsOffOrReport(
        ScriptContentVerificationMode mode)
    {
        var subDir = Path.Join(_workDirectory, "sub");
        Directory.CreateDirectory(subDir);
        var targetPath = Path.Join(_workDirectory, "Deploy.ps1");
        var markerPath = Path.Join(_workDirectory, $"parent-seg-{mode}-marker.txt");
        var scriptBody = $"[System.IO.File]::WriteAllText('{markerPath.Replace("'", "''")}', 'parent-seg-{mode}')";
        File.WriteAllText(targetPath, scriptBody, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

        var traversalScriptPath = subDir
            + Path.DirectorySeparatorChar
            + ".."
            + Path.DirectorySeparatorChar
            + "Deploy.ps1";

        var group = new ScriptGroup
        {
            DeployResultId = 507,
            ScriptsLocation = _workDirectory,
            PowerShellVersionNumber = "7.4",
            ContentVerification = mode,
            CommonProperties = new Dictionary<string, VariableValue>(),
            ScriptProperties = new List<ScriptProperties>
            {
                new()
                {
                    ScriptPath = traversalScriptPath,
                    ExpectedContentHash = null,
                    Properties = new Dictionary<string, VariableValue>()
                }
            }
        };

        AllocateEvidence(group, $"rev-traversal-{mode}", requestId: 107);
        var logger = CreateRunnerLogger();
        var runner = new PowerShellScriptRunner(logger, group);

        var exitCode = runner.Run(
            group.ScriptsLocation,
            traversalScriptPath,
            new Dictionary<string, VariableValue>(),
            new Dictionary<string, VariableValue>(),
            group.ContentVerification,
            expectedContentHash: null);

        Assert.AreEqual(-1, exitCode);
        Assert.IsFalse(File.Exists(markerPath));
    }

    [TestMethod]
    public void EnforceUnrecorded_RefusesBeforeAddScriptAndLeavesInvocationUnobserved()
    {
        var scriptPath = Path.Join(_workDirectory, "Unbaselined.ps1");
        var markerPath = Path.Join(_workDirectory, "unbaselined-marker.txt");
        File.WriteAllText(
            scriptPath,
            $"[System.IO.File]::WriteAllText('{markerPath.Replace("'", "''")}', 'SHOULD_NOT_RUN')");

        var group = new ScriptGroup
        {
            DeployResultId = 504,
            ScriptsLocation = _workDirectory,
            PowerShellVersionNumber = "7.4",
            CommonProperties = new Dictionary<string, VariableValue>(),
            ScriptProperties = new List<ScriptProperties>
            {
                new()
                {
                    ScriptPath = scriptPath,
                    ExpectedContentHash = null,
                    Properties = new Dictionary<string, VariableValue>()
                }
            }
        };

        Assert.AreEqual(
            ScriptContentVerificationMode.Enforce,
            group.ContentVerification,
            "Omitted ContentVerification on ScriptGroup must default fail-closed to Enforce.");

        AllocateEvidence(group, "rev-unbaselined", requestId: 104);
        var capture = new PowerShellEvidenceCapture();
        var logger = CreateRunnerLogger();
        var runner = new PowerShellScriptRunner(logger, group);

        Assert.ThrowsExactly<ScriptContentVerificationException>(() =>
            runner.Run(
                group.ScriptsLocation,
                scriptPath,
                new Dictionary<string, VariableValue>(),
                new Dictionary<string, VariableValue>()));

        Assert.IsFalse(File.Exists(markerPath));

        PowerShellEvidence? persistedReceipt = null;
        var requestsSource = Substitute.For<IRequestsPersistentSource>();
        requestsSource
            .When(source => source.SavePowerShellEvidence(Arg.Any<PowerShellEvidence>()))
            .Do(call => persistedReceipt = call.ArgAt<PowerShellEvidence>(0));

        Assert.IsFalse(capture.Ingest(group, requestsSource));
        Assert.IsNotNull(persistedReceipt);
        Assert.AreEqual("Prepared", persistedReceipt.Phase);
        Assert.IsNull(persistedReceipt.InvocationObservedAt);
    }

    [TestMethod]
    public async Task NetFrameworkRunner_EnforceMismatchAndUnrecorded_RefusesBeforeAddScriptAndPreservesPreparedReceipt()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var repoSrc = Path.GetFullPath(Path.Join(AppContext.BaseDirectory, "..", "..", "..", ".."));
        var netFxBin = new[]
            {
                Path.Join(repoSrc, "Dorc.NetFramework.Runner", "bin", "Release"),
                Path.Join(repoSrc, "Dorc.NetFramework.Runner", "bin", "Debug")
            }
            .FirstOrDefault(dir => File.Exists(Path.Join(dir, "Dorc.NetFramework.PowerShell.dll")));

        if (netFxBin == null)
        {
            Assert.Inconclusive("Dorc.NetFramework.PowerShell.dll has not been built on this host.");
            return;
        }

        var scriptPath = Path.Join(_workDirectory, "NetFxTampered.ps1");
        var markerPath = Path.Join(_workDirectory, "netfx-executed-marker.txt");
        File.WriteAllText(
            scriptPath,
            $"[System.IO.File]::WriteAllText('{markerPath.Replace("'", "''")}', 'NETFX_PAYLOAD')",
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        var actualHash = ScriptContentHash.ComputeFromFile(scriptPath);

        var group = new ScriptGroup
        {
            DeployResultId = 505,
            ScriptsLocation = _workDirectory,
            PowerShellVersionNumber = "5.1",
            ContentVerification = ScriptContentVerificationMode.Enforce,
            CommonProperties = new Dictionary<string, VariableValue>(),
            ScriptProperties = new List<ScriptProperties>
            {
                new()
                {
                    ScriptPath = scriptPath,
                    ExpectedContentHash = OtherBaseline,
                    Properties = new Dictionary<string, VariableValue>()
                }
            }
        };
        AllocateEvidence(group, "rev-netfx", requestId: 105);

        var groupJsonPath = Path.Join(_workDirectory, "group.json");
        File.WriteAllText(groupJsonPath, JsonSerializer.Serialize(group));

        var harnessPath = Path.Join(_workDirectory, "InvokeNetFxRunner.ps1");
        File.WriteAllText(harnessPath, @"
param([string]$BinDir, [string]$GroupJsonPath, [string]$ScriptPath, [string]$ExpectedBaseline)
$ErrorActionPreference = 'Stop'
$netstd = [System.Reflection.Assembly]::Load('netstandard, Version=2.0.0.0, Culture=neutral, PublicKeyToken=cc7b13ffcd2ddd51').Location
Add-Type -TypeDefinition @'
using System;
using System.IO;
using System.Reflection;
public static class NetFxResolver
{
    public static void Register(string binDir)
    {
        AppDomain.CurrentDomain.AssemblyResolve += (s, e) =>
        {
            var name = new AssemblyName(e.Name).Name;
            if (name.EndsWith("".resources"", StringComparison.OrdinalIgnoreCase)) return null;
            var path = Path.Combine(binDir, name + "".dll"");
            return File.Exists(path) ? Assembly.LoadFrom(path) : null;
        };
    }
}
'@
[NetFxResolver]::Register($BinDir)
$refs = @(
    $netstd,
    (Join-Path $BinDir 'Dorc.ApiModel.dll'),
    (Join-Path $BinDir 'Dorc.Runner.Logger.dll'),
    (Join-Path $BinDir 'OpenSearch.Client.dll'),
    (Join-Path $BinDir 'Microsoft.Extensions.Logging.Abstractions.dll'),
    (Join-Path $BinDir 'Newtonsoft.Json.dll'),
    (Join-Path $BinDir 'Dorc.NetFramework.PowerShell.dll')
)
Add-Type -IgnoreWarnings -ReferencedAssemblies $refs -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.IO;
using Dorc.ApiModel;
using Dorc.ApiModel.MonitorRunnerApi;
using Dorc.NetFramework.PowerShell;
using Dorc.Runner.Logger;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json;
using OpenSearch.Client;

public sealed class StubRunnerLogger : IRunnerLogger
{
    public ILogger FileLogger { get { return NullLogger.Instance; } }
    public IOpenSearchClient OpenSearchClient { get { return null; } }
    public void SetRequestId(int requestId) { }
    public void SetDeploymentResultId(int deploymentResultId) { }
    public void UpdateLog(int deploymentResultId, string log) { }
    public void Information(string message) { }
    public void Information(string message, params object[] values) { }
    public void Verbose(string message) { }
    public void Warning(string message) { }
    public void Error(string message) { Console.Error.WriteLine(message); }
    public void Error(string message, Exception exception) { Console.Error.WriteLine(message); }
    public void Error(Exception exception, string message, params object[] values) { Console.Error.WriteLine(message); }
    public void Debug(string message) { }
    public void FlushLogMessages() { }
}

public static class NetFxVerificationProbe
{
    public static int Execute(string groupJsonPath, string scriptPath, string expectedBaseline)
    {
        var group = JsonConvert.DeserializeObject<ScriptGroup>(File.ReadAllText(groupJsonPath));
        var runner = new PowerShellScriptRunner(new StubRunnerLogger(), group.DeployResultId, group);
        var props = new Dictionary<string, VariableValue>();
        var list = new List<ValueTuple<string, IDictionary<string, VariableValue>, string>>
        {
            ValueTuple.Create<string, IDictionary<string, VariableValue>, string>(
                scriptPath,
                props,
                string.IsNullOrEmpty(expectedBaseline) ? null : expectedBaseline)
        };
        try
        {
            runner.Run(group.ScriptsLocation, list, props, ScriptContentVerificationMode.Enforce);
            return 0;
        }
        catch (ScriptContentVerificationException)
        {
            return 42;
        }
    }
}
'@

exit ([NetFxVerificationProbe]::Execute($GroupJsonPath, $ScriptPath, $ExpectedBaseline))
");

        foreach (var (baseline, suffix) in new[] { (OtherBaseline, "mismatch"), (string.Empty, "unrecorded") })
        {
            var caseGroup = new ScriptGroup
            {
                DeployResultId = 505,
                ScriptsLocation = _workDirectory,
                PowerShellVersionNumber = "5.1",
                ContentVerification = ScriptContentVerificationMode.Enforce,
                CommonProperties = new Dictionary<string, VariableValue>(),
                ScriptProperties = new List<ScriptProperties>
                {
                    new()
                    {
                        ScriptPath = scriptPath,
                        ExpectedContentHash = string.IsNullOrEmpty(baseline) ? null : baseline,
                        Properties = new Dictionary<string, VariableValue>()
                    }
                }
            };
            AllocateEvidence(caseGroup, $"rev-netfx-{suffix}", requestId: 105);
            var caseGroupJsonPath = Path.Join(_workDirectory, $"group-{suffix}.json");
            File.WriteAllText(caseGroupJsonPath, JsonSerializer.Serialize(caseGroup));

            using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "powershell.exe",
                ArgumentList =
                {
                    "-NoProfile",
                    "-NonInteractive",
                    "-ExecutionPolicy",
                    "Bypass",
                    "-File",
                    harnessPath,
                    netFxBin,
                    caseGroupJsonPath,
                    scriptPath,
                    baseline
                },
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            })!;

            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();

            if (!process.WaitForExit(30_000))
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                    process.WaitForExit(5_000);
                }
                catch (InvalidOperationException)
                {
                }

                Assert.Fail($"powershell.exe harness timed out for {suffix}.");
            }

            await Task.WhenAll(stdoutTask, stderrTask).WaitAsync(TimeSpan.FromSeconds(10));
            var stdout = await stdoutTask;
            var stderr = await stderrTask;

            Assert.AreEqual(
                42,
                process.ExitCode,
                $"Expected ScriptContentVerificationException (42) for {suffix}, stdout: {stdout}, stderr: {stderr}");
            Assert.IsFalse(File.Exists(markerPath), ".NET Framework runner must never execute tampered or unrecorded script in Enforce mode.");
            StringAssert.Contains(stderr, "has not been executed");

            PowerShellEvidence? caseReceipt = null;
            var caseSource = Substitute.For<IRequestsPersistentSource>();
            caseSource
                .When(source => source.SavePowerShellEvidence(Arg.Any<PowerShellEvidence>()))
                .Do(call => caseReceipt = call.ArgAt<PowerShellEvidence>(0));

            var caseComplete = new PowerShellEvidenceCapture().Ingest(caseGroup, caseSource);
            Assert.IsFalse(caseComplete);
            Assert.IsNotNull(caseReceipt);
            Assert.AreEqual("Prepared", caseReceipt.Phase);
            Assert.IsNull(caseReceipt.InvocationObservedAt);
            Assert.IsNull(caseReceipt.CompletedAt);
            Assert.AreEqual(actualHash.ToUpperInvariant(), caseReceipt.TextSha256);
        }
    }

    [TestMethod]
    public async Task NetFrameworkRunner_DoubleDotScriptNameAndParentTraversalSegment_ExecuteAndCaptureEvidenceCleanly()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var repoSrc = Path.GetFullPath(Path.Join(AppContext.BaseDirectory, "..", "..", "..", ".."));
        var netFxBin = new[]
            {
                Path.Join(repoSrc, "Dorc.NetFramework.Runner", "bin", "Release"),
                Path.Join(repoSrc, "Dorc.NetFramework.Runner", "bin", "Debug")
            }
            .FirstOrDefault(dir => File.Exists(Path.Join(dir, "Dorc.NetFramework.PowerShell.dll")));

        if (netFxBin == null)
        {
            Assert.Inconclusive("Dorc.NetFramework.PowerShell.dll has not been built on this host.");
            return;
        }

        var subDir = Path.Join(_workDirectory, "sub");
        Directory.CreateDirectory(subDir);

        var doubleDotScriptPath = Path.Join(subDir, "Deploy..ps1");
        var doubleDotMarkerPath = Path.Join(_workDirectory, "netfx-double-dot.txt");
        File.WriteAllText(
            doubleDotScriptPath,
            $"[System.IO.File]::WriteAllText('{doubleDotMarkerPath.Replace("'", "''")}', 'netfx-double-dot-ok')",
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        var doubleDotHash = ScriptContentHash.ComputeFromFile(doubleDotScriptPath);

        var traversalTargetPath = Path.Join(_workDirectory, "Deploy.ps1");
        var traversalMarkerPath = Path.Join(_workDirectory, "netfx-traversal.txt");
        File.WriteAllText(
            traversalTargetPath,
            $"[System.IO.File]::WriteAllText('{traversalMarkerPath.Replace("'", "''")}', 'netfx-traversal-ok')",
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        var traversalScriptPath = subDir
            + Path.DirectorySeparatorChar
            + ".."
            + Path.DirectorySeparatorChar
            + "Deploy.ps1";

        var harnessPath = Path.Join(_workDirectory, "InvokeNetFxPathCases.ps1");
        File.WriteAllText(harnessPath, @"
param([string]$BinDir, [string]$GroupJsonPath, [string]$ScriptPath, [string]$ModeName, [string]$ExpectedBaseline)
$ErrorActionPreference = 'Stop'
$netstd = [System.Reflection.Assembly]::Load('netstandard, Version=2.0.0.0, Culture=neutral, PublicKeyToken=cc7b13ffcd2ddd51').Location
Add-Type -TypeDefinition @'
using System;
using System.IO;
using System.Reflection;
public static class NetFxPathResolver
{
    public static void Register(string binDir)
    {
        AppDomain.CurrentDomain.AssemblyResolve += (s, e) =>
        {
            var name = new AssemblyName(e.Name).Name;
            if (name.EndsWith("".resources"", StringComparison.OrdinalIgnoreCase)) return null;
            var path = Path.Combine(binDir, name + "".dll"");
            return File.Exists(path) ? Assembly.LoadFrom(path) : null;
        };
    }
}
'@
[NetFxPathResolver]::Register($BinDir)
$refs = @(
    $netstd,
    (Join-Path $BinDir 'Dorc.ApiModel.dll'),
    (Join-Path $BinDir 'Dorc.Runner.Logger.dll'),
    (Join-Path $BinDir 'OpenSearch.Client.dll'),
    (Join-Path $BinDir 'Microsoft.Extensions.Logging.Abstractions.dll'),
    (Join-Path $BinDir 'Newtonsoft.Json.dll'),
    (Join-Path $BinDir 'Dorc.NetFramework.PowerShell.dll')
)
Add-Type -IgnoreWarnings -ReferencedAssemblies $refs -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.IO;
using Dorc.ApiModel;
using Dorc.ApiModel.MonitorRunnerApi;
using Dorc.NetFramework.PowerShell;
using Dorc.Runner.Logger;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json;
using OpenSearch.Client;

public sealed class StubPathRunnerLogger : IRunnerLogger
{
    public ILogger FileLogger { get { return NullLogger.Instance; } }
    public IOpenSearchClient OpenSearchClient { get { return null; } }
    public void SetRequestId(int requestId) { }
    public void SetDeploymentResultId(int deploymentResultId) { }
    public void UpdateLog(int deploymentResultId, string log) { }
    public void Information(string message) { }
    public void Information(string message, params object[] values) { }
    public void Verbose(string message) { }
    public void Warning(string message) { }
    public void Error(string message) { Console.Error.WriteLine(message); }
    public void Error(string message, Exception exception) { Console.Error.WriteLine(message); }
    public void Error(Exception exception, string message, params object[] values) { Console.Error.WriteLine(message); }
    public void Debug(string message) { }
    public void FlushLogMessages() { }
}

public static class NetFxPathProbe
{
    public static int Execute(string groupJsonPath, string scriptPath, string modeName, string expectedBaseline)
    {
        var group = JsonConvert.DeserializeObject<ScriptGroup>(File.ReadAllText(groupJsonPath));
        var mode = (ScriptContentVerificationMode)Enum.Parse(typeof(ScriptContentVerificationMode), modeName);
        var runner = new PowerShellScriptRunner(new StubPathRunnerLogger(), group.DeployResultId, group);
        var props = new Dictionary<string, VariableValue>();
        var list = new List<ValueTuple<string, IDictionary<string, VariableValue>, string>>
        {
            ValueTuple.Create<string, IDictionary<string, VariableValue>, string>(
                scriptPath,
                props,
                string.IsNullOrEmpty(expectedBaseline) ? null : expectedBaseline)
        };
        runner.Run(group.ScriptsLocation, list, props, mode);
        return 0;
    }
}
'@

exit ([NetFxPathProbe]::Execute($GroupJsonPath, $ScriptPath, $ModeName, $ExpectedBaseline))
");

        var cases = new[]
        {
            (
                Suffix: "double-dot-enforce",
                ScriptPath: doubleDotScriptPath,
                Mode: ScriptContentVerificationMode.Enforce,
                Baseline: doubleDotHash,
                ExpectSuccess: true,
                ExpectedHash: doubleDotHash,
                MarkerPath: doubleDotMarkerPath,
                MarkerText: "netfx-double-dot-ok"
            ),
            (
                Suffix: "traversal-off",
                ScriptPath: traversalScriptPath,
                Mode: ScriptContentVerificationMode.Off,
                Baseline: string.Empty,
                ExpectSuccess: false,
                ExpectedHash: string.Empty,
                MarkerPath: traversalMarkerPath,
                MarkerText: string.Empty
            ),
            (
                Suffix: "traversal-report",
                ScriptPath: traversalScriptPath,
                Mode: ScriptContentVerificationMode.Report,
                Baseline: string.Empty,
                ExpectSuccess: false,
                ExpectedHash: string.Empty,
                MarkerPath: traversalMarkerPath,
                MarkerText: string.Empty
            )
        };

        foreach (var testCase in cases)
        {
            if (File.Exists(testCase.MarkerPath))
            {
                File.Delete(testCase.MarkerPath);
            }

            var caseGroup = new ScriptGroup
            {
                DeployResultId = 508,
                ScriptsLocation = _workDirectory,
                PowerShellVersionNumber = "5.1",
                ContentVerification = testCase.Mode,
                CommonProperties = new Dictionary<string, VariableValue>(),
                ScriptProperties = new List<ScriptProperties>
                {
                    new()
                    {
                        ScriptPath = testCase.ScriptPath,
                        ExpectedContentHash = string.IsNullOrEmpty(testCase.Baseline) ? null : testCase.Baseline,
                        Properties = new Dictionary<string, VariableValue>()
                    }
                }
            };
            AllocateEvidence(caseGroup, $"rev-netfx-{testCase.Suffix}", requestId: 108);
            var caseGroupJsonPath = Path.Join(_workDirectory, $"group-{testCase.Suffix}.json");
            File.WriteAllText(caseGroupJsonPath, JsonSerializer.Serialize(caseGroup));

            using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "powershell.exe",
                ArgumentList =
                {
                    "-NoProfile",
                    "-NonInteractive",
                    "-ExecutionPolicy",
                    "Bypass",
                    "-File",
                    harnessPath,
                    netFxBin,
                    caseGroupJsonPath,
                    testCase.ScriptPath,
                    testCase.Mode.ToString(),
                    testCase.Baseline
                },
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            })!;

            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();

            if (!process.WaitForExit(30_000))
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                    process.WaitForExit(5_000);
                }
                catch (InvalidOperationException)
                {
                }

                Assert.Fail($"powershell.exe harness timed out for {testCase.Suffix}.");
            }

            await Task.WhenAll(stdoutTask, stderrTask).WaitAsync(TimeSpan.FromSeconds(10));
            var stdout = await stdoutTask;
            var stderr = await stderrTask;

            if (!testCase.ExpectSuccess)
            {
                Assert.AreNotEqual(
                    0,
                    process.ExitCode,
                    $"Expected refusal for {testCase.Suffix}, stdout: {stdout}, stderr: {stderr}");
                Assert.IsFalse(File.Exists(testCase.MarkerPath));
                continue;
            }

            Assert.AreEqual(
                0,
                process.ExitCode,
                $"Expected success (0) for {testCase.Suffix}, stdout: {stdout}, stderr: {stderr}");
            Assert.AreEqual(testCase.MarkerText, File.ReadAllText(testCase.MarkerPath));

            PowerShellEvidence? caseReceipt = null;
            var caseSource = Substitute.For<IRequestsPersistentSource>();
            caseSource
                .When(source => source.SavePowerShellEvidence(Arg.Any<PowerShellEvidence>()))
                .Do(call => caseReceipt = call.ArgAt<PowerShellEvidence>(0));

            Assert.IsTrue(new PowerShellEvidenceCapture().Ingest(caseGroup, caseSource));
            Assert.IsNotNull(caseReceipt);
            Assert.AreEqual("InvocationReturned", caseReceipt.Phase);
            Assert.AreEqual(testCase.ScriptPath, caseReceipt.SourcePath);
            Assert.IsTrue(ScriptContentHash.Matches(testCase.ExpectedHash, caseReceipt.TextSha256));
        }
    }
}
