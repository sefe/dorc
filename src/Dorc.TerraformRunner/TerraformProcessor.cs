using Dorc.ApiModel;
using Dorc.ApiModel.MonitorRunnerApi;
using Dorc.Runner.Logger;
using Dorc.Terraform.Catalog;
using Dorc.TerraformRunner.CodeSources;
using Dorc.TerraformRunner.Pipes;
using Dorc.TerraformRunner.State;
using Microsoft.Extensions.Logging;
using System.ComponentModel;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Dorc.TerraformRunner
{
    internal class TerraformProcessor : ITerraformProcessor
    {
        private readonly IRunnerLogger logger;
        private readonly IScriptGroupPipeClient _scriptGroupPipeClient;
        private readonly ITerraformCodeSourceProviderFactory _codeSourceFactory;
        private readonly ITemplateCatalog _catalog;
        private readonly ITerraformStateBlobCleaner _stateBlobCleaner;
        private TerraformTemplateManifest? _manifest;
        private IReadOnlyDictionary<string, TerraformTemplateParameter> _parameters =
            new Dictionary<string, TerraformTemplateParameter>();

        // Azure subscription catalog deployments fall back to when the
        // environment designates no subscription (neither a 'Subscription'
        // cloud resource nor a TerraformSubscriptionId property). Comes from
        // Terraform:DefaultSubscriptionId in the runner's configuration; null
        // leaves subscription resolution to the runner identity's default.
        private readonly string? _defaultSubscriptionId;

        // Cleartext values of properties the request flagged sensitive, used
        // to scrub terraform command output before it is logged. Terraform
        // masks variables the MODULE marks `sensitive`, but a manifest can
        // flag a parameter sensitive whose module variable is not marked, so
        // this is the runner-side backstop for that gap. Populated per
        // operation from the ScriptGroup; empty when nothing is flagged.
        private IReadOnlyList<string> _sensitiveValues = Array.Empty<string>();

        // ARM_* variables injected into every terraform child process for this
        // operation. Populated from the environment's well-known credential
        // properties (TerraformClientId/ClientSecret/TenantId +
        // TerraformSubscriptionId) so each environment/subscription can own
        // its own service principal; empty when the environment defines none,
        // leaving authentication to the runner host's ambient identity.
        private IReadOnlyDictionary<string, string> _armEnvironment =
            new Dictionary<string, string>();
        private bool _usesPlatformBackend;
        private bool _isEphemeral;
        private string? _workloadSubscriptionId;

        public TerraformProcessor(
            IRunnerLogger logger,
            IScriptGroupPipeClient scriptGroupPipeClient,
            ITemplateCatalog catalog,
            ITerraformCodeSourceProviderFactory codeSourceFactory,
            string? defaultSubscriptionId = null,
            ITerraformStateBlobCleaner? stateBlobCleaner = null)
        {
            this.logger = logger;
            this._scriptGroupPipeClient = scriptGroupPipeClient;
            this._codeSourceFactory = codeSourceFactory;
            _catalog = catalog;
            this._defaultSubscriptionId = defaultSubscriptionId;
            this._stateBlobCleaner = stateBlobCleaner ?? new TerraformStateBlobCleaner(logger);
        }

        // Collects the cleartext values of the flagged-sensitive properties
        // from the ScriptGroup so terraform output can be scrubbed of them.
        // Names come from ScriptGroup.SensitivePropertyNames (set by the
        // Monitor dispatcher from the request's IsSensitive flags); values
        // come from CommonProperties. Only non-empty values are kept - an
        // empty value would otherwise redact the entire output.
        private async Task CollectSensitiveValuesAsync(ScriptGroup scriptGroup, CancellationToken cancellationToken)
        {
            _parameters = new Dictionary<string, TerraformTemplateParameter>();
            _manifest = null;
            if (scriptGroup.TerraformSourceType == TerraformSourceType.Catalog)
            {
                var manifest = await _catalog.GetAsync(scriptGroup.TerraformTemplateName,
                    scriptGroup.TerraformTemplateVersion, cancellationToken)
                    ?? throw new InvalidOperationException("The catalog template version is no longer available.");
                _parameters = manifest.Parameters.ToDictionary(p => p.Name, StringComparer.Ordinal);
                _manifest = manifest;
            }
            var names = (scriptGroup.SensitivePropertyNames ?? new List<string>())
                .Concat(_parameters.Values.Where(p => p.Sensitive).Select(p => p.Name))
                .Distinct(StringComparer.Ordinal);
            var props = scriptGroup.CommonProperties;

            // Sensitivity is metadata, not a guess based on the value. Even
            // whitespace can be a credential; only the empty value is skipped.
            _sensitiveValues = names
                .Select(name => props is not null && props.TryGetValue(name, out var vv) ? vv?.Value?.ToString()
                    : _parameters.TryGetValue(name, out var parameter) ? parameter.Default : null)
                .Where(v => !string.IsNullOrEmpty(v))
                .Select(v => v!)
                .ToList();
        }

        private string RedactSensitiveValues(string text)
        {
            if (string.IsNullOrEmpty(text) || _sensitiveValues.Count == 0) return text;
            var representations = _sensitiveValues.SelectMany(value =>
            {
                var jsonEscaped = JsonSerializer.Serialize(value)[1..^1];
                var jsonNetEscaped = Newtonsoft.Json.JsonConvert.SerializeObject(value)[1..^1];
                var hclEscaped = QuoteHclString(value)[1..^1];
                return new[] { value, jsonEscaped, jsonNetEscaped, hclEscaped };
            }).Where(value => value.Length > 0).Distinct().OrderByDescending(value => value.Length);
            foreach (var representation in representations)
                text = text.Replace(representation, "[REDACTED]");
            return text;
        }

        public async Task<bool> PreparePlanAsync(
            string pipeName,
            int requestId,
            string resultFilePath,
            string planContentFilePath,
            string? lockFilePath,
            CancellationToken cancellationToken,
            bool isDestroy = false)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ScriptGroup scriptGroupProperties = this._scriptGroupPipeClient.GetScriptGroupProperties(pipeName);
            var deployResultId = scriptGroupProperties.DeployResultId;
            var properties = scriptGroupProperties.CommonProperties;

            this.logger.SetRequestId(requestId);
            this.logger.SetDeploymentResultId(deployResultId);

            logger.Information($"TerraformProcessor.PreparePlan called for request' with id '{requestId}', deployment result id '{deployResultId}'.");
            
            var terraformWorkingDir = string.Empty;

            try
            {
                TerraformStateConfiguration.Resolve(scriptGroupProperties);
                await CollectSensitiveValuesAsync(scriptGroupProperties, cancellationToken);
                properties = ResolvePlanProperties(properties);
                terraformWorkingDir = CreateTerraformWorkingDirectory(requestId);
                await ProvisionTerraformWorkingDirectoryAsync(terraformWorkingDir, scriptGroupProperties, cancellationToken);

                // Create terraform plan
                var planContent = await CreateTerraformPlanAsync(properties, terraformWorkingDir, resultFilePath, planContentFilePath, requestId, isDestroy, cancellationToken);

                // persist .terraform.lock.hcl alongside the plan binary
                // so the apply phase resolves identical provider versions. The
                // dispatcher uploads this to blob storage; the apply path
                // restores it into the working dir before init.
                if (!string.IsNullOrEmpty(lockFilePath))
                {
                    PersistLockFile(terraformWorkingDir, lockFilePath);
                }

                WriteSourceArchive(
                    terraformWorkingDir, TerraformSourceArchive.PlanArchivePath(resultFilePath),
                    scriptGroupProperties, requestId, resultFilePath, isDestroy, cancellationToken);

                cancellationToken.ThrowIfCancellationRequested();
                logger.Information($"Terraform plan created for request '{requestId}'. Waiting for confirmation.");

                return true;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                logger.Error($"Failed to create Terraform plan for request '{requestId}': {RedactSensitiveValues(ex.Message)}");
                return false;
            }
            finally
            {
                // The working directory holds terraform.tfvars - every resolved deployment
                // property in plain text. Deleting it only on the success path left it behind
                // precisely when a deployment failed, which is when a host is most likely to
                // be looked at by someone other than the deployment account.
                DeleteTempTerraformFolder(terraformWorkingDir);
            }
        }

        public const string IsEphemeralPropertyName = "DOrcIsEphemeral";

        internal static bool IsEphemeral(IDictionary<string, VariableValue>? properties)
        {
            return properties is not null
                && properties.TryGetValue(IsEphemeralPropertyName, out var ephProp)
                && string.Equals(Convert.ToString(ephProp?.Value, CultureInfo.InvariantCulture), "true", StringComparison.OrdinalIgnoreCase);
        }

        private IDictionary<string, VariableValue> ResolvePlanProperties(IDictionary<string, VariableValue> properties)
        {
            var isEphemeral = IsEphemeral(properties);
            _isEphemeral = isEphemeral;

            // CommonProperties also contains unrelated deployment settings.
            // Only catalog parameters are Terraform inputs; legacy sources
            // retain the historical complete property set.
            if (_manifest is null)
            {
                if (isEphemeral)
                {
                    var hadTermProt = properties.Keys.Any(k =>
                        string.Equals(k, "termination_protection", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(k, "TF_VAR_termination_protection", StringComparison.OrdinalIgnoreCase));
                    var hadSvcTermProt = properties.Keys.Any(k =>
                        string.Equals(k, "service_termination_protection", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(k, "TF_VAR_service_termination_protection", StringComparison.OrdinalIgnoreCase));

                    foreach (var key in properties.Keys.Where(k =>
                                 string.Equals(k, "termination_protection", StringComparison.OrdinalIgnoreCase) ||
                                 string.Equals(k, "service_termination_protection", StringComparison.OrdinalIgnoreCase) ||
                                 string.Equals(k, "TF_VAR_termination_protection", StringComparison.OrdinalIgnoreCase) ||
                                 string.Equals(k, "TF_VAR_service_termination_protection", StringComparison.OrdinalIgnoreCase))
                             .ToList())
                    {
                        properties.Remove(key);
                    }

                    if (hadTermProt)
                    {
                        properties["termination_protection"] = new VariableValue { Value = "false", Type = typeof(bool) };
                    }

                    if (hadSvcTermProt)
                    {
                        properties["service_termination_protection"] = new VariableValue { Value = "false", Type = typeof(bool) };
                    }
                }

                return properties;
            }

            var effective = new Dictionary<string, VariableValue>(StringComparer.Ordinal);
            foreach (var parameter in _manifest.Parameters)
            {
                if (properties.TryGetValue(parameter.Name, out var supplied))
                    effective[parameter.Name] = supplied;
                else if (parameter.Default is not null)
                    effective[parameter.Name] = new VariableValue { Value = parameter.Default, Type = typeof(string) };
            }

            var validator = new ParameterValidator();
            var resolvedInputs = effective.ToDictionary(
                p => p.Key,
                p => (string?)Convert.ToString(p.Value.Value, CultureInfo.InvariantCulture),
                StringComparer.Ordinal);

            if (isEphemeral)
            {
                resolvedInputs = validator.EnforceEphemeralInvariants(_manifest, resolvedInputs, isEphemeral: true)
                    .ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);
                foreach (var kvp in resolvedInputs)
                {
                    effective[kvp.Key] = new VariableValue { Value = kvp.Value, Type = typeof(string) };
                }
            }

            var validation = validator.Validate(_manifest, resolvedInputs, isEphemeral: isEphemeral, isProd: false);
            if (!validation.IsValid)
                throw new InvalidOperationException("Invalid Terraform catalog inputs: " +
                    string.Join("; ", validation.Errors.Select(error => error.Message)));
            return effective;
        }

        // Retained as a standalone diagnostic artifact as well as in the
        // required source snapshot. Apply uses the snapshot's copy only.
        private void PersistLockFile(string workingDir, string lockFilePath)
        {
            // Defence-in-depth: reject `..` segments before composing paths.
            // Both inputs are platform-supplied today, but the rejection is
            // documented contract that matches DOrc's path-traversal posture.
            if (workingDir.Contains("..") || lockFilePath.Contains(".."))
            {
                throw new ArgumentException("paths must not contain parent-directory segments");
            }
            var source = Path.Join(workingDir, ".terraform.lock.hcl");
            if (!File.Exists(source))
            {
                logger.Warning(
                    $".terraform.lock.hcl not found in working dir; lock-file persistence skipped (no provider lock to record).");
                return;
            }
            var destDir = Path.GetDirectoryName(lockFilePath);
            if (!string.IsNullOrEmpty(destDir)) Directory.CreateDirectory(destDir);
            File.Copy(source, lockFilePath, true);
            logger.FileLogger.LogInformation("Persisted .terraform.lock.hcl to {LockFilePath}.", lockFilePath);
        }

        /// <summary>
        /// Creates the working directory and returns its path. Separate from provisioning so
        /// that the caller records the path before anything can be written into it, and can
        /// therefore delete it whatever provisioning goes on to do.
        /// </summary>
        protected virtual string CreateTerraformWorkingDirectory(int requestId)
        {
            var workingDir = Path.Join(
                DorcProgramData.Root,
                "terraform-workdir",
                $"{requestId}-terraform-{DateTime.UtcNow:yyyy-MM-dd-HH-mm-ss}");

            RestrictedWorkingDirectory.Create(workingDir);

            return workingDir;
        }

        private (string? SubscriptionId, bool FromEnvironment, string? Source) ConfigureOperation(
            ScriptGroup scriptGroup)
        {
            _usesPlatformBackend = TerraformStateConfiguration.Resolve(scriptGroup) is not null;

            // Per-environment subscription targeting, in precedence order:
            //  1. A 'Subscription' cloud resource attached to the environment
            //     (environment details > Cloud tab). The environment model
            //     itself names the subscription its deployments run against.
            //  2. The TerraformSubscriptionId environment property, kept for
            //     environments not yet carrying cloud resources.
            //  3. The runner's Terraform:DefaultSubscriptionId - a bootstrap
            //     fallback ONLY for fresh projects/environments not yet fully
            //     configured. With nothing set, the identity's default
            //     subscription applies.
            string? subscriptionId = null;
            string? subscriptionSource = null;
            var cloudTarget = TerraformCloudTargeting.ResolveSubscription(
                scriptGroup.CommonProperties, out var cloudTargetingWarning);
            if (cloudTargetingWarning is not null)
            {
                logger.Warning(cloudTargetingWarning);
            }
            if (cloudTarget is not null)
            {
                subscriptionId = cloudTarget.SubscriptionId;
                subscriptionSource =
                    $"the environment's '{cloudTarget.ResourceName}' subscription cloud resource";
            }
            if (subscriptionId is null
                && scriptGroup.CommonProperties is not null
                && scriptGroup.CommonProperties.TryGetValue(
                    TerraformProviderRenderer.SubscriptionIdPropertyName, out var subscriptionProperty))
            {
                subscriptionId = subscriptionProperty?.Value?.ToString();
                if (!string.IsNullOrWhiteSpace(subscriptionId))
                {
                    subscriptionSource = $"the '{TerraformProviderRenderer.SubscriptionIdPropertyName}' environment property";
                }
            }
            var subscriptionFromEnvironment = !string.IsNullOrWhiteSpace(subscriptionId);
            if (!subscriptionFromEnvironment)
            {
                subscriptionId = _defaultSubscriptionId;
            }

            // Per-environment identity: when the environment carries its own
            // service-principal credential properties, terraform authenticates
            // as that identity instead of the shared runner host identity.
            ApplyEnvironmentCredentials(scriptGroup, subscriptionId);
            _workloadSubscriptionId = subscriptionId;
            return (subscriptionId, subscriptionFromEnvironment, subscriptionSource);
        }

        private async Task ProvisionTerraformWorkingDirectoryAsync(
            string workingDir,
            ScriptGroup scriptGroup,
            CancellationToken cancellationToken)
        {
            var (subscriptionId, subscriptionFromEnvironment, subscriptionSource) = ConfigureOperation(scriptGroup);
            var backend = TerraformStateConfiguration.Resolve(scriptGroup);
            var provider = _codeSourceFactory.GetProvider(scriptGroup.TerraformSourceType);
            logger.Information($"Using Terraform source type: {scriptGroup.TerraformSourceType}");

            // Provision the code using the selected provider
            await provider.ProvisionCodeAsync(scriptGroup, workingDir, cancellationToken);

            // If a sub-path is specified, move only that directory to the root
            if (!string.IsNullOrEmpty(scriptGroup.TerraformSubPath))
            {
                await TerraformSourceSubPath.ApplyAsync(workingDir, scriptGroup.TerraformSubPath, cancellationToken);
                logger.FileLogger.LogInformation("Successfully extracted path {SubPath}.", scriptGroup.TerraformSubPath);
            }

            // "DOrc owns the backend" applies only when DOrc actually renders
            // one. When the platform state backend is completely configured
            // settings present) we reject user-checked-in backend blocks and
            // render _dorc_backend.tf in their place. On the legacy path (no
            // platform backend configured) a component's own backend block is
            // the only thing keeping its state remote across throwaway working
            // dirs, so rejecting it would break every pre-existing component.
            if (backend is not null)
            {
                // The validator throws with a precise error string identifying
                // the offending file when the engineer must remove the
                // declaration. It runs before the platform file is written, so
                // ANY backend declaration found here - including a file named
                // _dorc_backend.tf - is user content.
                TerraformBackendValidator.RejectIfUserBackendBlocksPresent(workingDir);

                TerraformBackendRenderer.WriteToWorkingDirectory(
                    workingDir,
                    backend);
                logger.FileLogger.LogInformation(
                    "Rendered platform backend with state key {StateKey}.", scriptGroup.TerraformStateKey);
            }

            // Catalog modules follow the module contract (no provider blocks;
            // the consuming root supplies them). DOrc runs the module AS the
            // root, so there is no consumer: render the provider configuration
            // the module cannot declare for itself or `terraform plan` fails
            // on azurerm's mandatory `features {}` block.
            if (scriptGroup.TerraformSourceType == TerraformSourceType.Catalog)
            {
                TerraformProviderRenderer.WriteAzureRmIfRequired(workingDir, subscriptionId);

                if (subscriptionFromEnvironment)
                {
                    logger.Information($"azurerm provider pinned to subscription '{subscriptionId!.Trim()}' from {subscriptionSource}.");
                }
                else if (!string.IsNullOrWhiteSpace(subscriptionId))
                {
                    logger.Warning($"Environment carries no '{TerraformCloudTargeting.SubscriptionResourceType}' cloud resource and no '{TerraformProviderRenderer.SubscriptionIdPropertyName}' property; falling back to the runner's default subscription '{subscriptionId.Trim()}' (Terraform:DefaultSubscriptionId). Attach a subscription cloud resource to target this environment's own subscription.");
                }
            }

            logger.Information($"Terraform working directory has been set up at: {workingDir}");
        }

        // Resolves per-environment service-principal credentials into the
        // ARM_* variables terraform processes for this operation run with.
        // The secret value is appended to the redaction list so it can never
        // appear in logged terraform error output, independent of whether the
        // property was flagged sensitive on the request.
        private void ApplyEnvironmentCredentials(ScriptGroup scriptGroup, string? subscriptionId)
        {
            var credentials = TerraformArmCredentials.Resolve(scriptGroup.CommonProperties);
            Dictionary<string, string> env;
            if (credentials is null)
            {
                env = string.IsNullOrWhiteSpace(subscriptionId)
                    ? new Dictionary<string, string>()
                    : new Dictionary<string, string> { ["ARM_SUBSCRIPTION_ID"] = subscriptionId.Trim() };
                logger.FileLogger.LogInformation(
                    "No per-environment Terraform credentials configured; terraform authenticates with the runner host's ambient Azure identity.");
            }
            else
            {
                env = new Dictionary<string, string>(credentials.ToArmEnvironment(subscriptionId));

                if (!_sensitiveValues.Contains(credentials.ClientSecret))
                {
                    _sensitiveValues = _sensitiveValues.Append(credentials.ClientSecret).ToList();
                }

                logger.Information(
                    $"Terraform authenticates as the environment's service principal (client ID '{credentials.ClientId}') from the " +
                    $"'{TerraformArmCredentials.ClientIdPropertyName}'/'{TerraformArmCredentials.ClientSecretPropertyName}'/'{TerraformArmCredentials.TenantIdPropertyName}' environment properties.");
            }

            // Optional Aiven API token for modules using the aiven provider;
            // orthogonal to the Azure credentials above.
            var aivenToken = TerraformArmCredentials.ResolveAivenApiToken(scriptGroup.CommonProperties);
            if (aivenToken is not null)
            {
                env["AIVEN_TOKEN"] = aivenToken;
                if (!_sensitiveValues.Contains(aivenToken))
                {
                    _sensitiveValues = _sensitiveValues.Append(aivenToken).ToList();
                }
                logger.Information(
                    $"Aiven API token from the '{TerraformArmCredentials.AivenApiTokenPropertyName}' environment property is injected as AIVEN_TOKEN on the terraform process.");
            }

            _armEnvironment = env;
        }

        private async Task<string> CreateTerraformPlanAsync(
            IDictionary<string, VariableValue> properties,
            string terraformWorkingDir,
            string resultFilePath,
            string planContentFilePath,
            int requestId,
            bool isDestroy,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                await RunTerraformCommandAsync(
                    terraformWorkingDir,
                    TerraformCommand.Init,
                    new[] { "init", "-no-color" },
                    cancellationToken);

                var (declaresTermProt, declaresSvcTermProt) =
                    await CreateTerraformVariablesFileAsync(terraformWorkingDir, properties, isDestroy, cancellationToken);

                var planArgList = new List<string> { "plan" };
                if (declaresTermProt)
                {
                    planArgList.Add("-var=termination_protection=false");
                }

                if (declaresSvcTermProt)
                {
                    planArgList.Add("-var=service_termination_protection=false");
                }

                if (isDestroy)
                {
                    planArgList.AddRange(new[] { "-destroy", "-input=false", "-no-color", $"-out={resultFilePath}", "-detailed-exitcode" });
                }
                else
                {
                    planArgList.AddRange(new[] { $"-out={resultFilePath}", "-detailed-exitcode", "-no-color" });
                }

                var planArgs = planArgList.ToArray();

                await RunTerraformCommandAsync(
                    terraformWorkingDir,
                    TerraformCommand.PlanDetailedExitCode,
                    planArgs,
                    cancellationToken);

                var rawPlanContent = await RunTerraformCommandAsync(
                    terraformWorkingDir,
                    TerraformCommand.Show,
                    new[] { "show", resultFilePath, "-no-color" },
                    cancellationToken);
                var planContent = RedactSensitiveValues(rawPlanContent);
                if (!String.IsNullOrEmpty(planContent))
                {
                    File.WriteAllText(planContentFilePath, planContent);
                }

                if (_isEphemeral &&
                    !isDestroy &&
                    Regex.IsMatch(
                        rawPlanContent,
                        @"\b(?:service_)?termination_protection\s*=\s*(?:\S+\s+->\s+)?(?:""true""|true\b|\(sensitive value\)|\(known after apply\))(?!\s*->)",
                        RegexOptions.IgnoreCase))
                {
                    throw new InvalidOperationException(
                        "Ephemeral Terraform plans must not enable termination_protection or service_termination_protection.");
                }

                logger.Information($"Terraform plan created successfully for request '{requestId}'");
                return planContent;
            }
            finally
            {
                logger.FlushLogMessages();
            }
        }

        private async Task<(bool DeclaresTerminationProtection, bool DeclaresServiceTerminationProtection)> CreateTerraformVariablesFileAsync(
            string workingDir,
            IDictionary<string, VariableValue> properties,
            bool isDestroy,
            CancellationToken cancellationToken)
        {
            var declaresTermProt = false;
            var declaresSvcTermProt = false;
            if (_isEphemeral)
            {
                foreach (var autoTfvars in Directory.EnumerateFiles(workingDir, "*.auto.tfvars*", SearchOption.TopDirectoryOnly)
                    .Concat(Directory.EnumerateFiles(workingDir, "terraform.tfvars.json", SearchOption.TopDirectoryOnly)))
                {
                    var content = await File.ReadAllTextAsync(autoTfvars, cancellationToken);
                    var scrubbed = Regex.Replace(
                        content,
                        @"(?<=\b(?:service_)?termination_protection""?\s*[:=]\s*)(?:""true""|true\b)",
                        "false",
                        RegexOptions.IgnoreCase);
                    await File.WriteAllTextAsync(autoTfvars, scrubbed, cancellationToken);
                }

                var allTfText = StripTfCommentsAndStrings(
                    string.Join("\n", Directory.EnumerateFiles(workingDir, "*.tf", SearchOption.TopDirectoryOnly)
                        .Select(File.ReadAllText)));
                var allModuleTfAndJson = StripTfCommentsAndStrings(
                    string.Join("\n", Directory.EnumerateFiles(workingDir, "*.tf", SearchOption.AllDirectories)
                        .Concat(Directory.EnumerateFiles(workingDir, "*.tf.json", SearchOption.AllDirectories))
                        .Where(path => !IsInsideDotTerraformDirectory(workingDir, path))
                        .Select(File.ReadAllText)));
                if (!isDestroy &&
                    Regex.IsMatch(
                        allModuleTfAndJson,
                        @"(?<!=)\b(?:service_)?termination_protection""?\s*[:=]\s*(?!=)(?:\s*(?<open>\())*\s*(?:""true""|true\b)(?:\s*(?<-open>\)))*(?(open)(?!))[ \t]*(?:[,}\r\n]|$)",
                        RegexOptions.IgnoreCase))
                {
                    throw new InvalidOperationException(
                        "Ephemeral Terraform plans must not hard-code termination_protection or service_termination_protection to true.");
                }
                declaresTermProt = _parameters.ContainsKey("termination_protection") ||
                                   Regex.IsMatch(allTfText, @"\bvariable\s+""termination_protection""\s*\{", RegexOptions.IgnoreCase);
                declaresSvcTermProt = _parameters.ContainsKey("service_termination_protection") ||
                                      Regex.IsMatch(allTfText, @"\bvariable\s+""service_termination_protection""\s*\{", RegexOptions.IgnoreCase);

                foreach (var tfJsonPath in Directory.EnumerateFiles(workingDir, "*.tf.json", SearchOption.TopDirectoryOnly))
                {
                    using var doc = JsonDocument.Parse(
                        await File.ReadAllTextAsync(tfJsonPath, cancellationToken),
                        new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
                    if (doc.RootElement.TryGetProperty("variable", out var variableRoot))
                    {
                        IEnumerable<JsonElement> variableBlocks = variableRoot.ValueKind == JsonValueKind.Array
                            ? variableRoot.EnumerateArray()
                            : new[] { variableRoot };
                        foreach (var block in variableBlocks)
                        {
                            foreach (var prop in block.EnumerateObject())
                            {
                                if (string.Equals(prop.Name, "termination_protection", StringComparison.OrdinalIgnoreCase))
                                {
                                    declaresTermProt = true;
                                }
                                else if (string.Equals(prop.Name, "service_termination_protection", StringComparison.OrdinalIgnoreCase))
                                {
                                    declaresSvcTermProt = true;
                                }
                            }
                        }
                    }
                }

                if (declaresTermProt)
                {
                    properties["termination_protection"] = new VariableValue { Value = "false", Type = typeof(bool) };
                }

                if (declaresSvcTermProt)
                {
                    properties["service_termination_protection"] = new VariableValue { Value = "false", Type = typeof(bool) };
                }
            }

            var variablesContent = new StringBuilder();

            foreach (var property in properties)
            {
                // Convert DOrc properties to Terraform variable format
                var value = Convert.ToString(property.Value.Value, CultureInfo.InvariantCulture) ?? "";

                _parameters.TryGetValue(property.Key, out var parameter);
                if (parameter?.Type == TerraformParameterType.Bool)
                {
                    if (!bool.TryParse(value, out var boolean))
                        throw new InvalidOperationException($"Terraform parameter '{property.Key}' must be a boolean.");
                    value = boolean ? "true" : "false";
                }
                else if (parameter?.Type == TerraformParameterType.Number)
                {
                    if (!ParameterValidator.TryParseNumberExact(value, out var number))
                        throw new InvalidOperationException($"Terraform parameter '{property.Key}' must be an invariant number.");
                    value = number.ToString(CultureInfo.InvariantCulture);
                }
                else if (parameter?.Type == TerraformParameterType.String || property.Value.Type == typeof(string))
                {
                    value = QuoteHclString(value);
                }
                else if (property.Value.Type == typeof(bool))
                {
                    value = value.ToLowerInvariant();
                }
                else
                {
                    value = Newtonsoft.Json.JsonConvert.SerializeObject(property.Value.Value);
                }

                // Only [a-zA-Z0-9_] symbols can be used in Terraform identifiers. Replace all others with '_'
                var propertyName = Regex.Replace(property.Key, "[^a-zA-Z0-9_]", "");

                variablesContent.AppendLine($"{propertyName} = {value}");
            }

            var variablesFilePath = Path.Combine(workingDir, "terraform.tfvars");
            await File.WriteAllTextAsync(variablesFilePath, variablesContent.ToString(), cancellationToken);
            return (declaresTermProt, declaresSvcTermProt);
        }

        private static bool IsInsideDotTerraformDirectory(string workingDir, string filePath)
        {
            var relativePath = Path.GetRelativePath(workingDir, filePath);
            return relativePath
                .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .Contains(".terraform", StringComparer.OrdinalIgnoreCase);
        }

        private static string StripTfCommentsAndStrings(string text)
        {
            return Regex.Replace(
                text,
                @"<<-?[ \t]*([\p{L}_][\p{L}\p{N}_-]*)\r?\n[\s\S]*?\r?\n[ \t]*\1(?=\r?\n|$)|""(?:\\.|[^""\\])*""|/\*[\s\S]*?\*/|(?://|#)[^\r\n]*",
                m => Regex.IsMatch(m.Value, @"^""(?:true|(?:service_)?termination_protection)""$", RegexOptions.IgnoreCase)
                    ? m.Value
                    : string.Empty);
        }

        internal static string QuoteHclString(string value)
        {
            var escaped = new StringBuilder("\"");
            foreach (var character in value)
            {
                escaped.Append(character switch
                {
                    '\\' => "\\\\",
                    '"' => "\\\"",
                    '\n' => "\\n",
                    '\r' => "\\r",
                    '\t' => "\\t",
                    < ' ' => $"\\u{(int)character:x4}",
                    _ => character.ToString()
                });
            }
            return escaped.ToString().Replace("${", "$${").Replace("%{", "%%{") + "\"";
        }

        protected virtual async Task<string> RunTerraformCommandAsync(
            string workingDir,
            TerraformCommand command,
            IReadOnlyList<string> commandArgs,
            CancellationToken cancellationToken)
        {
            using var process = new System.Diagnostics.Process();
            process.StartInfo.FileName = "terraform";
            // ArgumentList passes each argument as a discrete value; the runtime
            // applies platform-correct quoting. This eliminates the prior
            // "Arguments = string concatenation" injection surface where paths
            // with spaces or shell metacharacters could split into extra tokens.
            foreach (var arg in commandArgs) process.StartInfo.ArgumentList.Add(arg);
            // Per-environment identity: credentials resolved from environment
            // properties are injected into the terraform CHILD process only,
            // never the host environment, so concurrent deployments for other
            // environments cannot observe them.
            foreach (var variable in _armEnvironment)
            {
                process.StartInfo.Environment[variable.Key] = variable.Value;
            }
            if (_usesPlatformBackend)
            {
                // Host-level key/SAS credentials must not override the platform's
                // explicitly selected data-plane Azure AD authentication.
                process.StartInfo.Environment.Remove("ARM_ACCESS_KEY");
                process.StartInfo.Environment.Remove("ARM_SAS_TOKEN");
            }
            process.StartInfo.WorkingDirectory = workingDir;
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.RedirectStandardError = true;
            process.StartInfo.CreateNoWindow = true;

            var outputBuilder = new StringBuilder();
            var errorBuilder = new StringBuilder();

            process.OutputDataReceived += (_, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    outputBuilder.AppendLine(e.Data);
                }
            };

            process.ErrorDataReceived += (_, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    errorBuilder.AppendLine(e.Data);
                }
            };

            logger.Debug($"Running Terraform command: terraform {command}");

            try
            {
                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                try
                {
                    await process.WaitForExitAsync(cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    try
                    {
                        if (!process.HasExited)
                        {
                            // Kill the entire tree so any terraform-spawned
                            // provider plugins are also reaped.
                            process.Kill(entireProcessTree: true);
                        }
                        if (!process.WaitForExit(3000))
                            logger.Warning("Terraform did not exit within the cancellation cleanup deadline.");
                    }
                    catch (InvalidOperationException killEx)
                    {
                        // Process exited between HasExited check and Kill - benign.
                        logger.Debug($"Process exited before kill could fire: {killEx.Message}");
                    }
                    catch (Win32Exception killEx)
                    {
                        // OS-level kill failure (e.g. permissions). Log but do
                        // not re-throw so the cancellation flow proceeds.
                        logger.Warning($"OS-level kill failed for terraform process: {killEx.Message}");
                    }
                    logger.Warning($"Terraform command {command} was cancelled; process-tree termination requested.");
                    throw;
                }
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode == 2)
            {
                var errorMessage = "Terraform executable not found. Please ensure Terraform is installed and available in the system PATH.";
                logger.Error(errorMessage, ex);
                throw new InvalidOperationException(errorMessage, ex);
            }
            catch (Win32Exception ex)
            {
                var errorMessage = $"Failed to start Terraform process. Win32 error code: {ex.NativeErrorCode}. " +
                                   "Please ensure Terraform is properly installed and the user has necessary permissions.";
                logger.Error(errorMessage, ex);
                throw new InvalidOperationException(errorMessage, ex);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception e)
            {
                logger.Error($"Running of the Terraform process failed. Command: {command}, args: [{string.Join(" ", commandArgs)}] in {workingDir}", e);
                throw;
            }

            var output = outputBuilder.ToString();
            var error = errorBuilder.ToString();

            InterpretExitCode(command, process.ExitCode, error);

            // Command stdout is not logged. "terraform show" renders variable values, which
            // include the resolved deployment property set, so echoing output here wrote
            // secrets into the runner log - whose path is published on the deployment
            // request. The command and its outcome are enough for diagnosis; the plan itself
            // remains available through the API to callers authorised for the environment.
            logger.Information($"Terraform command {command} completed successfully ({output.Length} characters of output, not logged).");
            return output;
        }

        // Per-command exit-code semantics. Documented at  of the
        // terraform-hardening .
        //
        // - PlanDetailedExitCode: 0 = no changes, 1 = error, 2 = changes
        //   pending (success). Anything not 0/1/2 is also treated as an
        //   error so we don't silently accept unknown codes.
        // - Init / Apply / Show: any non-zero exit code is an error.
        private void InterpretExitCode(TerraformCommand command, int exitCode, string errorOutput)
        {
            if (command == TerraformCommand.PlanDetailedExitCode)
            {
                if (exitCode == 0 || exitCode == 2) return;
                var planMsg = $"terraform plan failed with exit code {exitCode}";
                logger.Error($"{planMsg}. Error: {RedactSensitiveValues(errorOutput)}");
                throw new InvalidOperationException(planMsg);
            }

            if (exitCode != 0)
            {
                var msg = $"terraform {command} failed with exit code {exitCode}";
                logger.Error($"{msg}. Error: {RedactSensitiveValues(errorOutput)}");
                throw new InvalidOperationException(msg);
            }
        }

        public async Task<bool> ExecuteConfirmedPlanAsync(
            string pipeName,
            int requestId,
            string planFile,
            string? lockFilePath,
            string? appliedResourcesFilePath,
            string? sourceArchiveFilePath,
            CancellationToken cancellationToken,
            bool isDestroy = false)
        {
            cancellationToken.ThrowIfCancellationRequested();

            ScriptGroup scriptGroupProperties = this._scriptGroupPipeClient.GetScriptGroupProperties(pipeName);
            var deployResultId = scriptGroupProperties.DeployResultId;

            this.logger.SetRequestId(requestId);
            this.logger.SetDeploymentResultId(deployResultId);

            logger.Information($"TerraformProcessor.ExecuteConfirmedPlan called for request' with id '{requestId}', deployment result id '{deployResultId}'.");

            // Execute the actual Terraform plan
            return await ExecuteTerraformPlanAsync(requestId, planFile, lockFilePath, appliedResourcesFilePath, sourceArchiveFilePath, scriptGroupProperties, isDestroy, cancellationToken);
        }

        private async Task<bool> ExecuteTerraformPlanAsync(
            int requestId,
            string planFile,
            string? lockFilePath,
            string? appliedResourcesFilePath,
            string? sourceArchiveFilePath,
            ScriptGroup scriptGroup,
            bool isDestroy,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var terraformWorkingDir = string.Empty;
            try
            {
                terraformWorkingDir = CreateTerraformWorkingDirectory(requestId);
                TerraformStateConfiguration.Resolve(scriptGroup);
                await CollectSensitiveValuesAsync(scriptGroup, cancellationToken);
                ConfigureOperation(scriptGroup);
                TerraformSourceArchive.RestorePlanSource(planFile, terraformWorkingDir, cancellationToken);
                _usesPlatformBackend |= File.Exists(Path.Join(terraformWorkingDir, TerraformBackendRenderer.BackendFileName));

                if (!string.IsNullOrWhiteSpace(sourceArchiveFilePath))
                {
                    if (sourceArchiveFilePath.Contains("..") || !Path.IsPathRooted(sourceArchiveFilePath))
                        throw new ArgumentException("Source-archive path must be absolute without parent-directory segments.");
                    // Stage the audit copy before changing infrastructure. Monitor
                    // publishes it only when apply succeeds, and always cleans it up.
                    File.Copy(TerraformSourceArchive.PlanArchivePath(planFile), sourceArchiveFilePath, true);
                }

                // The snapshot's lock file is authoritative. A separate lock
                // artifact must never overwrite the one approved with the plan.

                await RunTerraformCommandAsync(
                    terraformWorkingDir,
                    TerraformCommand.Init,
                    new[] { "init", "-no-color" },
                    cancellationToken);

                var applyArgs = isDestroy
                    ? new[] { "apply", "-input=false", "-no-color", "-auto-approve", planFile }
                    : new[] { "apply", "-auto-approve", planFile, "-no-color" };

                await RunTerraformCommandAsync(
                    terraformWorkingDir,
                    TerraformCommand.Apply,
                    applyArgs,
                    cancellationToken);

                logger.Information($"Terraform apply completed successfully for request ID: {requestId}");

                if (isDestroy)
                {
                    var hasConfiguredState = TerraformStateConfiguration.Resolve(scriptGroup) is not null;
                    var blobDeleted = await _stateBlobCleaner.DeleteStateBlobIfConfiguredAsync(
                        scriptGroup,
                        cancellationToken);
                    logger.Information(
                        $"Post-destroy state blob cleanup completed for request ID: {requestId} (blobDeleted={blobDeleted}).");
                    if (hasConfiguredState && !blobDeleted)
                    {
                        logger.Error(
                            $"Post-destroy state blob cleanup failed for request ID {requestId} (key '{scriptGroup.TerraformStateKey}').");
                        return false;
                    }
                }
                else
                {
                    await WriteAppliedResourcesAsync(terraformWorkingDir, appliedResourcesFilePath, scriptGroup.DeployResultId, cancellationToken);
                }

                cancellationToken.ThrowIfCancellationRequested();
                return true;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                logger.Error($"Terraform apply failed for request ID {requestId}: {RedactSensitiveValues(ex.Message)}");
                return false;
            }
            finally
            {
                DeleteTempTerraformFolder(terraformWorkingDir);
            }
        }

        // Captures what the apply left in state so the Monitor can register the
        // resources on the environment's Cloud tab. Best-effort by design: the
        // infrastructure change has already happened, so a failure here is
        // logged and swallowed rather than failing a completed deployment.
        private async Task WriteAppliedResourcesAsync(
            string terraformWorkingDir,
            string? appliedResourcesFilePath,
            int deploymentResultId,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(appliedResourcesFilePath)) return;

            // Defence-in-depth matching PersistLockFile: the path is
            // Monitor-composed today, but the rejection of parent-directory
            // segments and relative paths is documented contract.
            if (appliedResourcesFilePath.Contains("..") || !Path.IsPathRooted(appliedResourcesFilePath))
            {
                logger.Warning("Applied-resources path must be absolute without parent-directory segments; registration skipped.");
                return;
            }
            var canonicalAppliedResourcesPath = Path.GetFullPath(appliedResourcesFilePath);

            try
            {
                var stateJson = await RunTerraformCommandAsync(
                    terraformWorkingDir,
                    TerraformCommand.Show,
                    new[] { "show", "-json", "-no-color" },
                    cancellationToken);

                var complete = TerraformAppliedResources.TryParseCompleteShowJson(stateJson, out var resources);
                var safeResources = resources.Where(resource => !_sensitiveValues.Any(secret =>
                    !string.IsNullOrEmpty(secret) &&
                    (resource.ResourceIdentifier.Contains(secret, StringComparison.Ordinal) ||
                     resource.Name.Contains(secret, StringComparison.Ordinal)))).ToArray();
                complete &= safeResources.Length == resources.Count;
                resources = safeResources;

                File.WriteAllText(
                    canonicalAppliedResourcesPath,
                    JsonSerializer.Serialize(new TerraformInventorySnapshot
                    {
                        DeploymentResultId = deploymentResultId, IsComplete = complete, Resources = resources.ToList()
                    }));

                logger.Information(
                    $"Recorded {resources.Count} applied cloud resource{(resources.Count == 1 ? "" : "s")} for environment registration.");
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                logger.Warning(
                    $"Could not record the applied cloud resources: {RedactSensitiveValues(ex.Message)}. The deployment itself succeeded; the environment's Cloud tab will not be updated for this run.");
            }
        }

        // Preserves what was planned: the working directory's terraform
        // configuration (variable files/history/provider binaries/state excluded)
        // plus a provenance record pinning the module reference and the exact
        // git commit the code came from. This must succeed before approval:
        // apply restores this exact source and publishes these exact bytes.
        private void WriteSourceArchive(
            string terraformWorkingDir,
            string? sourceArchiveFilePath,
            ScriptGroup scriptGroup,
            int requestId,
            string resultFilePath,
            bool isDestroy,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(sourceArchiveFilePath)) return;

            // Defence-in-depth matching PersistLockFile / WriteAppliedResourcesAsync:
            // the path is Monitor-composed today, but the rejection of
            // parent-directory segments and relative paths is documented contract.
            if (sourceArchiveFilePath.Contains("..") || !Path.IsPathRooted(sourceArchiveFilePath))
            {
                throw new ArgumentException("Source-archive path must be absolute without parent-directory segments.");
            }
            var canonicalArchivePath = Path.GetFullPath(sourceArchiveFilePath);
            var planSha256 = File.Exists(resultFilePath)
                ? Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(resultFilePath))).ToLowerInvariant()
                : string.Empty;

            var provenance = new
            {
                RequestId = requestId,
                DeploymentResultId = scriptGroup.DeployResultId,
                Operation = isDestroy ? "Destroy" : "Plan",
                PlanSha256 = planSha256,
                DefinitionRevision = scriptGroup.DefinitionRevision,
                BuildIdentity = scriptGroup.SourceBuildIdentity,
                SourceType = scriptGroup.TerraformSourceType.ToString(),
                TemplateName = scriptGroup.TerraformTemplateName,
                TemplateVersion = scriptGroup.TerraformTemplateVersion,
                // Catalog resolution records the real clone target, not user input.
                GitRepoUrl = scriptGroup.TerraformGitRepoUrl,
                GitRef = scriptGroup.TerraformGitBranch,
                GitCommitSha = scriptGroup.TerraformResolvedGitSha,
                SubPath = scriptGroup.TerraformSubPath,
                scriptGroup.AzureOrganization,
                scriptGroup.AzureProjects,
                scriptGroup.AzureBuildId,
                scriptGroup.GitHubOwner,
                scriptGroup.GitHubRepo,
                scriptGroup.GitHubRunId,
                scriptGroup.GitHubApiBaseUrl,
                StateStorageAccount = scriptGroup.TerraformStateStorageAccount,
                StateContainerName = scriptGroup.TerraformStateContainerName,
                StateKey = scriptGroup.TerraformStateKey,
                StateSubscriptionId = scriptGroup.TerraformStateSubscriptionId,
                WorkloadTarget = TerraformWorkloadTarget.Describe(_manifest, scriptGroup.CommonProperties,
                    _workloadSubscriptionId, name =>
                        (scriptGroup.SensitivePropertyNames?.Contains(name, StringComparer.OrdinalIgnoreCase) ?? false) ||
                        scriptGroup.CommonProperties.TryGetValue(name, out var value) &&
                        _sensitiveValues.Contains(value?.Value?.ToString() ?? string.Empty)),
                ArchivedAtUtc = DateTime.UtcNow,
            };

            var entryCount = TerraformSourceArchive.Create(
                terraformWorkingDir,
                canonicalArchivePath,
                JsonSerializer.Serialize(provenance, new JsonSerializerOptions { WriteIndented = true }),
                cancellationToken: cancellationToken);

            logger.Information(
                $"Archived the planned terraform source ({entryCount} entries) " +
                $"for module '{scriptGroup.TerraformTemplateName ?? "(ad-hoc)"}@{scriptGroup.TerraformTemplateVersion ?? "-"}' " +
                $"at commit {scriptGroup.TerraformResolvedGitSha ?? "(unknown)"}.");
        }

        private void DeleteTempTerraformFolder(string folderPath)
        {
            if (String.IsNullOrEmpty(folderPath) || !Directory.Exists(folderPath))
                return;

            try
            {
                ResilientDirectoryDeletion.Delete(folderPath);
            }
            // Invoked from a finally block. A directory that will not delete is worth
            // recording - it is deployment properties left on disk - but it must not displace
            // the exception that caused the deployment to fail. ResilientDirectoryDeletion has
            // already retried and wrapped whatever it could not delete in an IOException.
            catch (IOException ex) { LogUndeletedWorkingDirectory(ex, folderPath); }
            catch (UnauthorizedAccessException ex) { LogUndeletedWorkingDirectory(ex, folderPath); }
        }

        private void LogUndeletedWorkingDirectory(Exception ex, string folderPath) =>
            logger.Error(ex, $"Failed to remove the Terraform working directory '{folderPath}': {ex.Message}");
    }
}
