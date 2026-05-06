using Dorc.ApiModel;
using Dorc.Core.BuildServer;
using Dorc.Core.Interfaces;
using Dorc.Core.Models;
using Dorc.PersistentData.Sources.Interfaces;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace Dorc.Core
{
    public class RequestsManager : IRequestsManager
    {
        private readonly ILogger _logger;
        private readonly IProjectsPersistentSource _projectsPersistentSource;
        private readonly IComponentsPersistentSource _componentsPersistentSource;
        private readonly IEnvironmentsPersistentSource _environmentsPersistentSource;
        private readonly IBuildServerClientFactory _buildServerClientFactory;

        public RequestsManager(ILoggerFactory loggerFactory,
            IProjectsPersistentSource projectsPersistentSource, IComponentsPersistentSource componentsPersistentSource,
            IEnvironmentsPersistentSource environmentsPersistentSource,
            IBuildServerClientFactory buildServerClientFactory)
        {
            _environmentsPersistentSource = environmentsPersistentSource;
            _componentsPersistentSource = componentsPersistentSource;
            _projectsPersistentSource = projectsPersistentSource;
            _logger = loggerFactory.CreateLogger<RequestsManager>();
            _buildServerClientFactory = buildServerClientFactory;
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
                        IsEnabled = true
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
                        IsEnabled = true,
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
                var folderUri = new Uri(project.ArtefactsUrl);
                var folder = folderUri.LocalPath;
                var builds = Directory.EnumerateDirectories(folder)
                    .Select(f => new DeployableArtefact
                    {
                        Id = f,
                        Name = Path.GetFileName(f),
                        Date = File.GetLastWriteTime(f)
                    });
                result.AddRange(builds);

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred in GetFolderBuilds");

                throw;
            }
        }

        public async Task<List<DeploymentRequestDetail>> BundleRequestDetailAsync(CreateRequest createRequest)
        {
            var project = _projectsPersistentSource.GetProject(createRequest.Project);
            if (project == null)
                throw new InvalidOperationException($"Project '{createRequest.Project}' not found.");

            var result = new List<DeploymentRequestDetail>();
            var bundleJson = new StreamReader(createRequest.BuildUrl).ReadToEnd();
            var bundle = JsonSerializer.Deserialize<BuildBundle>(bundleJson);

            if (bundle == null)
            {
                return result;
            }

            if (IsBuildServerProject(project))
            {
                var buildClient = _buildServerClientFactory.Create(project.SourceControlType);

                foreach (var buildItem in bundle.Items.GroupBy(i => i.Build))
                {
                    var buildDefinitionName = project.SourceControlType == SourceControlType.GitHub
                        ? buildItem.First().BuildDefinition
                        : createRequest.BuildDefinitionName.Split(';')[0].Trim() + "; " + buildItem.First().BuildDefinition;

                    var builds = await buildClient.GetBuildsAsync(project.ArtefactsUrl,
                        project.ArtefactsSubPaths, project.ArtefactsBuildRegex,
                        buildDefinitionName, false);

                    var matchedBuild = builds.FirstOrDefault(b =>
                        (b.Name ?? "").Replace(" [PINNED]", "").Equals(buildItem.Key));

                    // Fail fast with a clear message when the bundle references a build the
                    // CI/CD server no longer surfaces. Passing a null BuildUrl downstream
                    // produced a confusing "Unknown build type" or null-deref far from the
                    // root cause; surfacing it here points the operator at the real problem.
                    if (matchedBuild == null)
                        throw new InvalidOperationException(
                            $"Build '{buildItem.Key}' (definition '{buildDefinitionName}') was not " +
                            $"found in {project.SourceControlType} for project '{createRequest.Project}'. " +
                            "The build may have been deleted, retention-purged, or the bundle " +
                            "may have been created against a different project/source-control type.");

                    var request = new CreateRequest
                    {
                        BuildUrl = matchedBuild.Id,
                        BuildDefinitionName = buildDefinitionName,
                        Environment = createRequest.Environment,
                        Project = createRequest.Project,
                        Properties = createRequest.Properties,
                        Components = new List<string>()
                    };
                    foreach (var item in buildItem) request.Components.Add(item.Component);

                    var detail = RequestDetail(request);
                    result.Add(detail);
                }
            }
            else if (IsFileShareProject(project))
            {
                foreach (var buildItem in bundle.Items.GroupBy(i => i.Build))
                {
                    var request = new CreateRequest
                    {
                        Project = createRequest.Project,
                        Environment = createRequest.Environment,
                        BuildUrl = createRequest.BuildUrl.Replace("bundle.json", ""),
                        Components = new List<string>()
                    };
                    foreach (var item in buildItem) request.Components.Add(item.Component);
                    var detail = RequestDetail(request);
                    result.Add(detail);
                }
            }

            return result;
        }

        public DeploymentRequestDetail RequestDetail(CreateRequest createRequest)
        {
            var project = _projectsPersistentSource.GetProject(createRequest.Project);
            if (project == null)
                throw new InvalidOperationException($"Project '{createRequest.Project}' not found.");

            var buildDetail = new BuildDetail();
            if (IsBuildServerProject(project) && !string.IsNullOrEmpty(project.ArtefactsSubPaths))
            {
                buildDetail = BuildServerDetailAsync(createRequest, project).ConfigureAwait(false).GetAwaiter().GetResult();
            }
            else if (IsFileShareProject(project))
                buildDetail = ShareDetail(createRequest);
            else
                buildDetail.DropLocation = createRequest.DropFolder;

            var componentNames = new List<string>();

            foreach (var componentName in createRequest.Components)
            {
                var component = _componentsPersistentSource.GetComponentByName(componentName);
                if (component == null) continue;

                AddComponent(componentNames, component);
            }

            var requestDetail =
                new DeploymentRequestDetail
                {
                    EnvironmentName = createRequest.Environment,
                    Components = componentNames,
                    BuildDetail = buildDetail
                };

            if (createRequest.Properties == null)
                return requestDetail;

            foreach (var requestProperty in createRequest.Properties)
                requestDetail.Properties.Add(new PropertyPair(requestProperty.PropertyName,
                    requestProperty.PropertyValue));
            return requestDetail;
        }

        private async Task<BuildDetail> BuildServerDetailAsync(CreateRequest createRequest, ProjectApiModel project)
        {
            var buildClient = _buildServerClientFactory.Create(project.SourceControlType);

            // Validate the build exists before fetching artifact URLs to fail fast
            var buildInfo = await buildClient.ValidateBuildAsync(
                project.ArtefactsUrl, project.ArtefactsSubPaths, project.ArtefactsBuildRegex,
                createRequest.BuildDefinitionName, null, createRequest.BuildUrl, false);

            if (buildInfo == null)
            {
                throw new InvalidOperationException(
                    $"Unable to validate build '{createRequest.BuildDefinitionName}' with URL '{createRequest.BuildUrl}'.");
            }

            var artifactDownloadUrl = await buildClient.GetBuildArtifactDownloadUrlAsync(
                project.ArtefactsUrl, project.ArtefactsSubPaths, project.ArtefactsBuildRegex,
                createRequest.BuildDefinitionName, createRequest.BuildUrl);

            return new BuildDetail
            {
                DropLocation = artifactDownloadUrl,
                Project = project.ProjectName,
                BuildNumber = buildInfo.BuildNumber,
                Uri = buildInfo.BuildUri,
                BuildId = buildInfo.BuildId
            };
        }

        private BuildDetail ShareDetail(CreateRequest createRequest)
        {
            var buildDetail = new BuildDetail { DropLocation = new Uri(createRequest.BuildUrl).LocalPath };
            buildDetail.BuildNumber = Path.GetFileName(buildDetail.DropLocation.TrimEnd('\\'));
            return buildDetail;
        }

        private static void AddComponent(ICollection<string> componentNames, ComponentApiModel component)
        {
            if (!string.IsNullOrEmpty(component.ScriptPath) && !componentNames.Contains(component.ComponentName))
                componentNames.Add(component.ComponentName);

            foreach (var child in component.Children) AddComponent(componentNames, child);
        }

        private static bool IsFileBasedUrl(string? url)
            => !string.IsNullOrEmpty(url) && (url.StartsWith("file") || url.StartsWith(@"\\"));

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
    }
}
