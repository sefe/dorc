using System.Runtime.Versioning;
using System.Security.Cryptography;
using Dorc.ApiModel;
using Dorc.Core;
using Dorc.Monitor.Pipes;
using Dorc.Monitor.RequestProcessors;
using Dorc.Monitor.Tests.Init;
using Dorc.PersistentData.Contexts;
using Dorc.PersistentData.Model;
using Dorc.PersistentData.Sources.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Environment = Dorc.PersistentData.Model.Environment;

namespace Dorc.Monitor.IntegrationTests.Tests;

[TestClass]
[DoNotParallelize]
[SupportedOSPlatform("windows")]
public class PendingRequestProcessorTests : MonitorServiceTestBase
{
    [TestMethod]
    public void ComponentTreeOrchestration_HonorsStopOnFailure_AndSkipsNonProdOnlyOnProduction()
    {
        var ext = RandomNumberGenerator.GetHexString(6);
        var tempScriptDir = Path.Join(Path.GetTempPath(), $"dorc-ws3-tree-{ext}");
        Directory.CreateDirectory(tempScriptDir);

        try
        {
            File.WriteAllText(Path.Join(tempScriptDir, "pass.ps1"), "Write-Information 'Component succeeded'");
            File.WriteAllText(Path.Join(tempScriptDir, "fail.ps1"), "throw 'Simulated component failure'");

            var pipeServer = new ScriptGroupPipeServer(provider.GetRequiredService<ILogger<ScriptGroupPipeServer>>());
            SubstituteScopedWith<IScriptDispatcher>(new PipeBackedScriptDispatcher(
                pipeServer,
                provider.GetRequiredService<IRequestsPersistentSource>(),
                provider.GetRequiredService<ILoggerFactory>(),
                useNamedPipeClient: true));

            var requestsSource = provider.GetRequiredService<IRequestsPersistentSource>();
            var loggerFactory = provider.GetRequiredService<ILoggerFactory>();
            var serializer = new DeploymentRequestDetailSerializer();

            // Case A: StopOnFailure = true on first component -> halts remaining components, request -> Failed
            var stopTrueScenario = SeedMultiComponentScenario(
                $"{ext}-stop",
                tempScriptDir,
                isProd: false,
                firstComponentFails: true,
                firstComponentStopOnFailure: true,
                secondComponentNonProdOnly: false);

            var pendingProcessor = provider.GetRequiredService<IPendingRequestProcessor>();
            var reqA = requestsSource.GetRequest(stopTrueScenario.RequestId);
            pendingProcessor.Execute(new RequestToProcessDto(reqA, serializer.Deserialize(reqA.RequestDetails)), CancellationToken.None, loggerFactory);

            var updatedReqA = requestsSource.GetRequest(stopTrueScenario.RequestId);
            Assert.AreEqual(DeploymentRequestStatus.Failed.ToString(), updatedReqA.Status);
            var resultsA = GetDeploymentResultsForRequest(stopTrueScenario.RequestId);
            Assert.AreEqual(DeploymentResultStatus.Failed.ToString(), resultsA[stopTrueScenario.FirstComponentId]);
            Assert.AreEqual(DeploymentResultStatus.Cancelled.ToString(), resultsA[stopTrueScenario.SecondComponentId], "Second component must be cancelled without executing when StopOnFailure is true.");

            // Case B: StopOnFailure = false on first component -> continues and executes second component
            var stopFalseScenario = SeedMultiComponentScenario(
                $"{ext}-cont",
                tempScriptDir,
                isProd: false,
                firstComponentFails: true,
                firstComponentStopOnFailure: false,
                secondComponentNonProdOnly: false);

            var reqB = requestsSource.GetRequest(stopFalseScenario.RequestId);
            pendingProcessor.Execute(new RequestToProcessDto(reqB, serializer.Deserialize(reqB.RequestDetails)), CancellationToken.None, loggerFactory);

            var updatedReqB = requestsSource.GetRequest(stopFalseScenario.RequestId);
            Assert.AreEqual(DeploymentRequestStatus.Failed.ToString(), updatedReqB.Status);
            var resultsB = GetDeploymentResultsForRequest(stopFalseScenario.RequestId);
            Assert.AreEqual(DeploymentResultStatus.Failed.ToString(), resultsB[stopFalseScenario.FirstComponentId]);
            Assert.AreEqual(DeploymentResultStatus.Complete.ToString(), resultsB[stopFalseScenario.SecondComponentId]);

            // Case C: NonProdOnly = true on an IsProd = true environment -> skips second component with Warning status
            var nonProdSkipScenario = SeedMultiComponentScenario(
                $"{ext}-prod",
                tempScriptDir,
                isProd: true,
                firstComponentFails: false,
                firstComponentStopOnFailure: true,
                secondComponentNonProdOnly: true);

            var reqC = requestsSource.GetRequest(nonProdSkipScenario.RequestId);
            pendingProcessor.Execute(new RequestToProcessDto(reqC, serializer.Deserialize(reqC.RequestDetails)), CancellationToken.None, loggerFactory);

            var updatedReqC = requestsSource.GetRequest(nonProdSkipScenario.RequestId);
            Assert.AreEqual(DeploymentRequestStatus.Completed.ToString(), updatedReqC.Status);
            var resultsC = GetDeploymentResultsForRequest(nonProdSkipScenario.RequestId);
            Assert.AreEqual(DeploymentResultStatus.Complete.ToString(), resultsC[nonProdSkipScenario.FirstComponentId]);
            Assert.AreEqual(DeploymentResultStatus.Warning.ToString(), resultsC[nonProdSkipScenario.SecondComponentId]);
        }
        finally
        {
            if (Directory.Exists(tempScriptDir))
            {
                Directory.Delete(tempScriptDir, recursive: true);
            }
        }
    }

