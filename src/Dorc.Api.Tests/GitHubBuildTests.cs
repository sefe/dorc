using Dorc.Api.Model;
using Dorc.Api.Services;
using Dorc.ApiModel;
using Dorc.Core.BuildServer;
using Dorc.Core.Interfaces;
using Dorc.PersistentData.Sources.Interfaces;
using NSubstitute;

namespace Dorc.Api.Tests
{
    [TestClass]
    public class GitHubBuildTests
    {
        private IBuildServerClient _mockedBuildServerClient = null!;
        private IProjectsPersistentSource _mockedProjectsPds = null!;
        private IDeployLibrary _mockedDeployLibrary = null!;
        private IRequestsPersistentSource _mockedReqPs = null!;
        private MockedLog<GitHubDeployableBuild> _mockedLog = null!;

        [TestInitialize]
        public void Setup()
        {
            _mockedBuildServerClient = Substitute.For<IBuildServerClient>();
            _mockedProjectsPds = Substitute.For<IProjectsPersistentSource>();
            _mockedDeployLibrary = Substitute.For<IDeployLibrary>();
            _mockedReqPs = Substitute.For<IRequestsPersistentSource>();
            _mockedLog = new MockedLog<GitHubDeployableBuild>();

            _mockedProjectsPds.GetProject(Arg.Any<string>())
                .Returns(new ProjectApiModel
                {
                    ProjectName = "myGitHubProject",
                    ArtefactsUrl = "https://api.github.com/repos/owner/repo",
                    ArtefactsSubPaths = "build.yml",
                    ArtefactsBuildRegex = ".*",
                    SourceControlType = SourceControlType.GitHub
                });
        }

        private GitHubDeployableBuild CreateSut()
        {
            return new GitHubDeployableBuild(_mockedBuildServerClient, _mockedLog,
                _mockedProjectsPds, _mockedDeployLibrary, _mockedReqPs);
        }

        [TestMethod]
        public void IsValid_GitHubBuildType_ValidRun_ReturnsTrue()
        {
            var request = new RequestDto
            {
                BuildText = "CI Build",
                BuildNum = "Run #42",
                Project = "myGitHubProject",
                BuildUrl = "https://api.github.com/repos/owner/repo",
                Pinned = false
            };

            _mockedBuildServerClient.ValidateBuildAsync(
                    Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
                    Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<bool>(),
                    Arg.Any<CancellationToken>())
                .Returns(Task.FromResult<BuildServerBuildInfo?>(new BuildServerBuildInfo
                {
                    BuildUri = "12345678",
                    ProjectName = "CI Build",
                    DefinitionName = "CI Build",
                    BuildId = 12345678,
                    BuildNumber = "Run #42"
                }));

            var sut = CreateSut();
            var buildDetails = new BuildDetails(request, SourceControlType.GitHub);
            var result = sut.IsValid(buildDetails);

            Assert.IsTrue(result);
        }

        [TestMethod]
        public void IsValid_GitHubBuildType_NoMatchingRun_ReturnsFalse()
        {
            var request = new RequestDto
            {
                BuildText = "CI Build",
                BuildNum = "NonExistentRun",
                Project = "myGitHubProject",
                BuildUrl = "https://api.github.com/repos/owner/repo",
                Pinned = false
            };

            _mockedBuildServerClient.ValidateBuildAsync(
                    Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
                    Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<bool>(),
                    Arg.Any<CancellationToken>())
                .Returns(Task.FromResult<BuildServerBuildInfo?>(null));

            var sut = CreateSut();
            var buildDetails = new BuildDetails(request, SourceControlType.GitHub);
            var result = sut.IsValid(buildDetails);

            Assert.IsFalse(result);
            Assert.AreEqual("No matching GitHub Actions workflow run found", sut.ValidationResult);
        }

        [TestMethod]
        public void IsValid_WrongBuildType_ReturnsFalse()
        {
            var request = new RequestDto
            {
                BuildText = "CI Build",
                BuildNum = "Run #42",
                Project = "myGitHubProject",
                BuildUrl = "http://tfs/build",
                Pinned = false
            };

            var sut = CreateSut();
            // Default SourceControlType (AzureDevOps) so type will be TfsBuild, not GitHubBuild
            var buildDetails = new BuildDetails(request);
            var result = sut.IsValid(buildDetails);

            Assert.IsFalse(result);
            Assert.AreEqual("Failed Build Type Check - expected GitHub build", sut.ValidationResult);
        }

        [TestMethod]
        public void IsValid_NumericRunId_ValidatesCorrectly()
        {
            var request = new RequestDto
            {
                BuildText = "CI Build",
                BuildNum = "Run #42",
                Project = "myGitHubProject",
                BuildUrl = "9876543210",
                Pinned = false
            };

            _mockedBuildServerClient.ValidateBuildAsync(
                    Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
                    Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<bool>(),
                    Arg.Any<CancellationToken>())
                .Returns(Task.FromResult<BuildServerBuildInfo?>(new BuildServerBuildInfo
                {
                    BuildUri = "9876543210",
                    ProjectName = "CI Build",
                    DefinitionName = "CI Build",
                    BuildId = 9876543210L,
                    BuildNumber = "Run #42"
                }));

            var sut = CreateSut();
            var buildDetails = new BuildDetails(request, SourceControlType.GitHub);
            var result = sut.IsValid(buildDetails);

            Assert.IsTrue(result);
        }
    }
}
