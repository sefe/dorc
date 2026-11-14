using Dorc.ApiModel;
using Dorc.ApiModel.MonitorRunnerApi;
using Dorc.Core;
using Dorc.Core.BuildServer;
using Dorc.Core.Configuration;
using Dorc.Core.Security;
using Dorc.Monitor.Security;
using Dorc.PersistentData.Security;
using Microsoft.Extensions.Logging;
using Dorc.AzureDevOps.Client.Auth;

namespace Dorc.Monitor.TerraformSourceConfig
{
    /// <summary>
    /// Configures ScriptGroup with Terraform source-specific information
    /// </summary>
    public class TerraformSourceConfigurator
    {
        private const string TerraformGitPatPropertyName = "Terraform_Git_PAT";
        
        private readonly ILogger _logger;
        private readonly IConfigurationSettings _configurationSettings;
        private readonly IGitHubHostValidator _gitHubHostValidator;

        private readonly ISourceHostAllowList _sourceHosts;
        private readonly Func<string>? _azureBearerTokenProvider;

        public TerraformSourceConfigurator(ILogger logger, IConfigurationSettings configurationSettings,
            IGitHubHostValidator gitHubHostValidator,
            ISourceHostAllowList sourceHosts)
            : this(logger, configurationSettings, gitHubHostValidator, sourceHosts, null)
        {
        }

        internal TerraformSourceConfigurator(
            ILogger logger,
            IConfigurationSettings configurationSettings,
            IGitHubHostValidator gitHubHostValidator,
            ISourceHostAllowList sourceHosts,
            Func<string>? azureBearerTokenProvider)
        {
            _logger = logger;
            _configurationSettings = configurationSettings;
            _gitHubHostValidator = gitHubHostValidator;
            _sourceHosts = sourceHosts ?? throw new ArgumentNullException(nameof(sourceHosts));
            _azureBearerTokenProvider = azureBearerTokenProvider;
        }

        public void ConfigureScriptGroup(
            ScriptGroup scriptGroup,
            ComponentApiModel component,
            DeploymentRequestApiModel request,
            ProjectApiModel? project,
            IDictionary<string, VariableValue> properties,
            bool sharedFolderConfinementAlreadyChecked = false,
            string? configuredScriptRoot = null)
        {
            scriptGroup.TerraformSourceType = component.TerraformSourceType;
            scriptGroup.TerraformGitBranch = component.TerraformGitBranch ?? "main";
            scriptGroup.TerraformSubPath = component.TerraformSubPath ?? string.Empty;
            scriptGroup.TerraformTemplateName = component.TerraformTemplateName ?? string.Empty;
            scriptGroup.TerraformTemplateVersion = component.TerraformTemplateVersion ?? string.Empty;

            switch (component.TerraformSourceType)
            {
                case TerraformSourceType.Git:
                    ConfigureGitSource(scriptGroup, project, properties);
                    break;

                case TerraformSourceType.AzureArtifact:
                    ConfigureAzureArtifactSource(scriptGroup, request, project);
                    break;

                case TerraformSourceType.GitHubArtifact:
                    ConfigureGitHubArtifactSource(scriptGroup, request, project);
                    break;

                case TerraformSourceType.SharedFolder:
                    ConfigureSharedFolderSource(
                        scriptGroup,
                        component,
                        project,
                        properties,
                        sharedFolderConfinementAlreadyChecked,
                        configuredScriptRoot);
                    break;

                case TerraformSourceType.Catalog:
                    ConfigureCatalogSource(scriptGroup, properties);
                    break;

                default:
                    _logger.LogWarning($"Unknown Terraform source type: {component.TerraformSourceType}");
                    break;
            }
        }