    [TestMethod]
    public void CrashRecovery_CancelStaleRequests_FailsInterruptedComponentWithoutReRunningScript()
    {
        var ext = RandomNumberGenerator.GetHexString(6);
        var tempScriptDir = Path.Join(Path.GetTempPath(), $"dorc-ws3-crash-{ext}");
        var executionCounterFile = Path.Join(tempScriptDir, "exec-count.txt");
        Directory.CreateDirectory(tempScriptDir);

        try
        {
            File.WriteAllText(Path.Join(tempScriptDir, "pass.ps1"), $@"
Add-Content -Path '{executionCounterFile.Replace("'", "''")}' -Value 'ran'
");

            var scenario = SeedMultiComponentScenario(
                $"{ext}-crash",
                tempScriptDir,
                isProd: false,
                firstComponentFails: false,
                firstComponentStopOnFailure: true,
                secondComponentNonProdOnly: false);

            var contextFactory = provider.GetRequiredService<IDeploymentContextFactory>();
            using (var ctx = contextFactory.GetContext())
            {
                var req = ctx.DeploymentRequests.First(r => r.Id == scenario.RequestId);
                req.Status = DeploymentRequestStatus.Running.ToString();

                var comp1 = ctx.Components.First(c => c.Id == scenario.FirstComponentId);
                var comp2 = ctx.Components.First(c => c.Id == scenario.SecondComponentId);

                ctx.DeploymentResults.Add(new DeploymentResult
                {
                    DeploymentRequest = req,
                    Component = comp1,
                    Status = DeploymentResultStatus.Complete.ToString(),
                    Log = "Completed before crash"
                });
                ctx.DeploymentResults.Add(new DeploymentResult
                {
                    DeploymentRequest = req,
                    Component = comp2,
                    Status = DeploymentResultStatus.Running.ToString(),
                    Log = "In progress when Monitor crashed"
                });
                ctx.SaveChanges();
            }

            var pipeServer = new ScriptGroupPipeServer(provider.GetRequiredService<ILogger<ScriptGroupPipeServer>>());
            SubstituteScopedWith<IScriptDispatcher>(new PipeBackedScriptDispatcher(
                pipeServer,
                provider.GetRequiredService<IRequestsPersistentSource>(),
                provider.GetRequiredService<ILoggerFactory>(),
                useNamedPipeClient: true));

            var stateProcessor = provider.GetRequiredService<IDeploymentRequestStateProcessor>();
            stateProcessor.CancelStaleRequests(isProduction: false);

            var requestsSource = provider.GetRequiredService<IRequestsPersistentSource>();
            var recoveredRequest = requestsSource.GetRequest(scenario.RequestId);
            Assert.AreEqual(DeploymentRequestStatus.Pending.ToString(), recoveredRequest.Status, "Stale Running request must transition to Pending for reconciliation.");

            var pendingProcessor = provider.GetRequiredService<IPendingRequestProcessor>();
            var serializer = new DeploymentRequestDetailSerializer();
            pendingProcessor.Execute(
                new RequestToProcessDto(recoveredRequest, serializer.Deserialize(recoveredRequest.RequestDetails)),
                CancellationToken.None,
                provider.GetRequiredService<ILoggerFactory>());

            var finalRequest = requestsSource.GetRequest(scenario.RequestId);
            Assert.AreEqual(DeploymentRequestStatus.Failed.ToString(), finalRequest.Status);
            Assert.IsFalse(File.Exists(executionCounterFile), "Interrupted component script must not be blindly re-executed after crash.");

            using (var ctx = contextFactory.GetContext())
            {
                var comp2Result = ctx.DeploymentResults
                    .Include(dr => dr.DeploymentRequest)
                    .Include(dr => dr.Component)
                    .First(dr => dr.DeploymentRequest.Id == scenario.RequestId && dr.Component.Id == scenario.SecondComponentId);
                Assert.AreEqual(DeploymentResultStatus.Failed.ToString(), comp2Result.Status);
                StringAssert.Contains(comp2Result.Log, "The previous component execution was interrupted");
            }
        }
        finally
        {
            if (Directory.Exists(tempScriptDir))
            {
                Directory.Delete(tempScriptDir, recursive: true);
            }
        }
    }

    private void SubstituteScopedWith<T>(T instance) where T : class
    {
        collection.Replace(ServiceDescriptor.Scoped<T>(_ => instance));
        provider = collection.BuildServiceProvider();
    }

    private (int RequestId, int FirstComponentId, int SecondComponentId) SeedMultiComponentScenario(
        string suffix,
        string scriptDir,
        bool isProd,
        bool firstComponentFails,
        bool firstComponentStopOnFailure,
        bool secondComponentNonProdOnly)
    {
        var contextFactory = provider.GetRequiredService<IDeploymentContextFactory>();
        var requestsSource = provider.GetRequiredService<IRequestsPersistentSource>();
        var serializer = new DeploymentRequestDetailSerializer();

        var projectName = $"Proj-{suffix}";
        var envName = $"Env-{suffix}";
        var comp1Name = $"Comp1-{suffix}";
        var comp2Name = $"Comp2-{suffix}";

        int comp1Id;
        int comp2Id;

        using (var ctx = (DeploymentContext)contextFactory.GetContext())
        {
            var project = new Project
            {
                Name = projectName,
                ObjectId = Guid.NewGuid(),
                ArtefactsUrl = scriptDir,
                ArtefactsSubPaths = "."
            };
            var env = new Environment
            {
                Name = envName,
                ObjectId = Guid.NewGuid(),
                Secure = false,
                IsProd = isProd
            };
            project.Environments.Add(env);
            ctx.Projects.Add(project);
            ctx.Environments.Add(env);

            var comp1 = new Component
            {
                Name = comp1Name,
                ObjectId = Guid.NewGuid(),
                ComponentType = ComponentType.PowerShell,
                IsEnabled = true,
                StopOnFailure = firstComponentStopOnFailure,
                Script = new Script
                {
                    Name = comp1Name,
                    Path = Path.Join(scriptDir, firstComponentFails ? "fail.ps1" : "pass.ps1"),
                    NonProdOnly = false,
                    IsPathJSON = false,
                    PowerShellVersionNumber = "v7"
                }
            };
            var comp2 = new Component
            {
                Name = comp2Name,
                ObjectId = Guid.NewGuid(),
                ComponentType = ComponentType.PowerShell,
                IsEnabled = true,
                StopOnFailure = true,
                Script = new Script
                {
                    Name = comp2Name,
                    Path = Path.Join(scriptDir, "pass.ps1"),
                    NonProdOnly = secondComponentNonProdOnly,
                    IsPathJSON = false,
                    PowerShellVersionNumber = "v7"
                }
            };

            comp1.Projects.Add(project);
            comp2.Projects.Add(project);
            ctx.Components.Add(comp1);
            ctx.Components.Add(comp2);
            ctx.SaveChanges();

            comp1Id = comp1.Id;
            comp2Id = comp2.Id;
        }

        var detail = new DeploymentRequestDetail
        {
            EnvironmentName = envName,
            BuildDetail = new BuildDetail
            {
                Project = projectName,
                BuildNumber = "1.0.0",
                DropLocation = $"file:///{scriptDir.Replace('\\', '/')}"
            },
            Components = new List<string> { comp1Name, comp2Name }
        };

        var requestId = requestsSource.SubmitRequest(new DeploymentRequest
        {
            RequestDetails = serializer.Serialize(detail),
            UserName = "test-user",
            RequestedTime = DateTimeOffset.UtcNow,
            Project = projectName,
            Environment = envName,
            BuildNumber = "1.0.0",
            Components = $"{comp1Name}|{comp2Name}",
            Status = DeploymentRequestStatus.Pending.ToString(),
            IsProd = isProd
        });

        return (requestId, comp1Id, comp2Id);
    }

    private Dictionary<int, string> GetDeploymentResultsForRequest(int requestId)
    {
        var contextFactory = provider.GetRequiredService<IDeploymentContextFactory>();
        using var ctx = contextFactory.GetContext();
        return ctx.DeploymentResults
            .Include(dr => dr.DeploymentRequest)
            .Include(dr => dr.Component)
            .Where(dr => dr.DeploymentRequest.Id == requestId)
            .ToDictionary(dr => dr.Component.Id, dr => dr.Status ?? string.Empty);
    }
}
