using System.Text.Json;
using Dorc.ApiModel;
using Dorc.Core.BuildServer;
using Dorc.Core.Interfaces;
using Dorc.Core.Models;
using Dorc.PersistentData.Security;
using Dorc.PersistentData.Sources.Interfaces;
using Microsoft.Extensions.Logging;

namespace Dorc.Core
{
    public class RequestsManager : IRequestsManager
    {
        private readonly ILogger _logger;
        private readonly IProjectsPersistentSource _projectsPersistentSource;
        private readonly IComponentsPersistentSource _componentsPersistentSource;
        private readonly IEnvironmentsPersistentSource _environmentsPersistentSource;
        private readonly IBuildServerClientFactory _buildServerClientFactory;
        private readonly ISourceHostAllowList _sourceHosts;

        public RequestsManager(ILoggerFactory loggerFactory,
            IProjectsPersistentSource projectsPersistentSource, IComponentsPersistentSource componentsPersistentSource,
            IEnvironmentsPersistentSource environmentsPersistentSource,
            IBuildServerClientFactory buildServerClientFactory,
            ISourceHostAllowList sourceHosts)
        {
            _environmentsPersistentSource = environmentsPersistentSource;
            _componentsPersistentSource = componentsPersistentSource;
            _projectsPersistentSource = projectsPersistentSource;
            _logger = loggerFactory.CreateLogger<RequestsManager>();
            _buildServerClientFactory = buildServerClientFactory;
            _sourceHosts = sourceHosts;
        }

        public IEnumerable<DeployableComponent> GetComponents(int? projectId, int? parentId)
        {
            try
            {
                if (projectId == null) return new List<DeployableComponent>();

                var components = _projectsPersistentSource.GetComponentsForProject(projectId.Value);
                components = !parentId.HasValue
                    ? components.Where(c => c.ParentId == 0)
                    : components.Where(c => c.ParentId != 0 && c.ParentId == parentId);

                return components
                    .OrderBy(c => c.ComponentName)
                    .Select(c => new DeployableComponent
                    {
                        Name = c.ComponentName,
                        Id = c.ComponentId ?? 0,
                        NumOfChildren = c.Children.Count,
                        IsEnabled = c.IsEnabled
                    });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred in GetComponents");

                throw;
            }
        }

        public IEnumerable<DeployableComponent> GetComponents(int? projectId)
        {
            try
            {
                if (projectId == null) return new List<DeployableComponent>();

                var components = _projectsPersistentSource.GetComponentsForProject(projectId.Value);

                return components
                    .OrderBy(c => c.ComponentName)
                    .Select(c => new DeployableComponent
                    {
                        Name = c.ComponentName,
                        Id = c.ComponentId ?? 0,
                        NumOfChildren = c.Children.Count,
                        IsEnabled = c.IsEnabled,
                        ParentId = c.ParentId
                    });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred in GetComponents");

                throw;
            }
        }

        public IEnumerable<DeployableArtefact> GetBuildDefinitions(ProjectApiModel project)
        {
            try
            {
                if (project == null)
                    return new List<DeployableArtefact>();

                if (!IsBuildServerProject(project))
                    return
                        new List<DeployableArtefact> { new() { Id = project.ArtefactsUrl, Name = "Not a CI/CD Server Project" } };

                var buildClient = _buildServerClientFactory.Create(project.SourceControlType);
                return buildClient.GetDefinitions(project.ArtefactsUrl,
                    project.ArtefactsSubPaths, project.ArtefactsBuildRegex);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred in GetBuildDefinitions");

                throw;
            }
        }

        public async Task<IEnumerable<DeployableArtefact>> GetBuildsAsync(int? projectId, string environment, string buildDefinitionName)
        {
            try
            {
                List<DeployableArtefact> output;

                if (projectId == null) return Enumerable.Empty<DeployableArtefact>();

                if (string.IsNullOrEmpty(buildDefinitionName)) return Enumerable.Empty<DeployableArtefact>();

                var project = _projectsPersistentSource.GetProject(projectId.Value);
                if (project == null)
                    return Enumerable.Empty<DeployableArtefact>();

                if (IsBuildServerProject(project))
                {
                    var filterOnlyPinned = !string.IsNullOrEmpty(environment) &&
                                           (_environmentsPersistentSource.EnvironmentIsProd(environment) ||
                                            _environmentsPersistentSource.EnvironmentIsSecure(environment));

                    var buildClient = _buildServerClientFactory.Create(project.SourceControlType);
                    var builds = await buildClient.GetBuildsAsync(project.ArtefactsUrl,
                        project.ArtefactsSubPaths, project.ArtefactsBuildRegex,
                        buildDefinitionName, filterOnlyPinned);
                    output = builds.ToList();
                }
                else if (IsFileShareProject(project))
                    output = GetFolderBuilds(project).ToList();
                else
                    output = new List<DeployableArtefact>();

                return output.OrderByDescending(b => b.Date);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred in GetBuildsAsync");

                throw;
            }
        }