        private void ConfigureGitSource(
            ScriptGroup scriptGroup,
            ProjectApiModel? project,
            IDictionary<string, VariableValue> properties)
        {
            var repositoryUrl = project?.TerraformGitRepoUrl ?? string.Empty;

            RequireMayReceiveSourceCredentials(repositoryUrl);
            RequireHttpsSourceUrl(repositoryUrl, "Terraform Git");

            scriptGroup.TerraformGitRepoUrl = repositoryUrl;
            scriptGroup.AllowedTerraformSourceHosts = _sourceHosts.TerraformSourceHosts?.ToList() ?? new List<string>();

            if (IsAzureDevOpsRepository(repositoryUrl))
            {
                scriptGroup.AzureBearerToken = GetAzureBearerToken();
            }

            // Get PAT token from environment properties
            if (properties.TryGetValue(TerraformGitPatPropertyName, out var patValue))
            {
                scriptGroup.TerraformGitPat = patValue.Value?.ToString() ?? string.Empty;
            }
            else
            {
                _logger.LogWarning("Required PAT property is missing for Terraform Git source.");
            }
        }

        private void ConfigureCatalogSource(
            ScriptGroup scriptGroup,
            IDictionary<string, VariableValue> properties)
        {
            if (_sourceHosts.IsTerraformSourceUnconfigured)
            {
                var message =
                    "Refusing Terraform Catalog source because no source host allow-list is configured"
                    + $" ('{SourceHostAllowList.TerraformHostsSetting}').";
                _logger.LogError(message);
                throw new InvalidOperationException(message);
            }

            var allowedHosts = _sourceHosts.TerraformSourceHosts?.ToList() ?? new List<string>();
            scriptGroup.AllowedTerraformSourceHosts = allowedHosts;

            if (properties.TryGetValue(TerraformGitPatPropertyName, out var catalogPat))
            {
                scriptGroup.TerraformGitPat = catalogPat.Value?.ToString() ?? string.Empty;
            }

            // Only mint and forward DOrc's Entra token when at least one allowed Terraform
            // source host is an Azure DevOps authority. The runner's CatalogReferenceCodeSourceProvider
            // also checks the resolved manifest's Source.Locator before presenting any credential.
            if (allowedHosts.Any(host => IsAzureDevOpsRepository("https://" + host.Trim().TrimStart('.'))))
            {
                scriptGroup.AzureBearerToken = GetAzureBearerToken();
            }
        }

        private void ConfigureSharedFolderSource(
            ScriptGroup scriptGroup,
            ComponentApiModel component,
            ProjectApiModel? project,
            IDictionary<string, VariableValue> properties,
            bool sharedFolderConfinementAlreadyChecked,
            string? configuredScriptRoot = null)
        {
            if (SourceHost.IsRemoteOrUnc(component.ScriptPath))
            {
                RequireMayReceiveSourceCredentials(component.ScriptPath);
                scriptGroup.AllowedTerraformSourceHosts = _sourceHosts.TerraformSourceHosts?.ToList() ?? new List<string>();
            }

            if (!sharedFolderConfinementAlreadyChecked
                && !VerifySharedFolderConfinement(component, project, properties, configuredScriptRoot))
            {
                throw new InvalidOperationException(
                    $"Refusing Terraform SharedFolder script path '{LogText.SingleLine(component.ScriptPath)}' because it lies outside the allowed roots.");
            }
        }

