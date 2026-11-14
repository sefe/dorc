using Dorc.ApiModel;
using Dorc.Runner.Logger;
using Dorc.Terraform.Catalog;
using System.Text.RegularExpressions;

namespace Dorc.TerraformRunner.CodeSources
{
    // runtime: resolves a stock-template reference (catalog name +
    // version) into the manifest's Git source and delegates the actual
    // working-directory population to GitCodeSourceProvider. The catalog is
    // resolved via Dorc.Terraform.Catalog.ITemplateCatalog backed by
    // GitTemplateCatalog (filesystem-backed YAML manifests).
    public class CatalogReferenceCodeSourceProvider : ITerraformCodeSourceProvider
    {
        private readonly IRunnerLogger _logger;
        private readonly ITemplateCatalog _catalog;
        private readonly GitCodeSourceProvider _gitProvider;

        public CatalogReferenceCodeSourceProvider(IRunnerLogger logger, ITemplateCatalog catalog)
        {
            _logger = logger;
            _catalog = catalog;
            _gitProvider = new GitCodeSourceProvider(logger);
        }

        public TerraformSourceType SourceType => TerraformSourceType.Catalog;

        public async Task ProvisionCodeAsync(
            ScriptGroup scriptGroup,
            string workingDir,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(scriptGroup.TerraformTemplateName))
            {
                throw new InvalidOperationException(
                    "Catalog source mode requires TerraformTemplateName to be set on the component.");
            }
            if (string.IsNullOrEmpty(scriptGroup.TerraformTemplateVersion))
            {
                throw new InvalidOperationException(
                    "Catalog source mode requires TerraformTemplateVersion to be set on the component.");
            }

            var manifest = await _catalog.GetAsync(
                scriptGroup.TerraformTemplateName,
                scriptGroup.TerraformTemplateVersion,
                cancellationToken).ConfigureAwait(false);

            if (manifest is null)
            {
                throw new InvalidOperationException(
                    $"Stock template '{scriptGroup.TerraformTemplateName}@{scriptGroup.TerraformTemplateVersion}' " +
                    "was not found in the catalog. Check the spelling, the version, and the catalog manifests directory.");
            }
            if (manifest.Deprecated)
            {
                _logger.Warning(
                    $"Stock template '{manifest.Name}@{manifest.Version}' is deprecated. " +
                    $"Reason: {manifest.DeprecationReason}");
            }

            // Today's catalog only ships Git-sourced manifests; if a future
            // manifest carries a different source kind we must extend this
            // dispatch with the appropriate provider.
            if (!string.Equals(manifest.Source.Kind, "git", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Stock template '{manifest.Name}@{manifest.Version}' has an unsupported source kind '{manifest.Source.Kind}'. " +
                    "Only 'git' source kind is supported by the runner today.");
            }

            // The ref names the release tag and commit_sha pins its contents. The catalog
            // rejects ref-less manifests at load time; this guard is defence in
            // depth so a manifest that slips through any other path fails
            // loudly instead of silently deploying the clone default branch (a
            // moving target) under a fixed recorded version.
            if (string.IsNullOrWhiteSpace(manifest.Source.Ref))
            {
                throw new InvalidOperationException(
                    $"Stock template '{manifest.Name}@{manifest.Version}' has no source ref; " +
                    "a tag or commit pinning the module version is required. Fix the manifest.");
            }
            if (!Regex.IsMatch(manifest.Source.CommitSha ?? string.Empty, @"\A[0-9a-fA-F]{40}\z"))
                throw new InvalidOperationException(
                    $"Stock template '{manifest.Name}@{manifest.Version}' requires a full source commit_sha.");

            // Enforce host confinement on the catalog manifest's underlying source locator
            // before mutating TerraformGitRepoUrl or presenting any credential via GitCodeSourceProvider.
            EnforceCatalogSourceHostConfinement(manifest.Source.Locator, scriptGroup);

