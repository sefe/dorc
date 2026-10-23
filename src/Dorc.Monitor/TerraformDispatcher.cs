using Dorc.ApiModel;
using Dorc.ApiModel.MonitorRunnerApi;
using Dorc.Core;
using Dorc.Core.AzureStorageAccount;
using Dorc.Core.BuildServer;
using Dorc.Core.Configuration;
using Dorc.Monitor.Pipes;
using Dorc.Monitor.RunnerProcess;
using Dorc.Monitor.RunnerProcess.Interop.Windows.Kernel32;
using Dorc.Monitor.Terraform;
using Dorc.Core.Terraform;
using Dorc.Monitor.Security;
using Dorc.Monitor.TerraformSourceConfig;
using Dorc.PersistentData.Sources.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Text;
using Dorc.Kafka.Events.Publisher;

namespace Dorc.Monitor
{
    public class TerraformDispatcher : ITerraformDispatcher
    {
        private const string ProdDeployUsernamePropertyName = "DORC_ProdDeployUsername";
        private const string ProdDeployPasswordPropertyName = "DORC_ProdDeployPassword";
        private const string NonProdDeployUsernamePropertyName = "DORC_NonProdDeployUsername";
        private const string NonProdDeployPasswordPropertyName = "DORC_NonProdDeployPassword";

        private readonly ILogger logger;
        private readonly IRequestsPersistentSource _requestsPersistentSource;
        private readonly IConfigValuesPersistentSource _configValuesPersistentSource;
        private readonly IPropertiesPersistentSource _propertiesPersistentSource;
        private readonly IConfigurationSettings _configurationSettingsEngine;
        private readonly IDeploymentRequestProcessesPersistentSource _processesPersistentSource;
        private readonly IScriptGroupPipeServer _scriptGroupPipeServer;
        private readonly IAzureStorageAccountWorker _azureStorageAccountWorker;
        private readonly IProjectsPersistentSource _projectsPersistentSource;
        private readonly TerraformSourceConfigurator _sourceConfigurator;
        private readonly TerraformCloudResourceRegistrar _cloudResourceRegistrar;
        private readonly IEnvironmentsPersistentSource _environmentsPersistentSource;

        private bool isScriptExecutionSuccessful; // This field is needed to be instance-wide since Runner process errors are processed as instance-wide events.

        public TerraformDispatcher(
            ILogger<TerraformDispatcher> logger,
            IRequestsPersistentSource requestsPersistentSource,
            IConfigValuesPersistentSource configValuesPersistentSource,
            IConfigurationSettings configurationSettingsEngine,
            IDeploymentRequestProcessesPersistentSource processesPersistentSource,
            IScriptGroupPipeServer scriptGroupPipeServer,
            IAzureStorageAccountWorker azureStorageAccountWorker,
            IProjectsPersistentSource projectsPersistentSource,
            IGitHubHostValidator gitHubHostValidator,
            ICloudResourcesPersistentSource cloudResourcesPersistentSource,
            ICloudResourceAuditPersistentSource cloudResourceAuditPersistentSource,
            IEnvironmentsPersistentSource environmentsPersistentSource,
            IDatabasesPersistentSource databasesPersistentSource,
            IPropertiesPersistentSource propertiesPersistentSource)
        {
            this.logger = logger;
            this._requestsPersistentSource = requestsPersistentSource;
            this._configValuesPersistentSource = configValuesPersistentSource;
            _propertiesPersistentSource = propertiesPersistentSource;
            this._configurationSettingsEngine = configurationSettingsEngine;
            this._processesPersistentSource = processesPersistentSource;
            this._scriptGroupPipeServer = scriptGroupPipeServer;
            this._azureStorageAccountWorker = azureStorageAccountWorker;
            this._projectsPersistentSource = projectsPersistentSource;
            _environmentsPersistentSource = environmentsPersistentSource;
            this._sourceConfigurator = new TerraformSourceConfigurator(logger, _configurationSettingsEngine, gitHubHostValidator);
            this._cloudResourceRegistrar = new TerraformCloudResourceRegistrar(
                logger,
                cloudResourcesPersistentSource,
                cloudResourceAuditPersistentSource,
                environmentsPersistentSource,
                databasesPersistentSource);
        }

