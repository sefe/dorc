using System.Collections.Concurrent;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using Dorc.ApiModel;
using Dorc.Core;
using Dorc.Core.HighAvailability;
using Dorc.Core.Interfaces;
using Dorc.Monitor.Pipes;
using Dorc.Monitor.RequestProcessors;
using Dorc.Monitor.Tests.Init;
using Dorc.PersistentData.Contexts;
using Dorc.PersistentData.Model;
using Dorc.PersistentData.Sources.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Environment = Dorc.PersistentData.Model.Environment;

namespace Dorc.Monitor.IntegrationTests.Tests;

[TestClass]
[DoNotParallelize]
[SupportedOSPlatform("windows")]
public class ScriptDispatcherTests : MonitorServiceTestBase
{
    [TestMethod]
    public void NamedPipeAndFileTransport_ExecutePowerShellScript_WithVariableInterpolationAndRedaction()
    {
        var ext = RandomNumberGenerator.GetHexString(6);
        var tempScriptDir = Path.Combine(Path.GetTempPath(), $"dorc-ws3-scripts-{ext}");
        var outputMarkerFile = Path.Combine(tempScriptDir, "output-marker.txt");
        Directory.CreateDirectory(tempScriptDir);

        try
        {
            var scriptPath = Path.Combine(tempScriptDir, "deploy-step.ps1");
            File.WriteAllText(scriptPath, @"
$parsedTags = $TagList | ConvertFrom-Json
$summary = ""Env=$EnvironmentName|Region=$Region|Endpoint=$EndpointUrl|Secret=$ApiSecretKey|Tags=$($parsedTags -join ',')""
Set-Content -Path $OutputMarkerPath -Value $summary -NoNewline
Write-Information ""Deployed to $EndpointUrl with secret $ApiSecretKey""
");

            var seeded = SeedEndToEndDeploymentScenario(
                ext,
                tempScriptDir,
                "deploy-step.ps1",
                outputMarkerFile,
                isProd: false,
                nonProdOnly: false,
                stopOnFailure: true,
                psVersion: "v7");

            // 1. Named Pipe transport (ScriptGroupPipeServer <-> ScriptGroupPipeClient -> ScriptGroupProcessor -> PowerShellScriptRunner)
            var pipeServer = new ScriptGroupPipeServer(provider.GetRequiredService<ILogger<ScriptGroupPipeServer>>());
            var pipeDispatcher = CreateInProcessRunnerScriptDispatcher(pipeServer, useNamedPipeClient: true);
            SubstituteScopedWith<IScriptDispatcher>(pipeDispatcher);

            var pendingProcessor = provider.GetRequiredService<IPendingRequestProcessor>();
            var loggerFactory = provider.GetRequiredService<ILoggerFactory>();
            var requestsSource = provider.GetRequiredService<IRequestsPersistentSource>();

            var reqModel = requestsSource.GetRequest(seeded.RequestId);
            var serializer = new DeploymentRequestDetailSerializer();
            pendingProcessor.Execute(
                new RequestToProcessDto(reqModel, serializer.Deserialize(reqModel.RequestDetails)),
                CancellationToken.None,
                loggerFactory);

            var updatedReq = requestsSource.GetRequest(seeded.RequestId);
            Assert.AreEqual(DeploymentRequestStatus.Completed.ToString(), updatedReq.Status, $"Expected Completed but got {updatedReq.Status}: {updatedReq.Log}");
            Assert.IsTrue(File.Exists(outputMarkerFile), "PowerShell script should have written the output marker file.");

            var firstResult = requestsSource.GetDeploymentResultsForRequest(seeded.RequestId).Single();
            Assert.IsFalse((firstResult.Log ?? string.Empty).Contains($"TopSecretValue-{ext}", StringComparison.Ordinal),
                "DeploymentResult.Log must redact secure variable values.");
            StringAssert.Contains(firstResult.Log ?? string.Empty, "***");

            var markerContent = File.ReadAllText(outputMarkerFile);
            Assert.AreEqual(
                $"Env={seeded.ChildEnvName}|Region=eu-west-2|Endpoint=https://eu-west-2.{seeded.ChildEnvName}.sefe.internal|Secret=TopSecretValue-{ext}|Tags=alpha,beta",
                markerContent);

            // 2. JSON File fallback transport (ScriptGroupFileWriter <-> ScriptGroupFileReader -> ScriptGroupProcessor)
            File.Delete(outputMarkerFile);
            var secondRequestId = CreatePinnedDeploymentRequest(seeded.ProjectName, seeded.ChildEnvName, seeded.ComponentName, tempScriptDir);

            var fileWriter = new ScriptGroupFileWriter(provider.GetRequiredService<ILogger<ScriptGroupFileWriter>>());
            var fileDispatcher = CreateInProcessRunnerScriptDispatcher(fileWriter, useNamedPipeClient: false);
            SubstituteScopedWith<IScriptDispatcher>(fileDispatcher);

            var secondReqModel = requestsSource.GetRequest(secondRequestId);
            pendingProcessor = provider.GetRequiredService<IPendingRequestProcessor>();
            pendingProcessor.Execute(
                new RequestToProcessDto(secondReqModel, serializer.Deserialize(secondReqModel.RequestDetails)),
                CancellationToken.None,
                loggerFactory);

            var updatedSecondReq = requestsSource.GetRequest(secondRequestId);
            Assert.AreEqual(DeploymentRequestStatus.Completed.ToString(), updatedSecondReq.Status, $"Expected Completed on file transport but got {updatedSecondReq.Status}: {updatedSecondReq.Log}");
            Assert.IsTrue(File.Exists(outputMarkerFile), "PowerShell script should have executed via ScriptGroupFileReader.");
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
    public async Task DistributedLockLoss_AndMaxConcurrentDeploymentsBackpressure_AreEnforced()
    {
        var ext = RandomNumberGenerator.GetHexString(6);
        var env1 = $"env-ha-1-{ext}";
        var env2 = $"env-ha-2-{ext}";

        var req1 = Dorc.Monitor.Tests.Data.DeploymentRequestData.GetDeploymentRequest(env1, "user1", "proj1");
        AddDeploymentRequests(new[] { req1 });

        try
        {
            using var lockLostCts = new CancellationTokenSource();
            var fakeLock = Substitute.For<IDistributedLock>();
            fakeLock.IsValid.Returns(_ => !lockLostCts.IsCancellationRequested);
            fakeLock.LockLostToken.Returns(lockLostCts.Token);

            var lockService = Substitute.For<IDistributedLockService>();
            lockService.IsEnabled.Returns(true);
            lockService.TryAcquireLockAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult<IDistributedLock?>(fakeLock));

            collection.Replace(ServiceDescriptor.Singleton<IDistributedLockService>(lockService));

            var observedCancellationOnLockLoss = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var req1Started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseReq1 = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var req2Executed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var activeConcurrentCount = 0;
            var maxObservedConcurrent = 0;

            using var engineCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            Dorc.PersistentData.Model.DeploymentRequest? req2 = null;

            var pendingMock = Substitute.For<IPendingRequestProcessor>();
            pendingMock.When(p => p.Execute(Arg.Any<RequestToProcessDto>(), Arg.Any<CancellationToken>(), Arg.Any<ILoggerFactory>()))
                .Do(callInfo =>
                {
                    var dto = callInfo.Arg<RequestToProcessDto>();
                    var token = callInfo.Arg<CancellationToken>();

                    var current = Interlocked.Increment(ref activeConcurrentCount);
                    int initialMax;
                    do
                    {
                        initialMax = maxObservedConcurrent;
                        if (current <= initialMax) break;
                    } while (Interlocked.CompareExchange(ref maxObservedConcurrent, current, initialMax) != initialMax);

                    try
                    {
                        if (dto.Request.Id == req1.Id)
                        {
                            var requestsSource = provider.GetRequiredService<IRequestsPersistentSource>();
                            requestsSource.UpdateRequestStatus(dto.Request.Id, DeploymentRequestStatus.Running);
                            req1Started.TrySetResult(true);
                            try
                            {
                                releaseReq1.Task.WaitAsync(token).GetAwaiter().GetResult();
                            }
                            catch (OperationCanceledException)
                            {
                                observedCancellationOnLockLoss.TrySetResult(true);
                                requestsSource.UpdateRequestStatus(dto.Request.Id, DeploymentRequestStatus.Cancelled);
                                throw;
                            }
                        }
                        else if (req2 != null && dto.Request.Id == req2.Id)
                        {
                            var requestsSource = provider.GetRequiredService<IRequestsPersistentSource>();
                            requestsSource.UpdateRequestStatus(dto.Request.Id, DeploymentRequestStatus.Completed);
                            req2Executed.TrySetResult(true);
                            engineCts.Cancel();
                        }
                    }
                    finally
                    {
                        Interlocked.Decrement(ref activeConcurrentCount);
                    }
                });

            SubstituteTransientWith(pendingMock);

            var configMock = Substitute.For<IMonitorConfiguration>();
            configMock.IsProduction.Returns(false);
            configMock.RequestProcessingIterationDelayMs.Returns(20);
            configMock.MaxConcurrentDeployments.Returns(1);
            SubstituteTransientWith(configMock);

            var engine = provider.GetRequiredService<IDeploymentEngine>();
            var cancellationSources = new ConcurrentDictionary<int, CancellationTokenSource>();

            var engineTask = engine.ProcessDeploymentRequestsAsync(false, cancellationSources, engineCts.Token, 20);
            await req1Started.Task.WaitAsync(TimeSpan.FromSeconds(5));

            // Enqueue req2 on a different environment while req1 is in-flight at MaxConcurrentDeployments=1.
            req2 = Dorc.Monitor.Tests.Data.DeploymentRequestData.GetDeploymentRequest(env2, "user2", "proj2");
            AddDeploymentRequests(new[] { req2 });

            // Give the engine several poll intervals; req2 must NOT start while req1 holds the single concurrency slot.
            var startedWhileCapped = await Task.WhenAny(req2Executed.Task, Task.Delay(150)) == req2Executed.Task;
            Assert.IsFalse(startedWhileCapped, "Second environment request must not start while at MaxConcurrentDeployments=1.");

            // Trigger distributed lock loss on req1; req1 cancels, freeing the slot so req2 runs next.
            lockLostCts.Cancel();

            Assert.IsTrue(await observedCancellationOnLockLoss.Task.WaitAsync(TimeSpan.FromSeconds(5)),
                "Losing the distributed environment lock must immediately cancel the in-flight deployment token.");
            Assert.IsTrue(await req2Executed.Task.WaitAsync(TimeSpan.FromSeconds(5)),
                "Second request should execute once the in-flight deployment terminates.");
            await engineTask.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.AreEqual(1, maxObservedConcurrent, "MaxConcurrentDeployments=1 must cap concurrent executions at 1.");
        }
        finally
        {
            DeleteAllAddedDeploymentRequests();
        }
    }

    private void SubstituteScopedWith<T>(T instance) where T : class
    {
        collection.Replace(ServiceDescriptor.Scoped<T>(_ => instance));
        provider = collection.BuildServiceProvider();
    }

    private IScriptDispatcher CreateInProcessRunnerScriptDispatcher(IScriptGroupPipeServer pipeServer, bool useNamedPipeClient)
    {
        return new PipeBackedScriptDispatcher(
            pipeServer,
            provider.GetRequiredService<IRequestsPersistentSource>(),
            provider.GetRequiredService<ILoggerFactory>(),
            useNamedPipeClient);
    }

    private (int RequestId, string ProjectName, string ChildEnvName, string ComponentName) SeedEndToEndDeploymentScenario(
        string ext,
        string scriptDir,
        string scriptFileName,
        string outputMarkerFile,
        bool isProd,
        bool nonProdOnly,
        bool stopOnFailure,
        string psVersion)
    {
        var contextFactory = provider.GetRequiredService<IDeploymentContextFactory>();
        var propertiesSource = provider.GetRequiredService<IPropertiesPersistentSource>();
        var propertyValuesSource = provider.GetRequiredService<IPropertyValuesPersistentSource>();

        var projectName = $"Proj-{ext}";
        var parentEnvName = $"ParentEnv-{ext}";
        var childEnvName = $"ChildEnv-{ext}";
        var componentName = $"Comp-{ext}";

        int requestId;

        using (var ctx = (DeploymentContext)contextFactory.GetContext())
        {
            var project = new Project
            {
                Name = projectName,
                ObjectId = Guid.NewGuid(),
                ArtefactsUrl = scriptDir,
                ArtefactsSubPaths = "."
            };
            var parentEnv = new Environment
            {
                Name = parentEnvName,
                ObjectId = Guid.NewGuid(),
                Secure = false,
                IsProd = isProd
            };
            var childEnv = new Environment
            {
                Name = childEnvName,
                ObjectId = Guid.NewGuid(),
                Secure = false,
                IsProd = isProd,
                ParentId = null
            };
            ctx.Projects.Add(project);
            ctx.Environments.Add(parentEnv);
            ctx.SaveChanges();

            childEnv.ParentEnvironment = parentEnv;
            childEnv.ParentId = parentEnv.Id;
            ctx.Environments.Add(childEnv);
            project.Environments.Add(parentEnv);
            project.Environments.Add(childEnv);

            var script = new Script
            {
                Name = componentName,
                Path = Path.Combine(scriptDir, scriptFileName),
                NonProdOnly = nonProdOnly,
                IsPathJSON = false,
                PowerShellVersionNumber = psVersion
            };
            var component = new Component
            {
                Name = componentName,
                ObjectId = Guid.NewGuid(),
                ComponentType = ComponentType.PowerShell,
                IsEnabled = true,
                StopOnFailure = stopOnFailure,
                Script = script
            };
            component.Projects.Add(project);
            ctx.Components.Add(component);
            ctx.SaveChanges();
        }

        propertiesSource.CreateProperty(new PropertyApiModel { Name = "Region", Secure = false, IsArray = false }, "test");
        propertyValuesSource.AddPropertyValue(new PropertyValueDto
        {
            Value = "global-region",
            Property = new PropertyApiModel { Name = "Region", Secure = false }
        });
        propertyValuesSource.AddPropertyValue(new PropertyValueDto
        {
            Value = "eu-west-1",
            PropertyValueFilter = parentEnvName,
            Property = new PropertyApiModel { Name = "Region", Secure = false }
        });
        propertyValuesSource.AddPropertyValue(new PropertyValueDto
        {
            Value = "eu-west-2",
            PropertyValueFilter = childEnvName,
            Property = new PropertyApiModel { Name = "Region", Secure = false }
        });

        propertiesSource.CreateProperty(new PropertyApiModel { Name = "EndpointUrl", Secure = false, IsArray = false }, "test");
        propertyValuesSource.AddPropertyValue(new PropertyValueDto
        {
            Value = "https://$Region$.$EnvironmentName$.sefe.internal",
            Property = new PropertyApiModel { Name = "EndpointUrl", Secure = false }
        });

        propertiesSource.CreateProperty(new PropertyApiModel { Name = "ApiSecretKey", Secure = true, IsArray = false }, "test");
        propertyValuesSource.AddPropertyValue(new PropertyValueDto
        {
            Value = $"TopSecretValue-{ext}",
            PropertyValueFilter = childEnvName,
            Property = new PropertyApiModel { Name = "ApiSecretKey", Secure = true }
        });

        propertiesSource.CreateProperty(new PropertyApiModel { Name = "TagList", Secure = false, IsArray = true }, "test");
        propertyValuesSource.AddPropertyValue(new PropertyValueDto
        {
            Value = "[\"alpha\",\"beta\"]",
            PropertyValueFilter = childEnvName,
            Property = new PropertyApiModel { Name = "TagList", Secure = false, IsArray = true }
        });

        propertiesSource.CreateProperty(new PropertyApiModel { Name = "OutputMarkerPath", Secure = false, IsArray = false }, "test");
        propertyValuesSource.AddPropertyValue(new PropertyValueDto
        {
            Value = outputMarkerFile,
            PropertyValueFilter = childEnvName,
            Property = new PropertyApiModel { Name = "OutputMarkerPath", Secure = false }
        });

        requestId = CreatePinnedDeploymentRequest(projectName, childEnvName, componentName, scriptDir);
        return (requestId, projectName, childEnvName, componentName);
    }

    private int CreatePinnedDeploymentRequest(string projectName, string envName, string componentName, string scriptDir)
    {
        var requestsSource = provider.GetRequiredService<IRequestsPersistentSource>();
        var serializer = new DeploymentRequestDetailSerializer();
        var detail = new DeploymentRequestDetail
        {
            EnvironmentName = envName,
            BuildDetail = new BuildDetail
            {
                Project = projectName,
                BuildNumber = "1.0.0",
                DropLocation = $"file:///{scriptDir.Replace('\\', '/')}"
            },
            Components = new List<string> { componentName }
        };

        return requestsSource.SubmitRequest(new DeploymentRequest
        {
            RequestDetails = serializer.Serialize(detail),
            UserName = "test-user",
            RequestedTime = DateTimeOffset.UtcNow,
            Project = projectName,
            Environment = envName,
            BuildNumber = "1.0.0",
            Components = componentName,
            Status = DeploymentRequestStatus.Pending.ToString(),
            IsProd = false
        });
    }
}
