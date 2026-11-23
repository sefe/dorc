using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Security.Claims;
using System.Security.Principal;
using Dorc.ApiModel;
using Dorc.Core.AzureDevOpsServer;
using Dorc.Core.BuildServer;
using Dorc.Core.Interfaces;
using Dorc.PersistentData;
using Dorc.PersistentData.Model;
using Dorc.PersistentData.Sources.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NSubstitute;

namespace Dorc.Core.Tests
{
    [TestClass]
    public class BundleManifestRequestSubmissionTests
    {
        private IProjectsPersistentSource _projectsPersistentSource = null!;
        private IComponentsPersistentSource _componentsPersistentSource = null!;
        private IEnvironmentsPersistentSource _environmentsPersistentSource = null!;
        private IRequestsPersistentSource _requestsPersistentSource = null!;
        private IBuildServerClientFactory _buildServerClientFactory = null!;
        private DeploymentRequestSubmitter _requestSubmitter = null!;

        [TestInitialize]
        public void Setup()
        {
            _projectsPersistentSource = Substitute.For<IProjectsPersistentSource>();
            _componentsPersistentSource = Substitute.For<IComponentsPersistentSource>();
            _environmentsPersistentSource = Substitute.For<IEnvironmentsPersistentSource>();
            _requestsPersistentSource = Substitute.For<IRequestsPersistentSource>();
            _buildServerClientFactory = Substitute.For<IBuildServerClientFactory>();

            _requestSubmitter = new DeploymentRequestSubmitter(
                _projectsPersistentSource,
                _componentsPersistentSource,
                Substitute.For<IManageProjectsPersistentSource>(),
                _environmentsPersistentSource,
                Substitute.For<ILoggerFactory>(),
                _requestsPersistentSource,
                Substitute.For<IClaimsPrincipalReader>(),
                Substitute.For<IDeploymentEventsPublisher>(),
                _buildServerClientFactory);
        }

        [TestMethod]
        public void SubmitRequest_CatalogMode_SkipsArtifactResolutionForArtifactConfiguredProject()
        {
            var user = new ClaimsPrincipal();
            StubArtifactConfiguredGitHubProject(user);

            _requestsPersistentSource.SubmitRequest(Arg.Any<DeploymentRequest>()).Returns(555);

            var requestId = _requestSubmitter.SubmitRequest(
                "MyProject", "MyEnv", uri: "", buildDefinitionName: "",
                new List<string> { "Comp1" }, new List<RequestProperty>(), user, isCatalog: true);

            Assert.AreEqual(555, requestId, "Catalog-mode request should be persisted and its id returned");
            _buildServerClientFactory.DidNotReceive().Create(Arg.Any<SourceControlType>());
        }

        [TestMethod]
        public void SubmitRequest_NonCatalogMode_AttemptsArtifactResolutionForArtifactConfiguredProject()
        {
            var user = new ClaimsPrincipal();
            StubArtifactConfiguredGitHubProject(user);

            var gitHubClient = Substitute.For<IBuildServerClient>();
            gitHubClient.GetBuildArtifactDownloadUrlAsync(
                    Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>())
                .Returns("https://api.github.com/repos/owner/repo/actions/artifacts/1/zip");
            _buildServerClientFactory.Create(SourceControlType.GitHub).Returns(gitHubClient);

            _requestsPersistentSource.SubmitRequest(Arg.Any<DeploymentRequest>()).Returns(556);

            var requestId = _requestSubmitter.SubmitRequest(
                "MyProject", "MyEnv", uri: "12345", buildDefinitionName: "build.yml",
                new List<string> { "Comp1" }, new List<RequestProperty>(), user, isCatalog: false);

            Assert.AreEqual(556, requestId);
            _buildServerClientFactory.Received(1).Create(SourceControlType.GitHub);
            gitHubClient.Received(1).GetBuildArtifactDownloadUrlAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>());
        }