        public bool Dispatch(
            ComponentApiModel component,
            DeploymentResultApiModel deploymentResult,
            IDictionary<string, VariableValue> properties,
            int requestId,
            bool isProduction,
            string environmentName,
            StringBuilder resultLogBuilder,
            TerraformRunnerOperations terreformOperation,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            logger.LogInformation("Dispatching Terraform component {Component} ({ComponentId}), result {ResultId}, environment {Environment}.",
                component.ComponentName, component.ComponentId, deploymentResult.Id, environmentName);

            // The request is loaded before the guard because the project name
            // is part of the state identity: environments are shared across
            // projects and component names are not globally unique, so
            // (project, environment, component) is the smallest collision-free
            // key - see docs/Terraform/STATE-MODEL.md.
            var request = _requestsPersistentSource.GetRequest(requestId);
            var projectName = request?.Project ?? string.Empty;
            var catalogInputs = component.ComponentId.HasValue
                ? _requestsPersistentSource.GetCatalogRequestInputs(requestId, component.ComponentId.Value) : null;
            if (catalogInputs != null)
            {
                properties = TerraformCatalogRequestSnapshot.Apply(component, properties, request.RequestDetails, catalogInputs);
            }

            // serialise plan/apply per (project, environment, component)
            // within this monitor instance. Acquired once around the whole
            // Dispatch body; released on scope exit. Cross-monitor concurrency
            // relies on the azurerm backend's blob-lease state lock under the
            // hood.
            using var concurrencyGuard = TryAcquireConcurrencyGuard(
                projectName, environmentName, component.ComponentName, deploymentResult.Id, terreformOperation, cancellationToken);
            if (concurrencyGuard is null)
            {
                isScriptExecutionSuccessful = false;
                return isScriptExecutionSuccessful;
            }

            // Update status to Running
            _requestsPersistentSource.UpdateResultStatus(
                deploymentResult,
                DeploymentResultStatus.Running);

            logger.LogInformation("Updated deployment result {ResultId} status to Running.", deploymentResult.Id);

            isScriptExecutionSuccessful = true;

            var processCredentials = GetProcessCredentials(isProduction, environmentName);
            string processAccountName = processCredentials.Item1;
            string processAccountPassword = processCredentials.Item2;
            if (string.IsNullOrEmpty(processAccountName)
                || string.IsNullOrEmpty(processAccountPassword))
            {
                logger.LogError("Unable to find valid DOrc credentials for environment {Environment}.", environmentName);

                isScriptExecutionSuccessful = false;
                return isScriptExecutionSuccessful;
            }

            // Project information for Terraform source configuration (the
            // request itself was loaded before the concurrency guard).
            var project = _requestsPersistentSource.GetSubmittedProject(requestId, component.ComponentId!.Value);

            var scriptGroup = GetScriptGroup(
                component.ScriptPath,
                properties,
                deploymentResult.Id,
                component,
                request,
                project);
            scriptGroup.DefinitionRequestId = requestId;
            scriptGroup.DefinitionRevision = _requestsPersistentSource.GetRequestDefinitions(requestId)
                .Single(d => d.ComponentId == component.ComponentId).DefinitionRevision;
            scriptGroup.SourceBuildIdentity = request.BuildNumber;

            ConfigurePlatformState(scriptGroup, project, component.ComponentName, environmentName);

            var domainName = _configurationSettingsEngine.GetConfigurationDomainNameIntra();
            var contextBuilder = new ProcessSecurityContextBuilder(logger)
            {
                UserName = processAccountName,
                Domain = domainName,
                Password = processAccountPassword
            };

            using (var pipeCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            using (var securityContext = contextBuilder.Build())
            {
                var startedScriptGroupPipeName = $"DOrcMonitor-{HostInstanceId.Value}-{requestId}";
                // The script group is made available to the account the Runner will be started
                // as, taken from the same credential resolution that built the process security
                // context rather than assumed to be the Monitor's own identity.
                var readerIdentity = new ScriptGroupReaderIdentity(domainName, processAccountName);
                var stagedFiles = new List<string>();

                try
                {
                    Task scriptGroupPipeTask = _scriptGroupPipeServer.Start(
                            startedScriptGroupPipeName,
                            scriptGroup,
                            readerIdentity,
                            pipeCancellationTokenSource.Token);
                    logger.LogInformation("Server named pipe {PipeName} has started.", startedScriptGroupPipeName);

                    var runnerLogPathSetting = new ConfigurationBuilder().AddJsonFile("appsettings.json").Build()
                    .GetSection("AppSettings")["RunnerLogPath"]!;
                    var runnerLogPath = runnerLogPathSetting + $"\\{startedScriptGroupPipeName}.txt";
                    var uncLogPath = runnerLogPath.Replace("c:", @"\\" + System.Environment.GetEnvironmentVariable("COMPUTERNAME"));

                    _requestsPersistentSource.UpdateUncLogPath(requestId, uncLogPath);

                    // The plan and its human-readable rendering carry the variable values the
                    // configuration was rendered with. They are staged in a directory that
                    // admits the Monitor, SYSTEM, Administrators and the account the Runner
                    // will write them as - and nobody else on the host.
                    var planStorageDir = TerraformPlanStorage.EnsureRestricted(
                        DorcProgramData.Root,
                        readerIdentity,
                        logger);
                    var terraformPlanFileName = deploymentResult.Id.CreateTerraformPlanBlobName();
                    var terraformPlanFilePath = Path.Join(planStorageDir, terraformPlanFileName);
                    var planSourceArchiveFilePath = terraformPlanFilePath + ".source.zip";
                    var terraformPlanContentFileName = deploymentResult.Id.CreateTerraformPlanContentBlobName();
                    var terraformPlanContentFilePath = Path.Join(planStorageDir, terraformPlanContentFileName);
                    // persist .terraform.lock.hcl across plan+apply so
                    // both phases resolve identical provider versions. It is
                    // staged in the same restricted directory as the plan.
                    var terraformLockFileName = $"{deploymentResult.Id}.terraform.lock.hcl";
                    var terraformLockFilePath = Path.Join(planStorageDir, terraformLockFileName);
                    // Written by the runner after a successful apply; read back
                    // below to register the applied resources on the environment.
                    var appliedResourcesFilePath = terreformOperation == TerraformRunnerOperations.ApplyPlan
                        ? Path.Join(planStorageDir, $"{deploymentResult.Id}.applied-resources.json")
                        : string.Empty;
                    // Written by the runner after a successful apply: the exact
                    // terraform configuration deployed (without tfvars) plus a
                    // provenance record (module ref + resolved commit SHA).
                    // Uploaded below to the same blob container as the plan.
                    var sourceArchiveFileName = $"{deploymentResult.Id}.terraform-source.zip";
                    var sourceArchiveFilePath = terreformOperation == TerraformRunnerOperations.ApplyPlan
                        ? Path.Join(planStorageDir, sourceArchiveFileName)
                        : string.Empty;

                    stagedFiles.AddRange(new[]
                    {
                        terraformPlanFilePath, terraformPlanContentFilePath, terraformLockFilePath,
                        planSourceArchiveFilePath, appliedResourcesFilePath, sourceArchiveFilePath,
                        terraformPlanFilePath + ".cancel"
                    });
                    // Never let an earlier failed dispatch supply stale artifacts.
                    foreach (var file in stagedFiles) TryDeleteFile(file);

                    Dorc.PersistentData.Model.TerraformDeploymentOutcome? postApplyOutcome = null;
                    if (terreformOperation == TerraformRunnerOperations.ApplyPlan)
                    {
                        _azureStorageAccountWorker.DownloadFileFromBlobs(terraformPlanFileName, terraformPlanFilePath);
                        // Required: an approved plan may never be paired with a
                        // fresh checkout of a mutable ref. Older plans must be recreated.
                        TerraformPlanSourceArtifacts.Download(_azureStorageAccountWorker, terraformPlanFilePath);
                        TerraformPlanArtifactIntegrity.Verify(
                            _requestsPersistentSource.GetTerraformPlanApproval(deploymentResult.Id),
                            requestId, deploymentResult.Id, terraformPlanFilePath, planSourceArchiveFilePath);
                        postApplyOutcome = new Dorc.PersistentData.Model.TerraformDeploymentOutcome
                        {
                            DeploymentResultId = deploymentResult.Id
                        };
                        _requestsPersistentSource.SaveTerraformDeploymentOutcome(postApplyOutcome);
                        // The source snapshot includes the authoritative provider
                        // lock; never replace it with a separately downloaded file.
                    }

                    var processStarter = new TerraformRunnerProcessStarter(logger)
                    {
                        RunnerExecutableFullName = new ConfigurationBuilder().AddJsonFile("appsettings.json").Build().GetSection("AppSettings")["TerraformDeploymentRunnerPath"],
                        ScriptGroupPipeName = startedScriptGroupPipeName,
                        RunnerLogPath = runnerLogPath,
                        PlanFilePath = terraformPlanFilePath,
                        PlanContentFilePath = terraformPlanContentFilePath,
                        LockFilePath = terraformLockFilePath,
                        AppliedResourcesFilePath = appliedResourcesFilePath,
                        SourceArchiveFilePath = sourceArchiveFilePath,
                        TerraformRunnerOperation = terreformOperation
                    };
                    try
                    {
                        Interop.Kernel32.STARTUPINFO startupInfo = new ProcessStartupInfoBuilder(logger).Build();
                        logger.LogInformation("Starting Runner process.");

                        cancellationToken.ThrowIfCancellationRequested();

                        var process = processStarter.Start(startupInfo, securityContext);
                        try
                        {
                            _processesPersistentSource.AssociateProcessWithRequest((int)process.Id, requestId);

                            logger.LogDebug("Waiting for process to exit.");
                            var resultCode = process.WaitForExit(cancellationToken,
                                () => File.WriteAllText(terraformPlanFilePath + ".cancel", string.Empty));
                            logger.LogInformation("Runner finished for request {RequestId} with result code {ResultCode}.", requestId, resultCode);

                            if (postApplyOutcome != null)
                            {
                                postApplyOutcome.ApplyOutcome = resultCode == 0 ? "Succeeded" : "FailedOrUnknown";
                                postApplyOutcome.AppliedAt = resultCode == 0 ? DateTimeOffset.UtcNow : null;
                                _requestsPersistentSource.SaveTerraformDeploymentOutcome(postApplyOutcome);
                            }
                            cancellationToken.ThrowIfCancellationRequested();

                            if (resultCode == RunnerProcess.RunnerProcess.ProcessTerminatedExitCode)
                            {
                                logger.LogInformation("The Runner process is terminated.");
                                throw new OperationCanceledException("The Runner process is terminated.");
                            }
                            else if (resultCode != 0)
                            {
                                isScriptExecutionSuccessful = false;
                                logger.LogError("Terraform Runner failed with exit code {ExitCode}.", resultCode);
                            }
                        }
                        finally
                        {
                            pipeCancellationTokenSource.Cancel();
                            try { _processesPersistentSource.RemoveProcess((int)process.Id); }
                            finally { process.Dispose(); }
                        }
                    }
                    catch (Exception e)
                    {
                        pipeCancellationTokenSource.Cancel();
                        logger.LogError(e, "Failure while operating the Runner process for request {RequestId}.", requestId);
                        throw;
                    }
                
                    cancellationToken.ThrowIfCancellationRequested();
                    if (isScriptExecutionSuccessful)
                    switch (terreformOperation)
                    {
                        case TerraformRunnerOperations.CreatePlan:
                            // save Terraform binary plan file to Azure Storage Account
                            _azureStorageAccountWorker.SaveFileToBlobs(terraformPlanFilePath);
                            // save Terraform human-readable plan file to Azure Storage Account
                            _azureStorageAccountWorker.SaveFileToBlobs(terraformPlanContentFilePath);
                            TerraformPlanSourceArtifacts.Upload(_azureStorageAccountWorker, terraformPlanFilePath);
                            var receipt = new Dorc.PersistentData.Model.TerraformDeploymentReceipt
                            {
                                DeploymentResultId = deploymentResult.Id,
                                DeploymentRequestId = requestId,
                                ProjectId = project.ProjectId,
                                DefinitionRevision = _requestsPersistentSource.GetRequestDefinitions(requestId)
                                    .Single(d => d.ComponentId == component.ComponentId).DefinitionRevision,
                                SourceType = component.TerraformSourceType.ToString(),
                                RequestedSource = component.TerraformSourceType == TerraformSourceType.Git ? project.TerraformGitRepoUrl :
                                    component.TerraformSourceType == TerraformSourceType.SharedFolder ? component.ScriptPath :
                                    component.TerraformSourceType == TerraformSourceType.Catalog ? component.TerraformTemplateName : project.ArtefactsUrl,
                                RequestedRef = component.TerraformGitBranch,
                                SourceSubPath = component.TerraformSubPath,
                                BuildIdentity = request.BuildNumber,
                                ComponentId = component.ComponentId ?? throw new InvalidOperationException("Terraform component identity is missing."),
                                EnvironmentId = _environmentsPersistentSource.GetEnvironment(environmentName)?.EnvironmentId,
                                ProjectName = projectName,
                                EnvironmentName = environmentName,
                                ComponentName = component.ComponentName
                            };
                            TerraformReceiptCapture.ReadPlannedSource(receipt,
                                planSourceArchiveFilePath, terraformPlanContentFilePath, terraformPlanFilePath);
                            _requestsPersistentSource.SaveTerraformDeploymentReceipt(receipt);
                            // save .terraform.lock.hcl alongside the plan so
                            // the apply phase resolves identical provider versions.
                            // Lock file is only present when terraform init wrote
                            // it (i.e. for projects with required_providers). The
                            // runner skips persistence cleanly when missing.
                            if (File.Exists(terraformLockFilePath))
                            {
                                _azureStorageAccountWorker.SaveFileToBlobs(terraformLockFilePath);
                            }

                            // Update status to WaitingConfirmation
                            cancellationToken.ThrowIfCancellationRequested();
                            _requestsPersistentSource.UpdateResultStatus(
                                deploymentResult,
                                DeploymentResultStatus.WaitingConfirmation);

                            logger.LogInformation("Terraform plan created for component {Component}. Waiting for confirmation.", component.ComponentName);
                            break;

                        case TerraformRunnerOperations.ApplyPlan:
                            // Surface what the apply created on the environment's
                            // Cloud tab. Best-effort: the infrastructure change
                            // has already happened, so registration problems are
                            // logged, never failed.
                            var registration = _cloudResourceRegistrar.RegisterAppliedResources(
                                appliedResourcesFilePath,
                                environmentName,
                                request?.UserName ?? string.Empty,
                                deploymentResult.Id);
                            postApplyOutcome!.InventoryOutcome = registration.Status;
                            postApplyOutcome.RegisteredCount = registration.RegisteredCount;
                            postApplyOutcome.FailedCount = registration.FailedCount;
                            postApplyOutcome.InventoryWarningsJson = System.Text.Json.JsonSerializer.Serialize(registration.Warnings);
                            try
                            {
                                if (File.Exists(appliedResourcesFilePath))
                                {
                                    _azureStorageAccountWorker.SaveFileToBlobs(appliedResourcesFilePath);
                                    using var inventoryStream = File.OpenRead(appliedResourcesFilePath);
                                    postApplyOutcome.InventoryArtifactSha256 = Convert.ToHexString(
                                        System.Security.Cryptography.SHA256.HashData(inventoryStream));
                                }
                                var appliedReceipt = _requestsPersistentSource.GetTerraformDeploymentReceipt(deploymentResult.Id);
                                if (appliedReceipt != null && registration.EnvironmentId > 0)
                                    _requestsPersistentSource.RecordTerraformInventory(appliedReceipt.ComponentId, registration.EnvironmentId,
                                        deploymentResult.Id, postApplyOutcome.AppliedAt!.Value, registration.Observed,
                                        registration.Status == "Succeeded");
                            }
                            catch (Exception ex)
                            {
                                postApplyOutcome.InventoryOutcome = "RecoveryOrProvenanceFailed";
                                logger.LogWarning(ex, "Terraform succeeded but inventory recovery material or provenance could not be retained.");
                            }
                            TryDeleteFile(appliedResourcesFilePath);

                            // Persist the deployed-source archive next to the
                            // plan artefacts. Best-effort like the resource
                            // registration: the runner only writes the archive
                            // when archival itself succeeded, and a missing
                            // file must not fail a completed deployment.
                            if (File.Exists(sourceArchiveFilePath))
                            {
                                try
                                {
                                    _azureStorageAccountWorker.SaveFileToBlobs(sourceArchiveFilePath);
                                    postApplyOutcome.ArchiveOutcome = "Succeeded";
                                    logger.LogInformation("Deployed Terraform source archived to blob {ArtifactName}.", sourceArchiveFileName);
                                }
                                catch (Exception ex)
                                {
                                    postApplyOutcome.ArchiveOutcome = "Failed";
                                    logger.LogWarning(ex, "Could not upload deployed Terraform source archive {ArtifactName}; the deployment itself succeeded.", sourceArchiveFileName);
                                }
                            }
                            else postApplyOutcome.ArchiveOutcome = "Unavailable";
                            TryDeleteFile(sourceArchiveFilePath);
                            _requestsPersistentSource.SaveTerraformDeploymentOutcome(postApplyOutcome);

                            cancellationToken.ThrowIfCancellationRequested();
                            _requestsPersistentSource.UpdateResultStatus(
                                deploymentResult,
                                DeploymentResultStatus.Complete);

                            logger.LogInformation("Terraform plan applied for component {Component}. Completed.", component.ComponentName);
                            break;
                    }
                }
                finally
                {
                    // The bundle carries the resolved deployment properties, secrets
                    // included. It is needed for the span of one Runner process, so it is
                    // withdrawn as soon as that process is gone - on the failure and
                    // cancellation paths as much as the successful one.
                    _scriptGroupPipeServer.Expire(startedScriptGroupPipeName);
                    foreach (var file in stagedFiles) TryDeleteFile(file);
                }

            }

            return isScriptExecutionSuccessful;
        }

        private void TryDeleteFile(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            try
            {
                if (File.Exists(path)) File.Delete(path);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Could not delete staged Terraform file {ArtifactPath}.", path);
            }
        }

        private (string, string) GetProcessCredentials(bool isProduction, string environmentName)
        {
            if (isProduction)
            {
                return (GetConfigValue(ProdDeployUsernamePropertyName),
                    GetConfigValue(ProdDeployPasswordPropertyName));
            }

            return (GetConfigValue(NonProdDeployUsernamePropertyName),
                GetConfigValue(NonProdDeployPasswordPropertyName));
        }

        private string GetConfigValue(string configValue)
        {
            if (string.IsNullOrEmpty(configValue))
                throw new ApplicationException($"Config value name is empty, should have a value");
            return _configValuesPersistentSource.GetConfigValue(configValue);
        }

        private ScriptGroup GetScriptGroup(
            string scriptsLocation,
            IDictionary<string, VariableValue> properties,
            int deploymentResultId,
            ComponentApiModel component,
            DeploymentRequestApiModel request,
            ProjectApiModel? project)
        {
            var scriptGroup = new ScriptGroup()
            {
                ID = Guid.NewGuid(),
                DeployResultId = deploymentResultId,
                ScriptsLocation = scriptsLocation,
                CommonProperties = properties,
                ScriptProperties = new List<ScriptProperties>(),
                // Exact names of sensitive wizard parameters, so runner-side
                // log redaction covers flagged properties whose names don't
                // match the heuristic pattern.
                SensitivePropertyNames = RequestPropertyRedaction
                    .GetSensitivePropertyNames(request?.RequestDetails ?? string.Empty)
                    .ToList()
            };

            // Use the configurator to set Terraform-specific fields based on source type
            _sourceConfigurator.ConfigureScriptGroup(scriptGroup, component, request, project, properties);
            scriptGroup.SensitivePropertyNames = scriptGroup.SensitivePropertyNames
                .Concat(_propertiesPersistentSource.GetAllProperties().Where(p => p.Secure).Select(p => p.Name))
                .Concat(_configValuesPersistentSource.GetAllConfigValues(false).Where(c => c.Secure).Select(c => c.Key))
                .Distinct(StringComparer.Ordinal).ToList();

            return scriptGroup;
        }

        internal static void ConfigurePlatformState(ScriptGroup scriptGroup, ProjectApiModel submittedProject,
            string componentName, string environmentName)
        {
            if (scriptGroup.TerraformSourceType != TerraformSourceType.Catalog) return;
            var backend = TerraformStateBackends.RequireSubmitted(submittedProject.TerraformStateBackends);
            scriptGroup.TerraformStateStorageAccount = backend.StorageAccount;
            scriptGroup.TerraformStateContainerName = backend.ContainerName;
            scriptGroup.TerraformStateResourceGroup = backend.ResourceGroup;
            scriptGroup.TerraformStateSubscriptionId = backend.SubscriptionId;
            scriptGroup.TerraformStateKey =
                TerraformStateKeySanitizer.Sanitize(submittedProject.ProjectName) + "/" +
                TerraformStateKeySanitizer.Sanitize(componentName) + "/" +
                TerraformStateKeySanitizer.Sanitize(environmentName) + ".tfstate";
        }

        private IDisposable? TryAcquireConcurrencyGuard(
            string projectName,
            string environmentName,
            string componentName,
            int deploymentResultId,
            TerraformRunnerOperations operation,
            CancellationToken cancellationToken)
        {
            try
            {
                // Use a short timeout: by the time this dispatcher is called the
                // upstream queue has already done its own gating; a wait here
                // longer than a couple of minutes signals a genuinely stuck
                // sibling operation and we should refuse rather than queue.
                return TerraformConcurrencyGuard.Instance.Acquire(
                    projectName,
                    environmentName,
                    componentName,
                    operationCorrelationId: $"{operation}:{deploymentResultId}",
                    cancellationToken: cancellationToken);
            }
            catch (TerraformConcurrentOperationException ex)
            {
                logger.LogError(ex,
                    "Refused Terraform {Operation} for {Component} in {Project}/{Environment}: operation {ContendingOperation} is in flight.",
                    operation, componentName, projectName, environmentName, ex.ContendingOperationId);
                return null;
            }
        }

        private class TerraformPlanInfo
        {
            public string WorkingDirectory { get; set; } = "";
            public string PlanFileName { get; set; } = "";
            public string PlanFilePath { get; set; } = "";
        }
    }
}