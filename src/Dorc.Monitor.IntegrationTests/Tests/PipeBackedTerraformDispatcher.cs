using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Dorc.ApiModel;
using Dorc.ApiModel.MonitorRunnerApi;
using Dorc.Core.AzureStorageAccount;
using Dorc.Core.Terraform;
using Dorc.Monitor.Pipes;
using Dorc.Monitor.RunnerProcess;
using Dorc.Monitor.Terraform;
using Dorc.Monitor.TerraformSourceConfig;
using Dorc.PersistentData.Model;
using Dorc.PersistentData.Sources.Interfaces;
using Dorc.Runner.Logger;
using Dorc.Terraform.Catalog;
using Dorc.TerraformRunner;
using Dorc.TerraformRunner.CodeSources;
using Dorc.TerraformRunner.Pipes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Dorc.Monitor.IntegrationTests.Tests;

[SupportedOSPlatform("windows")]
internal sealed class DirectoryBackedAzureStorageWorker(string rootDir) : IAzureStorageAccountWorker
{
    private string ResolveBlobPath(string nameOrPath) =>
        Path.Join(rootDir, Path.GetFileName(nameOrPath).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

    public void SaveFileToBlobs(string fileName)
    {
        var dest = ResolveBlobPath(fileName);
        File.Copy(fileName, dest, overwrite: true);
    }

    public string LoadFileFromBlobs(string blobName) =>
        File.ReadAllText(ResolveBlobPath(blobName));

    public void DownloadFileFromBlobs(string blobName, string filePath) =>
        File.Copy(ResolveBlobPath(blobName), filePath, overwrite: true);

    public string GetFileSha256FromBlobs(string blobName)
    {
        using var stream = File.OpenRead(ResolveBlobPath(blobName));
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    public string LoadVerifiedTextFromBlobs(string blobName, string expectedSha256, int maximumBytes) =>
        LoadFileFromBlobs(blobName);

    public void CopyVerifiedBlob(string sourceBlobName, string destinationBlobName, string expectedSha256) =>
        File.Copy(ResolveBlobPath(sourceBlobName), ResolveBlobPath(destinationBlobName), overwrite: true);
}

[SupportedOSPlatform("windows")]
internal sealed class PipeBackedTerraformDispatcher : ITerraformDispatcher
{
    private readonly IServiceProvider _provider;
    private readonly IAzureStorageAccountWorker _storageWorker;
    private readonly string _workingRootDir;
    private readonly string _sourceRepoDir;
    private readonly string _subscriptionId;
    private readonly string _storageAccountName;

    public string? LastAppliedMainTf { get; private set; }

    public PipeBackedTerraformDispatcher(
        IServiceProvider provider,
        IAzureStorageAccountWorker storageWorker,
        string workingRootDir,
        string sourceRepoDir,
        string subscriptionId,
        string storageAccountName)
    {
        _provider = provider;
        _storageWorker = storageWorker;
        _workingRootDir = workingRootDir;
        _sourceRepoDir = sourceRepoDir;
        _subscriptionId = subscriptionId;
        _storageAccountName = storageAccountName;
    }

    public bool Dispatch(
        ComponentApiModel component,
        DeploymentResultApiModel deploymentResult,
        IDictionary<string, VariableValue> properties,
        int requestId,
        bool isProduction,
        string environmentName,
        StringBuilder resultLogBuilder,
        Dorc.Monitor.RunnerProcess.TerraformRunnerOperations terreformOperation,
        CancellationToken cancellationToken)
    {
        var requestsSource = _provider.GetRequiredService<IRequestsPersistentSource>();
        var environmentsSource = _provider.GetRequiredService<IEnvironmentsPersistentSource>();
        var propertiesSource = _provider.GetRequiredService<IPropertiesPersistentSource>();
        var configSource = _provider.GetRequiredService<IConfigValuesPersistentSource>();
        var loggerFactory = _provider.GetRequiredService<ILoggerFactory>();
        var logger = loggerFactory.CreateLogger<TerraformDispatcher>();

        var request = requestsSource.GetRequest(requestId);
        var catalogInputs = component.ComponentId.HasValue
            ? requestsSource.GetCatalogRequestInputs(requestId, component.ComponentId.Value)
            : null;
        if (catalogInputs != null)
        {
            properties = TerraformCatalogRequestSnapshot.Apply(component, properties, request.RequestDetails, catalogInputs);
        }

        var project = requestsSource.GetSubmittedProject(requestId, component.ComponentId!.Value);
        var scriptGroup = new ScriptGroup
        {
            ID = Guid.NewGuid(),
            DeployResultId = deploymentResult.Id,
            ScriptsLocation = component.ScriptPath,
            CommonProperties = properties,
            ScriptProperties = new List<ScriptProperties>(),
            TerraformSourceType = TerraformSourceType.Catalog,
            TerraformTemplateName = component.TerraformTemplateName,
            TerraformTemplateVersion = component.TerraformTemplateVersion,
            TerraformGitRepoUrl = _sourceRepoDir,
            DefinitionRequestId = requestId,
            DefinitionRevision = requestsSource.GetRequestDefinitions(requestId)
                .Single(d => d.ComponentId == component.ComponentId).DefinitionRevision,
            SourceBuildIdentity = request.BuildNumber,
            SensitivePropertyNames = propertiesSource.GetAllProperties().Where(p => p.Secure).Select(p => p.Name)
                .Concat(configSource.GetAllConfigValues(false).Where(c => c.Secure).Select(c => c.Key))
                .Distinct(StringComparer.Ordinal)
                .ToList()
        };

        TerraformDispatcher.ConfigurePlatformState(scriptGroup, project, component.ComponentName, environmentName);

        var pipeName = $"DOrcTfPipe-{Guid.NewGuid():N}";
        var pipeServer = new ScriptGroupPipeServer(loggerFactory.CreateLogger<ScriptGroupPipeServer>());
        using var pipeCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var currentIdentity = System.Security.Principal.WindowsIdentity.GetCurrent().Name.Split('\\');
        var readerIdentity = new ScriptGroupReaderIdentity(
            currentIdentity.Length > 1 ? currentIdentity[0] : System.Environment.UserDomainName,
            currentIdentity.Last());
        var serverTask = pipeServer.Start(pipeName, scriptGroup, readerIdentity, pipeCts.Token);

        var stageDir = Path.Join(_workingRootDir, $"stage-{deploymentResult.Id}-{terreformOperation}");
        Directory.CreateDirectory(stageDir);
        var planFilePath = Path.Join(stageDir, deploymentResult.Id.CreateTerraformPlanBlobName());
        var planSourceArchivePath = planFilePath + ".source.zip";
        var planContentFilePath = Path.Join(stageDir, deploymentResult.Id.CreateTerraformPlanContentBlobName());
        var lockFilePath = Path.Join(stageDir, $"{deploymentResult.Id}.terraform.lock.hcl");
        var appliedResourcesPath = Path.Join(stageDir, $"{deploymentResult.Id}.applied-resources.json");
        var deployedSourceArchivePath = Path.Join(stageDir, $"{deploymentResult.Id}.terraform-source.zip");

        try
        {
            if (terreformOperation == Dorc.Monitor.RunnerProcess.TerraformRunnerOperations.ApplyPlan)
            {
                _storageWorker.DownloadFileFromBlobs(Path.GetFileName(planFilePath), planFilePath);
                TerraformPlanSourceArtifacts.Download(_storageWorker, planFilePath);
                TerraformPlanArtifactIntegrity.Verify(
                    requestsSource.GetTerraformPlanApproval(deploymentResult.Id),
                    requestId,
                    deploymentResult.Id,
                    planFilePath,
                    planSourceArchivePath);
            }

            var runnerLogger = Substitute.For<IRunnerLogger>();
            var expectedServerSid = System.Security.Principal.WindowsIdentity.GetCurrent().User!.Value;
            var pipeClient = new ScriptGroupPipeClient(loggerFactory.CreateLogger<ScriptGroupPipeClient>(), expectedServerSid);
            var catalog = Substitute.For<ITemplateCatalog>();
            catalog.GetAsync("azure-storage-account", "1.0.0", Arg.Any<CancellationToken>())
                .Returns(new TerraformTemplateManifest(
                    "azure-storage-account",
                    "1.0.0",
                    new TerraformTemplateSource("git", _sourceRepoDir, "0123456789abcdef0123456789abcdef01234567"),
                    new[]
                    {
                        new TerraformTemplateParameter("storage_account_name", TerraformParameterType.String, true, null, null, null, null, null, null),
                        new TerraformTemplateParameter("access_key", TerraformParameterType.String, false, null, null, null, null, null, null, true)
                    },
                    Array.Empty<TerraformTemplateOutput>(),
                    null,
                    Array.Empty<string>(),
                    null,
                    new Dictionary<string, string>(),
                    ">=1.9",
                    null,
                    false,
                    null));

            var processor = new SimulatedCliTerraformProcessor(
                _workingRootDir,
                _sourceRepoDir,
                _subscriptionId,
                _storageAccountName,
                runnerLogger,
                pipeClient,
                catalog);

            bool ok = terreformOperation == Dorc.Monitor.RunnerProcess.TerraformRunnerOperations.CreatePlan
                ? processor.PreparePlanAsync(pipeName, requestId, planFilePath, planContentFilePath, lockFilePath, cancellationToken).GetAwaiter().GetResult()
                : processor.ExecuteConfirmedPlanAsync(pipeName, requestId, planFilePath, lockFilePath, appliedResourcesPath, deployedSourceArchivePath, cancellationToken).GetAwaiter().GetResult();

            serverTask.GetAwaiter().GetResult();
            if (!ok)
                return false;

            if (terreformOperation == Dorc.Monitor.RunnerProcess.TerraformRunnerOperations.CreatePlan)
            {
                _storageWorker.SaveFileToBlobs(planFilePath);
                _storageWorker.SaveFileToBlobs(planContentFilePath);
                TerraformPlanSourceArtifacts.Upload(_storageWorker, planFilePath);
                var receipt = new TerraformDeploymentReceipt
                {
                    DeploymentResultId = deploymentResult.Id,
                    DeploymentRequestId = requestId,
                    ProjectId = project.ProjectId,
                    DefinitionRevision = scriptGroup.DefinitionRevision ?? "1",
                    SourceType = component.TerraformSourceType.ToString(),
                    RequestedSource = component.TerraformTemplateName,
                    ComponentId = component.ComponentId!.Value,
                    EnvironmentId = environmentsSource.GetEnvironment(environmentName)?.EnvironmentId,
                    ProjectName = request.Project,
                    EnvironmentName = environmentName,
                    ComponentName = component.ComponentName
                };
                TerraformReceiptCapture.ReadPlannedSource(receipt, planSourceArchivePath, planContentFilePath, planFilePath);
                requestsSource.SaveTerraformDeploymentReceipt(receipt);
                requestsSource.UpdateResultStatus(deploymentResult, DeploymentResultStatus.WaitingConfirmation);
            }
            else
            {
                LastAppliedMainTf = processor.AppliedMainTf;
                var registrar = new TerraformCloudResourceRegistrar(
                    logger,
                    _provider.GetRequiredService<ICloudResourcesPersistentSource>(),
                    _provider.GetRequiredService<ICloudResourceAuditPersistentSource>(),
                    environmentsSource,
                    _provider.GetRequiredService<IDatabasesPersistentSource>());
                var registration = registrar.RegisterAppliedResources(appliedResourcesPath, environmentName, request.UserName, deploymentResult.Id);
                var outcome = new TerraformDeploymentOutcome
                {
                    DeploymentResultId = deploymentResult.Id,
                    ApplyOutcome = "Succeeded",
                    AppliedAt = DateTimeOffset.UtcNow,
                    InventoryOutcome = registration.Status,
                    RegisteredCount = registration.RegisteredCount,
                    FailedCount = registration.FailedCount,
                    ArchiveOutcome = File.Exists(deployedSourceArchivePath) ? "Succeeded" : "Unavailable"
                };
                requestsSource.SaveTerraformDeploymentOutcome(outcome);
                requestsSource.UpdateResultStatus(deploymentResult, DeploymentResultStatus.Complete);
            }

            return true;
        }
        finally
        {
            pipeCts.Cancel();
            pipeServer.Expire(pipeName);
        }
    }

    private sealed class SimulatedCliTerraformProcessor : TerraformProcessor
    {
        private readonly string _workingRoot;
        private readonly string _subscriptionId;
        private readonly string _storageAccountName;

        public string? AppliedMainTf { get; private set; }

        public SimulatedCliTerraformProcessor(
            string workingRoot,
            string sourceRepoDir,
            string subscriptionId,
            string storageAccountName,
            IRunnerLogger logger,
            IScriptGroupPipeClient pipeClient,
            ITemplateCatalog catalog)
            : base(logger, pipeClient, catalog, CreateCodeSourceFactory(sourceRepoDir))
        {
            _workingRoot = workingRoot;
            _subscriptionId = subscriptionId;
            _storageAccountName = storageAccountName;
        }

        private static ITerraformCodeSourceProviderFactory CreateCodeSourceFactory(string sourceRepoDir)
        {
            var provider = Substitute.For<ITerraformCodeSourceProvider>();
            provider.ProvisionCodeAsync(Arg.Any<ScriptGroup>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(call =>
                {
                    var targetDir = call.ArgAt<string>(1);
                    foreach (var file in Directory.GetFiles(sourceRepoDir))
                    {
                        File.Copy(file, Path.Join(targetDir, Path.GetFileName(file)), overwrite: true);
                    }
                    return Task.CompletedTask;
                });
            var factory = Substitute.For<ITerraformCodeSourceProviderFactory>();
            factory.GetProvider(Arg.Any<TerraformSourceType>()).Returns(provider);
            return factory;
        }

        protected override string CreateTerraformWorkingDirectory(int requestId)
        {
            var dir = Path.Join(_workingRoot, $"exec-{requestId}-{Guid.NewGuid():N}");
            Directory.CreateDirectory(dir);
            return dir;
        }

        protected override Task<string> RunTerraformCommandAsync(
            string workingDir,
            TerraformCommand command,
            IReadOnlyList<string> args,
            CancellationToken cancellationToken)
        {
            if (command == TerraformCommand.Init)
            {
                var lockFile = Path.Join(workingDir, ".terraform.lock.hcl");
                if (!File.Exists(lockFile))
                {
                    File.WriteAllText(lockFile, "# provider lock v1");
                }
                return Task.FromResult("Terraform has been successfully initialized!");
            }

            if (command == TerraformCommand.PlanDetailedExitCode)
            {
                var outArg = args.Single(a => a.StartsWith("-out=", StringComparison.Ordinal))[5..];
                File.WriteAllText(outArg, "binary-plan-payload");
                return Task.FromResult("Plan: 1 to add, 0 to change, 0 to destroy.");
            }

            if (command == TerraformCommand.Show && !args.Contains("-json"))
            {
                return Task.FromResult($"Terraform will create azurerm_storage_account.primary with name={_storageAccountName} and key=SuperSecretAccessKey!99");
            }

            if (command == TerraformCommand.Apply)
            {
                AppliedMainTf = File.ReadAllText(Path.Join(workingDir, "main.tf"));
                return Task.FromResult("Apply complete! Resources: 1 added, 0 changed, 0 destroyed.");
            }

            if (command == TerraformCommand.Show && args.Contains("-json"))
            {
                var stateJson = JsonSerializer.Serialize(new
                {
                    values = new
                    {
                        root_module = new
                        {
                            resources = new[]
                            {
                                new
                                {
                                    address = "azurerm_storage_account.primary",
                                    mode = "managed",
                                    type = "azurerm_storage_account",
                                    name = "primary",
                                    provider_name = "registry.terraform.io/hashicorp/azurerm",
                                    values = new
                                    {
                                        id = $"/subscriptions/{_subscriptionId}/resourceGroups/rg-tfstate/providers/Microsoft.Storage/storageAccounts/{_storageAccountName}",
                                        name = _storageAccountName
                                    }
                                }
                            }
                        }
                    }
                });
                return Task.FromResult(stateJson);
            }

            return Task.FromResult(string.Empty);
        }
    }
}
