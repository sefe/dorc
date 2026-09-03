using System.Security.Claims;
using Dorc.Api.Interfaces;
using Dorc.Api.Model;
using Dorc.ApiModel;
using Dorc.Core;
using Dorc.PersistentData.Sources.Interfaces;

namespace Dorc.Api.Services
{
    public class RequestService : IRequestService
    {
        private readonly IDeployableBuildFactory _deployableBuildFactory;
        private readonly ILogger _log;
        private readonly IProjectsPersistentSource _projectsPersistentSource;

        public RequestService(ILogger<RequestService> log, IDeployableBuildFactory deployableBuildFactory,
            IProjectsPersistentSource projectsPersistentSource)
        {
            _projectsPersistentSource = projectsPersistentSource;
            _log = log;
            _deployableBuildFactory = deployableBuildFactory;
        }

        public RequestStatusDto CreateRequest(RequestDto request, ClaimsPrincipal user)
        {
            CheckRequest(ref request);
            var build = _deployableBuildFactory.CreateInstance(request);
            if (build == null)
            {
                _log.LogError("Wrong build type for project {Project} with build URL {BuildUrl}",
                    LogSanitizer.Sanitize(request?.Project), LogSanitizer.Sanitize(request?.BuildUrl));
                throw new WrongBuildTypeException($"Wrong build type. BuildUrl should start with 'http', 'file', or be a numeric run ID (GitHub), but got {request.BuildUrl}");
            }

            if (request.BuildNum != null && request.BuildNum.Contains(" [PINNED]"))
                request.BuildNum = request.BuildNum.Replace(" [PINNED]", "");

            var project = _projectsPersistentSource.GetProject(request.Project);
            var sourceControlType = project?.SourceControlType ?? SourceControlType.AzureDevOps;
            if (build.IsValid(new BuildDetails(request, sourceControlType)))
                return build.Process(request, user);

            _log.LogError("Build validation failed. {ValidationResult}", LogSanitizer.Sanitize(build.ValidationResult));
            throw new WrongBuildTypeException($"Build validation failed. {build.ValidationResult}");
        }

        public void CheckRequest(ref RequestDto request)
        {
            if (!string.IsNullOrEmpty(request.BuildUrl)) return;
            var projectName = request.Project;
            var project = _projectsPersistentSource.GetProject(projectName);
            if (project == null)
            {
                _log.LogError("Unable to locate a project with the name {ProjectName}", LogSanitizer.Sanitize(projectName));
                throw new Exception($"Unable to locate a project with the name '{projectName}'");
            }
            request.BuildUrl = project.ArtefactsUrl.Split(';')[0];
        }
    }
}