using System.Diagnostics;
using System.Security.Claims;
using Dorc.ApiModel;
using Dorc.Core.AzureDevOpsServer;
using Dorc.Core.Exceptions;
using Dorc.Core.Interfaces;
using Dorc.PersistentData;
using Dorc.PersistentData.Model;
using Dorc.PersistentData.Sources.Interfaces;
using Microsoft.Extensions.Logging;

// ReSharper disable AsyncConverter.AsyncWait

namespace Dorc.Core
{
    public class DeployLibrary : IDeployLibrary
    {
        private readonly IProjectsPersistentSource _projectsPersistentSource;
        private readonly IComponentsPersistentSource _componentsPersistentSource;
        private readonly IManageProjectsPersistentSource _manageProjectsPersistentSource;
        private readonly IEnvironmentsPersistentSource _environmentsPersistentSource;
        private readonly ILogger<DeployLibrary> _logger;
        private readonly IRequestsPersistentSource _requestsPersistentSource;
        private readonly IClaimsPrincipalReader _claimsPrincipalReader;
        private readonly ILoggerFactory _loggerFactory;

        public DeployLibrary(IProjectsPersistentSource projectsPersistentSource,
            IComponentsPersistentSource componentsPersistentSource,
            IManageProjectsPersistentSource manageProjectsPersistentSource,
            IEnvironmentsPersistentSource environmentsPersistentSource,
            ILogger<DeployLibrary> logger,
            IRequestsPersistentSource requestsPersistentSource,
            IClaimsPrincipalReader claimsPrincipalReader,
            ILoggerFactory loggerFactory
            )
        {
            _requestsPersistentSource = requestsPersistentSource;
            _logger = logger;
            _environmentsPersistentSource = environmentsPersistentSource;
            _manageProjectsPersistentSource = manageProjectsPersistentSource;
            _componentsPersistentSource = componentsPersistentSource;
            _projectsPersistentSource = projectsPersistentSource;
            _claimsPrincipalReader = claimsPrincipalReader;
            _loggerFactory = loggerFactory;
        }

        public int SubmitRequest(string projectName, string environmentName, string uri,
            string buildDefinitionName, List<string> requestComponents, List<RequestProperty> requestProperties,
            ClaimsPrincipal user)
        {
            var project = _projectsPersistentSource.GetProject(projectName);
            if (project == null)
                throw new InvalidOperationException(
                    $"Could not find deployment project called '{projectName}'");
            var environment = _environmentsPersistentSource.GetEnvironment(environmentName, user);
            if (environment == null)
                throw new InvalidOperationException($"Could not find deployment environment called '{environmentName}'");
            var components = requestComponents.Select(
                x =>
                {
                    var component = _componentsPersistentSource.GetComponentByName(x);
                    if (component == null)
                        throw new InvalidOperationException(
                            $"Cannot find deployment component called '{x}'");
                    return component;
                }).ToArray();
            var properties = requestProperties
                ?.Select(x => new KeyValuePair<string, string>(x.PropertyName, x.PropertyValue))
                .ToArray();
            return CreateRequest(project, environment, uri, buildDefinitionName, components, properties, user)
                .RequestId;
        }

        private List<int> CopyEnvironment(string fromEnvironment, string toEnvironment,
            ProjectComponentPair[] projectComponents, string[] skipComponents, IEnumerable<RequestProperty> properties, ClaimsPrincipal user)
        {
            var requestIds = new List<int>();
            var orderedComponents = _manageProjectsPersistentSource.GetOrderedComponents(projectComponents.Select(p => p.Component));

            var sourceEnvironment = _environmentsPersistentSource.GetEnvironment(fromEnvironment, user);
            if (sourceEnvironment == null)
                throw new InvalidOperationException($"Cannot find environment named '{fromEnvironment}'");

            var environmentComponentStatusModels = _environmentsPersistentSource.GetEnvironmentComponentStatuses(sourceEnvironment.EnvironmentId);

            var componentBuilds =
                orderedComponents
                    .Join(environmentComponentStatusModels,
                        x => x.ComponentName, x => x.Component,
                        (c, cs) => new { Component = c, ComponentStatus = cs })
                    .Batch(x => x.ComponentStatus.BuildNumber);

            if (!componentBuilds.Any())
                throw new InvalidOperationException("No build found");
            foreach (var batch in componentBuilds)
            {
                Trace.WriteLine($"Build: {batch.Key}");
                var componentStatus = batch.First().ComponentStatus;
                var buildDetail = new BuildDetail
                {
                    BuildNumber = componentStatus.BuildNumber,
                    DropLocation = componentStatus.DropLocation,
                    Uri = componentStatus.Uri,
                    Project = componentStatus.Project
                };
                var requestDetail = new DeploymentRequestDetail
                {
                    BuildDetail = buildDetail,
                    Components = batch.Select(x => x.Component.ComponentName).ToList(),
                    ComponentsToSkip =
                        (skipComponents != null
                            ? batch.Select(x => x.Component.ComponentName).Where(skipComponents.Contains)
                            : Enumerable.Empty<string>()).ToList(),
                    EnvironmentName = toEnvironment,
                    Properties =
                        properties?.Select(x => new PropertyPair(x.PropertyName, x.PropertyValue)).ToList()
                };

                Trace.WriteLine($"Creating Req: build:{requestDetail.BuildDetail.BuildNumber} Comp:{string.Join("|", requestDetail.Components)}");
                var requestId = CreateRequest(requestDetail, user);
                requestIds.Add(requestId); 
            }

            return requestIds;
        }