        [TestMethod]
        public void SubmitRequest_BundleBuild_ExpandsBundleJsonIntoIndividualComponentRequests()
        {
            var user = new ClaimsPrincipal();
            var project = new ProjectApiModel
            {
                ProjectId = 10,
                ProjectName = "SampleProject",
                ArtefactsUrl = "https://dev.azure.com/org",
                ArtefactsSubPaths = "MyAdoProject",
                ArtefactsBuildRegex = "^SampleApp\\..*",
                SourceControlType = SourceControlType.AzureDevOps
            };
            _projectsPersistentSource.GetProject("SampleProject").Returns(project);
            _environmentsPersistentSource.GetEnvironment("SampleApp-UT-01", Arg.Any<IPrincipal>())
                .Returns(new EnvironmentApiModel { EnvironmentId = 20, EnvironmentName = "SampleApp-UT-01" });
            _environmentsPersistentSource.EnvironmentIsProd("SampleApp-UT-01").Returns(false);
            _environmentsPersistentSource.EnvironmentIsSecure("SampleApp-UT-01").Returns(false);

            var compDb = new ComponentApiModel
            {
                ComponentId = 1,
                ComponentName = "010 - Database Deploy Dacpac",
                ScriptPath = "DeployDatabase.ps1",
                IsEnabled = true,
                Children = new List<ComponentApiModel>()
            };
            var compWebApi = new ComponentApiModel
            {
                ComponentId = 2,
                ComponentName = "050 - Application Layer Web API",
                ScriptPath = "DeployWebApi.ps1",
                IsEnabled = true,
                Children = new List<ComponentApiModel>()
            };
            var compWeb = new ComponentApiModel
            {
                ComponentId = 3,
                ComponentName = "060 - Blazor Web Portal",
                ScriptPath = "DeployWeb.ps1",
                IsEnabled = true,
                Children = new List<ComponentApiModel>()
            };

            _componentsPersistentSource.GetComponentByName(compDb.ComponentName).Returns(compDb);
            _componentsPersistentSource.GetComponentByName(compWebApi.ComponentName).Returns(compWebApi);
            _componentsPersistentSource.GetComponentByName(compWeb.ComponentName).Returns(compWeb);

            var adoClient = Substitute.For<IBuildServerClient>();
            _buildServerClientFactory.Create(SourceControlType.AzureDevOps).Returns(adoClient);

            const string bundleVstfsUri = "vstfs:///Build/Build/1001";
            const string bundleZipUrl = "https://dev.azure.com/org/00000000-0000-0000-0000-000000000001/_apis/build/builds/1001/artifacts?artifactName=drop&$format=zip";

            adoClient.ValidateBuildAsync(
                    project.ArtefactsUrl, project.ArtefactsSubPaths, project.ArtefactsBuildRegex,
                    "MyAdoProject; SampleApp.Bundle", null, bundleVstfsUri, false)
                .Returns(new BuildServerBuildInfo
                {
                    BuildId = 1001,
                    BuildNumber = "SampleApp.Bundle_main_1.0.0.1",
                    BuildUri = bundleVstfsUri,
                    DefinitionName = "SampleApp.Bundle",
                    ProjectName = "MyAdoProject"
                });

            adoClient.GetBuildArtifactDownloadUrlAsync(
                    project.ArtefactsUrl, project.ArtefactsSubPaths, project.ArtefactsBuildRegex,
                    "MyAdoProject; SampleApp.Bundle", bundleVstfsUri)
                .Returns(bundleZipUrl);

            const string bundleJson = """
                {
                  "items": [
                    {"Component":"010 - Database Deploy Dacpac","BuildDefinition":"SampleApp.Database","Build":"SampleApp.Database_release_1.0.0.4"},
                    {"Component":"050 - Application Layer Web API","BuildDefinition":"SampleApp.WebApi","Build":"SampleApp.WebApi_release_1.0.0.4"},
                    {"Component":"060 - Blazor Web Portal","BuildDefinition":"SampleApp.Web","Build":"SampleApp.Web_release_1.0.0.4"}
                  ]
                }
                """;

            adoClient.TryReadArtifactFileTextAsync(bundleZipUrl, "bundle.json").Returns(bundleJson);

            adoClient.ValidateBuildAsync(
                    project.ArtefactsUrl, project.ArtefactsSubPaths, project.ArtefactsBuildRegex,
                    "MyAdoProject; SampleApp.Database", "SampleApp.Database_release_1.0.0.4", null, false)
                .Returns(new BuildServerBuildInfo
                {
                    BuildId = 1002,
                    BuildNumber = "SampleApp.Database_release_1.0.0.4",
                    BuildUri = "vstfs:///Build/Build/1002",
                    DefinitionName = "SampleApp.Database",
                    ProjectName = "MyAdoProject"
                });
            adoClient.GetBuildArtifactDownloadUrlAsync(
                    project.ArtefactsUrl, project.ArtefactsSubPaths, project.ArtefactsBuildRegex,
                    "MyAdoProject; SampleApp.Database", "vstfs:///Build/Build/1002")
                .Returns("file://fileserver/Drops/SampleApp.Database/SampleApp.Database_release_1.0.0.4");

            adoClient.ValidateBuildAsync(
                    project.ArtefactsUrl, project.ArtefactsSubPaths, project.ArtefactsBuildRegex,
                    "MyAdoProject; SampleApp.WebApi", "SampleApp.WebApi_release_1.0.0.4", null, false)
                .Returns(new BuildServerBuildInfo
                {
                    BuildId = 1003,
                    BuildNumber = "SampleApp.WebApi_release_1.0.0.4",
                    BuildUri = "vstfs:///Build/Build/1003",
                    DefinitionName = "SampleApp.WebApi",
                    ProjectName = "MyAdoProject"
                });
            adoClient.GetBuildArtifactDownloadUrlAsync(
                    project.ArtefactsUrl, project.ArtefactsSubPaths, project.ArtefactsBuildRegex,
                    "MyAdoProject; SampleApp.WebApi", "vstfs:///Build/Build/1003")
                .Returns("file://fileserver/Drops/SampleApp.WebApi/SampleApp.WebApi_release_1.0.0.4");

            var submittedRequests = new List<DeploymentRequest>();
            var nextId = 900;
            _requestsPersistentSource.SubmitRequest(Arg.Do<DeploymentRequest>(r => submittedRequests.Add(r)))
                .Returns(_ => ++nextId);

            var firstRequestId = _requestSubmitter.SubmitRequest(
                "SampleProject",
                "SampleApp-UT-01",
                bundleVstfsUri,
                "MyAdoProject; SampleApp.Bundle",
                new List<string> { compDb.ComponentName, compWebApi.ComponentName },
                new List<RequestProperty> { new() { PropertyName = "PropA", PropertyValue = "ValA", IsSensitive = true } },
                user);

            Assert.AreEqual(901, firstRequestId);
            Assert.AreEqual(2, submittedRequests.Count, "Bundle should expand into 2 deployment requests for the 2 selected component builds");
            Assert.AreEqual("SampleApp.Database_release_1.0.0.4", submittedRequests[0].BuildNumber);
            Assert.AreEqual(compDb.ComponentName, submittedRequests[0].Components);
            Assert.AreEqual("SampleApp.WebApi_release_1.0.0.4", submittedRequests[1].BuildNumber);
            Assert.AreEqual(compWebApi.ComponentName, submittedRequests[1].Components);

            var serializer = new DeploymentRequestDetailSerializer();
            var detail0 = serializer.Deserialize(submittedRequests[0].RequestDetails);
            Assert.AreEqual(
                "file://fileserver/Drops/SampleApp.Database/SampleApp.Database_release_1.0.0.4",
                detail0.BuildDetail.DropLocation);
            Assert.AreEqual(1, detail0.Properties.Count);
            Assert.IsTrue(detail0.Properties[0].IsSensitive);
        }