        /// <summary>
        /// S-017 path confinement for Terraform SharedFolder definitions only.
        /// Git, AzureArtifact, GitHubArtifact and Catalog definitions do not provision code
        /// from a stored filesystem script path and are explicitly excluded.
        ///
        /// Validates the literal stored <see cref="ComponentApiModel.ScriptPath"/> directly,
        /// because <c>TerraformDispatcher</c> passes it unexpanded as <c>ScriptsLocation</c>
        /// and <c>SharedFolderCodeSourceProvider</c> never expands property tokens.
        /// </summary>
        public bool VerifySharedFolderConfinement(
            ComponentApiModel component,
            ProjectApiModel? project,
            IDictionary<string, VariableValue>? properties,
            string? configuredScriptRoot = null)
        {
            if (component.TerraformSourceType != TerraformSourceType.SharedFolder)
            {
                return true;
            }

            var enforcing = _configurationSettings.GetScriptPathEnforcementEnabled();
            var allowedRoots = BuildAllowedSharedFolderRoots(project, properties, configuredScriptRoot);
            var storedPath = component.ScriptPath;

            if (string.IsNullOrWhiteSpace(storedPath))
            {
                ReportSharedFolderConfinement(
                    enforcing,
                    storedPath,
                    allowedRoots,
                    "the Terraform SharedFolder script path is empty.");
                return !enforcing;
            }

            if (PropertyTokenExpander.ContainsUnexpandedDropFolderToken(storedPath))
            {
                ReportSharedFolderConfinement(
                    enforcing,
                    storedPath,
                    allowedRoots,
                    "it references an unexpanded DropFolder token that cannot be shown to resolve beneath an allowed root.");
                return !enforcing;
            }

            if (HasDotDotSegment(storedPath))
            {
                ReportSharedFolderConfinement(
                    enforcing,
                    storedPath,
                    allowedRoots,
                    "it contains a parent directory reference ('..'), which can resolve outside the allowed roots.");
                return !enforcing;
            }

            if (allowedRoots.Count == 0)
            {
                ReportSharedFolderConfinement(
                    enforcing,
                    storedPath,
                    allowedRoots,
                    "no allowed filesystem roots (ArtefactsSubPaths, ArtefactsUrl, or ScriptRoot) are configured.");
                return !enforcing;
            }

            if (!PathConfinement.IsWithinAny(storedPath, allowedRoots))
            {
                ReportSharedFolderConfinement(
                    enforcing,
                    storedPath,
                    allowedRoots,
                    "the SharedFolder path does not lie beneath any allowed root.");
                return !enforcing;
            }

            if (!string.IsNullOrWhiteSpace(component.TerraformSubPath))
            {
                var subPathDecision = ScriptPathConfinement.Check(component.TerraformSubPath);
                if (!subPathDecision.Allowed)
                {
                    ReportSharedFolderConfinement(
                        enforcing,
                        component.TerraformSubPath,
                        allowedRoots,
                        subPathDecision.Reason);
                    return !enforcing;
                }
            }

            return true;
        }

