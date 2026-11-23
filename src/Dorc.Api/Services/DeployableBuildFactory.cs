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
        private readonly IDeploymentRequestSubmitter _requestSubmitter;
        private readonly IRequestsPersistentSource _requestsPersistentSource;
        private readonly Func<GitHubDeployableBuild> _gitHubDeployableBuildFactory;

        public DeployableBuildFactory(IFileSystemHelper fileSystemHelper, ILoggerFactory loggerFactory,
            IProjectsPersistentSource projectsPersistentSource, IDeploymentRequestSubmitter requestSubmitter,
            IRequestsPersistentSource requestsPersistentSource,
            Func<GitHubDeployableBuild> gitHubDeployableBuildFactory)
        {
            _requestsPersistentSource = requestsPersistentSource;
            _requestSubmitter = requestSubmitter;
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
                    return new FileShareDeployableBuild(_fileSystemHelper, _requestSubmitter, _requestsPersistentSource, _projectsPersistentSource);

                case BuildType.AzureDevOpsBuild:
                    {
                        var azureDevOpsUrl = project.ArtefactsUrl;
                        var webClientLogger = _loggerFactory.CreateLogger<AzureDevOpsServerWebClient>();
                        var buildLogger = _loggerFactory.CreateLogger<AzureDevOpsDeployableBuild>();
                        return new AzureDevOpsDeployableBuild(new AzureDevOpsServerWebClient(azureDevOpsUrl, webClientLogger), buildLogger, _projectsPersistentSource, _requestSubmitter, _requestsPersistentSource);
                    }

                case BuildType.GitHubBuild:
                    return _gitHubDeployableBuildFactory();

                case BuildType.Catalog:
                    return new CatalogDeployableBuild(_requestSubmitter, _requestsPersistentSource);

                case BuildType.UnknownBuildType:
                default:
                    return null;
            }
        }
    }
}