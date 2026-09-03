using Dorc.ApiModel;
using Dorc.Core.BuildServer;
using Dorc.Core.Models;
using Dorc.PersistentData.Sources.Interfaces;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Dorc.Core.Tests
{
    [TestClass]
    public class RequestsManagerTests
    {
        private ILoggerFactory _loggerFactory = null!;
        private IProjectsPersistentSource _projectsPds = null!;
        private IComponentsPersistentSource _componentsPds = null!;
        private IEnvironmentsPersistentSource _environmentsPds = null!;
        private IBuildServerClientFactory _buildServerClientFactory = null!;
        private IBuildServerClient _mockBuildClient = null!;

        [TestInitialize]
        public void Setup()
        {
            _loggerFactory = Substitute.For<ILoggerFactory>();
            _loggerFactory.CreateLogger(Arg.Any<string>()).Returns(Substitute.For<ILogger>());
            _projectsPds = Substitute.For<IProjectsPersistentSource>();
            _componentsPds = Substitute.For<IComponentsPersistentSource>();
            _environmentsPds = Substitute.For<IEnvironmentsPersistentSource>();
            _buildServerClientFactory = Substitute.For<IBuildServerClientFactory>();
            _mockBuildClient = Substitute.For<IBuildServerClient>();
            _buildServerClientFactory.Create(Arg.Any<SourceControlType>()).Returns(_mockBuildClient);
        }

        private RequestsManager CreateSut()
        {
            return new RequestsManager(_loggerFactory, _projectsPds, _componentsPds,
                _environmentsPds, _buildServerClientFactory);
        }

        // --- GetBuildDefinitions ---

        [TestMethod]
        public void GetBuildDefinitions_NullProject_ReturnsEmpty()
        {
            var sut = CreateSut();
            var result = sut.GetBuildDefinitions(null);
            Assert.IsFalse(result.Any());
        }

        [TestMethod]
        public void GetBuildDefinitions_FileShareProject_ReturnsNotCiCdMessage()
        {
            var project = new ProjectApiModel
            {
                ArtefactsUrl = "file://some/path",
                SourceControlType = SourceControlType.AzureDevOps
            };

            var sut = CreateSut();
            var result = sut.GetBuildDefinitions(project).ToList();

            Assert.AreEqual(1, result.Count);
            Assert.AreEqual("Not a CI/CD Server Project", result[0].Name);
        }

        [TestMethod]
        public void GetBuildDefinitions_HttpProject_AzureDevOps_DelegatesToFactory()
        {
            var project = new ProjectApiModel
            {
                ArtefactsUrl = "https://dev.azure.com/org",
                ArtefactsSubPaths = "MyProject",
                ArtefactsBuildRegex = ".*",
                SourceControlType = SourceControlType.AzureDevOps
            };

            _mockBuildClient.GetDefinitions(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>())
                .Returns(new List<DeployableArtefact> { new() { Id = "1", Name = "Build1" } });

            var sut = CreateSut();
            var result = sut.GetBuildDefinitions(project).ToList();

            Assert.AreEqual(1, result.Count);
            Assert.AreEqual("Build1", result[0].Name);
            _buildServerClientFactory.Received(1).Create(SourceControlType.AzureDevOps);
        }

        [TestMethod]
        public void GetBuildDefinitions_HttpProject_GitHub_DelegatesToFactory()
        {
            var project = new ProjectApiModel
            {
                ArtefactsUrl = "https://api.github.com/repos/owner/repo",
                ArtefactsSubPaths = "build.yml",
                ArtefactsBuildRegex = ".*",
                SourceControlType = SourceControlType.GitHub
            };

            _mockBuildClient.GetDefinitions(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>())
                .Returns(new List<DeployableArtefact> { new() { Id = "42", Name = "CI Workflow" } });

            var sut = CreateSut();
            var result = sut.GetBuildDefinitions(project).ToList();

            Assert.AreEqual(1, result.Count);
            Assert.AreEqual("CI Workflow", result[0].Name);
            _buildServerClientFactory.Received(1).Create(SourceControlType.GitHub);
        }

        // --- GetBuildsAsync ---

        [TestMethod]
        public async Task GetBuildsAsync_NullProjectId_ReturnsEmpty()
        {
            var sut = CreateSut();
            var result = await sut.GetBuildsAsync(null, "env", "def");
            Assert.IsFalse(result.Any());
        }

        [TestMethod]
        public async Task GetBuildsAsync_EmptyDefinitionName_ReturnsEmpty()
        {
            var sut = CreateSut();
            var result = await sut.GetBuildsAsync(1, "env", "");
            Assert.IsFalse(result.Any());
        }

        [TestMethod]
        public async Task GetBuildsAsync_HttpProject_DelegatesToBuildClient()
        {
            var project = new ProjectApiModel
            {
                ArtefactsUrl = "https://dev.azure.com/org",
                ArtefactsSubPaths = "MyProject",
                ArtefactsBuildRegex = ".*",
                SourceControlType = SourceControlType.AzureDevOps
            };
            _projectsPds.GetProject(1).Returns(project);
            _environmentsPds.EnvironmentIsProd("dev").Returns(false);
            _environmentsPds.EnvironmentIsSecure("dev").Returns(false);

            _mockBuildClient.GetBuildsAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
                    Arg.Any<string>(), false, Arg.Any<CancellationToken>())
                .Returns(new List<DeployableArtefact>
                {
                    new() { Id = "1", Name = "Build 1", Date = DateTime.Now },
                    new() { Id = "2", Name = "Build 2", Date = DateTime.Now.AddHours(-1) }
                });

            var sut = CreateSut();
            var result = (await sut.GetBuildsAsync(1, "dev", "MyProject; MyDef")).ToList();

            Assert.AreEqual(2, result.Count);
            _buildServerClientFactory.Received(1).Create(SourceControlType.AzureDevOps);
        }

        [TestMethod]
        public async Task GetBuildsAsync_ProdEnvironment_PassesPinnedTrue()
        {
            var project = new ProjectApiModel
            {
                ArtefactsUrl = "https://dev.azure.com/org",
                ArtefactsSubPaths = "MyProject",
                ArtefactsBuildRegex = ".*",
                SourceControlType = SourceControlType.AzureDevOps
            };
            _projectsPds.GetProject(1).Returns(project);
            _environmentsPds.EnvironmentIsProd("prod").Returns(true);

            _mockBuildClient.GetBuildsAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
                    Arg.Any<string>(), true, Arg.Any<CancellationToken>())
                .Returns(new List<DeployableArtefact>());

            var sut = CreateSut();
            await sut.GetBuildsAsync(1, "prod", "MyProject; MyDef");

            await _mockBuildClient.Received(1).GetBuildsAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<string>(), true, Arg.Any<CancellationToken>());
        }

        [TestMethod]
        public async Task GetBuildsAsync_GitHubProject_DelegatesToGitHubClient()
        {
            var project = new ProjectApiModel
            {
                ArtefactsUrl = "https://api.github.com/repos/owner/repo",
                ArtefactsSubPaths = "build.yml",
                ArtefactsBuildRegex = ".*",
                SourceControlType = SourceControlType.GitHub
            };
            _projectsPds.GetProject(1).Returns(project);
            _environmentsPds.EnvironmentIsProd("dev").Returns(false);
            _environmentsPds.EnvironmentIsSecure("dev").Returns(false);

            _mockBuildClient.GetBuildsAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
                    Arg.Any<string>(), false, Arg.Any<CancellationToken>())
                .Returns(new List<DeployableArtefact> { new() { Id = "100", Name = "Run #1" } });

            var sut = CreateSut();
            var result = (await sut.GetBuildsAsync(1, "dev", "CI Build")).ToList();

            Assert.AreEqual(1, result.Count);
            _buildServerClientFactory.Received(1).Create(SourceControlType.GitHub);
        }

        [TestMethod]
        public async Task GetBuildsAsync_CancelledToken_PropagatesCancellation()
        {
            var project = new ProjectApiModel
            {
                ArtefactsUrl = "https://dev.azure.com/org",
                ArtefactsSubPaths = "MyProject",
                ArtefactsBuildRegex = ".*",
                SourceControlType = SourceControlType.AzureDevOps
            };
            _projectsPds.GetProject(1).Returns(project);
            _environmentsPds.EnvironmentIsProd("dev").Returns(false);
            _environmentsPds.EnvironmentIsSecure("dev").Returns(false);

            _mockBuildClient.GetBuildsAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
                    Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
                .Returns(callInfo =>
                {
                    var ct = callInfo.Arg<CancellationToken>();
                    ct.ThrowIfCancellationRequested();
                    return new List<DeployableArtefact>();
                });

            using var cts = new CancellationTokenSource();
            cts.Cancel();

            var sut = CreateSut();
            await Assert.ThrowsAsync<OperationCanceledException>(
                () => sut.GetBuildsAsync(1, "dev", "MyProject; MyDef", cts.Token));
        }

        // --- GetComponents ---

        [TestMethod]
        public void GetComponents_NullProjectId_ReturnsEmpty()
        {
            var sut = CreateSut();
            var result = sut.GetComponents(null);
            Assert.IsFalse(result.Any());
        }

        [TestMethod]
        public void GetComponents_WithParentId_NullProjectId_ReturnsEmpty()
        {
            var sut = CreateSut();
            var result = sut.GetComponents(null, 1);
            Assert.IsFalse(result.Any());
        }

        [TestMethod]
        public void GetComponents_ValidProject_ReturnsComponents()
        {
            _projectsPds.GetComponentsForProject(1).Returns(new List<ComponentApiModel>
            {
                new() { ComponentId = 10, ComponentName = "CompA", ParentId = 0, ScriptPath = "s.ps1", Children = new List<ComponentApiModel>() },
                new() { ComponentId = 20, ComponentName = "CompB", ParentId = 0, ScriptPath = "s2.ps1", Children = new List<ComponentApiModel>() }
            });

            var sut = CreateSut();
            var result = sut.GetComponents(1).ToList();

            Assert.AreEqual(2, result.Count);
            Assert.AreEqual("CompA", result[0].Name);
            Assert.AreEqual("CompB", result[1].Name);
        }

        // --- RequestDetailAsync & BundleRequestDetailAsync ---

        [TestMethod]
        public async Task RequestDetailAsync_BuildServerProject_PropagatesCancellationToken()
        {
            var project = new ProjectApiModel
            {
                ProjectName = "MyProject",
                ArtefactsUrl = "https://dev.azure.com/org",
                ArtefactsSubPaths = "MyProject",
                ArtefactsBuildRegex = ".*",
                SourceControlType = SourceControlType.AzureDevOps
            };
            _projectsPds.GetProject("MyProject").Returns(project);

            var createRequest = new CreateRequest
            {
                Project = "MyProject",
                Environment = "dev",
                BuildDefinitionName = "MyProject; MyDef",
                BuildUrl = "https://dev.azure.com/org/build/123",
                Components = new List<string>()
            };

            _mockBuildClient.ValidateBuildAsync(
                    Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
                    Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<bool>(),
                    Arg.Any<CancellationToken>())
                .Returns(new BuildServerBuildInfo
                {
                    BuildId = 123,
                    BuildNumber = "1.0.0",
                    BuildUri = "vstfs:///123",
                    DefinitionName = "MyDef",
                    ProjectName = "MyProject"
                });

            _mockBuildClient.GetBuildArtifactDownloadUrlAsync(
                    Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
                    Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns("https://dev.azure.com/org/artifacts/drop.zip");

            using var cts = new CancellationTokenSource();
            var token = cts.Token;

            var sut = CreateSut();
            var detail = await sut.RequestDetailAsync(createRequest, token);

            Assert.AreEqual("https://dev.azure.com/org/artifacts/drop.zip", detail.BuildDetail.DropLocation);
            Assert.AreEqual("1.0.0", detail.BuildDetail.BuildNumber);

            await _mockBuildClient.Received(1).ValidateBuildAsync(
                "https://dev.azure.com/org", "MyProject", ".*",
                "MyProject; MyDef", null, "https://dev.azure.com/org/build/123", false, token);

            await _mockBuildClient.Received(1).GetBuildArtifactDownloadUrlAsync(
                "https://dev.azure.com/org", "MyProject", ".*",
                "MyProject; MyDef", "https://dev.azure.com/org/build/123", token);
        }

        [TestMethod]
        public async Task RequestDetailAsync_CancelledToken_ThrowsOperationCanceledException()
        {
            var project = new ProjectApiModel
            {
                ProjectName = "MyProject",
                ArtefactsUrl = "https://dev.azure.com/org",
                ArtefactsSubPaths = "MyProject",
                ArtefactsBuildRegex = ".*",
                SourceControlType = SourceControlType.AzureDevOps
            };
            _projectsPds.GetProject("MyProject").Returns(project);

            var createRequest = new CreateRequest
            {
                Project = "MyProject",
                Environment = "dev",
                BuildDefinitionName = "MyProject; MyDef",
                BuildUrl = "https://dev.azure.com/org/build/123",
                Components = new List<string>()
            };

            _mockBuildClient.ValidateBuildAsync(
                    Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
                    Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<bool>(),
                    Arg.Any<CancellationToken>())
                .Returns(callInfo =>
                {
                    var ct = callInfo.Arg<CancellationToken>();
                    ct.ThrowIfCancellationRequested();
                    return (BuildServerBuildInfo?)null;
                });

            using var cts = new CancellationTokenSource();
            cts.Cancel();

            var sut = CreateSut();
            await Assert.ThrowsAsync<OperationCanceledException>(
                () => sut.RequestDetailAsync(createRequest, cts.Token));
        }
    }
}