        private static IReadOnlyList<string> BuildAllowedSharedFolderRoots(
            ProjectApiModel? project,
            IDictionary<string, VariableValue>? properties,
            string? configuredScriptRoot)
        {
            var scriptRoot = configuredScriptRoot != null
                ? configuredScriptRoot
                : properties != null && properties.TryGetValue(PropertyValueScopeOptionsFixed.ScriptRoot, out var sr)
                    ? sr?.Value?.ToString()
                    : null;

            var canonicalScriptRoot = PathConfinement.Canonicalise(scriptRoot) != null ? scriptRoot! : null;
            var urlRoots = PathConfinement.SplitRoots(project?.ArtefactsUrl)
                .Where(r => PathConfinement.Canonicalise(r) != null)
                .ToList();
            var subEntries = PathConfinement.SplitRoots(project?.ArtefactsSubPaths).ToList();

            var anchoredSubRoots = subEntries
                .Where(s => PathConfinement.Canonicalise(s) != null)
                .ToList();
            var relativeSubPaths = subEntries
                .Where(s => PathConfinement.Canonicalise(s) == null && ScriptPathConfinement.Check(s).Allowed)
                .ToList();

            var allowedRoots = new List<string>();
            allowedRoots.AddRange(anchoredSubRoots);

            // Relative ArtefactsSubPaths narrow filesystem ArtefactsUrl roots only. When a
            // project uses an HTTPS ArtefactsUrl (e.g. Azure DevOps or GitHub) with relative
            // ArtefactsSubPaths, ScriptRoot remains an independent estate root and must not be
            // narrowed by subpaths intended for ArtefactsUrl.
            if (urlRoots.Count > 0)
            {
                if (relativeSubPaths.Count > 0)
                {
                    foreach (var urlRoot in urlRoots)
                    {
                        foreach (var rel in relativeSubPaths)
                        {
                            allowedRoots.Add(PropertyTokenExpander.ResolveAgainstRoot(urlRoot, rel));
                        }
                    }
                }
                else
                {
                    allowedRoots.AddRange(urlRoots);
                }
            }

            if (canonicalScriptRoot != null)
            {
                allowedRoots.Add(canonicalScriptRoot);
            }

            return allowedRoots
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static bool HasDotDotSegment(string path)
        {
            return path.Split('\\', '/').Any(segment => segment.Trim() == "..");
        }

        private void ReportSharedFolderConfinement(
            bool enforcing,
            string? scriptPath,
            IReadOnlyList<string> allowedRoots,
            string reason)
        {
            var safeScriptPath = LogText.SingleLine(scriptPath);
            var safeRoots = LogText.SingleLine(string.Join("; ", allowedRoots));
            var safeReason = LogText.SingleLine(reason);

            if (enforcing)
            {
                _logger.LogError(
                    "Refusing Terraform SharedFolder path '{ScriptPath}' against allowed roots '{AllowedRoots}', because"
                    + " {Reason} Register the folder beneath an allowed root, or correct the component's stored path.",
                    safeScriptPath, safeRoots, safeReason);
            }
            else
            {
                _logger.LogWarning(
                    "Terraform SharedFolder path '{ScriptPath}' would be refused once confinement is enforced,"
                    + " because {Reason} Executing it because 'EnforceScriptPathConfinement' is not enabled."
                    + " Allowed roots: '{AllowedRoots}'.",
                    safeScriptPath, safeReason, safeRoots);
            }
        }

        /// <summary>
        /// Whether this repository may be offered the deployment's Git credentials at all.
        ///
        /// The repository URL is project configuration, settable at per-project modify rights.
        /// Both credentials it can attract are estate-wide: the Terraform PAT, and an Entra
        /// access token issued to DOrc's own application registration. Neither is scoped to a
        /// particular repository, so pointing a project at a host of one's choosing collected
        /// them both.
        ///
        /// A refusal throws rather than silently leaving the source unset: the deployment is
        /// going to fail either way, and failing here puts the policy reason in front of the
        /// operator instead of a later "repository URL is not configured" from the runner.
        /// </summary>
        private void RequireMayReceiveSourceCredentials(string repositoryUrl)
        {
            if (_sourceHosts.IsTerraformSourceUnconfigured)
            {
                var message =
                    "Refusing Terraform Git source because no source host allow-list is configured"
                    + $" ('{SourceHostAllowList.TerraformHostsSetting}').";
                _logger.LogError(message);
                throw new InvalidOperationException(message);
            }

            var decision = _sourceHosts.CheckTerraformSource(repositoryUrl);
            if (decision != null && decision.Allowed)
            {
                return;
            }

            var refusal =
                $"Withholding Git credentials from '{LogText.SingleLine(repositoryUrl)}',"
                + $" because {LogText.SingleLine(decision?.Reason ?? "host is not permitted")}";
            _logger.LogError(refusal);
            throw new InvalidOperationException(refusal);
        }

        private void RequireMayReceiveArtefactCredentials(string artefactUrl, string sourceLabel)
        {
            if (_sourceHosts.IsArtefactSourceUnconfigured)
            {
                var message =
                    $"Refusing Terraform {sourceLabel} source because no artefact host allow-list is configured"
                    + $" ('{SourceHostAllowList.ArtefactHostsSetting}').";
                _logger.LogError(message);
                throw new InvalidOperationException(message);
            }

            var decision = _sourceHosts.CheckArtefactSource(artefactUrl);
            if (decision != null && decision.Allowed)
            {
                return;
            }

            var refusal =
                $"Withholding {sourceLabel} credentials from '{LogText.SingleLine(artefactUrl)}',"
                + $" because {LogText.SingleLine(decision?.Reason ?? "host is not permitted")}";
            _logger.LogError(refusal);
            throw new InvalidOperationException(refusal);
        }

        private void RequireHttpsSourceUrl(string url, string sourceLabel)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
                !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            {
                var message =
                    $"Refusing {sourceLabel} source '{LogText.SingleLine(url)}': an https:// URL is required before attaching credentials.";
                _logger.LogError(message);
                throw new InvalidOperationException(message);
            }
        }

