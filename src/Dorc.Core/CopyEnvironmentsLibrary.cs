using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Security.Claims;
using Dorc.ApiModel;
using Dorc.Core.Exceptions;
using Dorc.PersistentData;
using Dorc.PersistentData.Model;
using Dorc.PersistentData.Sources.Interfaces;
using log4net;

namespace Dorc.Core
{
    public interface ICopyEnvironmentsLibrary
    {
        List<int> CopyEnvBuildAllComponents(string sourceEnv, string targetEnv, string projectName, ClaimsPrincipal user);
        List<int> DeployCopyEnvBuildWithComponentNames(string sourceEnv, string targetEnv, string projectName, string components, ClaimsPrincipal user);
    }

    public class CopyEnvironmentsLibrary : ICopyEnvironmentsLibrary
    {
        private readonly IProjectsPersistentSource _projectsPersistentSource;
        private readonly IComponentsPersistentSource _componentsPersistentSource;
        private readonly IManageProjectsPersistentSource _manageProjectsPersistentSource;
        private readonly IEnvironmentsPersistentSource _environmentsPersistentSource;
        private readonly IRequestsPersistentSource _requestsPersistentSource;
        private readonly IClaimsPrincipalReader _claimsPrincipalReader;
        private readonly ILog _logger;

        public CopyEnvironmentsLibrary(
            IProjectsPersistentSource projectsPersistentSource,
            IComponentsPersistentSource componentsPersistentSource,
            IManageProjectsPersistentSource manageProjectsPersistentSource,
            IEnvironmentsPersistentSource environmentsPersistentSource,
            IRequestsPersistentSource requestsPersistentSource,
            IClaimsPrincipalReader claimsPrincipalReader,
            ILog logger)
        {
            _projectsPersistentSource = projectsPersistentSource;
            _componentsPersistentSource = componentsPersistentSource;
            _manageProjectsPersistentSource = manageProjectsPersistentSource;
            _environmentsPersistentSource = environmentsPersistentSource;
            _requestsPersistentSource = requestsPersistentSource;
            _claimsPrincipalReader = claimsPrincipalReader;
            _logger = logger;
        }

        public List<int> CopyEnvBuildAllComponents(string sourceEnv, string targetEnv, string projectName, ClaimsPrincipal user)
        {
            var dontDeploy = new[] { "" };
            var skipComponents = new[] { "" };
            var project = _projectsPersistentSource.GetProject(projectName);
            if (project == null) throw new WrongComponentsException($"Project \"{projectName}\" not found.");
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
                    Components = string.Join("|", requestDetail.Components),
                    EnvironmentOwnerEmail = "" // CLI tools don't resolve environment owner email
                };

            var requestId = _requestsPersistentSource.SubmitRequest(deploymentRequest);

            return requestId;
        }
    }
}