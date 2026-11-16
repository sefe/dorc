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
        private readonly ITerraformCloudResourceRegistrar _cloudResourceRegistrar;
        private readonly IEnvironmentsPersistentSource _environmentsPersistentSource;

        internal Func<ProcessSecurityContextBuilder, ProcessSecurityContextBuilder.ProcessSecurityContext?> SecurityContextFactory { get; set; } = static b => b.Build();
        internal Func<TerraformRunnerProcessStarter, ProcessSecurityContextBuilder.ProcessSecurityContext?, int, string, CancellationToken, uint> RunnerProcessRunner { get; set; }

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
            IPropertiesPersistentSource propertiesPersistentSource,
            ITerraformCloudResourceRegistrar? cloudResourceRegistrar = null)
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
            this._cloudResourceRegistrar = cloudResourceRegistrar ?? new TerraformCloudResourceRegistrar(
                logger,
                cloudResourcesPersistentSource,
                cloudResourceAuditPersistentSource,
                environmentsPersistentSource,
                databasesPersistentSource,
                requestsPersistentSource);
            RunnerProcessRunner = ExecuteRunnerProcess;
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

            request = _requestsPersistentSource.GetRequest(requestId)!;
            var targetEnvironment = _environmentsPersistentSource.GetEnvironment(environmentName);
            var isDestroyRequest = Dorc.PersistentData.Sources.RequestsPersistentSource.IsDestroyRequestXml(request.RequestDetails);
            if (!isDestroyRequest &&
                (string.Equals(request.Status, DeploymentRequestStatus.Cancelling.ToString(), StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(request.Status, DeploymentRequestStatus.Cancelled.ToString(), StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(targetEnvironment?.EphemeralState, EphemeralEnvironmentStates.TearingDown, StringComparison.OrdinalIgnoreCase)))
            {
                isScriptExecutionSuccessful = false;
                return isScriptExecutionSuccessful;
            }

            var componentId = component.ComponentId.GetValueOrDefault();
            var priorReceipt = isDestroyRequest
                ? FindLatestStateMutatingReceipt(environmentName, targetEnvironment?.EnvironmentId, projectName, component.ComponentId)
                : null;
            var catalogInputs = priorReceipt is not null
                ? _requestsPersistentSource.GetCatalogRequestInputs(priorReceipt.DeploymentRequestId, componentId) ??
                  _requestsPersistentSource.GetCatalogRequestInputs(requestId, componentId)
                : _requestsPersistentSource.GetCatalogRequestInputs(requestId, componentId);
            var provisionedRequestProps = isDestroyRequest && targetEnvironment is not null
                ? _requestsPersistentSource.GetProvisionedRequestPropertiesForEnvironment(targetEnvironment.EnvironmentId, projectName)
                : null;
            properties.Remove(ComponentProcessor.EphemeralPropertyName);
            properties = TerraformCatalogRequestSnapshot.Apply(
                component,
                properties,
                request!.RequestDetails,
                catalogInputs,
                provisionedRequestProps);

            var isEphemeral = IsEphemeralOperation(
                targetEnvironment?.IsEphemeral == true,
                isProduction || targetEnvironment?.EnvironmentIsProd == true);
            if (isEphemeral)
            {
                ComponentProcessor.ApplyEphemeralPropertyInvariants(properties);
            }
            else
            {
                properties.Remove(ComponentProcessor.EphemeralPropertyName);
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

            var domainName = _configurationSettingsEngine.GetConfigurationDomainNameIntra();
            var contextBuilder = new ProcessSecurityContextBuilder(logger)
            {
                UserName = processAccountName,
                Domain = domainName,
                Password = processAccountPassword
            };

            using (var pipeCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            using (var securityContext = SecurityContextFactory(contextBuilder))
            {
                var startedScriptGroupPipeName = $"DOrcMonitor-{HostInstanceId.Value}-{requestId}";
                // The script group is made available to the account the Runner will be started
                // as, taken from the same credential resolution that built the process security
                // context rather than assumed to be the Monitor's own identity.
                var readerIdentity = new ScriptGroupReaderIdentity(domainName, processAccountName);
                var stagedFiles = new List<string>();
                // The plan and its human-readable rendering carry the variable values the
                // configuration was rendered with. They are staged in a directory that
                // admits the Monitor, SYSTEM, Administrators and the account the Runner
                // will write them as - and nobody else on the host.
                var planStorageDir = TerraformPlanStorage.EnsureRestricted(
                    DorcProgramData.Root,
                    readerIdentity,
                    logger);

                var scriptGroup = new ScriptGroup();
                var stagedDestroySourceDir = string.Empty;

                try
                {
                    scriptGroup = GetScriptGroup(
                        component.ScriptPath,
                        properties,
                        deploymentResult.Id,
                        component,
                        request,
                        project,
                        out stagedDestroySourceDir);

                    scriptGroup.DefinitionRequestId = requestId;
                    scriptGroup.DefinitionRevision = _requestsPersistentSource.GetRequestDefinitions(requestId)
                        .Single(d => d.ComponentId == component.ComponentId).DefinitionRevision;

                    ConfigurePlatformState(
                        scriptGroup,
                        project,
                        component.ComponentName,
                        environmentName,
                        isEphemeral,
                        targetEnvironment?.EnvironmentId ?? 0,
                        _requestsPersistentSource.GetTerraformReceiptsForEnvironment(environmentName),
                        component.ComponentId);

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
                    var isApplyOperation = terreformOperation == TerraformRunnerOperations.ApplyPlan ||
                                           terreformOperation == TerraformRunnerOperations.ApplyDestroyPlan;
                    var sourceArchiveFileName = $"{deploymentResult.Id}.terraform-source.zip";
                    var sourceArchiveFilePath = isApplyOperation
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
                    if (isApplyOperation)
                    {
                        _azureStorageAccountWorker.DownloadFileFromBlobs(terraformPlanFileName, terraformPlanFilePath);
                        // Required: an approved plan may never be paired with a
                        // fresh checkout of a mutable ref. Older plans must be recreated.
                        TerraformPlanSourceArtifacts.Download(_azureStorageAccountWorker, terraformPlanFilePath);
                        TerraformPlanArtifactIntegrity.Verify(
                            _requestsPersistentSource.GetTerraformPlanApproval(deploymentResult.Id),
                            requestId, deploymentResult.Id, terraformPlanFilePath, planSourceArchiveFilePath);
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
                        cancellationToken.ThrowIfCancellationRequested();

                        if (isApplyOperation)
                        {
                            postApplyOutcome = new Dorc.PersistentData.Model.TerraformDeploymentOutcome
                            {
                                DeploymentResultId = deploymentResult.Id
                            };
                            _requestsPersistentSource.SaveTerraformDeploymentOutcome(postApplyOutcome);
                        }

                        uint resultCode;
                        try
                        {
                            resultCode = RunnerProcessRunner(
                                processStarter,
                                securityContext,
                                requestId,
                                terraformPlanFilePath,
                                cancellationToken);
                        }
                        finally
                        {
                            pipeCancellationTokenSource.Cancel();
                        }

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
                        case TerraformRunnerOperations.CreateDestroyPlan:
                            // save Terraform binary plan file to Azure Storage Account
                            _azureStorageAccountWorker.SaveFileToBlobs(terraformPlanFilePath);
                            // save Terraform human-readable plan file to Azure Storage Account
                            _azureStorageAccountWorker.SaveFileToBlobs(terraformPlanContentFilePath);
                            TerraformPlanSourceArtifacts.Upload(_azureStorageAccountWorker, terraformPlanFilePath);
                            ArchiveSharedFolderTfvarsIfPresent(component, deploymentResult.Id, planStorageDir);
                            var receipt = new Dorc.PersistentData.Model.TerraformDeploymentReceipt
                            {
                                DeploymentResultId = deploymentResult.Id,
                                DeploymentRequestId = requestId,
                                ProjectId = project.ProjectId,
                                Operation = terreformOperation == TerraformRunnerOperations.CreateDestroyPlan ? "Destroy" : "Plan",
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

                        case TerraformRunnerOperations.ApplyDestroyPlan:
                            CompleteApplyDestroyPlan(
                                component,
                                deploymentResult,
                                environmentName,
                                sourceArchiveFilePath,
                                postApplyOutcome!,
                                cancellationToken);
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
                    if (!string.IsNullOrWhiteSpace(stagedDestroySourceDir))
                    {
                        TryDeleteDirectory(stagedDestroySourceDir);
                    }
                }

            }

            return isScriptExecutionSuccessful;
        }

        internal void CompleteApplyDestroyPlan(
            ComponentApiModel component,
            DeploymentResultApiModel deploymentResult,
            string environmentName,
            string sourceArchiveFilePath,
            Dorc.PersistentData.Model.TerraformDeploymentOutcome postApplyOutcome,
            CancellationToken cancellationToken)
        {
            var targetEnvironmentId = _environmentsPersistentSource.GetEnvironment(environmentName)?.EnvironmentId ?? 0;
            try
            {
                _cloudResourceRegistrar.DeregisterEnvironmentResources(targetEnvironmentId, deploymentResult.Id);
                postApplyOutcome.InventoryOutcome = "Deregistered";
            }
            catch
            {
                postApplyOutcome.InventoryOutcome = "Failed";
                _requestsPersistentSource.SaveTerraformDeploymentOutcome(postApplyOutcome);
                throw;
            }
            postApplyOutcome.ArchiveOutcome = File.Exists(sourceArchiveFilePath) ? "Succeeded" : "Unavailable";
            if (File.Exists(sourceArchiveFilePath))
            {
                _azureStorageAccountWorker.SaveFileToBlobs(sourceArchiveFilePath);
                TryDeleteFile(sourceArchiveFilePath);
            }
            _requestsPersistentSource.SaveTerraformDeploymentOutcome(postApplyOutcome);

            cancellationToken.ThrowIfCancellationRequested();
            _requestsPersistentSource.UpdateResultStatus(
                deploymentResult,
                DeploymentResultStatus.Complete);

            logger.LogInformation("Terraform destroy plan applied for component {Component}. Completed.", component.ComponentName);
        }

        [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
        private uint ExecuteRunnerProcess(
            TerraformRunnerProcessStarter processStarter,
            ProcessSecurityContextBuilder.ProcessSecurityContext? securityContext,
            int requestId,
            string terraformPlanFilePath,
            CancellationToken cancellationToken)
        {
            Interop.Kernel32.STARTUPINFO startupInfo = new ProcessStartupInfoBuilder(logger).Build();
            logger.LogInformation("Starting Runner process.");

            var process = processStarter.Start(startupInfo, securityContext);
            try
            {
                _processesPersistentSource.AssociateProcessWithRequest((int)process.Id, requestId);

                logger.LogDebug("Waiting for process to exit.");
                var resultCode = process.WaitForExit(cancellationToken,
                    () => File.WriteAllText(terraformPlanFilePath + ".cancel", string.Empty));
                logger.LogInformation("Runner finished for request {RequestId} with result code {ResultCode}.", requestId, resultCode);
                return resultCode;
            }
            finally
            {
                try { _processesPersistentSource.RemoveProcess((int)process.Id); }
                finally { process.Dispose(); }
            }
        }

        internal void TryDeleteFile(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            var baseFull = Path.GetFullPath(DorcProgramData.Root).TrimEnd(Path.DirectorySeparatorChar);
            var fullPath = Path.GetFullPath(path);
            if (!fullPath.StartsWith(baseFull + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            {
                throw new ArgumentException("Invalid file path");
            }
            try
            {
                if (File.Exists(fullPath)) File.Delete(fullPath);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Could not delete staged Terraform file {ArtifactPath}.", fullPath);
            }
        }

        internal void TryDeleteDirectory(string path)
        {
            if (!Directory.Exists(path)) return;
            var baseFull = Path.GetFullPath(DorcProgramData.Root).TrimEnd(Path.DirectorySeparatorChar);
            var fullPath = Path.GetFullPath(path);
            if (!fullPath.StartsWith(baseFull + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            {
                throw new ArgumentException("Invalid file path");
            }
            try
            {
                foreach (var file in Directory.GetFiles(fullPath, "*", SearchOption.AllDirectories))
                {
                    File.SetAttributes(file, FileAttributes.Normal);
                }
                Directory.Delete(fullPath, recursive: true);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Could not delete staged Terraform directory {DirectoryPath}.", fullPath);
            }
        }

        internal void ArchiveSharedFolderTfvarsIfPresent(
            ComponentApiModel component,
            int deploymentResultId,
            string planStorageDir)
        {
            if (component.TerraformSourceType != TerraformSourceType.SharedFolder ||
                string.IsNullOrWhiteSpace(component.ScriptPath))
            {
                return;
            }

            var sharedSourceDir = Path.Join(component.ScriptPath, component.TerraformSubPath);
            var baseFull = Path.GetFullPath(component.ScriptPath).TrimEnd(Path.DirectorySeparatorChar);
            var fullPath = Path.GetFullPath(sharedSourceDir);
            if (!fullPath.StartsWith(baseFull + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                && fullPath != baseFull)
            {
                throw new ArgumentException("Invalid file path");
            }
            if (!Directory.Exists(fullPath))
            {
                return;
            }

            var tfvarsFiles = Directory.GetFiles(fullPath, "*tfvars*");
            if (tfvarsFiles.Length == 0)
            {
                return;
            }

            var tfvarsZipPath = Path.Join(planStorageDir, $"{deploymentResultId}.shared-tfvars.zip");
            TryDeleteFile(tfvarsZipPath);
            using (var zip = System.IO.Compression.ZipFile.Open(tfvarsZipPath, System.IO.Compression.ZipArchiveMode.Create))
            {
                foreach (var file in tfvarsFiles)
                {
                    AddTfvarsFileToZip(zip, fullPath, file);
                }
            }
            _azureStorageAccountWorker.SaveFileToBlobs(tfvarsZipPath);
            TryDeleteFile(tfvarsZipPath);
        }

        internal static void AddTfvarsFileToZip(
            System.IO.Compression.ZipArchive zip,
            string sharedSourceDirFull,
            string file)
        {
            var fullPath = Path.GetFullPath(file);
            if (!fullPath.StartsWith(sharedSourceDirFull + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            {
                throw new ArgumentException("Invalid file path");
            }
            using var entryStream = zip.CreateEntry(Path.GetFileName(fullPath)).Open();
            using var fileStream = File.OpenRead(fullPath);
            fileStream.CopyTo(entryStream);
        }

        internal bool TryStagePriorAppliedSourceArchive(
            int priorDeploymentResultId,
            out string stagedArchiveDir)
        {
            stagedArchiveDir = string.Empty;
            var outcome = _requestsPersistentSource.GetTerraformDeploymentOutcome(priorDeploymentResultId);
            if (outcome == null)
            {
                return false;
            }

            var archiveBlobName = string.Equals(outcome.ArchiveOutcome, "Succeeded", StringComparison.OrdinalIgnoreCase)
                ? $"{priorDeploymentResultId}.terraform-source.zip"
                : $"{priorDeploymentResultId.CreateTerraformPlanBlobName()}.source.zip";
            var stagingRoot = Path.Join(DorcProgramData.Root, TerraformPlanStorage.DirectoryName);
            Directory.CreateDirectory(stagingRoot);
            var tempDir = Path.Join(stagingRoot, $"dorc-destroy-src-{priorDeploymentResultId}-{Guid.NewGuid():N}");
            var tempZipPath = tempDir + ".zip";
            var tempTfvarsZipPath = tempDir + ".tfvars.zip";
            try
            {
                _azureStorageAccountWorker.DownloadFileFromBlobs(archiveBlobName, tempZipPath);
                Directory.CreateDirectory(tempDir);
                System.IO.Compression.ZipFile.ExtractToDirectory(tempZipPath, tempDir, overwriteFiles: true);
                TryDeleteFile(tempZipPath);
                TryDeleteFile(Path.Join(tempDir, "_dorc_provenance.json"));
                TryDeleteFile(Path.Join(tempDir, "_dorc_backend.tf"));
                TryRestoreRetainedSharedFolderTfvars(priorDeploymentResultId, tempTfvarsZipPath, tempDir);
                stagedArchiveDir = tempDir;
                return true;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Could not stage retained Terraform source archive {ArchiveBlobName}; falling back to recorded source location.", archiveBlobName);
                TryDeleteFile(tempZipPath);
                TryDeleteFile(tempTfvarsZipPath);
                TryDeleteDirectory(tempDir);
                return false;
            }
        }

        private void TryRestoreRetainedSharedFolderTfvars(int priorDeploymentResultId, string tempTfvarsZipPath, string tempDir)
        {
            try
            {
                _azureStorageAccountWorker.DownloadFileFromBlobs($"{priorDeploymentResultId}.shared-tfvars.zip", tempTfvarsZipPath);
                System.IO.Compression.ZipFile.ExtractToDirectory(tempTfvarsZipPath, tempDir, overwriteFiles: true);
                TryDeleteFile(tempTfvarsZipPath);
            }
            catch
            {
                TryDeleteFile(tempTfvarsZipPath);
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
            ProjectApiModel? project,
            out string stagedDestroySourceDir)
        {
            stagedDestroySourceDir = string.Empty;
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
            var effectiveRequest = request;
            Dorc.PersistentData.Model.TerraformDeploymentReceipt? priorReceipt = null;
            if (request is not null && Dorc.PersistentData.Sources.RequestsPersistentSource.IsDestroyRequestXml(request.RequestDetails))
            {
                var targetEnvironmentId = _environmentsPersistentSource.GetEnvironment(request.EnvironmentName)?.EnvironmentId;
                priorReceipt = FindLatestStateMutatingReceipt(
                    request.EnvironmentName,
                    targetEnvironmentId,
                    request.Project,
                    component.ComponentId);
                if (priorReceipt is not null)
                {
                    if (Enum.TryParse<TerraformSourceType>(priorReceipt.SourceType, ignoreCase: true, out var parsedSourceType))
                    {
                        component.TerraformSourceType = parsedSourceType;
                    }
                    component.TerraformSubPath = priorReceipt.SourceSubPath ?? string.Empty;
                    if (component.TerraformSourceType == TerraformSourceType.SharedFolder)
                    {
                        if (TryStagePriorAppliedSourceArchive(
                                priorReceipt.DeploymentResultId,
                                out stagedDestroySourceDir))
                        {
                            component.TerraformSubPath = string.Empty;
                            scriptGroup.ScriptsLocation = stagedDestroySourceDir;
                        }
                        else if (!string.IsNullOrWhiteSpace(priorReceipt.RequestedSource))
                        {
                            scriptGroup.ScriptsLocation = priorReceipt.RequestedSource;
                        }
                    }
                    else if (component.TerraformSourceType == TerraformSourceType.Git &&
                             !string.IsNullOrWhiteSpace(priorReceipt.GitCommitSha))
                    {
                        component.TerraformGitBranch = priorReceipt.GitCommitSha;
                        if (!string.IsNullOrWhiteSpace(priorReceipt.RequestedSource))
                        {
                            project.TerraformGitRepoUrl = priorReceipt.RequestedSource;
                        }
                    }
                    else if (component.TerraformSourceType == TerraformSourceType.Catalog &&
                             !string.IsNullOrWhiteSpace(priorReceipt.TemplateVersion))
                    {
                        component.TerraformTemplateVersion = priorReceipt.TemplateVersion;
                        if (!string.IsNullOrWhiteSpace(priorReceipt.TemplateName))
                        {
                            component.TerraformTemplateName = priorReceipt.TemplateName;
                        }
                    }
                    else if (!string.IsNullOrWhiteSpace(priorReceipt.RequestedSource))
                    {
                        project.ArtefactsUrl = priorReceipt.RequestedSource;
                    }
                }
            }

            if (request is not null &&
                component.TerraformSourceType != TerraformSourceType.Catalog &&
                ((string.IsNullOrWhiteSpace(request.BuildUri) && string.IsNullOrWhiteSpace(request.DropLocation)) ||
                 Dorc.PersistentData.Sources.RequestsPersistentSource.IsDestroyRequestXml(request.RequestDetails)))
            {
                var priorRequest = _requestsPersistentSource.GetLatestAppliedRequestForEnvironment(
                    request.EnvironmentName,
                    project?.ProjectName ?? request.Project,
                    component.ComponentId);
                if (priorRequest is not null)
                {
                    effectiveRequest = new DeploymentRequestApiModel
                    {
                        Id = request.Id,
                        Project = request.Project,
                        EnvironmentName = request.EnvironmentName,
                        BuildUri = priorRequest.BuildUri,
                        DropLocation = priorRequest.DropLocation,
                        BuildNumber = priorRequest.BuildNumber ?? request.BuildNumber,
                        Components = request.Components,
                        RequestDetails = request.RequestDetails,
                        UserName = request.UserName,
                        IsProd = request.IsProd
                    };
                    RestorePriorAppliedInputs(priorRequest, component.ComponentId!.Value, project!, properties);
                }
            }
            scriptGroup.SourceBuildIdentity = effectiveRequest.BuildNumber;
            _sourceConfigurator.ConfigureScriptGroup(scriptGroup, component, effectiveRequest, project, properties);
            scriptGroup.SensitivePropertyNames = scriptGroup.SensitivePropertyNames
                .Concat(_propertiesPersistentSource.GetAllProperties().Where(p => p.Secure).Select(p => p.Name))
                .Concat(_configValuesPersistentSource.GetAllConfigValues(false).Where(c => c.Secure).Select(c => c.Key))
                .Distinct(StringComparer.Ordinal).ToList();

            return scriptGroup;
        }

        internal void RestorePriorAppliedInputs(
            DeploymentRequestApiModel priorRequest,
            int componentId,
            ProjectApiModel project,
            IDictionary<string, VariableValue> properties)
        {
            var priorProject = _requestsPersistentSource.GetSubmittedProject(priorRequest.Id, componentId);
            if (priorProject is not null)
            {
                if (!string.IsNullOrWhiteSpace(priorProject.ArtefactsUrl))
                {
                    project.ArtefactsUrl = priorProject.ArtefactsUrl;
                }
                project.ArtefactsSubPaths = priorProject.ArtefactsSubPaths;
                project.ArtefactsBuildRegex = priorProject.ArtefactsBuildRegex;
            }

            if (!string.IsNullOrWhiteSpace(priorRequest.RequestDetails))
            {
                var priorDetails = new Dorc.Core.DeploymentRequestDetailSerializer().Deserialize(priorRequest.RequestDetails);
                foreach (var prop in priorDetails.Properties)
                {
                    if (!properties.ContainsKey(prop.Name))
                    {
                        properties[prop.Name] = new VariableValue { Value = prop.Value, Type = typeof(string) };
                    }
                }
            }
        }

        internal static bool IsEphemeralOperation(
            bool isEphemeralEnvironment,
            bool isProductionEnvironment)
        {
            return isEphemeralEnvironment && !isProductionEnvironment;
        }

        internal static bool IsEphemeralOperation(
            IDictionary<string, VariableValue>? properties,
            EnvironmentApiModel? environment)
        {
            return IsEphemeralOperation(
                environment?.IsEphemeral == true,
                environment?.EnvironmentIsProd == true);
        }

        private Dorc.PersistentData.Model.TerraformDeploymentReceipt? FindLatestStateMutatingReceipt(
            string environmentName,
            int? targetEnvironmentId,
            string? projectName,
            int? componentId)
        {
            var candidates = (_requestsPersistentSource.GetTerraformReceiptsForEnvironment(environmentName)
                ?? Array.Empty<Dorc.PersistentData.Model.TerraformDeploymentReceipt>())
                .Where(r => r.ComponentId == componentId &&
                            r.Operation != "Destroy" &&
                            (targetEnvironmentId is null || r.EnvironmentId is null || r.EnvironmentId == targetEnvironmentId) &&
                            string.Equals(r.ProjectName, projectName, StringComparison.OrdinalIgnoreCase))
                .Select(r => (r, _requestsPersistentSource.GetTerraformDeploymentOutcome(r.DeploymentResultId)?.ApplyOutcome));

            return Dorc.PersistentData.Model.TerraformApplyReceiptSelection.SelectStateMutatingReceipt(candidates);
        }

        internal static void ConfigurePlatformState(ScriptGroup scriptGroup, ProjectApiModel submittedProject,
            string componentName, string environmentName, bool isEphemeral = false, int environmentId = 0,
            IReadOnlyList<Dorc.PersistentData.Model.TerraformDeploymentReceipt>? existingReceipts = null,
            int? componentId = null)
        {
            if (scriptGroup.TerraformSourceType != TerraformSourceType.Catalog && !isEphemeral) return;
            var backends = submittedProject.TerraformStateBackends;
            var backend = isEphemeral
                ? TerraformStateBackends.Require(backends, environmentId)
                : TerraformStateBackends.RequireSubmitted(backends);
            var sanitizedProjectPrefix = TerraformStateKeySanitizer.Sanitize(submittedProject.ProjectName) + "/";
            var sanitizedComponentSegment = TerraformStateKeySanitizer.Sanitize(componentName);
            var sanitizedEnvironmentSuffix = "/" + TerraformStateKeySanitizer.Sanitize(environmentName) + ".tfstate";
            var defaultStateKey = sanitizedProjectPrefix + sanitizedComponentSegment + sanitizedEnvironmentSuffix;
            var relevantReceipts = (existingReceipts ?? Array.Empty<Dorc.PersistentData.Model.TerraformDeploymentReceipt>())
                .Where(r =>
                    (environmentId <= 0 || r.EnvironmentId is null || r.EnvironmentId == environmentId) &&
                    (string.IsNullOrWhiteSpace(r.ProjectName) || string.Equals(r.ProjectName, submittedProject.ProjectName, StringComparison.OrdinalIgnoreCase)))
                .ToList();
            if (componentId is > 0)
            {
                var suffixCounter = 0;
                while (relevantReceipts.Any(r =>
                    r.ComponentId != componentId.Value &&
                    (string.Equals(r.StateKey, defaultStateKey, StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(
                         $"{sanitizedProjectPrefix}{TerraformStateKeySanitizer.Sanitize(r.ComponentName)}{sanitizedEnvironmentSuffix}",
                         defaultStateKey,
                         StringComparison.OrdinalIgnoreCase))))
                {
                    suffixCounter++;
                    var suffix = suffixCounter == 1
                        ? $"-{componentId.Value}"
                        : $"-{componentId.Value}-{suffixCounter - 1}";
                    defaultStateKey = $"{sanitizedProjectPrefix}{sanitizedComponentSegment}{suffix}{sanitizedEnvironmentSuffix}";
                }
            }
            var stateKey = relevantReceipts
                .Where(r =>
                    componentId is > 0 &&
                    r.ComponentId == componentId.Value &&
                    !string.IsNullOrWhiteSpace(r.StateKey) &&
                    r.StateKey.StartsWith(sanitizedProjectPrefix, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(r => r.DeploymentResultId)
                .Select(r => r.StateKey!)
                .FirstOrDefault() ?? defaultStateKey;
            if (relevantReceipts.Any(r =>
                    ((componentId is > 0 && r.ComponentId == componentId.Value) ||
                     (string.Equals(r.ComponentName, componentName, StringComparison.OrdinalIgnoreCase) &&
                      (string.IsNullOrEmpty(r.StateKey) || string.Equals(r.StateKey, stateKey, StringComparison.OrdinalIgnoreCase)))) &&
                    !string.IsNullOrEmpty(r.StateStorageAccount) &&
                    (r.StateStorageAccount != backend.StorageAccount || r.StateContainerName != backend.ContainerName)))
            {
                throw new InvalidOperationException("Retained Terraform execution evidence for this ephemeral environment identifies a different state backend.");
            }
            scriptGroup.TerraformStateStorageAccount = backend.StorageAccount;
            scriptGroup.TerraformStateContainerName = backend.ContainerName;
            scriptGroup.TerraformStateResourceGroup = backend.ResourceGroup;
            scriptGroup.TerraformStateSubscriptionId = backend.SubscriptionId;
            scriptGroup.TerraformStateKey = stateKey;
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