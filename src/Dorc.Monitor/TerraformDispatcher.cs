using Dorc.ApiModel;
using Dorc.ApiModel.MonitorRunnerApi;
using Dorc.Core;
using Dorc.Core.AzureStorageAccount;
using Dorc.Core.BuildServer;
using Dorc.Core.Configuration;
using Dorc.Core.Security;
using Dorc.Monitor.Pipes;
using Dorc.Monitor.RunnerProcess;
using Dorc.Monitor.RunnerProcess.Interop.Windows.Kernel32;
using Dorc.Monitor.Terraform;
using Dorc.Core.Terraform;
using Dorc.Monitor.Security;
using Dorc.Monitor.TerraformSourceConfig;
using Dorc.PersistentData.Security;
using Dorc.PersistentData.Sources.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Security;
using System.Text;
using Dorc.Kafka.Events.Publisher;

namespace Dorc.Monitor
{
    internal sealed record TerraformRunnerInvocation(
        int RequestId,
        int DeploymentResultId,
        string QualifiedAccountName,
        string ScriptGroupPipeName,
        string PlanDirectory,
        string PlanFilePath,
        string PlanContentFilePath,
        string PlanSourceArchiveFilePath,
        string LockFilePath,
        string AppliedResourcesFilePath,
        string SourceArchiveFilePath,
        string CancelFilePath,
        TerraformRunnerOperations Operation);

    public class TerraformDispatcher : ITerraformDispatcher
    {
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
        private readonly IScriptScopeConfigValues _scriptScopeConfigValues;
        private readonly IDeploymentCredentialSource _credentialSource;
        private readonly string _planStorageRoot;
        private readonly Func<TerraformRunnerInvocation, CancellationToken, uint>? _executeRunner;

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
            IScriptScopeConfigValues scriptScopeConfigValues,
            IDeploymentCredentialSource credentialSource,
            ISourceHostAllowList sourceHostAllowList)
            : this(
                logger,
                requestsPersistentSource,
                configValuesPersistentSource,
                configurationSettingsEngine,
                processesPersistentSource,
                scriptGroupPipeServer,
                azureStorageAccountWorker,
                projectsPersistentSource,
                gitHubHostValidator,
                cloudResourcesPersistentSource,
                cloudResourceAuditPersistentSource,
                environmentsPersistentSource,
                databasesPersistentSource,
                propertiesPersistentSource,
                scriptScopeConfigValues,
                credentialSource,
                sourceHostAllowList,
                Path.Join(DorcProgramData.Root, TerraformPlanStore.DirectoryName),
                executeRunner: null)
        {
        }

        internal TerraformDispatcher(
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
            IScriptScopeConfigValues scriptScopeConfigValues,
            IDeploymentCredentialSource credentialSource,
            ISourceHostAllowList sourceHostAllowList,
            string planStorageRoot,
            Func<TerraformRunnerInvocation, CancellationToken, uint>? executeRunner = null)
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
            this._scriptScopeConfigValues = scriptScopeConfigValues;
            this._credentialSource = credentialSource;
            _planStorageRoot = planStorageRoot;
            _executeRunner = executeRunner;
            this._sourceConfigurator = new TerraformSourceConfigurator(logger, _configurationSettingsEngine, gitHubHostValidator, sourceHostAllowList);
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
            string? executionIdentityReference,
            RequestExecutionIdentityAdoption identityAdoption,
            StringBuilder resultLogBuilder,
            TerraformRunnerOperations terreformOperation,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            logger.LogInformation("Dispatching Terraform component {Component} ({ComponentId}), result {ResultId}, environment {Environment}.",
                LogText.SingleLine(component.ComponentName), component.ComponentId, deploymentResult.Id, LogText.SingleLine(environmentName));

            // See ScriptDispatcher: one resolution point rather than a copy per dispatch path.
            // Environment-keyed. An environment naming its own execution identity deploys under
            // it; one that does not falls back to the tier default and behaves exactly as before,
            // so migration proceeds environment by environment rather than as a flag day.
            using var credential = identityAdoption.Resolve(
                _credentialSource,
                isProduction ? DeploymentTier.Production : DeploymentTier.NonProduction,
                executionIdentityReference);

