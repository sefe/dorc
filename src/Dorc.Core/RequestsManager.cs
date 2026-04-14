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

                if (!project.ArtefactsUrl.StartsWith("http"))
                    return
                        new List<DeployableArtefact> { new() { Id = project.ArtefactsUrl, Name = "Not a CI/CD Server Project" } };

                var buildClient = _buildServerClientFactory.Create(project.SourceControlType);
                return buildClient.GetBuildDefinitions(project.ArtefactsUrl,
                    project.ArtefactsSubPaths, project.ArtefactsBuildRegex);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred in GetBuildDefinitions");

                throw;
            }
        }

        public async Task<IEnumerable<DeployableArtefact>> GetBuildsAsync(int? projectId, string environment, string buildDefinitionName, CancellationToken cancellationToken = default)
        {
            try
            {
                List<DeployableArtefact> output;

                if (projectId == null) return Enumerable.Empty<DeployableArtefact>();

                if (string.IsNullOrEmpty(buildDefinitionName)) return Enumerable.Empty<DeployableArtefact>();

                var project = _projectsPersistentSource.GetProject(projectId.Value);

                if (project.ArtefactsUrl.StartsWith("http"))
                {
                    var filterOnlyPinned = !string.IsNullOrEmpty(environment) &&
                                           (_environmentsPersistentSource.EnvironmentIsProd(environment) ||
                                            _environmentsPersistentSource.EnvironmentIsSecure(environment));

                    var buildClient = _buildServerClientFactory.Create(project.SourceControlType);
                    var builds = await buildClient.GetBuildsAsync(project.ArtefactsUrl,
                        project.ArtefactsSubPaths, project.ArtefactsBuildRegex,
                        buildDefinitionName, filterOnlyPinned, cancellationToken);
                    output = builds.ToList();
                }
                else if (project.ArtefactsUrl.StartsWith("file"))
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

        public async Task<List<DeploymentRequestDetail>> BundleRequestDetailAsync(CreateRequest createRequest, CancellationToken cancellationToken = default)
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

            if (project.ArtefactsUrl.StartsWith("http"))
            {
                var buildClient = _buildServerClientFactory.Create(project.SourceControlType);

                foreach (var buildItem in bundle.Items.GroupBy(i => i.Build))
                {
                    var buildDefinitionName = project.SourceControlType == SourceControlType.GitHub
                        ? buildItem.First().BuildDefinition
                        : createRequest.BuildDefinitionName.Split(';')[0].Trim() + "; " + buildItem.First().BuildDefinition;

                    var builds = await buildClient.GetBuildsAsync(project.ArtefactsUrl,
                        project.ArtefactsSubPaths, project.ArtefactsBuildRegex,
                        buildDefinitionName, false, cancellationToken);

                    var matchedBuild = builds.FirstOrDefault(b =>
                        (b.Name ?? "").Replace(" [PINNED]", "").Equals(buildItem.Key));

                    var request = new CreateRequest
                    {
                        BuildUrl = matchedBuild?.Id,
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
            else if (project.ArtefactsUrl.StartsWith("file"))
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
            if (!string.IsNullOrEmpty(project.ArtefactsUrl) && project.ArtefactsUrl.StartsWith("http") &&
                !string.IsNullOrEmpty(project.ArtefactsSubPaths))
            {
                buildDetail = BuildServerDetailAsync(createRequest, project).ConfigureAwait(false).GetAwaiter().GetResult();
            }
            else if (!string.IsNullOrEmpty(project.ArtefactsUrl) && project.ArtefactsUrl.StartsWith("file"))
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

        private async Task<BuildDetail> BuildServerDetailAsync(CreateRequest createRequest, ProjectApiModel project, CancellationToken cancellationToken = default)
        {
            var buildClient = _buildServerClientFactory.Create(project.SourceControlType);

            // Validate the build exists before fetching artifact URLs to fail fast
            var buildInfo = await buildClient.ValidateBuildAsync(
                project.ArtefactsUrl, project.ArtefactsSubPaths, project.ArtefactsBuildRegex,
                createRequest.BuildDefinitionName, null, createRequest.BuildUrl, false, cancellationToken);

            if (buildInfo == null)
            {
                throw new InvalidOperationException(
                    $"Unable to validate build '{createRequest.BuildDefinitionName}' with URL '{createRequest.BuildUrl}'.");
            }

            var artifactDownloadUrl = await buildClient.GetBuildArtifactDownloadUrlAsync(
                project.ArtefactsUrl, project.ArtefactsSubPaths, project.ArtefactsBuildRegex,
                createRequest.BuildDefinitionName, createRequest.BuildUrl, cancellationToken);

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
    }
}