        [TestMethod]
        public void SubmitRequest_BundleBuild_ThrowsWhenReferencedComponentBuildIsMissing()
        {
            var user = new ClaimsPrincipal();
            var project = new ProjectApiModel
            {
                ProjectId = 10,
                ProjectName = "SampleProject",
                ArtefactsUrl = "https://dev.azure.com/org",
                ArtefactsSubPaths = "MyAdoProject",
                ArtefactsBuildRegex = "^SampleApp\\..*",
                SourceControlType = SourceControlType.AzureDevOps
            };
            _projectsPersistentSource.GetProject("SampleProject").Returns(project);
            _environmentsPersistentSource.GetEnvironment("SampleApp-UT-01", Arg.Any<IPrincipal>())
                .Returns(new EnvironmentApiModel { EnvironmentId = 20, EnvironmentName = "SampleApp-UT-01" });

            var compDb = new ComponentApiModel
            {
                ComponentId = 1,
                ComponentName = "010 - Database Deploy Dacpac",
                ScriptPath = "DeployDatabase.ps1",
                IsEnabled = true,
                Children = new List<ComponentApiModel>()
            };
            _componentsPersistentSource.GetComponentByName(compDb.ComponentName).Returns(compDb);

            var adoClient = Substitute.For<IBuildServerClient>();
            _buildServerClientFactory.Create(SourceControlType.AzureDevOps).Returns(adoClient);

            const string bundleVstfsUri = "vstfs:///Build/Build/1001";
            const string bundleZipUrl = "https://dev.azure.com/org/00000000-0000-0000-0000-000000000001/_apis/build/builds/1001/artifacts?artifactName=drop&$format=zip";

            adoClient.ValidateBuildAsync(
                    project.ArtefactsUrl, project.ArtefactsSubPaths, project.ArtefactsBuildRegex,
                    "MyAdoProject; SampleApp.Bundle", null, bundleVstfsUri, false)
                .Returns(new BuildServerBuildInfo
                {
                    BuildId = 1001,
                    BuildNumber = "SampleApp.Bundle_main_1.0.0.1",
                    BuildUri = bundleVstfsUri,
                    DefinitionName = "SampleApp.Bundle",
                    ProjectName = "MyAdoProject"
                });

            adoClient.GetBuildArtifactDownloadUrlAsync(
                    project.ArtefactsUrl, project.ArtefactsSubPaths, project.ArtefactsBuildRegex,
                    "MyAdoProject; SampleApp.Bundle", bundleVstfsUri)
                .Returns(bundleZipUrl);

            adoClient.TryReadArtifactFileTextAsync(bundleZipUrl, "bundle.json")
                .Returns("""{"items":[{"Component":"010 - Database Deploy Dacpac","BuildDefinition":"SampleApp.Database","Build":"Purged_Build_1.0"}]}""");

            adoClient.ValidateBuildAsync(
                    project.ArtefactsUrl, project.ArtefactsSubPaths, project.ArtefactsBuildRegex,
                    "MyAdoProject; SampleApp.Database", "Purged_Build_1.0", null, false)
                .Returns((BuildServerBuildInfo?)null);

            var ex = Assert.ThrowsExactly<InvalidOperationException>(() =>
                _requestSubmitter.SubmitRequest(
                    "SampleProject",
                    "SampleApp-UT-01",
                    bundleVstfsUri,
                    "MyAdoProject; SampleApp.Bundle",
                    new List<string> { compDb.ComponentName },
                    new List<RequestProperty>(),
                    user));

            StringAssert.Contains(ex.Message, "Purged_Build_1.0");
            _requestsPersistentSource.DidNotReceive().SubmitRequest(Arg.Any<DeploymentRequest>());
        }