        /// <summary>
        /// Compared on the parsed host, not by substring. A test for "dev.azure.com" anywhere in
        /// the URL is satisfied by https://dev.azure.com.attacker.net/repo.git, which then
        /// received an Entra access token issued to DOrc.
        ///
        /// visualstudio.com is matched on its suffix because organisations live at
        /// myorg.visualstudio.com; that is a suffix of the authority, not of the URL text, so it
        /// cannot be satisfied by an attacker's host that merely ends in the same characters
        /// somewhere in a path.
        /// </summary>
        internal static bool IsAzureDevOpsRepository(string repositoryUrl)
        {
            var host = SourceHost.Of(repositoryUrl);

            if (host == null)
            {
                return false;
            }

            return string.Equals(host, "dev.azure.com", StringComparison.OrdinalIgnoreCase)
                || string.Equals(host, "visualstudio.com", StringComparison.OrdinalIgnoreCase)
                || host.EndsWith(".visualstudio.com", StringComparison.OrdinalIgnoreCase);
        }

        internal static string? ExtractAzureDevOpsOrganization(string artefactsUrl)
        {
            if (!IsAzureDevOpsRepository(artefactsUrl) ||
                !Uri.TryCreate(artefactsUrl, UriKind.Absolute, out var uri) ||
                !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            if (string.Equals(uri.Host, "dev.azure.com", StringComparison.OrdinalIgnoreCase))
            {
                var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
                return segments.Length > 0 ? segments[0] : null;
            }

            if (uri.Host.EndsWith(".visualstudio.com", StringComparison.OrdinalIgnoreCase))
            {
                var suffixLength = ".visualstudio.com".Length;
                var prefix = uri.Host[..^suffixLength];
                var firstDot = prefix.IndexOf('.');
                var org = firstDot >= 0 ? prefix[..firstDot] : prefix;
                return string.IsNullOrWhiteSpace(org) ? null : org;
            }

            return null;
        }

        private void ConfigureAzureArtifactSource(
            ScriptGroup scriptGroup,
            DeploymentRequestApiModel request,
            ProjectApiModel? project)
        {
            var artefactsUrl = project?.ArtefactsUrl ?? string.Empty;
            RequireMayReceiveArtefactCredentials(artefactsUrl, "Azure DevOps artifact");

            var isFileDrop = !string.IsNullOrEmpty(request.DropLocation)
                && request.DropLocation.StartsWith("file://", StringComparison.OrdinalIgnoreCase);

            if (isFileDrop)
            {
                if (SourceHost.IsRemoteOrUnc(request.DropLocation))
                {
                    RequireMayReceiveArtefactCredentials(request.DropLocation, "Azure DevOps file-share drop");
                }

                scriptGroup.ScriptsLocation = request.DropLocation;
                scriptGroup.AllowedArtefactHosts = _sourceHosts.ArtefactHosts?.ToList() ?? new List<string>();
                return;
            }

            RequireHttpsSourceUrl(artefactsUrl, "Azure DevOps artifact");
            var organization = ExtractAzureDevOpsOrganization(artefactsUrl);
            if (string.IsNullOrEmpty(organization))
            {
                var message =
                    $"Withholding Azure DevOps artifact credentials from '{LogText.SingleLine(artefactsUrl)}': URL is not a recognised Azure DevOps organisation URL.";
                _logger.LogError(message);
                throw new InvalidOperationException(message);
            }

            // For Azure artifacts, use existing build information from the request
            if (!string.IsNullOrEmpty(request.BuildUri))
            {
                // Extract build ID from BuildUri with proper validation
                try
                {
                    var uri = new Uri(request.BuildUri);
                    scriptGroup.AzureBuildId = uri.LocalPath.Split('/').Last();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, $"Failed to parse BuildUri: {request.BuildUri}");
                }
            }

            scriptGroup.ScriptsLocation = request.DropLocation;
            scriptGroup.AzureProjects = project!.ArtefactsSubPaths;
            scriptGroup.AzureOrganization = organization;
            scriptGroup.AllowedArtefactHosts = _sourceHosts.ArtefactHosts?.ToList() ?? new List<string>();
            scriptGroup.AzureBearerToken = GetAzureBearerToken();
        }

