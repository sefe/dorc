using System.Security.Claims;
using Dorc.Api.Interfaces;
using Dorc.Api.Model;
using Dorc.ApiModel;
using Dorc.Core.BuildServer;
using Dorc.Core.Interfaces;
using Dorc.PersistentData.Sources.Interfaces;

namespace Dorc.Api.Services
{
    public class GitHubDeployableBuild : IDeployableBuild
    {
        private readonly IBuildServerClient _buildServerClient;
        private readonly ILogger _log;
        private readonly IProjectsPersistentSource _projectsPersistentSource;
        private readonly IDeployLibrary _deployLibrary;
        private readonly IRequestsPersistentSource _requestsPersistentSource;
        private string _validationMessage = string.Empty;
        private BuildServerBuildInfo? _buildInfo;

        public GitHubDeployableBuild(IBuildServerClient buildServerClient, ILogger<GitHubDeployableBuild> log,
            IProjectsPersistentSource projectsPersistentSource, IDeployLibrary deployLibrary,
            IRequestsPersistentSource requestsPersistentSource)
        {
            _requestsPersistentSource = requestsPersistentSource;
            _deployLibrary = deployLibrary;
            _projectsPersistentSource = projectsPersistentSource;
            _log = log;
            _buildServerClient = buildServerClient;
        }

        public bool IsValid(BuildDetails dorcBuild)
        {
            var buildTypeOk = dorcBuild.Type == BuildType.GitHubBuild;
            if (!buildTypeOk)
            {
                _validationMessage = "Failed Build Type Check - expected GitHub build";
                _log.LogWarning(_validationMessage);
                return false;
            }

            var project = _projectsPersistentSource.GetProject(dorcBuild.Project);
            if (project == null)
            {
                _validationMessage = $"Project '{dorcBuild.Project}' not found";
                _log.LogWarning(_validationMessage);
                return false;
            }

            _buildInfo = _buildServerClient.ValidateBuildAsync(
                project.ArtefactsUrl, project.ArtefactsSubPaths, project.ArtefactsBuildRegex,
                dorcBuild.BuildText, dorcBuild.BuildNum, dorcBuild.VstsUrl,
                dorcBuild.Pinned ?? false)
                .ConfigureAwait(false).GetAwaiter().GetResult();

            if (_buildInfo == null)
            {
                _validationMessage = "No matching GitHub Actions workflow run found";
                _log.LogWarning(_validationMessage);
                return false;
            }

            _log.LogDebug("Successfully validated GitHub Actions build.");
            return true;
        }

        public string ValidationResult => _validationMessage;

        public RequestStatusDto Process(RequestDto request, ClaimsPrincipal user)
        {
            if (_buildInfo == null)
                return null;

            var requestId = _deployLibrary.SubmitRequest(
                request.Project,
                request.Environment,
                _buildInfo.BuildUri,
                _buildInfo.DefinitionName,
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
                return new RequestStatusDto
                {
                    Id = 0,
                    Status = "DeployLibrary has returned zero result"
                };
            }

            return _requestsPersistentSource.GetRequestStatus(id);
        }
    }
}
