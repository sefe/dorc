using Dorc.Api.Interfaces;
using Dorc.Api.Model;
using Dorc.Api.Services;
using Dorc.ApiModel;
using Dorc.Core.Interfaces;
using Dorc.PersistentData.Sources.Interfaces;
using NSubstitute;

namespace Dorc.Api.Tests
{
    [TestClass]
    public class FileShareBuildTests
    {
        //build exist and has correct type
        [TestMethod]
        public void FileShareBuildTest1()
        {
            RequestDto request = new RequestDto
            {
                BuildUrl = "file://some_path",
                BuildText = "buildText",
                BuildNum = "buildNUm",
            };
            var mockedReqPs = Substitute.For<IRequestsPersistentSource>();

            var mockedHelper = Substitute.For<IFileSystemHelper>();
            mockedHelper.DirectoryExists(Arg.Any<string>())
                .Returns(true);
            var buildDetails = new BuildDetails(request.BuildUrl);
            var mockedDeployLibrary = Substitute.For<IDeployLibrary>();
            var fsBuild = new FileShareDeployableBuild(mockedHelper, mockedDeployLibrary, mockedReqPs);
            var result = fsBuild.IsValid(buildDetails);
            Assert.IsTrue(result);
        }

        // build exists but wrong type
        [TestMethod]
        public void FileShareBuildTest2()
        {
            RequestDto request = new RequestDto
            {
                BuildUrl = "ftp://some_path",
                BuildText = "buildText",
                BuildNum = "buildNUm",
            };
            var mockedReqPs = Substitute.For<IRequestsPersistentSource>();

            var mockedHelper = Substitute.For<IFileSystemHelper>();
            mockedHelper.DirectoryExists(Arg.Any<string>())
                .Returns(true);
            var buildDetails = new BuildDetails(request.BuildUrl);
            var mockedDeployLibrary = Substitute.For<IDeployLibrary>();
            var fsBuild = new FileShareDeployableBuild(mockedHelper, mockedDeployLibrary, mockedReqPs);
            var result = fsBuild.IsValid(buildDetails);
            Assert.IsFalse(result);
        }

        // build doesnt exists and correct type
        [TestMethod]
        public void FileShareBuildTest3()
        {
            RequestDto request = new RequestDto
            {
                BuildUrl = "file://some_path",
                BuildText = "buildText",
                BuildNum = "buildNUm",
            };
            var mockedReqPs = Substitute.For<IRequestsPersistentSource>();

            var mockedHelper = Substitute.For<IFileSystemHelper>();
            mockedHelper.DirectoryExists(Arg.Any<string>())
                .Returns(false);
            var buildDetails = new BuildDetails(request.BuildUrl);
            var mockedDeployLibrary = Substitute.For<IDeployLibrary>();
            var fsBuild = new FileShareDeployableBuild(mockedHelper, mockedDeployLibrary, mockedReqPs);
            var result = fsBuild.IsValid(buildDetails);
            Assert.IsFalse(result);
        }

        // build doesnt exists and wrong type
        [TestMethod]
        public void FileShareBuildTest4()
        {
            RequestDto request = new RequestDto
            {
                BuildUrl = "http://some_path",
                BuildText = "buildText",
                BuildNum = "buildNUm",
            };
            var mockedReqPs = Substitute.For<IRequestsPersistentSource>();

            var mockedHelper = Substitute.For<IFileSystemHelper>();
            mockedHelper.DirectoryExists(Arg.Any<string>())
                .Returns(false);
            var buildDetails = new BuildDetails(request.BuildUrl);
            var mockedDeployLibrary = Substitute.For<IDeployLibrary>();
            var fsBuild = new FileShareDeployableBuild(mockedHelper, mockedDeployLibrary, mockedReqPs);
            var result = fsBuild.IsValid(buildDetails);
            Assert.IsFalse(result);
        }

        // build ArtefactsUrl not properly formed
        [TestMethod]
        public void FileShareBuildTest5()
        {
            RequestDto request = new RequestDto
            {
                BuildUrl = "file:\\some_path",
                BuildText = "buildText",
                BuildNum = "buildNUm",
            };
            var mockedReqPs = Substitute.For<IRequestsPersistentSource>();

            var mockedHelper = Substitute.For<IFileSystemHelper>();
            mockedHelper.DirectoryExists(Arg.Any<string>())
                .Returns(false);
            var buildDetails = new BuildDetails(request.BuildUrl);
            var mockedDeployLibrary = Substitute.For<IDeployLibrary>();
            var fsBuild = new FileShareDeployableBuild(mockedHelper, mockedDeployLibrary, mockedReqPs);
            var result = fsBuild.IsValid(buildDetails);
            Assert.IsFalse(result);
        }
    }
}