        private void ConfigureGitHubArtifactSource(
            ScriptGroup scriptGroup,
            DeploymentRequestApiModel request,
            ProjectApiModel? project)
        {
            var artefactsUrl = project?.ArtefactsUrl ?? string.Empty;
            RequireMayReceiveArtefactCredentials(artefactsUrl, "GitHub artifact");
            RequireHttpsSourceUrl(artefactsUrl, "GitHub artifact");

            // Derive API base URL with host validation BEFORE attaching GitHubToken.
            // Never swallow validation failures: leaving GitHubApiBaseUrl blank would cause
            // the runner to default to https://api.github.com with the estate GitHub token attached.
            var apiBaseUrl = _gitHubHostValidator.GetApiBase(artefactsUrl);

            var uri = new Uri(artefactsUrl);
            var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            var reposIndex = Array.IndexOf(segments, "repos");
            string owner;
            string repo;
            if (reposIndex >= 0 && reposIndex + 2 < segments.Length)
            {
                owner = segments[reposIndex + 1];
                repo = segments[reposIndex + 2];
            }
            else if (reposIndex < 0 && segments.Length >= 2 && !string.Equals(segments[0], "api", StringComparison.OrdinalIgnoreCase))
            {
                owner = segments[0];
                repo = segments[1].EndsWith(".git", StringComparison.OrdinalIgnoreCase)
                    ? segments[1][..^4]
                    : segments[1];
            }
            else
            {
                throw new InvalidOperationException(
                    $"Cannot extract GitHub owner/repository from ArtefactsUrl '{LogText.SingleLine(artefactsUrl)}'.");
            }

            scriptGroup.GitHubOwner = owner;
            scriptGroup.GitHubRepo = repo;
            scriptGroup.GitHubApiBaseUrl = apiBaseUrl;
            scriptGroup.AllowedArtefactHosts = _sourceHosts.ArtefactHosts?.ToList() ?? new List<string>();

            // BuildUri contains the run ID for GitHub Actions
            if (!string.IsNullOrEmpty(request.BuildUri))
            {
                scriptGroup.GitHubRunId = request.BuildUri;
            }

            // Get GitHub token from configuration
            var gitHubToken = _configurationSettings.GetGitHubToken();
            if (!string.IsNullOrEmpty(gitHubToken))
            {
                scriptGroup.GitHubToken = gitHubToken;
            }
            else
            {
                _logger.LogWarning("GitHub token not configured. Set 'GitHubToken' in AppSettings to download GitHub Actions artifacts.");
            }
        }

        private string GetAzureBearerToken()
        {
            if (_azureBearerTokenProvider != null)
            {
                return _azureBearerTokenProvider();
            }

            var tenant = _configurationSettings.GetAzureEntraTenantId();
            var clientId = _configurationSettings.GetAzureEntraClientId();
            var secret = _configurationSettings.GetAzureEntraClientSecret();
            
            try
            {
                var aadConnectionSettings = new AadConnectionSettings(
                    clientId, new string[] { }, secret, tenant);
                var authTokenGenerator = AuthTokenGeneratorFactory
                    .GetAuthTokenGenerator(aadConnectionSettings);
                return authTokenGenerator.GetToken();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get Azure bearer token");
                return string.Empty;
            }
        }
    }
}