        private IEnumerable<DeployableArtefact> GetFolderBuilds(ProjectApiModel project)
        {
            try
            {
                var result = new List<DeployableArtefact>();
                foreach (var rawPath in Security.PathConfinement.SplitRoots(project.ArtefactsUrl))
                {
                    RequirePermittedRemoteArtefactPath(rawPath, "artefact share");

                    var normalizedPath = SourceHost.NormalizeExtendedUnc(rawPath);
                    var folder = Uri.TryCreate(normalizedPath, UriKind.Absolute, out var folderUri) && folderUri.IsFile
                        ? folderUri.LocalPath
                        : normalizedPath;

                    var builds = Directory.EnumerateDirectories(folder)
                        .Select(f => new DeployableArtefact
                        {
                            Id = f,
                            Name = Path.GetFileName(f),
                            Date = File.GetLastWriteTime(f)
                        });
                    result.AddRange(builds);
                }

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred in GetFolderBuilds");

                throw;
            }
        }

        private static bool IsFileBasedUrl(string? url)
            => !string.IsNullOrEmpty(url)
               && !url.StartsWith("http", StringComparison.OrdinalIgnoreCase)
               && !url.StartsWith("dorc-gh://", StringComparison.OrdinalIgnoreCase)
               && (url.StartsWith("file", StringComparison.OrdinalIgnoreCase) || SourceHost.IsRemoteOrUnc(url));

        // Project classification routes by SourceControlType primarily, falling back to URL shape
        // only when the type is the legacy AzureDevOps default (0). Routing by URL alone would
        // mis-classify a FileShare project with an accidentally http:// ArtefactsUrl and crash
        // inside the factory (which has no build-server client for FileShare).
        private static bool IsBuildServerProject(ProjectApiModel project)
        {
            if (project.SourceControlType == SourceControlType.GitHub)
                return true;
            if (project.SourceControlType == SourceControlType.FileShare)
                return false;
            // AzureDevOps (the default value) — original URL-shape gate preserved for backwards
            // compatibility with unmigrated projects whose URL might be http or file://.
            return !string.IsNullOrEmpty(project.ArtefactsUrl) &&
                   project.ArtefactsUrl.StartsWith("http");
        }

        private static bool IsFileShareProject(ProjectApiModel project)
        {
            if (project.SourceControlType == SourceControlType.FileShare)
                return true;
            if (project.SourceControlType == SourceControlType.GitHub)
                return false;
            // AzureDevOps default — fall back to URL shape for unmigrated rows.
            return IsFileBasedUrl(project.ArtefactsUrl);
        }