        private int CreateRequest(DeploymentRequestDetail requestDetail, ClaimsPrincipal user)
        {
            var serializer = new DeploymentRequestDetailSerializer();

            var deploymentRequest =
                new DeploymentRequest
                {
                    RequestDetails = serializer.Serialize(requestDetail),
                    UserName = _claimsPrincipalReader.GetUserFullDomainName(user),
                    RequestedTime = DateTimeOffset.Now,
                    Project = requestDetail.BuildDetail.Project,
                    Environment = requestDetail.EnvironmentName,
                    BuildNumber = requestDetail.BuildDetail.BuildNumber,
                    Components = string.Join("|", requestDetail.Components)
                };

            var requestId = _requestsPersistentSource.SubmitRequest(deploymentRequest);

            return requestId;
        }

        private CreateResponse CreateRequest(ProjectApiModel project, EnvironmentApiModel environment,
            string buildUrl, string buildDefinitionName,
            ComponentApiModel[] components, IEnumerable<KeyValuePair<string, string>> properties, ClaimsPrincipal user)
        {
            try
            {
                var request = new CreateRequest
                {
                    Project = project.ProjectName,
                    Environment = environment.EnvironmentName,
                    BuildDefinitionName = buildDefinitionName,
                    BuildUrl = buildUrl,
                    Components = components.Select(c => c.ComponentName).ToList(),
                    Properties =
                        properties?.Select(x => new RequestProperty { PropertyName = x.Key, PropertyValue = x.Value })
                };

                var response = CreateRequestAsync(request, user).Result;

                return response;
            }
            catch (AggregateException ae)
            {
                if (ae.InnerException != null)
                    throw ae.InnerException;
            }

            return null;
        }

        private async Task<CreateResponse> CreateRequestAsync(CreateRequest createRequest, ClaimsPrincipal user)
        {
            var project = _projectsPersistentSource.GetProject(createRequest.Project);

            var buildDetail = new BuildDetail
            {
                Project = createRequest.Project
            };

            if (!string.IsNullOrEmpty(project.ArtefactsUrl) && project.ArtefactsUrl.StartsWith("http") &&
                !string.IsNullOrEmpty(project.ArtefactsSubPaths))
            {
                var azureDevOpsServerWebClient = new AzureDevOpsServerWebClient(project.ArtefactsUrl, _loggerFactory.CreateLogger<AzureDevOpsServerWebClient>());

                var projects = project.ArtefactsSubPaths.Split(';');

                foreach (var adoProject in projects)
                {
                    if (string.IsNullOrEmpty(createRequest.BuildDefinitionName))
                        throw new ApplicationException("BuildDefinitionName is required to create a request.");
                    var fullBuildDefinitionName = createRequest.BuildDefinitionName.Split(';');
                    var projectName = fullBuildDefinitionName[0].Trim();
                    var buildDefinition = fullBuildDefinitionName[1].Trim();

                    var buildDefsForProject = azureDevOpsServerWebClient.GetBuildDefinitionsForProjects(project.ArtefactsUrl,
                        project.ArtefactsSubPaths, project.ArtefactsBuildRegex);

                    var buildDefinitionReference = buildDefsForProject.First(def =>
                        def.Name.Equals(buildDefinition) && def.Project.Name.Equals(projectName));

                    var buildsFromDefinitionsAsync = await azureDevOpsServerWebClient.GetBuildsFromDefinitionsAsync(project.ArtefactsUrl,
                        [buildDefinitionReference]).ConfigureAwait(false);

                    var buildValue = buildsFromDefinitionsAsync.FirstOrDefault(build =>
                        build.Uri.ToString().Equals(createRequest.BuildUrl));
                    if (buildValue == null)
                        continue;

                    var artifacts =
                        azureDevOpsServerWebClient.GetBuildArtifacts(project.ArtefactsUrl, adoProject, createRequest.BuildUrl);
                    
                    if (artifacts == null || artifacts.Count == 0)
                    {
                        throw new InvalidOperationException(
                            "Can't find artifact, ensure that it was published correctly in Azure DevOps");
                    }
                    
                    if (artifacts.Count > 1)
                    {
                        var drop = artifacts.FirstOrDefault(v => v.Name.ToLower().Equals("drop"));
                        buildDetail.DropLocation = drop?.Resource.DownloadUrl;
                    }
                    else
                    {
                        buildDetail.DropLocation = artifacts[0].Resource.DownloadUrl;
                    }

                    buildDetail.BuildNumber = buildValue.BuildNumber;
                    buildDetail.Uri = buildValue.Uri;
                    buildDetail.Project = project.ProjectName;
                }
            }
            else if (!string.IsNullOrEmpty(project.ArtefactsUrl) && project.ArtefactsUrl.StartsWith("file"))
            {
                buildDetail.DropLocation = new Uri(createRequest.BuildUrl).LocalPath;
                buildDetail.BuildNumber = Path.GetFileName(buildDetail.DropLocation);
            }
            else
            {
                buildDetail.DropLocation = createRequest.DropFolder;
            }

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

            if (createRequest.Properties != null)
                foreach (var requestProperty in createRequest.Properties)
                    requestDetail.Properties.Add(new PropertyPair(requestProperty.PropertyName,
                        requestProperty.PropertyValue));

            var requestId = CreateRequest(requestDetail, user);

            return new CreateResponse
            {
                Message = "Done",
                RequestId = requestId
            };
        }

