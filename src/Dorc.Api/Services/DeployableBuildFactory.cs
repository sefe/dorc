using Dorc.Api.Interfaces;
using Dorc.Api.Model;
using Dorc.ApiModel;
using Dorc.Core.AzureDevOpsServer;
using Dorc.Core.BuildServer;
using Dorc.Core.Interfaces;
using Dorc.PersistentData.Sources.Interfaces;
using Microsoft.Extensions.Logging;

namespace Dorc.Api.Services
{
    public class DeployableBuildFactory : IDeployableBuildFactory
    {
        private readonly IFileSystemHelper _fileSystemHelper;
        private readonly ILoggerFactory _loggerFactory;
        private readonly IProjectsPersistentSource _projectsPersistentSource;
        private readonly IDeployLibrary _deployLibrary;
        private readonly IRequestsPersistentSource _requestsPersistentSource;
        private readonly Func<GitHubDeployableBuild> _gitHubDeployableBuildFactory;

        public DeployableBuildFactory(IFileSystemHelper fileSystemHelper, ILoggerFactory loggerFactory,
            IProjectsPersistentSource projectsPersistentSource, IDeployLibrary deployLibrary,
            IRequestsPersistentSource requestsPersistentSource,
            Func<GitHubDeployableBuild> gitHubDeployableBuildFactory)
        {
            _requestsPersistentSource = requestsPersistentSource;
            _deployLibrary = deployLibrary;
            _projectsPersistentSource = projectsPersistentSource;
            _loggerFactory = loggerFactory;
            _fileSystemHelper = fileSystemHelper;
            _gitHubDeployableBuildFactory = gitHubDeployableBuildFactory;
        }
        public IDeployableBuild CreateInstance(RequestDto request)
        {
            var project = _projectsPersistentSource.GetProject(request.Project);
            if (project == null)
                return null;

            var sourceControlType = project.SourceControlType;
            var buildDetail = new BuildDetails(request, sourceControlType);

            switch (buildDetail.Type)
            {
                case BuildType.FileShareBuild:
                    return new FileShareDeployableBuild(_fileSystemHelper, _deployLibrary, _requestsPersistentSource);

                case BuildType.TfsBuild:
                    {
                        var tfsUrl = project.ArtefactsUrl;
                        var webClientLogger = _loggerFactory.CreateLogger<AzureDevOpsServerWebClient>();
                        var buildLogger = _loggerFactory.CreateLogger<AzureDevOpsDeployableBuild>();
                        return new AzureDevOpsDeployableBuild(new AzureDevOpsServerWebClient(tfsUrl, webClientLogger), buildLogger, _projectsPersistentSource, _deployLibrary, _requestsPersistentSource);
                    }

                case BuildType.GitHubBuild:
                    return _gitHubDeployableBuildFactory();

                case BuildType.UnknownBuildType:
                default:
                    return null;
            }
        }
    }
}