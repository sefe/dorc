using System.Security.Claims;
using Dorc.Api.Interfaces;
using Dorc.Api.Model;
using Dorc.ApiModel;
using Dorc.Core.BuildServer;
using Dorc.Core.Interfaces;
using Dorc.PersistentData.Sources.Interfaces;

namespace Dorc.Api.Services
{
    /// <remarks>
    /// This class follows the same stateful pattern as <see cref="AzureDevOpsDeployableBuild"/>:
    /// <see cref="IsValid"/> stores validated build info that <see cref="Process"/> later uses.
    /// Instances are created per-request by <see cref="DeployableBuildFactory"/> and must NOT be
    /// shared across threads or reused.
    /// </remarks>
    public class GitHubDeployableBuild : IDeployableBuild
    {
        private readonly IBuildServerClient _buildServerClient;
        private readonly ILogger _log;
        private readonly IProjectsPersistentSource _projectsPersistentSource;
        private readonly IDeployLibrary _deployLibrary;
        private readonly IRequestsPersistentSource _requestsPersistentSource;
        private string _validationMessage = string.Empty;
        private string _buildUri = string.Empty;
        private string _definitionName = string.Empty;

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
                _validationMessage = "Project not found";
                _log.LogWarning("GitHub build validation failed: project not found");
                return false;
            }

            BuildServerBuildInfo? buildInfo;
            try
            {
                buildInfo = _buildServerClient.ValidateBuildAsync(
                    project.ArtefactsUrl, project.ArtefactsSubPaths, project.ArtefactsBuildRegex,
                    dorcBuild.BuildText, dorcBuild.BuildNum, dorcBuild.VstsUrl,
                    dorcBuild.Pinned ?? false)
                    .ConfigureAwait(false).GetAwaiter().GetResult();
            }
            catch (HttpRequestException ex)
            {
                _validationMessage = $"GitHub API request failed: {ex.StatusCode} — {ex.Message}";
                _log.LogWarning(ex, "GitHub build validation failed with HTTP error");
                return false;
            }
            catch (ArgumentException ex)
            {
                _validationMessage = $"GitHub build validation configuration error: {ex.Message}";
                _log.LogWarning(ex, "GitHub build validation failed due to invalid configuration");
                return false;
            }

            if (buildInfo == null)
            {
                _validationMessage = "No matching GitHub Actions workflow run found";
                _log.LogWarning(_validationMessage);
                return false;
            }

            _buildUri = buildInfo.BuildUri;
            _definitionName = buildInfo.DefinitionName;

            _log.LogDebug("Successfully validated GitHub Actions build.");
            return true;
        }

        public string ValidationResult => _validationMessage;

        public RequestStatusDto Process(RequestDto request, ClaimsPrincipal user)
        {
            if (string.IsNullOrEmpty(_buildUri))
                return new RequestStatusDto { Id = 0, Status = "Build validation must succeed before processing" };

            var requestId = _deployLibrary.SubmitRequest(
                request.Project,
                request.Environment,
                _buildUri,
                _definitionName,
                request.Components.ToList(),
                request.RequestProperties.ToList(),
                user);

            int id;
            try
            {
                id = Convert.ToInt32(requestId);
            }
            catch (Exception e) when (e is FormatException or OverflowException or InvalidCastException or ArgumentNullException)
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
