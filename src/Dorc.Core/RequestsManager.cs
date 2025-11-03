using Dorc.ApiModel;
using Dorc.Core.AzureDevOpsServer;
using Dorc.Core.Interfaces;
using Dorc.Core.Models;
using Dorc.PersistentData.Sources.Interfaces;
using log4net;
using Org.OpenAPITools.Model;
using System.Text.Json;

namespace Dorc.Core
{
    public class RequestsManager : IRequestsManager
    {
        private readonly ILog _logger;
        private readonly IProjectsPersistentSource _projectsPersistentSource;
        private readonly IComponentsPersistentSource _componentsPersistentSource;
        private readonly IEnvironmentsPersistentSource _environmentsPersistentSource;

        public RequestsManager(ILog logger,
            IProjectsPersistentSource projectsPersistentSource, IComponentsPersistentSource componentsPersistentSource,
            IEnvironmentsPersistentSource environmentsPersistentSource)
        {
            _environmentsPersistentSource = environmentsPersistentSource;
            _componentsPersistentSource = componentsPersistentSource;
            _projectsPersistentSource = projectsPersistentSource;
            _logger = logger;
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
                _logger.Error("An error occurred in GetComponents", ex);

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
                _logger.Error("An error occurred in GetComponents", ex);

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
                        new List<DeployableArtefact> { new() { Id = project.ArtefactsUrl, Name = "Not an Azure DevOps Server Project" } };

                var tfsRestClient = new AzureDevOpsServerWebClient(project.ArtefactsUrl, _logger);

                var output = tfsRestClient.GetBuildDefinitionsForProjects(project.ArtefactsUrl,
                        project.ArtefactsSubPaths, project.ArtefactsBuildRegex);

                var uiOutput = output.Select(i => new DeployableArtefact { Id = i.Id.ToString(), Name = i.Project.Name + "; " + i.Name });

                return uiOutput.OrderBy(d => d.Name);

            }
            catch (Exception ex)
            {
                _logger.Error("An error occurred in GetBuildDefinitions", ex);

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

                if (project.ArtefactsUrl.StartsWith("http"))
                {
                    // Remove the Azure DevOps Server Project Name from the Build Definition Name
                    var azureDevOpsBuildDefinitionAndBuildName = buildDefinitionName.Split(';');
                    var azureDevOpsProjectName = azureDevOpsBuildDefinitionAndBuildName[0].Trim();
                    var azureDevOpsBuildDefinitionName = azureDevOpsBuildDefinitionAndBuildName[1].Trim();

                    var tfsRestClient = new AzureDevOpsServerWebClient(project.ArtefactsUrl, _logger);

                    var buildDefsForProject = tfsRestClient.GetBuildDefinitionsForProjects(project.ArtefactsUrl,
                            project.ArtefactsSubPaths, project.ArtefactsBuildRegex);

                    var buildDefinitionReference = buildDefsForProject.First(def =>
                        def.Name.Equals(azureDevOpsBuildDefinitionName) && def.Project.Name.Equals(azureDevOpsProjectName));

                    var buildsFromDefinitionsAsync = await tfsRestClient.GetBuildsFromDefinitionsAsync(project.ArtefactsUrl,
                        new List<BuildDefinitionReference> { buildDefinitionReference }).ConfigureAwait(false);

                    var filterOnlyPinned = !string.IsNullOrEmpty(environment) &&
                                           (_environmentsPersistentSource.EnvironmentIsProd(environment) ||
                                            _environmentsPersistentSource.EnvironmentIsSecure(environment));

                    var tfsBuildModels = (from build in buildsFromDefinitionsAsync
                                          where (!filterOnlyPinned || build.KeepForever) &&
                                            build.Status == Build.StatusEnum.Completed &&
                                            (build.Result == Build.ResultEnum.Succeeded || build.Result == Build.ResultEnum.PartiallySucceeded)
                                          select new DeployableArtefact { Id = build.Url, Date = build.FinishTime, Name = CheckForPinnedBuild(build) }).ToList();

                    output = tfsBuildModels;
                }
                else if (project.ArtefactsUrl.StartsWith("file"))
                    output = GetFolderBuilds(project).ToList();
                else
                    output = new List<DeployableArtefact>();

                return output.OrderByDescending(b => b.Date);
            }
            catch (Exception ex)
            {
                _logger.Error("An error occurred in GetBuildsAsync", ex);

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
                _logger.Error("An error occurred in GetFolderBuilds", ex);

                throw;
            }
        }

        private static string CheckForPinnedBuild(Build b)
        {
            if (b.KeepForever != null && b.KeepForever)
                return b.BuildNumber += " [PINNED]";
            return b.BuildNumber;
        }

