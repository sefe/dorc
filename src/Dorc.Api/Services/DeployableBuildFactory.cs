using Dorc.Api.Interfaces;
using Dorc.Api.Model;
using Dorc.ApiModel;
using Dorc.Core.AzureDevOpsServer;
using Dorc.Core.Interfaces;
using Dorc.PersistentData.Sources.Interfaces;
using Microsoft.Extensions.Logging;

namespace Dorc.Api.Services
{
    public class DeployableBuildFactory : IDeployableBuildFactory
    {
        private readonly IFileSystemHelper _fileSystemHelper;
        private readonly ILogger _log;
        private readonly IProjectsPersistentSource _projectsPersistentSource;
        private readonly IDeployLibrary _deployLibrary;
        private readonly IRequestsPersistentSource _requestsPersistentSource;

        public DeployableBuildFactory(IFileSystemHelper fileSystemHelper, ILogger log,
            IProjectsPersistentSource projectsPersistentSource, IDeployLibrary deployLibrary,
            IRequestsPersistentSource requestsPersistentSource)
        {
            _requestsPersistentSource = requestsPersistentSource;
            _deployLibrary = deployLibrary;
            _projectsPersistentSource = projectsPersistentSource;
            _log = log;
            _fileSystemHelper = fileSystemHelper;
        }
        public IDeployableBuild CreateInstance(RequestDto request)
        {
            var buildDetail = new BuildDetails(request);
            switch (buildDetail.Type)
            {
                case BuildType.FileShareBuild:

                    return new FileShareDeployableBuild(_fileSystemHelper, _deployLibrary, _requestsPersistentSource);
                case BuildType.TfsBuild:
                    {
                        var project = _projectsPersistentSource.GetProject(request.Project);
                        var tfsUrl = project.ArtefactsUrl;
                        return new AzureDevOpsDeployableBuild(new AzureDevOpsServerWebClient(tfsUrl, _log), _log, _projectsPersistentSource, _deployLibrary, _requestsPersistentSource);
                    }
                case BuildType.UnknownBuildType:
                    {
                        return null;
                    }
                default:
                    return null;
            }
        }
    }
}