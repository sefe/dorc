using Dorc.Api.Build;
using Dorc.Api.Interfaces;
using Dorc.ApiModel;
using Dorc.Core.Interfaces;
using Dorc.PersistentData.Sources.Interfaces;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Dorc.Api.Tests
{
    [TestClass]
    public class BuildFactoryTests
    {
        [TestMethod]
        public void CreateInstanceTest()
        {
            var mockedProjectsPds = Substitute.For<IProjectsPersistentSource>();
            var mockedDeployLibrary = Substitute.For<IDeployLibrary>();

            var project = new ProjectApiModel { ProjectName = "myProject", ArtefactsUrl = "https://tfs/tfs/org/" };

            mockedProjectsPds.GetProject(Arg.Any<string>()).Returns(project);
            var mockedFileSystemHelper = Substitute.For<IFileOperations>();
            var request = new RequestDto { BuildUrl = "file://some_path", Project = "myProject" };

            ILoggerFactory mockedLoggerFactory = Substitute.For<ILoggerFactory>();
            var mockedReqPs = Substitute.For<IRequestsPersistentSource>();
            IDeployableBuildFactory factory = new DeployableBuildFactory(mockedFileSystemHelper, mockedLoggerFactory, mockedProjectsPds, mockedDeployLibrary, mockedReqPs);
            var fileShareBuild = factory.CreateInstance(request);
            Assert.IsTrue(fileShareBuild is FileShareDeployableBuild);
            request = new RequestDto { BuildUrl = "http://some_path", Project = "myProject" };

            var tfsBuild = factory.CreateInstance(request);
            Assert.IsTrue(tfsBuild is AzureDevOpsDeployableBuild);
            request = new RequestDto { BuildUrl = "ftp://some_path", Project = "myProject" };
            var unknownBuild = factory.CreateInstance(request);
            Assert.IsNull(unknownBuild);
        }
    }
}