        [TestMethod]
        public void TryExtractTextFromZipOrRawStream_ReadsNestedBundleJsonEntry()
        {
            using var zipStream = new MemoryStream();
            using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create, leaveOpen: true))
            {
                var entry = archive.CreateEntry("drop/bundle.json");
                using var writer = new StreamWriter(entry.Open());
                writer.Write("""{"items":[{"Component":"Comp1","BuildDefinition":"Def1","Build":"Build_1"}]}""");
            }
            zipStream.Position = 0;

            var extracted = AzureDevOpsServerWebClient.TryExtractTextFromZipOrRawStream(zipStream, "bundle.json");

            Assert.IsNotNull(extracted);
            StringAssert.Contains(extracted, "Comp1");
        }

        private void StubArtifactConfiguredGitHubProject(ClaimsPrincipal user)
        {
            var project = new ProjectApiModel
            {
                ProjectId = 1,
                ProjectName = "MyProject",
                ArtefactsUrl = "https://api.github.com/repos/owner/repo",
                ArtefactsSubPaths = "owner/repo",
                SourceControlType = SourceControlType.GitHub
            };
            _projectsPersistentSource.GetProject(Arg.Any<string>()).Returns(project);

            var environment = new EnvironmentApiModel
            {
                EnvironmentId = 1,
                EnvironmentName = "MyEnv"
            };
            _environmentsPersistentSource.GetEnvironment(Arg.Any<string>(), Arg.Any<IPrincipal>())
                .Returns(environment);

            var component = new ComponentApiModel
            {
                ComponentId = 1,
                ComponentName = "Comp1",
                ScriptPath = "deploy.ps1",
                IsEnabled = true,
                Children = new List<ComponentApiModel>()
            };
            _componentsPersistentSource.GetComponentByName("Comp1").Returns(component);
        }
    }
}