        private void AddComponent(ICollection<string> componentNames, ComponentApiModel component)
        {
            if (!string.IsNullOrEmpty(component.ScriptPath)
                && !componentNames.Contains(component.ComponentName))
            {
                componentNames.Add(component.ComponentName);
            }

            _componentsPersistentSource.LoadChildren(component);

            foreach (var child in component.Children)
            {
                AddComponent(componentNames, child);
            }
        }

        public List<int> CopyEnvBuildWithComponentIds(string sourceEnv, string targetEnv, string strProjectName,
            int[] doDeploy, ClaimsPrincipal user)
        {
            var skipComponents = new[] { "" };
            var project = _projectsPersistentSource.GetProject(strProjectName);
            var projComponents = _projectsPersistentSource.GetComponentsForProject(project.ProjectId)
                .Where(x => !x.Children.Any())
                .Where(x => doDeploy.Any(i => i == x.ComponentId))
                .Select(x => new ProjectComponentPair { Project = strProjectName, Component = x.ComponentName }).ToArray();
            var requestProperties = new RequestProperty[] { };
            var response = CopyEnvironment(sourceEnv, targetEnv, projComponents, skipComponents, requestProperties, user);
            var returnValue = response.Any() ? response : throw new Exception("CopyEnvBuildWithComponentIds Fail");
            return returnValue;
        }

        public List<int> CopyEnvBuildAllComponents(string sourceEnv, string targetEnv, string projectName,
            ClaimsPrincipal user)
        {
            var dontDeploy = new[] { "" };
            var skipComponents = new[] { "" };
            var project = _projectsPersistentSource.GetProject(projectName);
            var projComponents = _projectsPersistentSource.GetComponentsForProject(project.ProjectId)
                .Where(x => !x.Children.Any())
                .Where(x => !dontDeploy.Contains(x.ComponentName))
                .Select(x => new ProjectComponentPair { Project = projectName, Component = x.ComponentName }).ToArray();
            var requestProperties = new RequestProperty[] { };

            var response = CopyEnvironment(sourceEnv, targetEnv, projComponents, skipComponents, requestProperties, user);
            var returnValue = response.Any() ? response : throw new Exception("CopyEnvBuildAllComponents Fail");

            return returnValue;
        }

        public List<int> DeployCopyEnvBuildWithComponentNames(string sourceEnv, string targetEnv, string projectName,
            string components, ClaimsPrincipal user)
        {
            var dontDeploy = new[] { "" };
            var skipComponents = new[] { "" };
            var project = _projectsPersistentSource.GetProject(projectName);
            if (project == null) throw new WrongComponentsException($"Project \"{projectName}\" not found.");
            var componentsNames = components.Split(';').ToList();
            var projComponents = _projectsPersistentSource.GetComponentsForProject(project.ProjectId)
                .Where(x => !x.Children.Any())
                .Where(x => !dontDeploy.Contains(x.ComponentName));
            foreach (var component in componentsNames)
                if (!projComponents.Contains(_componentsPersistentSource.GetComponentByName(component)))
                    throw new WrongComponentsException($"Component \"{component}\" doesn't belong to the project.");

            var selectedComponents = componentsNames
                .Select(x => new ProjectComponentPair { Project = projectName, Component = x }).ToArray();

            var requestProperties = new RequestProperty[] { };

            var response = CopyEnvironment(sourceEnv, targetEnv, selectedComponents, skipComponents, requestProperties, user);
            var returnValue = response.Any() ? response : throw new Exception("DeployCopyEnvBuildWithComponentNames Fail");

            return returnValue;
        }
    }
}