            if (credential == null)
            {
                var refusalMessage = RequestExecutionIdentityAdoption.FormatRefusalMessage(
                    environmentName,
                    executionIdentityReference,
                    _credentialSource.Description);
                logger.LogError(
                    "No deployment credential is available for environment '{EnvironmentName}'."
                    + " Credentials are read from {Source}.",
                    LogText.SingleLine(environmentName),
                    _credentialSource.Description);
                resultLogBuilder?.AppendLine(refusalMessage);

                isScriptExecutionSuccessful = false;
                return isScriptExecutionSuccessful;
            }

            var processAccountName = credential.UserName;

            // The request is loaded before the guard because the project name
            // is part of the state identity: environments are shared across
            // projects and component names are not globally unique, so
            // (project, environment, component) is the smallest collision-free
            // key - see docs/Terraform/STATE-MODEL.md.
            // Resolve frozen submitted component and project snapshots before execution.
            component = ResolveSubmittedComponent(requestId, component);
            var project = component.ComponentId.HasValue
                ? _requestsPersistentSource.GetSubmittedProject(requestId, component.ComponentId.Value)
                : null;

            var request = _requestsPersistentSource.GetRequest(requestId);
            var projectName = request?.Project ?? string.Empty;
            properties = ApplyCatalogInputs(component, properties, requestId, request);

