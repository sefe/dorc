using Dorc.Core.AzureDevOpsServer;
using Dorc.Core.BuildServer;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Org.OpenAPITools.Model;

namespace Dorc.Core.Tests
{
    [TestClass]
    public class AzureDevOpsBuildServerClientTests
    {
        private const string ServerUrl = "https://dev.azure.com/sefe";
        private const string ProjectName = "Data and Analytics";
        private const string DefinitionName = "Angara.DataCatalogue.Build";
        private const string FullDefinitionName = $"{ProjectName}; {DefinitionName}";
        private const string BuildUrl = "https://dev.azure.com/sefe/project/_apis/build/Builds/220403";

        private ILoggerFactory _loggerFactory = null!;
        private IAzureDevOpsServerWebClient _webClient = null!;
        private AzureDevOpsBuildServerClient _sut = null!;

        [TestInitialize]
        public void Setup()
        {
            _loggerFactory = Substitute.For<ILoggerFactory>();
            _loggerFactory.CreateLogger(Arg.Any<string>()).Returns(Substitute.For<ILogger>());
            _webClient = Substitute.For<IAzureDevOpsServerWebClient>();
            _sut = new AzureDevOpsBuildServerClient(_loggerFactory, _ => _webClient);
        }

        [TestMethod]
        public void PublicConstructor_CreatesInstanceAndDefaultClientFactory()
        {
            var client = new AzureDevOpsBuildServerClient(_loggerFactory);
            Assert.IsNotNull(client);

            var createClientMethod = typeof(AzureDevOpsBuildServerClient).GetMethod(
                "CreateClient",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            Assert.IsNotNull(createClientMethod);

            var createdWebClient = createClientMethod.Invoke(client, new object[] { ServerUrl });
            Assert.IsInstanceOfType(createdWebClient, typeof(AzureDevOpsServerWebClient));
        }

        [TestMethod]
        public async Task GetBuildArtifactDownloadUrlAsync_MultipleArtifactsWithCapitalDrop_ReturnsDropDownloadUrl()
        {
            ConfigureBuild(220403, BuildUrl, "vstfs:///Build/Build/220403");

            _webClient.GetBuildArtifacts(ServerUrl, ProjectName, 220403).Returns(new List<BuildArtifact>
            {
                new(id: 1, name: "Code Coverage Report_220403",
                    resource: new ArtifactResource(downloadUrl: "https://dev.azure.com/coverage.zip")),
                new(id: 2, name: "Drop",
                    resource: new ArtifactResource(downloadUrl: "https://dev.azure.com/Drop.zip"))
            });

            var result = await _sut.GetBuildArtifactDownloadUrlAsync(
                ServerUrl, ProjectName, ".*", FullDefinitionName, BuildUrl);

            Assert.AreEqual("https://dev.azure.com/Drop.zip", result);
        }

        [TestMethod]
        public async Task GetBuildArtifactDownloadUrlAsync_MultipleArtifactsWithLowercaseDrop_ReturnsDropDownloadUrl()
        {
            ConfigureBuild(220403, BuildUrl, "vstfs:///Build/Build/220403");

            _webClient.GetBuildArtifacts(ServerUrl, ProjectName, 220403).Returns(new List<BuildArtifact>
            {
                new(id: 1, name: "symbols",
                    resource: new ArtifactResource(downloadUrl: "https://dev.azure.com/symbols.zip")),
                new(id: 2, name: "drop",
                    resource: new ArtifactResource(downloadUrl: "https://dev.azure.com/drop.zip"))
            });

            var result = await _sut.GetBuildArtifactDownloadUrlAsync(
                ServerUrl, ProjectName, ".*", FullDefinitionName, BuildUrl);

            Assert.AreEqual("https://dev.azure.com/drop.zip", result);
        }

        [TestMethod]
        public async Task GetBuildArtifactDownloadUrlAsync_SingleArtifact_ReturnsArtifactDownloadUrlRegardlessOfName()
        {
            ConfigureBuild(220403, BuildUrl, "vstfs:///Build/Build/220403");

            _webClient.GetBuildArtifacts(ServerUrl, ProjectName, 220403).Returns(new List<BuildArtifact>
            {
                new(id: 1, name: "Package",
                    resource: new ArtifactResource(downloadUrl: "https://dev.azure.com/package.zip"))
            });

            var result = await _sut.GetBuildArtifactDownloadUrlAsync(
                ServerUrl, ProjectName, ".*", FullDefinitionName, "vstfs:///Build/Build/220403");

            Assert.AreEqual("https://dev.azure.com/package.zip", result);
        }

        [TestMethod]
        public async Task GetBuildArtifactDownloadUrlAsync_MultipleArtifactsWithoutDrop_SearchesNextProjectThenThrows()
        {
            const string multiProjects = "FirstProject;SecondProject";
            ConfigureBuild(220403, BuildUrl, "vstfs:///Build/Build/220403", projectPaths: multiProjects);

            _webClient.GetBuildArtifacts(ServerUrl, "FirstProject", 220403).Returns(new List<BuildArtifact>
            {
                new(id: 1, name: "coverage",
                    resource: new ArtifactResource(downloadUrl: "https://dev.azure.com/coverage.zip")),
                new(id: 2, name: "logs",
                    resource: new ArtifactResource(downloadUrl: "https://dev.azure.com/logs.zip"))
            });
            _webClient.GetBuildArtifacts(ServerUrl, "SecondProject", 220403).Returns(new List<BuildArtifact>());

            var ex = await Assert.ThrowsExactlyAsync<ApplicationException>(() =>
                _sut.GetBuildArtifactDownloadUrlAsync(
                    ServerUrl, multiProjects, ".*", FullDefinitionName, BuildUrl));

            Assert.AreEqual("Failed to locate artifacts for the Build in Azure DevOps Server!", ex.Message);
        }

        [TestMethod]
        public async Task GetBuildArtifactDownloadUrlAsync_BuildNotFound_ThrowsApplicationException()
        {
            ConfigureBuild(220403, BuildUrl, "vstfs:///Build/Build/220403");

            var ex = await Assert.ThrowsExactlyAsync<ApplicationException>(() =>
                _sut.GetBuildArtifactDownloadUrlAsync(
                    ServerUrl, ProjectName, ".*", FullDefinitionName, "https://dev.azure.com/non-existent"));

            Assert.AreEqual("Failed to locate the Build requested in Azure DevOps Server!", ex.Message);
        }

        [TestMethod]
        public async Task GetBuildArtifactDownloadUrlAsync_MultipleDefinitionsAndBuilds_MatchesTargetAndThrowsWhenInvalidFormat()
        {
            var otherProjectRef = new TeamProjectReference(name: "OtherProject");
            var targetProjectRef = new TeamProjectReference(name: ProjectName);
            var defs = new List<BuildDefinitionReference>
            {
                new(id: 1, name: "OtherDef", project: otherProjectRef),
                new(id: 2, name: DefinitionName, project: otherProjectRef),
                new(id: 6259, name: DefinitionName, project: targetProjectRef)
            };

            _webClient.GetBuildDefinitionsForProjects(ServerUrl, ProjectName, ".*").Returns(defs);
            _webClient.GetBuildsFromDefinitionsAsync(ServerUrl, Arg.Any<List<BuildDefinitionReference>>())
                .Returns(new List<Build>
                {
                    new(id: 100, url: "https://dev.azure.com/other", uri: "vstfs:///Build/Build/100"),
                    new(id: 220403, url: BuildUrl, uri: "vstfs:///Build/Build/220403")
                });
            _webClient.GetBuildArtifacts(ServerUrl, ProjectName, 220403).Returns(new List<BuildArtifact>
            {
                new(id: 1, name: "Drop", resource: new ArtifactResource(downloadUrl: "https://dev.azure.com/Drop.zip"))
            });

            var url = await _sut.GetBuildArtifactDownloadUrlAsync(
                ServerUrl, ProjectName, ".*", FullDefinitionName, BuildUrl);
            Assert.AreEqual("https://dev.azure.com/Drop.zip", url);

            await Assert.ThrowsExactlyAsync<ArgumentException>(() =>
                _sut.GetBuildArtifactDownloadUrlAsync(ServerUrl, ProjectName, ".*", "InvalidFormatWithoutSemicolon", BuildUrl));
        }

        private void ConfigureBuild(int buildId, string url, string uri, string projectPaths = ProjectName)
        {
            var projectRef = new TeamProjectReference(name: ProjectName);
            var buildDefRef = new BuildDefinitionReference(id: 6259, name: DefinitionName, project: projectRef);

            _webClient.GetBuildDefinitionsForProjects(ServerUrl, projectPaths, ".*")
                .Returns(new List<BuildDefinitionReference> { buildDefRef });

            _webClient.GetBuildsFromDefinitionsAsync(
                    ServerUrl,
                    Arg.Is<List<BuildDefinitionReference>>(l => l.Count == 1 && l[0].Id == 6259))
                .Returns(new List<Build>
                {
                    new(id: buildId, url: url, uri: uri, buildNumber: "1.0.220403.0")
                });
        }
    }
}