        private static readonly JsonSerializerOptions BundleJsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        public async Task<IReadOnlyList<string>> GetBundleComponentsAsync(int projectId, string? buildDefinitionName, string buildUrl)
        {
            if (string.IsNullOrWhiteSpace(buildUrl))
                return Array.Empty<string>();

            var project = _projectsPersistentSource.GetProject(projectId);
            if (project == null)
                return Array.Empty<string>();

            var isPotentialBundle =
                (!string.IsNullOrEmpty(buildDefinitionName) &&
                 buildDefinitionName.Contains("bundle", StringComparison.OrdinalIgnoreCase)) ||
                buildUrl.EndsWith("bundle.json", StringComparison.OrdinalIgnoreCase);

            if (!isPotentialBundle)
                return Array.Empty<string>();

            string? bundleJson = null;

            if (IsBuildServerProject(project))
            {
                if (string.IsNullOrWhiteSpace(buildDefinitionName))
                    return Array.Empty<string>();

                var buildClient = _buildServerClientFactory.Create(project.SourceControlType);
                var dropLocation = await buildClient.GetBuildArtifactDownloadUrlAsync(
                    project.ArtefactsUrl ?? string.Empty,
                    project.ArtefactsSubPaths ?? string.Empty,
                    project.ArtefactsBuildRegex ?? string.Empty,
                    buildDefinitionName,
                    buildUrl).ConfigureAwait(false);

                if (string.IsNullOrWhiteSpace(dropLocation))
                    return Array.Empty<string>();

                bundleJson = await buildClient.TryReadArtifactFileTextAsync(
                        project.ArtefactsUrl ?? string.Empty,
                        dropLocation,
                        "bundle.json")
                    .ConfigureAwait(false);
            }
            else if (IsFileShareProject(project))
            {
                var permittedRoots = Security.PathConfinement.SplitRoots(project.ArtefactsUrl).ToList();
                if (SourceHost.IsRemoteOrUnc(buildUrl))
                {
                    RequirePermittedRemoteArtefactPath(buildUrl, "bundle build URL");
                }

                var normalizedBuildUrl = SourceHost.NormalizeExtendedUnc(buildUrl);
                var localPath = normalizedBuildUrl.StartsWith("file:", StringComparison.OrdinalIgnoreCase) &&
                                Uri.TryCreate(normalizedBuildUrl, UriKind.Absolute, out var fileUri)
                    ? fileUri.LocalPath
                    : normalizedBuildUrl;

                if (permittedRoots.Count == 0 || !Security.PathConfinement.IsWithinAny(localPath, permittedRoots))
                    return Array.Empty<string>();

                var canonicalDrop = Security.PathConfinement.Canonicalise(localPath);
                if (string.IsNullOrWhiteSpace(canonicalDrop) || canonicalDrop.Contains("..", StringComparison.Ordinal))
                    return Array.Empty<string>();

                RequirePermittedRemoteArtefactPath(canonicalDrop, "bundle drop folder");

                var dirPath = canonicalDrop.EndsWith(@"\bundle.json", StringComparison.OrdinalIgnoreCase)
                    ? Path.GetDirectoryName(canonicalDrop)
                    : canonicalDrop;

                if (!string.IsNullOrWhiteSpace(dirPath))
                {
                    var dirInfo = new DirectoryInfo(dirPath);
                    if (dirInfo.Exists)
                    {
                        var bundleFile = dirInfo.EnumerateFiles("bundle.json", SearchOption.TopDirectoryOnly).FirstOrDefault();
                        if (bundleFile != null)
                        {
                            using var reader = bundleFile.OpenText();
                            bundleJson = await reader.ReadToEndAsync().ConfigureAwait(false);
                        }
                    }
                }
            }

            if (string.IsNullOrWhiteSpace(bundleJson))
                return Array.Empty<string>();

            var bundle = JsonSerializer.Deserialize<BuildBundle>(bundleJson, BundleJsonOptions);
            if (bundle?.Items == null || !bundle.Items.Any())
                return Array.Empty<string>();

            var projectComponents = _projectsPersistentSource.GetComponentsForProject(projectId)
                .Select(c => c.ComponentName)
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var bundleComponents = bundle.Items
                .Select(i => i.Component?.Trim() ?? string.Empty)
                .Where(c => !string.IsNullOrWhiteSpace(c))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var missingComponents = bundleComponents
                .Where(c => !projectComponents.Contains(c))
                .ToList();

            if (missingComponents.Count > 0)
            {
                throw new InvalidOperationException(
                    $"Bundle validation failed: the following component(s) in bundle.json do not exist in project '{project.ProjectName}': " +
                    string.Join(", ", missingComponents.Select(c => $"'{c}'")));
            }

            return bundleComponents;
        }

        private void RequirePermittedRemoteArtefactPath(string? path, string context)
        {
            if (!SourceHost.IsRemoteOrUnc(path))
            {
                return;
            }

            if (!SourceHost.TryExtractRemoteHost(path, out _))
            {
                throw new InvalidOperationException(
                    $"Refusing to access remote {context} '{path}': remote or UNC authority could not be verified.");
            }

            if (_sourceHosts.IsArtefactSourceUnconfigured)
            {
                throw new InvalidOperationException(
                    $"Refusing to present default Windows credentials to remote {context} '{path}' because no artefact host allow-list is configured ('{SourceHostAllowList.ArtefactHostsSetting}').");
            }

            var decision = _sourceHosts.CheckArtefactSource(path);
            if (decision == null || !decision.Allowed)
            {
                throw new InvalidOperationException(
                    $"Refusing to present default Windows credentials to remote {context} '{path}', because {decision?.Reason ?? "host is not permitted"}");
            }
        }
    }
}