            if (component.TerraformSourceType == TerraformSourceType.SharedFolder)
            {
                var configuredScriptRoot = _configValuesPersistentSource.GetConfigValue(
                    PropertyValueScopeOptionsFixed.ScriptRoot,
                    string.Empty);
                if (!_sourceConfigurator.VerifySharedFolderConfinement(component, project, properties, configuredScriptRoot))
                {
                    isScriptExecutionSuccessful = false;
                    return isScriptExecutionSuccessful;
                }
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

            var scriptGroup = BuildScriptGroup(
                component.ScriptPath,
                properties,
                deploymentResult.Id,
                component,
                request,
                project,
                sharedFolderConfinementAlreadyChecked: true);
            scriptGroup.DefinitionRequestId = requestId;
            scriptGroup.DefinitionRevision = _requestsPersistentSource.GetRequestDefinitions(requestId)
                .Single(d => d.ComponentId == component.ComponentId).DefinitionRevision;
            scriptGroup.SourceBuildIdentity = request.BuildNumber;

            ConfigurePlatformState(scriptGroup, project, component.ComponentName, environmentName);

            var domainName = _configurationSettingsEngine.GetConfigurationDomainNameIntra();
            var qualifiedAccountName = credential.QualifiedAccountName(domainName);
            var contextBuilder = new ProcessSecurityContextBuilder(logger)
            {
                UserName = processAccountName,
                Domain = domainName
            };

            using (var pipeCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            using (var securityContext = _executeRunner is null ? contextBuilder.Build(credential.Password) : null)
            {
                var startedScriptGroupPipeName = $"DOrcMonitor-{HostInstanceId.Value}-{requestId}";
                // The script group and per-result plan directory are made available to the
                // environment-resolved execution identity the Runner executes as, rather than
                // assumed to be the Monitor's own identity or a shared folder across the host.
                var readerIdentity = new ScriptGroupReaderIdentity(domainName, processAccountName);
                TerraformPlanStore? planStore = null;

                try
                {
                    Task scriptGroupPipeTask = _scriptGroupPipeServer.Start(
                            startedScriptGroupPipeName,
                            scriptGroup,
                            readerIdentity,
                            pipeCancellationTokenSource.Token);
                    logger.LogInformation("Server named pipe {PipeName} has started.", startedScriptGroupPipeName);

                    var runnerLogPathSetting = new ConfigurationBuilder().AddJsonFile("appsettings.json").Build()
                    .GetSection("AppSettings")["RunnerLogPath"] ?? string.Empty;
                    var runnerLogPath = runnerLogPathSetting + $"\\{startedScriptGroupPipeName}.txt";
                    var uncLogPath = runnerLogPath.Replace("c:", @"\\" + System.Environment.GetEnvironmentVariable("COMPUTERNAME"));

                    _requestsPersistentSource.UpdateUncLogPath(requestId, uncLogPath);

                    // Allocate a fresh per-dispatch directory (<root>\{resultId}-{nonce}) confined
                    // initially to Monitor, SYSTEM, and Administrators only.
                    planStore = TerraformPlanStore.Reserve(
                        logger,
                        _planStorageRoot,
                        deploymentResult.Id);

                    var terraformPlanFileName = deploymentResult.Id.CreateTerraformPlanBlobName();
                    var terraformPlanFilePath = planStore.PathOf(terraformPlanFileName);
                    var planSourceArchiveFileName = terraformPlanFileName + ".source.zip";
                    var planSourceArchiveFilePath = planStore.PathOf(planSourceArchiveFileName);
                    var terraformPlanContentFileName = deploymentResult.Id.CreateTerraformPlanContentBlobName();
                    var terraformPlanContentFilePath = planStore.PathOf(terraformPlanContentFileName);
                    // Persist .terraform.lock.hcl alongside the plan for diagnostics; apply
                    // restores the authoritative lockfile from .tfplan.source.zip.
                    var terraformLockFileName = $"{deploymentResult.Id}.terraform.lock.hcl";
                    var terraformLockFilePath = planStore.PathOf(terraformLockFileName);
                    // Written by the runner after a successful apply; read back below to
                    // register the applied resources on the environment and upload recovery state.
                    var appliedResourcesFileName = $"{deploymentResult.Id}.applied-resources.json";
                    var appliedResourcesFilePath = terreformOperation == TerraformRunnerOperations.ApplyPlan
                        ? planStore.PathOf(appliedResourcesFileName)
                        : string.Empty;
                    // Written by the runner after a successful apply: the exact terraform
                    // configuration deployed (without tfvars) plus a provenance record.
                    var sourceArchiveFileName = $"{deploymentResult.Id}.terraform-source.zip";
                    var sourceArchiveFilePath = terreformOperation == TerraformRunnerOperations.ApplyPlan
                        ? planStore.PathOf(sourceArchiveFileName)
                        : string.Empty;
                    var cancelSignalFileName = terraformPlanFileName + ".cancel";
                    var cancelSignalFilePath = planStore.PathOf(cancelSignalFileName);

                    Dorc.PersistentData.Model.TerraformDeploymentOutcome? postApplyOutcome = null;
                    if (terreformOperation == TerraformRunnerOperations.ApplyPlan)
                    {
                        // Download and verify approved plan inputs while planStore.Directory is
                        // still restricted to Monitor, SYSTEM, and Administrators only.
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

                    try
                    {
                        logger.LogInformation("Starting Runner process.");
                        cancellationToken.ThrowIfCancellationRequested();

                        uint resultCode;
                        try
                        {
                            planStore.AdmitRunner(qualifiedAccountName);

                            if (_executeRunner is not null)
                            {
                                try
                                {
                                    var invocation = new TerraformRunnerInvocation(
                                        requestId,
                                        deploymentResult.Id,
                                        qualifiedAccountName,
                                        startedScriptGroupPipeName,
                                        planStore.Directory,
                                        terraformPlanFilePath,
                                        terraformPlanContentFilePath,
                                        planSourceArchiveFilePath,
                                        terraformLockFilePath,
                                        appliedResourcesFilePath,
                                        sourceArchiveFilePath,
                                        cancelSignalFilePath,
                                        terreformOperation);
                                    resultCode = _executeRunner(invocation, cancellationToken);
                                }
                                finally
                                {
                                    pipeCancellationTokenSource.Cancel();
                                }
                            }
                            else
                            {
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
                                Interop.Kernel32.STARTUPINFO startupInfo = new ProcessStartupInfoBuilder(logger).Build();
                                var process = processStarter.Start(startupInfo, securityContext!);
                                try
                                {
                                    _processesPersistentSource.AssociateProcessWithRequest((int)process.Id, requestId);

                                    logger.LogDebug("Waiting for process to exit.");
                                    resultCode = process.WaitForExit(cancellationToken,
                                        () => planStore.WriteCancelSignal(cancelSignalFileName));
                                }
                                finally
                                {
                                    pipeCancellationTokenSource.Cancel();
                                    try { _processesPersistentSource.RemoveProcess((int)process.Id); }
                                    finally { process.Dispose(); }
                                }
                            }
                        }
                        finally
                        {
                            pipeCancellationTokenSource.Cancel();
                            planStore.RevokeRunner();
                        }

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
                            // Failed or cancelled plan/apply executions skip planStore.Expire()
                            // below: Runner access was already revoked in finally above, and the
                            // confined Monitor-only per-dispatch directory (including downloaded
                            // apply inputs and any partial recovery outputs) stays on disk until
                            // TerraformPlanStore's 7-day orphan sweep removes it.
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
                            // Stage every Runner-written artefact once via handle-pinned,
                            // reparse-free I/O into a Monitor-only directory before blob upload
                            // and receipt provenance/digest verification.
                            var stagedPlanFilePath = planStore.StageVerifiedArtefact(terraformPlanFileName);
                            var stagedPlanContentFilePath = planStore.StageVerifiedArtefact(terraformPlanContentFileName);
                            var stagedPlanSourceArchiveFilePath = planStore.StageVerifiedArtefact(planSourceArchiveFileName);
                            var hasStagedLockFile = planStore.TryStageVerifiedArtefact(
                                terraformLockFileName,
                                out var stagedLockFilePath);

                            _azureStorageAccountWorker.SaveFileToBlobs(stagedPlanFilePath);
                            _azureStorageAccountWorker.SaveFileToBlobs(stagedPlanContentFilePath);
                            TerraformPlanSourceArtifacts.Upload(_azureStorageAccountWorker, stagedPlanFilePath);
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
                            TerraformReceiptCapture.ReadPlannedSource(
                                receipt,
                                stagedPlanSourceArchiveFilePath,
                                stagedPlanContentFilePath,
                                stagedPlanFilePath);
                            _requestsPersistentSource.SaveTerraformDeploymentReceipt(receipt);
                            if (hasStagedLockFile)
                            {
                                _azureStorageAccountWorker.SaveFileToBlobs(stagedLockFilePath!);
                            }

                            // Update status to WaitingConfirmation before expiring local state,
                            // so a cancellation or persistence error retains local recovery files.
                            cancellationToken.ThrowIfCancellationRequested();
                            _requestsPersistentSource.UpdateResultStatus(
                                deploymentResult,
                                DeploymentResultStatus.WaitingConfirmation);

                            // Binary plan, rendered plan, planned-source archive, receipt, and
                            // lockfile have all been durably uploaded/persisted. Withdraw the
                            // local dispatch directory now; any earlier failure retains it for
                            // operator recovery up to the 7-day stale sweep.
                            planStore.Expire();

                            logger.LogInformation("Terraform plan created for component {Component}. Waiting for confirmation.", component.ComponentName);
                            break;

                        case TerraformRunnerOperations.ApplyPlan:
                            // Stage post-apply sidecars once via handle-pinned, reparse-free I/O
                            // into a Monitor-only directory before registration, upload, or hashing.
                            TerraformRegistrationOutcome registration;
                            string? stagedAppliedResourcesPath = null;
                            var inventoryStageFailed = false;
                            try
                            {
                                if (planStore.TryStageVerifiedArtefact(appliedResourcesFileName, out stagedAppliedResourcesPath))
                                {
                                    registration = _cloudResourceRegistrar.RegisterAppliedResources(
                                        stagedAppliedResourcesPath,
                                        environmentName,
                                        request?.UserName ?? string.Empty,
                                        deploymentResult.Id);
                                }
                                else
                                {
                                    registration = new TerraformRegistrationOutcome();
                                }
                            }
                            catch (Exception ex)
                            {
                                inventoryStageFailed = true;
                                registration = new TerraformRegistrationOutcome { Status = "Failed", FailedCount = 1 };
                                logger.LogWarning(ex, "Could not stage verified Terraform inventory snapshot.");
                            }

                            postApplyOutcome!.InventoryOutcome = inventoryStageFailed
                                ? "RecoveryOrProvenanceFailed"
                                : registration.Status;
                            postApplyOutcome.RegisteredCount = registration.RegisteredCount;
                            postApplyOutcome.FailedCount = registration.FailedCount;
                            postApplyOutcome.InventoryWarningsJson = System.Text.Json.JsonSerializer.Serialize(registration.Warnings);
                            if (!inventoryStageFailed)
                            {
                                try
                                {
                                    if (stagedAppliedResourcesPath is not null)
                                    {
                                        _azureStorageAccountWorker.SaveFileToBlobs(stagedAppliedResourcesPath);
                                        using var inventoryStream = File.OpenRead(stagedAppliedResourcesPath);
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
                            }

                            // Persist the deployed-source archive next to the
                            // plan artefacts. Best-effort like the resource
                            // registration: the runner only writes the archive
                            // when archival itself succeeded, and a missing
                            // file must not fail a completed deployment.
                            try
                            {
                                if (planStore.TryStageVerifiedArtefact(sourceArchiveFileName, out var stagedSourceArchivePath))
                                {
                                    _azureStorageAccountWorker.SaveFileToBlobs(stagedSourceArchivePath);
                                    postApplyOutcome.ArchiveOutcome = "Succeeded";
                                    logger.LogInformation("Deployed Terraform source archived to blob {ArtifactName}.", sourceArchiveFileName);
                                }
                                else
                                {
                                    postApplyOutcome.ArchiveOutcome = "Unavailable";
                                }
                            }
                            catch (Exception ex)
                            {
                                postApplyOutcome.ArchiveOutcome = "Failed";
                                logger.LogWarning(ex, "Could not upload deployed Terraform source archive {ArtifactName}; the deployment itself succeeded.", sourceArchiveFileName);
                            }

                            _requestsPersistentSource.SaveTerraformDeploymentOutcome(postApplyOutcome);

                            cancellationToken.ThrowIfCancellationRequested();
                            _requestsPersistentSource.UpdateResultStatus(
                                deploymentResult,
                                DeploymentResultStatus.Complete);

                            // Confirmed plan-only completion expires immediately above, and a
                            // clean apply expires immediately here once resource registration,
                            // inventory upload, deployed-source archive upload, and outcome
                            // persistence all succeed without full or partial failure. By contrast,
                            // a failed or cancelled apply (or a post-apply registration/provenance/
                            // archive failure) skips Expire(), retaining the confined Monitor-only
                            // dispatch directory for operator retry/recovery until the 7-day
                            // orphan sweep expires it.
                            var retainLocalRecoveryArtefacts =
                                registration.Status == "Failed"
                                || registration.Status == "PartialFailure"
                                || postApplyOutcome.InventoryOutcome == "RecoveryOrProvenanceFailed"
                                || postApplyOutcome.ArchiveOutcome == "Failed";
                            if (!retainLocalRecoveryArtefacts)
                            {
                                planStore.Expire();
                            }

                            logger.LogInformation("Terraform plan applied for component {Component}. Completed.", component.ComponentName);
                            break;
                    }
                }
                finally
                {
                    // Dispose releases pinned NTFS handles, removes any temporary Monitor-only
                    // _staging-* directory, and drops in-process sweep leases without deleting
                    // a retained dispatch directory when Expire() was skipped for recovery.
                    planStore?.Dispose();

                    // The bundle carries the resolved deployment properties, secrets
                    // included. It is needed for the span of one Runner process, so it is
                    // withdrawn as soon as that process is gone - on the failure and
                    // cancellation paths as much as the successful one.
                    _scriptGroupPipeServer.Expire(startedScriptGroupPipeName);
                }

            }

            return isScriptExecutionSuccessful;
        }

        /// <summary>
        /// See ScriptDispatcher: the same invariant, on the branch that serialises a script
        /// group for the Terraform Runner.
        /// </summary>
        private IDictionary<string, VariableValue> WithheldKeysRemoved(
            IDictionary<string, VariableValue> properties)
        {
            var withheld = properties.Keys.Where(_scriptScopeConfigValues.IsWithheld).ToList();

            if (withheld.Count == 0)
            {
                return properties;
            }

            logger.LogWarning(
                "Removed {Count} withheld key(s) from the Terraform script group before dispatch: {Keys}.",
                withheld.Count,
                string.Join(", ", withheld));

            return properties
                .Where(property => !_scriptScopeConfigValues.IsWithheld(property.Key))
                .ToDictionary(property => property.Key, property => property.Value);
        }

        internal IDictionary<string, VariableValue> ApplyCatalogInputs(
            ComponentApiModel component,
            IDictionary<string, VariableValue> properties,
            int requestId,
            DeploymentRequestApiModel? request)
        {
            var catalogInputs = component.ComponentId.HasValue
                ? _requestsPersistentSource.GetCatalogRequestInputs(requestId, component.ComponentId.Value)
                : null;
            return catalogInputs != null
                ? TerraformCatalogRequestSnapshot.Apply(component, properties, request?.RequestDetails ?? string.Empty, catalogInputs)
                : properties;
        }

        private ComponentApiModel ResolveSubmittedComponent(int requestId, ComponentApiModel fallback)
        {
            if (!fallback.ComponentId.HasValue)
            {
                return fallback;
            }

            var submitted = _requestsPersistentSource.GetSubmittedComponents(requestId);
            var matched = submitted?.FirstOrDefault(c => c.ComponentId == fallback.ComponentId);
            if (matched != null)
            {
                return matched;
            }

            var definitionRow = _requestsPersistentSource.GetRequestDefinitions(requestId)
                ?.FirstOrDefault(d => d.ComponentId == fallback.ComponentId);
            if (!string.IsNullOrWhiteSpace(definitionRow?.DefinitionJson))
            {
                var deserialized = System.Text.Json.JsonSerializer.Deserialize<ComponentApiModel>(definitionRow.DefinitionJson);
                if (deserialized != null)
                {
                    return deserialized;
                }
            }

            return fallback;
        }

        internal ScriptGroup GetScriptGroup(
            string scriptsLocation,
            IDictionary<string, VariableValue> properties,
            int deploymentResultId,
            ComponentApiModel component,
            DeploymentRequestApiModel request,
            ProjectApiModel? project)
            => BuildScriptGroup(
                scriptsLocation,
                properties,
                deploymentResultId,
                component,
                request,
                project,
                sharedFolderConfinementAlreadyChecked: false);

        private ScriptGroup BuildScriptGroup(
            string scriptsLocation,
            IDictionary<string, VariableValue> properties,
            int deploymentResultId,
            ComponentApiModel component,
            DeploymentRequestApiModel request,
            ProjectApiModel? project,
            bool sharedFolderConfinementAlreadyChecked)
        {
            var commonProperties = WithheldKeysRemoved(properties);
            var scriptGroup = new ScriptGroup()
            {
                ID = Guid.NewGuid(),
                DeployResultId = deploymentResultId,
                ScriptsLocation = scriptsLocation,
                CommonProperties = commonProperties,
                ScriptProperties = new List<ScriptProperties>(),
                // Exact names of sensitive wizard parameters, so runner-side
                // log redaction covers flagged properties whose names don't
                // match the heuristic pattern.
                SensitivePropertyNames = RequestPropertyRedaction
                    .GetSensitivePropertyNames(request?.RequestDetails ?? string.Empty)
                    .ToList()
            };

            // Use the configurator to set Terraform-specific fields based on source type
            var configuredScriptRoot = _configValuesPersistentSource.GetConfigValue(
                PropertyValueScopeOptionsFixed.ScriptRoot,
                string.Empty);
            _sourceConfigurator.ConfigureScriptGroup(
                scriptGroup,
                component,
                request,
                project,
                commonProperties,
                sharedFolderConfinementAlreadyChecked,
                configuredScriptRoot);
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