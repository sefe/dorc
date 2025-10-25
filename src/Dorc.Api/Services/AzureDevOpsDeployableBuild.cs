using System.Security.Claims;
using Dorc.Api.Interfaces;
using Dorc.Api.Model;
using Dorc.ApiModel;
using Dorc.Core.AzureDevOpsServer;
using Dorc.Core.Interfaces;
using Dorc.PersistentData.Sources.Interfaces;
using Microsoft.Extensions.Logging;
using Org.OpenAPITools.Model;

// ReSharper disable AsyncConverter.AsyncWait

namespace Dorc.Api.Services
{
    public class AzureDevOpsDeployableBuild : IDeployableBuild
    {
        public string AzureDevOpsBuildUrl = "";
        private string _azureDevOpsProject;
        private string _azureDevOpsBuildDefinitionName;
        private readonly IAzureDevOpsServerWebClient _azureDevOpsServerWebClient;
        private readonly ILogger _log;
        private readonly IProjectsPersistentSource _projectsPersistentSource;
        private readonly IDeployLibrary _deployLibrary;
        private readonly IRequestsPersistentSource _requestsPersistentSource;
        private string _validationMessage;

        public AzureDevOpsDeployableBuild(IAzureDevOpsServerWebClient azureDevOpsServerWebClient, ILogger log,
            IProjectsPersistentSource projectsPersistentSource, IDeployLibrary deployLibrary,
            IRequestsPersistentSource requestsPersistentSource)
        {
            _requestsPersistentSource = requestsPersistentSource;
            _deployLibrary = deployLibrary;
            _projectsPersistentSource = projectsPersistentSource;
            _log = log;
            _azureDevOpsServerWebClient = azureDevOpsServerWebClient;
        }

        private void SetBuildRefs(Build build)
        {
            _azureDevOpsProject = build.Project.Name;
            _azureDevOpsBuildDefinitionName = build.Definition.Name;
            AzureDevOpsBuildUrl = build.Uri.ToString();
        }

        public bool IsValid(BuildDetails dorcBuild)
        {
            var buildTypeOk = dorcBuild.Type == BuildType.TfsBuild;
            if (!buildTypeOk)
            {
                _validationMessage = "Failed Build Type Check";
                _log.LogWarning(_validationMessage);
                return false;
            }

            var project = _projectsPersistentSource.GetProject(dorcBuild.Project);
            var builds = new List<Build>();

            var buildDefsForProject = _azureDevOpsServerWebClient.GetBuildDefinitionsForProjects(project.ArtefactsUrl,
                project.ArtefactsSubPaths, project.ArtefactsBuildRegex);

            _log.LogDebug($"Found {buildDefsForProject.Count} build definitions in the project.");

            var buildDefs = new List<BuildDefinitionReference>();
            if (dorcBuild.BuildText != null && dorcBuild.BuildText.Contains(";"))
            {
                var buildDef = dorcBuild.BuildText.Split(';')[1].Trim();

                var bDef = buildDefsForProject
                    .Where(def => buildDef.Equals(def.Name)).ToList();

                buildDefs.AddRange(bDef);

                if (!buildDefs.Any())
                {
                    _validationMessage = $"Found No Build Definitions in the project with name {dorcBuild.BuildText}";
                    _log.LogDebug($"Found No Build Definitions in the project with name {dorcBuild.BuildText}");
                    return false;
                }

                var buildsFromDefinitionsAsync = _azureDevOpsServerWebClient.GetBuildsFromDefinitionsAsync(project.ArtefactsUrl,
                    buildDefs).Result;

                _log.LogDebug($"Found {buildsFromDefinitionsAsync.Count} builds in the project.");

                var pinned = dorcBuild.Pinned ?? false ? buildsFromDefinitionsAsync.Where(b => b.KeepForever) : buildsFromDefinitionsAsync;
                builds.AddRange(pinned);

            }

            if (dorcBuild.BuildText != null && !dorcBuild.BuildText.Contains(";"))
            {
                var projects = project.ArtefactsSubPaths.Split(';');
                foreach (var proj in projects)
                {
                    var buildsFromDefinitionsAsync = _azureDevOpsServerWebClient.GetBuildsFromBuildNumberAsync(project.ArtefactsUrl, dorcBuild.BuildText, proj).Result;

                    _log.LogDebug($"Found {buildsFromDefinitionsAsync.Count} builds in the project.");

                    var pinned = dorcBuild.Pinned ?? false ? buildsFromDefinitionsAsync.Where(b => b.KeepForever) : buildsFromDefinitionsAsync;
                    builds.AddRange(pinned);
                }
            }

            if (dorcBuild.VstsUrl != null && dorcBuild.VstsUrl.ToLower().StartsWith("vstfs"))
            {
                try
                {
                    AzureDevOpsBuildUrl = dorcBuild.VstsUrl;
                    var buildDetails = builds.FirstOrDefault(b => b.Uri.ToString().Equals(dorcBuild.VstsUrl));

                    SetBuildRefs(buildDetails);
                    var isVsTfs = AzureDevOpsBuildUrl.StartsWith("vstfs");
                    if (!isVsTfs)
                    {
                        _validationMessage = "VsTfsUrl uri was provided but it doesn't start from 'vstfs'";
                    }

                    return isVsTfs;
                }
                catch
                {
                    _validationMessage = "VsTfsUrl uri was provided but no build was found by this ArtefactsUrl";
                    return false;
                }
            }

            if (!builds.Any())
            {
                _log.LogWarning("No Builds was found for specified arguments");
                _validationMessage = "No Builds was found for specified arguments";
                return false;
            }

            foreach (var buildDetails in builds)
            {
                if (dorcBuild.BuildNum.ToLower().Equals("latest"))
                {
                    SetBuildRefs(buildDetails);
                    break;
                }

                if (!buildDetails.BuildNumber.Trim().Equals(dorcBuild.BuildNum.Trim(), StringComparison.OrdinalIgnoreCase))
                    continue;

                SetBuildRefs(buildDetails);
                break;
            }

            if (!AzureDevOpsBuildUrl.StartsWith("vstfs"))
                return false;

            _log.LogDebug("Successfully validated Build.");
            return true;
        }

        public string ValidationResult => _validationMessage;

        public RequestStatusDto Process(RequestDto request, ClaimsPrincipal user)
        {
            if (!AzureDevOpsBuildUrl.StartsWith("vstfs"))
            {
                return null;
            }

            var requestId = _deployLibrary.SubmitRequest(
                request.Project,
                request.Environment,
                AzureDevOpsBuildUrl,
                _azureDevOpsProject + "; " + _azureDevOpsBuildDefinitionName,
                request.Components.ToList(),
                request.RequestProperties.ToList(),
                user);

            int id;
            try
            {
                id = Convert.ToInt32(requestId);
            }
            catch (Exception e)
            {
                return new RequestStatusDto { Id = 0, Status = e.Message };
            }

            if (id <= 0)
            {
                return new RequestStatusDto()
                {
                    Id = 0,
                    Status = "DeployLibrary has returned zero result"
                };
            }

            return _requestsPersistentSource.GetRequestStatus(id);
        }
    }
}