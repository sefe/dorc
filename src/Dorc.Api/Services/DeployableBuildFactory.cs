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
        private readonly IBuildServerClientFactory _buildServerClientFactory;

        public DeployableBuildFactory(IFileSystemHelper fileSystemHelper, ILoggerFactory loggerFactory,
            IProjectsPersistentSource projectsPersistentSource, IDeployLibrary deployLibrary,
            IRequestsPersistentSource requestsPersistentSource,
            IBuildServerClientFactory buildServerClientFactory)
        {
            _requestsPersistentSource = requestsPersistentSource;
            _deployLibrary = deployLibrary;
            _projectsPersistentSource = projectsPersistentSource;
            _loggerFactory = loggerFactory;
            _fileSystemHelper = fileSystemHelper;
            _buildServerClientFactory = buildServerClientFactory;
        }
        public IDeployableBuild CreateInstance(RequestDto request)
        {
            var project = _projectsPersistentSource.GetProject(request.Project);
            var sourceControlType = project?.SourceControlType ?? SourceControlType.AzureDevOps;
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
                    {
                        var gitHubClient = _buildServerClientFactory.Create(SourceControlType.GitHub);
                        var buildLogger = _loggerFactory.CreateLogger<GitHubDeployableBuild>();
                        return new GitHubDeployableBuild(gitHubClient, buildLogger, _projectsPersistentSource, _deployLibrary, _requestsPersistentSource);
                    }

                case BuildType.UnknownBuildType:
                default:
                    return null;
            }
        }
    }
}