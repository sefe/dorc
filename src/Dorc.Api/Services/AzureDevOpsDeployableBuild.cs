using System.Security.Claims;
using Dorc.Api.Interfaces;
using Dorc.Api.Model;
using Dorc.ApiModel;
using Dorc.Core.AzureDevOpsServer;
using Dorc.Core.Interfaces;
using Dorc.PersistentData.Sources.Interfaces;
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

        public AzureDevOpsDeployableBuild(IAzureDevOpsServerWebClient azureDevOpsServerWebClient, ILogger<AzureDevOpsDeployableBuild> log,
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
            if (dorcBuild.Type != BuildType.TfsBuild)
            {
                _validationMessage = "Failed Build Type Check";
                _log.LogWarning(_validationMessage);
                return false;
            }

            var project = _projectsPersistentSource.GetProject(dorcBuild.Project);
            var buildDefsForProject = _azureDevOpsServerWebClient.GetBuildDefinitionsForProjects(project.ArtefactsUrl,
                project.ArtefactsSubPaths, project.ArtefactsBuildRegex);
            _log.LogDebug($"Found {buildDefsForProject.Count} build definitions in the project.");

            var builds = GetBuilds(dorcBuild, project, buildDefsForProject);

            if (dorcBuild.VstsUrl != null && dorcBuild.VstsUrl.ToLower().StartsWith("vstfs"))
                return ValidateVstsUrl(dorcBuild, builds);

            if (!builds.Any())
            {
                _log.LogWarning("No Builds was found for specified arguments");
                _validationMessage = "No Builds was found for specified arguments";
                return false;
            }

            SetBuildRefsFromMatchingBuild(dorcBuild, builds);

            if (!AzureDevOpsBuildUrl.StartsWith("vstfs"))
                return false;

            _log.LogDebug("Successfully validated Build.");
            return true;
        }

        private List<Build> GetBuilds(BuildDetails dorcBuild, ProjectApiModel project,
            List<BuildDefinitionReference> buildDefsForProject)
        {
            if (dorcBuild.BuildText != null && dorcBuild.BuildText.Contains(";"))
                return GetBuildsFromDefinition(dorcBuild, project, buildDefsForProject);

            if (dorcBuild.BuildText != null && !dorcBuild.BuildText.Contains(";"))
                return GetBuildsFromBuildNumber(dorcBuild, project);

            return [];
        }

        private List<Build> GetBuildsFromDefinition(BuildDetails dorcBuild, ProjectApiModel project,
            List<BuildDefinitionReference> buildDefsForProject)
        {
            var buildDef = dorcBuild.BuildText.Split(';')[1].Trim();
            var buildDefs = buildDefsForProject.Where(def => buildDef.Equals(def.Name)).ToList();

            if (!buildDefs.Any())
            {
                _validationMessage = $"Found No Build Definitions in the project with name {dorcBuild.BuildText}";
                _log.LogDebug($"Found No Build Definitions in the project with name {dorcBuild.BuildText}");
                return [];
            }

            var buildsFromDefinitionsAsync = _azureDevOpsServerWebClient.GetBuildsFromDefinitionsAsync(project.ArtefactsUrl,
                buildDefs).Result;
            _log.LogDebug($"Found {buildsFromDefinitionsAsync.Count} builds in the project.");

            return FilterByPinned(dorcBuild, buildsFromDefinitionsAsync);
        }

        private List<Build> GetBuildsFromBuildNumber(BuildDetails dorcBuild, ProjectApiModel project)
        {
            var builds = new List<Build>();
            var projects = project.ArtefactsSubPaths.Split(';');

            foreach (var proj in projects)
            {
                var buildsFromDefinitionsAsync = _azureDevOpsServerWebClient
                    .GetBuildsFromBuildNumberAsync(project.ArtefactsUrl, dorcBuild.BuildText, proj).Result;
                _log.LogDebug($"Found {buildsFromDefinitionsAsync.Count} builds in the project.");

                builds.AddRange(FilterByPinned(dorcBuild, buildsFromDefinitionsAsync));
            }

            return builds;
        }

        private static List<Build> FilterByPinned(BuildDetails dorcBuild, List<Build> builds)
        {
            var pinned = dorcBuild.Pinned ?? false ? builds.Where(b => b.KeepForever) : builds;
            return pinned.ToList();
        }

        private bool ValidateVstsUrl(BuildDetails dorcBuild, List<Build> builds)
        {
            try
            {
                AzureDevOpsBuildUrl = dorcBuild.VstsUrl;
                var buildDetails = builds.FirstOrDefault(b => b.Uri.ToString().Equals(dorcBuild.VstsUrl));
                SetBuildRefs(buildDetails);

                var isVsTfs = AzureDevOpsBuildUrl.StartsWith("vstfs");
                if (!isVsTfs)
                    _validationMessage = "VsTfsUrl uri was provided but it doesn't start from 'vstfs'";

                return isVsTfs;
            }
            catch
            {
                _validationMessage = "VsTfsUrl uri was provided but no build was found by this ArtefactsUrl";
                return false;
            }
        }

        private void SetBuildRefsFromMatchingBuild(BuildDetails dorcBuild, List<Build> builds)
        {
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