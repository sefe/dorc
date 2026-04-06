using Dorc.Api.Interfaces;
using Dorc.Api.Services;
using Dorc.ApiModel;
using Dorc.Core.BuildServer;
using Dorc.Core.Interfaces;
using Dorc.PersistentData.Sources.Interfaces;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Dorc.Api.Tests
{
    [TestClass]
    public class BuildFactoryTests
    {
        private IProjectsPersistentSource _mockedProjectsPds = null!;
        private IDeployLibrary _mockedDeployLibrary = null!;
        private IFileSystemHelper _mockedFileSystemHelper = null!;
        private ILoggerFactory _mockedLoggerFactory = null!;
        private IRequestsPersistentSource _mockedReqPs = null!;
        private IBuildServerClientFactory _mockedBuildServerClientFactory = null!;

        [TestInitialize]
        public void Setup()
        {
            _mockedProjectsPds = Substitute.For<IProjectsPersistentSource>();
            _mockedDeployLibrary = Substitute.For<IDeployLibrary>();
            _mockedFileSystemHelper = Substitute.For<IFileSystemHelper>();
            _mockedLoggerFactory = Substitute.For<ILoggerFactory>();
            _mockedReqPs = Substitute.For<IRequestsPersistentSource>();
            _mockedBuildServerClientFactory = Substitute.For<IBuildServerClientFactory>();
            _mockedBuildServerClientFactory.Create(SourceControlType.GitHub)
                .Returns(Substitute.For<IBuildServerClient>());
        }

        private DeployableBuildFactory CreateFactory()
        {
            return new DeployableBuildFactory(_mockedFileSystemHelper, _mockedLoggerFactory,
                _mockedProjectsPds, _mockedDeployLibrary, _mockedReqPs, _mockedBuildServerClientFactory);
        }

        [TestMethod]
        public void CreateInstanceTest()
        {
            var project = new ProjectApiModel { ProjectName = "myProject", ArtefactsUrl = "https://tfs/tfs/org/" };
            _mockedProjectsPds.GetProject(Arg.Any<string>()).Returns(project);

            var factory = CreateFactory();

            var fileShareBuild = factory.CreateInstance(new RequestDto { BuildUrl = "file://some_path", Project = "myProject" });
            Assert.IsTrue(fileShareBuild is FileShareDeployableBuild);

            var tfsBuild = factory.CreateInstance(new RequestDto { BuildUrl = "http://some_path", Project = "myProject" });
            Assert.IsTrue(tfsBuild is AzureDevOpsDeployableBuild);

            var unknownBuild = factory.CreateInstance(new RequestDto { BuildUrl = "ftp://some_path", Project = "myProject" });
            Assert.IsNull(unknownBuild);
        }

        [TestMethod]
        public void CreateInstance_GitHubProject_HttpUrl_ReturnsGitHubBuild()
        {
            var project = new ProjectApiModel
            {
                ProjectName = "myProject",
                ArtefactsUrl = "https://api.github.com/repos/owner/repo",
                SourceControlType = SourceControlType.GitHub
            };
            _mockedProjectsPds.GetProject(Arg.Any<string>()).Returns(project);

            var factory = CreateFactory();
            var build = factory.CreateInstance(new RequestDto { BuildUrl = "https://api.github.com/repos/owner/repo", Project = "myProject" });
            Assert.IsTrue(build is GitHubDeployableBuild);
        }

        [TestMethod]
        public void CreateInstance_GitHubProject_NumericRunId_ReturnsGitHubBuild()
        {
            var project = new ProjectApiModel
            {
                ProjectName = "myProject",
                ArtefactsUrl = "https://api.github.com/repos/owner/repo",
                SourceControlType = SourceControlType.GitHub
            };
            _mockedProjectsPds.GetProject(Arg.Any<string>()).Returns(project);

            var factory = CreateFactory();
            var build = factory.CreateInstance(new RequestDto { BuildUrl = "12345678901", Project = "myProject" });
            Assert.IsTrue(build is GitHubDeployableBuild);
        }

        [TestMethod]
        public void CreateInstance_NullProject_ReturnsNull()
        {
            _mockedProjectsPds.GetProject(Arg.Any<string>()).Returns((ProjectApiModel?)null);

            var factory = CreateFactory();
            var build = factory.CreateInstance(new RequestDto { BuildUrl = "http://some_path", Project = "nonexistent" });
            Assert.IsNull(build);
        }
    }
}