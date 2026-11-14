using System.Diagnostics;
using System.Security.Claims;
using System.Text.Json;
using Dorc.ApiModel;
using Dorc.Core.AzureDevOpsServer;
using Dorc.Core.BuildServer;
using Dorc.Core.Events;
using Dorc.Core.Exceptions;
using Dorc.Core.Interfaces;
using Dorc.PersistentData;
using Dorc.PersistentData.Security;
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
        private readonly ILoggerFactory _loggerFactory;
        private readonly IRequestsPersistentSource _requestsPersistentSource;
        private readonly IClaimsPrincipalReader _claimsPrincipalReader;
        private readonly IDeploymentEventsPublisher _deploymentEventsPublisher;
        private readonly IBuildServerClientFactory _buildServerClientFactory;
        private readonly ISourceHostAllowList _sourceHosts;

        public DeployLibrary(IProjectsPersistentSource projectsPersistentSource,
            IComponentsPersistentSource componentsPersistentSource,
            IManageProjectsPersistentSource manageProjectsPersistentSource,
            IEnvironmentsPersistentSource environmentsPersistentSource,
            ILoggerFactory loggerFactory,
            IRequestsPersistentSource requestsPersistentSource,
            IClaimsPrincipalReader claimsPrincipalReader,
            IDeploymentEventsPublisher deploymentEventsPublisher,
            IBuildServerClientFactory buildServerClientFactory,
            ISourceHostAllowList sourceHosts
            )
        {
            _sourceHosts = sourceHosts;
            _requestsPersistentSource = requestsPersistentSource;
            _loggerFactory = loggerFactory;
            _environmentsPersistentSource = environmentsPersistentSource;
            _manageProjectsPersistentSource = manageProjectsPersistentSource;
            _componentsPersistentSource = componentsPersistentSource;
            _projectsPersistentSource = projectsPersistentSource;
            _claimsPrincipalReader = claimsPrincipalReader;
            _deploymentEventsPublisher = deploymentEventsPublisher;
            _buildServerClientFactory = buildServerClientFactory;
        }

        public int SubmitRequest(string projectName, string environmentName, string uri,
            string buildDefinitionName, List<string> requestComponents, List<RequestProperty> requestProperties,
            ClaimsPrincipal user, bool isCatalog = false, CatalogSubmissionIdentity? catalogSubmission = null)
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
            return CreateRequest(project, environment, uri, buildDefinitionName, components, requestProperties, user, isCatalog, catalogSubmission)
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
                        properties?.Select(x => new PropertyPair(x.PropertyName, x.PropertyValue) { IsSensitive = x.IsSensitive }).ToList()
                };

                Trace.WriteLine($"Creating Req: build:{requestDetail.BuildDetail.BuildNumber} Comp:{string.Join("|", requestDetail.Components)}");
                var requestId = CreateRequest(requestDetail, user);
                requestIds.Add(requestId); 
            }

            return requestIds;
        }

        private int CreateRequest(DeploymentRequestDetail requestDetail, ClaimsPrincipal user,
            CatalogSubmissionIdentity? catalogSubmission = null)
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

            var requestId = catalogSubmission == null
                ? _requestsPersistentSource.SubmitRequest(deploymentRequest)
                : _requestsPersistentSource.SubmitCatalogRequest(deploymentRequest, catalogSubmission);

            // Fire-and-forget event: new request created (Pending)
            _ = _deploymentEventsPublisher.PublishNewRequestAsync(
                new DeploymentRequestEventData(
                    RequestId: requestId,
                    Status: deploymentRequest.Status.ToString(),
                    StartedTime: deploymentRequest.RequestedTime,
                    CompletedTime: null,
                    Timestamp: DateTimeOffset.UtcNow
                ));

            return requestId;
        }

        private CreateResponse CreateRequest(ProjectApiModel project, EnvironmentApiModel environment,
            string buildUrl, string buildDefinitionName,
            ComponentApiModel[] components, IEnumerable<RequestProperty> properties, ClaimsPrincipal user,
            bool isCatalog = false, CatalogSubmissionIdentity? catalogSubmission = null)
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
                    Properties = properties,
                    IsCatalog = isCatalog,
                    CatalogSubmission = catalogSubmission
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

            // Catalog-mode deploys carry no build artifact: the runner's
            // CatalogReferenceCodeSourceProvider resolves the source from the
            // component's manifest at dispatch time. Skip artifact resolution
            // entirely - it would otherwise throw for any project that has an
            // ADO/GitHub/file-share artifact source configured (empty
            // BuildUrl/BuildDefinitionName can't resolve an artifact).
            if (createRequest.IsCatalog)
            {
                buildDetail.DropLocation = string.Empty;
            }
            else if (IsBuildServerProject(project) && !string.IsNullOrEmpty(project.ArtefactsSubPaths))
            {
                if (string.IsNullOrEmpty(createRequest.BuildDefinitionName))
                    throw new ApplicationException("BuildDefinitionName is required to create a request.");

                var buildClient = _buildServerClientFactory.Create(project.SourceControlType);

                var buildInfo = await buildClient.ValidateBuildAsync(
                    project.ArtefactsUrl,
                    project.ArtefactsSubPaths,
                    project.ArtefactsBuildRegex,
                    createRequest.BuildDefinitionName,
                    null,
                    createRequest.BuildUrl,
                    false).ConfigureAwait(false);

                if (buildInfo != null)
                {
                    buildDetail.BuildNumber = buildInfo.BuildNumber;
                    buildDetail.Uri = buildInfo.BuildUri;
                    buildDetail.BuildId = buildInfo.BuildId;
                }
                else
                {
                    buildDetail.BuildNumber = createRequest.BuildUrl;
                    buildDetail.Uri = createRequest.BuildUrl;
                }

                buildDetail.DropLocation = await buildClient.GetBuildArtifactDownloadUrlAsync(
                    project.ArtefactsUrl,
                    project.ArtefactsSubPaths,
                    project.ArtefactsBuildRegex,
                    createRequest.BuildDefinitionName,
                    buildInfo?.BuildUri ?? createRequest.BuildUrl).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(buildDetail.DropLocation)
                    && !buildDetail.DropLocation.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                    && !buildDetail.DropLocation.StartsWith("dorc-gh://", StringComparison.OrdinalIgnoreCase))
                {
                    RequirePermittedRemoteArtefactPath(buildDetail.DropLocation, "drop location");
                }
                buildDetail.Project = project.ProjectName;
            }
            else if (IsFileShareProject(project) || IsFileBasedUrl(project.ArtefactsUrl) || IsFileBasedUrl(createRequest.BuildUrl))
            {
                RequirePermittedRemoteArtefactPath(createRequest.BuildUrl, "build URL");

                var permittedRoots = Security.PathConfinement.SplitRoots(project.ArtefactsUrl).ToList();
                if (permittedRoots.Count == 0 || !Security.PathConfinement.IsWithinAny(createRequest.BuildUrl, permittedRoots))
                {
                    throw new InvalidOperationException(
                        $"The build location is outside the artefacts location configured for project '{project.ProjectName}'.");
                }

                foreach (var root in permittedRoots.Where(root => Security.PathConfinement.IsWithin(createRequest.BuildUrl, root)))
                {
                    RequirePermittedRemoteArtefactPath(root, "project artefact root");
                }

                var normalizedBuildUrl = SourceHost.NormalizeExtendedUnc(createRequest.BuildUrl);
                if (normalizedBuildUrl.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
                {
                    if (!Uri.TryCreate(normalizedBuildUrl, UriKind.Absolute, out var buildUri) || !buildUri.IsFile)
                    {
                        throw new InvalidOperationException(
                            $"The build location is outside the artefacts location configured for project '{project.ProjectName}'.");
                    }

                    buildDetail.DropLocation = buildUri.LocalPath;
                }
                else
                {
                    buildDetail.DropLocation = normalizedBuildUrl.Replace('/', '\\');
                }

                if (!Security.PathConfinement.IsWithinAny(buildDetail.DropLocation, permittedRoots))
                {
                    throw new InvalidOperationException(
                        $"The build location is outside the artefacts location configured for project '{project.ProjectName}'.");
                }

                RequirePermittedRemoteArtefactPath(buildDetail.DropLocation, "drop location");
                buildDetail.BuildNumber = Path.GetFileName(buildDetail.DropLocation);
            }
            else
            {
                if (!string.IsNullOrWhiteSpace(createRequest.DropFolder)
                    && !createRequest.DropFolder.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                    && !createRequest.DropFolder.StartsWith("dorc-gh://", StringComparison.OrdinalIgnoreCase))
                {
                    RequirePermittedRemoteArtefactPath(createRequest.DropFolder, "drop location");
                }

                buildDetail.DropLocation = createRequest.DropFolder;
            }

            var componentNames = new List<string>();

            foreach (var componentName in createRequest.Components)
            {
                var component = _componentsPersistentSource.GetComponentByName(componentName);
                if (component == null) continue;

                AddComponent(componentNames, component);
            }

            if (!createRequest.IsCatalog && IsPotentialBundle(createRequest, buildDetail))
            {
                var bundleRequestIds = await TryExpandAndSubmitBundleRequestsAsync(
                    project, createRequest, buildDetail, componentNames, user).ConfigureAwait(false);

                if (bundleRequestIds != null && bundleRequestIds.Count > 0)
                {
                    return new CreateResponse
                    {
                        Message = "Done",
                        RequestId = bundleRequestIds.First()
                    };
                }
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
                        requestProperty.PropertyValue)
                    { IsSensitive = requestProperty.IsSensitive });

            var requestId = CreateRequest(requestDetail, user, createRequest.CatalogSubmission);

            return new CreateResponse
            {
                Message = "Done",
                RequestId = requestId
            };
        }

        private static readonly JsonSerializerOptions BundleJsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        private static bool IsPotentialBundle(CreateRequest createRequest, BuildDetail buildDetail)
        {
            if (!string.IsNullOrEmpty(createRequest.BuildDefinitionName) &&
                createRequest.BuildDefinitionName.Contains("bundle", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (!string.IsNullOrEmpty(createRequest.BuildUrl) &&
                createRequest.BuildUrl.EndsWith("bundle.json", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (!string.IsNullOrEmpty(buildDetail.DropLocation) &&
                buildDetail.DropLocation.EndsWith("bundle.json", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return false;
        }

        private async Task<List<int>?> TryExpandAndSubmitBundleRequestsAsync(
            ProjectApiModel project,
            CreateRequest createRequest,
            BuildDetail bundleBuildDetail,
            List<string> expandedComponentNames,
            ClaimsPrincipal user)
        {
            if (string.IsNullOrWhiteSpace(bundleBuildDetail.DropLocation))
                return null;

            var isBuildServer = IsBuildServerProject(project);
            var isFileShare = IsFileShareProject(project);

            string? bundleJson = null;
            IBuildServerClient? buildClient = null;

            if (isBuildServer)
            {
                buildClient = _buildServerClientFactory.Create(project.SourceControlType);
                bundleJson = await buildClient.TryReadArtifactFileTextAsync(
                        project.ArtefactsUrl ?? string.Empty,
                        bundleBuildDetail.DropLocation,
                        "bundle.json")
                    .ConfigureAwait(false);
            }
            else if (isFileShare)
            {
                var permittedRoots = Security.PathConfinement.SplitRoots(project.ArtefactsUrl).ToList();
                foreach (var root in permittedRoots)
                {
                    RequirePermittedRemoteArtefactPath(root, "project artefact root");
                }
                RequirePermittedRemoteArtefactPath(bundleBuildDetail.DropLocation, "bundle drop folder");

                if (permittedRoots.Count == 0 ||
                    !Security.PathConfinement.IsWithinAny(bundleBuildDetail.DropLocation, permittedRoots))
                {
                    return null;
                }

                var canonicalDrop = Security.PathConfinement.Canonicalise(bundleBuildDetail.DropLocation);
                if (string.IsNullOrWhiteSpace(canonicalDrop) || canonicalDrop.Contains("..", StringComparison.Ordinal))
                {
                    return null;
                }

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
                return null;

            var bundle = JsonSerializer.Deserialize<BuildBundle>(bundleJson, BundleJsonOptions);
            if (bundle?.Items == null || !bundle.Items.Any())
                return null;

            // Preserve the component ordering from expandedComponentNames while filtering to components
            // requested by the user that exist in bundle.json. If no components were explicitly selected,
            // default to all components listed in bundle.json.
            List<BuildItem> matchedItems;
            if (expandedComponentNames.Count == 0)
            {
                matchedItems = bundle.Items
                    .Where(item => !string.IsNullOrWhiteSpace(item.Component))
                    .ToList();
            }
            else
            {
                var componentOrder = expandedComponentNames
                    .Select((name, index) => (name, index))
                    .ToDictionary(x => x.name, x => x.index, StringComparer.OrdinalIgnoreCase);

                matchedItems = bundle.Items
                    .Where(item => !string.IsNullOrWhiteSpace(item.Component) &&
                                   componentOrder.ContainsKey(item.Component))
                    .OrderBy(item => componentOrder[item.Component])
                    .ToList();
            }

            if (matchedItems.Count == 0)
            {
                throw new InvalidOperationException(
                    $"None of the selected components ({string.Join(", ", expandedComponentNames)}) " +
                    $"were found in bundle.json for build '{bundleBuildDetail.BuildNumber}'.");
            }

            var properties = createRequest.Properties?
                .Select(p => new PropertyPair(p.PropertyName, p.PropertyValue) { IsSensitive = p.IsSensitive })
                .ToList() ?? new List<PropertyPair>();

            var requestIds = new List<int>();

            if (isBuildServer && buildClient != null)
            {
                var isPinnedRequired = _environmentsPersistentSource.EnvironmentIsProd(createRequest.Environment) ||
                                       _environmentsPersistentSource.EnvironmentIsSecure(createRequest.Environment);

                var adoProjectPrefix = !string.IsNullOrEmpty(createRequest.BuildDefinitionName) &&
                                       createRequest.BuildDefinitionName.Contains(';')
                    ? createRequest.BuildDefinitionName.Split(';')[0].Trim()
                    : project.ArtefactsSubPaths?.Split(';', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim() ?? string.Empty;

                foreach (var buildGroup in matchedItems.GroupBy(i => (BuildDefinition: i.BuildDefinition?.Trim() ?? string.Empty, Build: i.Build?.Trim() ?? string.Empty)))
                {
                    var itemDefinition = buildGroup.Key.BuildDefinition;
                    var itemBuildNumber = buildGroup.Key.Build;

                    var fullDefinitionName = project.SourceControlType == SourceControlType.GitHub || itemDefinition.Contains(';')
                        ? itemDefinition
                        : $"{adoProjectPrefix}; {itemDefinition}";

                    var buildInfo = await buildClient.ValidateBuildAsync(
                        project.ArtefactsUrl ?? string.Empty,
                        project.ArtefactsSubPaths ?? string.Empty,
                        project.ArtefactsBuildRegex ?? string.Empty,
                        fullDefinitionName,
                        itemBuildNumber,
                        null,
                        isPinnedRequired).ConfigureAwait(false);

                    if (buildInfo == null)
                    {
                        throw new InvalidOperationException(
                            $"Build '{itemBuildNumber}' (definition '{fullDefinitionName}') referenced in bundle.json was not " +
                            $"found{(isPinnedRequired ? " (or is not pinned)" : string.Empty)} in {project.SourceControlType} " +
                            $"for project '{createRequest.Project}'.");
                    }

                    var dropLocation = await buildClient.GetBuildArtifactDownloadUrlAsync(
                        project.ArtefactsUrl,
                        project.ArtefactsSubPaths,
                        project.ArtefactsBuildRegex,
                        fullDefinitionName,
                        buildInfo.BuildUri).ConfigureAwait(false);

                    var componentBuildDetail = new BuildDetail
                    {
                        Project = project.ProjectName,
                        BuildNumber = buildInfo.BuildNumber,
                        Uri = buildInfo.BuildUri,
                        BuildId = buildInfo.BuildId,
                        DropLocation = dropLocation
                    };

                    var detail = new DeploymentRequestDetail
                    {
                        EnvironmentName = createRequest.Environment,
                        Components = buildGroup.Select(i => i.Component).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                        BuildDetail = componentBuildDetail,
                        Properties = properties.Select(p => new PropertyPair(p.Name, p.Value) { IsSensitive = p.IsSensitive }).ToList()
                    };

                    requestIds.Add(CreateRequest(detail, user, createRequest.CatalogSubmission));
                }
            }
            else if (isFileShare)
            {
                var baseDropDirectory = File.Exists(bundleBuildDetail.DropLocation)
                    ? Path.GetDirectoryName(bundleBuildDetail.DropLocation) ?? bundleBuildDetail.DropLocation
                    : bundleBuildDetail.DropLocation;

                foreach (var buildGroup in matchedItems.GroupBy(i => i.Build?.Trim() ?? string.Empty))
                {
                    var componentBuildDetail = new BuildDetail
                    {
                        Project = project.ProjectName,
                        DropLocation = baseDropDirectory,
                        BuildNumber = !string.IsNullOrWhiteSpace(buildGroup.Key)
                            ? buildGroup.Key
                            : Path.GetFileName(baseDropDirectory.TrimEnd('\\', '/'))
                    };

                    var detail = new DeploymentRequestDetail
                    {
                        EnvironmentName = createRequest.Environment,
                        Components = buildGroup.Select(i => i.Component).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                        BuildDetail = componentBuildDetail,
                        Properties = properties.Select(p => new PropertyPair(p.Name, p.Value) { IsSensitive = p.IsSensitive }).ToList()
                    };

                    requestIds.Add(CreateRequest(detail, user, createRequest.CatalogSubmission));
                }
            }

            return requestIds;
        }

        private static bool IsFileBasedUrl(string? url)
            => !string.IsNullOrEmpty(url)
               && !url.StartsWith("http", StringComparison.OrdinalIgnoreCase)
               && !url.StartsWith("dorc-gh://", StringComparison.OrdinalIgnoreCase)
               && (url.StartsWith("file", StringComparison.OrdinalIgnoreCase) || SourceHost.IsRemoteOrUnc(url));

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
                    $"Refusing to present default Windows credentials because no artefact host allow-list is configured ('{SourceHostAllowList.ArtefactHostsSetting}').");
            }

            var decision = _sourceHosts.CheckArtefactSource(path);
            if (decision == null || !decision.Allowed)
            {
                throw new InvalidOperationException(
                    $"Refusing to present default Windows credentials to '{path}', because {decision?.Reason ?? "host is not permitted"}");
            }
        }

        private static bool IsBuildServerProject(ProjectApiModel project)
            => project.SourceControlType == SourceControlType.GitHub ||
               (project.SourceControlType == SourceControlType.AzureDevOps &&
                !string.IsNullOrEmpty(project.ArtefactsUrl) &&
                project.ArtefactsUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase));

        private static bool IsFileShareProject(ProjectApiModel project)
            => project.SourceControlType == SourceControlType.FileShare ||
               (project.SourceControlType == SourceControlType.AzureDevOps &&
                IsFileBasedUrl(project.ArtefactsUrl));

        private void AddComponent(ICollection<string> componentNames, ComponentApiModel component)
        {
            _componentsPersistentSource.LoadChildren(component);
            foreach (var child in component.Children)
            {
                if (child.IsEnabled)
                {
                    AddComponent(componentNames, child);
                    continue;
                }

                _componentsPersistentSource.LoadChildren(child);
                if (child.Children.Count == 0 && !componentNames.Contains(child.ComponentName))
                {
                    componentNames.Add(child.ComponentName);
                }
            }

            // Disabled leaves must be persisted so deployment results can show
            // that they were deliberately skipped. Execution filters them later.
            if (component.Children.Count == 0 && !componentNames.Contains(component.ComponentName))
            {
                componentNames.Add(component.ComponentName);
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