            // Set the Git fields on the script group so GitCodeSourceProvider
            // sees an unambiguous Git clone target. The original Catalog mode
            // values are preserved on scriptGroup for downstream consumers.
            scriptGroup.TerraformGitRepoUrl = manifest.Source.Locator;
            scriptGroup.TerraformGitBranch = manifest.Source.Ref; // explicitly resolved as a tag below
            // Sub-path precedence: explicit scriptGroup.TerraformSubPath > manifest
            // SubPath > stock-modules/<name> convention. The first level is enforced
            // by the if-wrapper here; the latter two are encoded in ResolveSubPath.
            if (string.IsNullOrEmpty(scriptGroup.TerraformSubPath))
            {
                scriptGroup.TerraformSubPath = ResolveSubPath(manifest);
            }

            _logger.Information(
                $"Resolved catalog reference '{manifest.Name}@{manifest.Version}' to " +
                $"git {manifest.Source.Locator} ref {manifest.Source.Ref}; delegating fetch to GitCodeSourceProvider.");

            await _gitProvider.ProvisionCodeAsync(
                scriptGroup, workingDir, cancellationToken, manifest.Source.CommitSha).ConfigureAwait(false);
        }

        // Adjudicates between the manifest's optional Source.SubPath and the
        // stock-modules/<name> layout convention. The third precedence level
        // (an explicit scriptGroup.TerraformSubPath overriding everything)
        // lives at the call site above. Public for unit-testability the
        // helper is a pure function of the manifest with no leak risk.
        public static string ResolveSubPath(TerraformTemplateManifest manifest)
        {
            return string.IsNullOrEmpty(manifest.Source.SubPath)
                ? $"stock-modules/{manifest.Name}"
                : manifest.Source.SubPath!;
        }

        internal static void EnforceCatalogSourceHostConfinement(string locator, ScriptGroup scriptGroup)
        {
            if (!SourceHost.IsRemoteOrUnc(locator))
            {
                // Local disk repository (no remote/UNC authority): never forward credentials,
                // even when AllowedTerraformSourceHosts is populated for remote templates.
                scriptGroup.TerraformGitPat = string.Empty;
                scriptGroup.AzureBearerToken = string.Empty;
                return;
            }

            var allowedHosts = scriptGroup.AllowedTerraformSourceHosts;
            if (allowedHosts == null || allowedHosts.Count == 0)
            {
                scriptGroup.TerraformGitPat = string.Empty;
                scriptGroup.AzureBearerToken = string.Empty;
                throw new InvalidOperationException(
                    $"Refusing to provision remote catalog template from '{locator}' because no Terraform source host allow-list was provided.");
            }

            if (!SourceHost.TryExtractRemoteHost(locator, out var locatorHost) ||
                !IsPermittedExactHost(locatorHost, allowedHosts))
            {
                scriptGroup.TerraformGitPat = string.Empty;
                scriptGroup.AzureBearerToken = string.Empty;
                throw new InvalidOperationException(
                    $"Refusing to provision catalog template from '{locator}': host '{locatorHost ?? "<none>"}' is not in the configured Terraform source host allow-list.");
            }

            if (!Uri.TryCreate(locator, UriKind.Absolute, out var locatorUri) ||
                !string.Equals(locatorUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            {
                scriptGroup.TerraformGitPat = string.Empty;
                scriptGroup.AzureBearerToken = string.Empty;
                throw new InvalidOperationException(
                    $"Refusing to present credentials to catalog template source '{locator}': HTTPS is required.");
            }

            var isAzureDevOps = SourceHost.Is(locator, "dev.azure.com")
                || SourceHost.Is(locator, "visualstudio.com");
            if (!isAzureDevOps)
            {
                scriptGroup.AzureBearerToken = string.Empty;
            }
        }

        private static bool IsPermittedExactHost(string locatorHost, IList<string> allowedHosts)
        {
            for (var i = 0; i < allowedHosts.Count; i++)
            {
                var entry = allowedHosts[i]?.Trim();
                if (!string.IsNullOrEmpty(entry) &&
                    string.Equals(locatorHost, entry, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