        public async Task<List<DeploymentRequestDetail>> BundleRequestDetailAsync(CreateRequest createRequest)
        {
            var project = _projectsPersistentSource.GetProject(createRequest.Project);
            var tfsClient = new AzureDevOpsServerWebClient(project.ArtefactsUrl, _logger);
            var result = new List<DeploymentRequestDetail>();
            string bundleJson;
            using (var reader = new StreamReader(createRequest.BuildUrl))
            {
                bundleJson = reader.ReadToEnd();
            }
            var bundle = JsonSerializer.Deserialize<BuildBundle>(bundleJson);

            if (bundle == null)
            {
                return result;
            }

            if (project.ArtefactsUrl.StartsWith("http"))
            {
                foreach (var buildItem in bundle.Items.GroupBy(i => i.Build))
                {
                    // Remove the Azure DevOps Server Project Name from the Build Definition Name
                    var azureDevOpsBuildDefinitionAndBuildName = createRequest.BuildDefinitionName.Split(';');
                    var azureDevOpsProjectName = azureDevOpsBuildDefinitionAndBuildName[0].Trim();
                    var azureDevOpsBuildDefinitionName = buildItem.First().BuildDefinition;

                    var buildDefsForProject = tfsClient.GetBuildDefinitionsForProjects(project.ArtefactsUrl,
                            project.ArtefactsSubPaths, project.ArtefactsBuildRegex);

                    var buildDefinitionReference = buildDefsForProject.First(def =>
                        def.Name.Equals(azureDevOpsBuildDefinitionName) && def.Project.Name.Equals(azureDevOpsProjectName));

                    var buildsFromDefinitionsAsync = await tfsClient.GetBuildsFromDefinitionsAsync(project.ArtefactsUrl,
                        new List<BuildDefinitionReference> { buildDefinitionReference }).ConfigureAwait(false);

                    var build = buildsFromDefinitionsAsync
                        .FirstOrDefault(b => b.BuildNumber.Equals(buildItem.Key));

                    var request = new CreateRequest
                    {
                        BuildUrl = build?.Url,
                        BuildDefinitionName = build?.Project.Name + "; " + build?.Definition.Name,
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
            var buildDetail = new BuildDetail();
            if (!string.IsNullOrEmpty(project.ArtefactsUrl) && project.ArtefactsUrl.StartsWith("http") &&
                !string.IsNullOrEmpty(project.ArtefactsSubPaths))
            {
                buildDetail = AzureDevOpsServerDetailAsync(createRequest).Result;
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

        private async Task<BuildDetail> AzureDevOpsServerDetailAsync(CreateRequest createRequest)
        {
            var buildDetail = new BuildDetail();
            var project = _projectsPersistentSource.GetProject(createRequest.Project);
            var tfsClient = new AzureDevOpsServerWebClient(project.ArtefactsUrl, _logger);

            var tfsProjects = project.ArtefactsSubPaths.Split(';');

            foreach (var tfsProject in tfsProjects)
            {
                // Remove the Azure DevOps Server Project Name from the Build Definition Name
                var fullBuildDefinitionName = createRequest.BuildDefinitionName.Split(';');
                var projectName = fullBuildDefinitionName[0].Trim();
                var buildDefinition = fullBuildDefinitionName[1].Trim();

                var buildDefsForProject = tfsClient.GetBuildDefinitionsForProjects(project.ArtefactsUrl,
                        project.ArtefactsSubPaths, project.ArtefactsBuildRegex);

                var buildDefinitionReference = buildDefsForProject.First(def =>
                    def.Name.Equals(buildDefinition) && def.Project.Name.Equals(projectName));

                var buildsFromDefinitionsAsync = await tfsClient.GetBuildsFromDefinitionsAsync(project.ArtefactsUrl,
                    new List<BuildDefinitionReference> { buildDefinitionReference }).ConfigureAwait(false);

                var buildValue = buildsFromDefinitionsAsync.FirstOrDefault(build =>
                    build.Url.Equals(createRequest.BuildUrl));
                if (buildValue == null)
                    continue;

                var tfsBuildArtifactResponse =
                    tfsClient.GetBuildArtifacts(project.ArtefactsUrl, tfsProject, createRequest.BuildUrl);
                if (tfsBuildArtifactResponse.Count > 1)
                {
                    var drop = tfsBuildArtifactResponse.FirstOrDefault(v => v.Name.Equals("drop"));
                    buildDetail.DropLocation = drop?.Resource.DownloadUrl;
                }
                else
                {
                    buildDetail.DropLocation = tfsBuildArtifactResponse[0].Resource.DownloadUrl;
                }

                buildDetail.BuildNumber = buildValue.BuildNumber;
                buildDetail.Uri = buildValue.Uri.ToString();
                buildDetail.Project = project.ProjectName;
                buildDetail.BuildId = buildValue.Id;
                return buildDetail;
            }

            throw new ApplicationException("Failed to locate the Build requested in Azure DevOps Server!");
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
