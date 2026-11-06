using System.Collections.Generic;
using System.Security.Claims;
using System.Security.Principal;
using Dorc.ApiModel;
using Dorc.Core.BuildServer;
using Dorc.PersistentData.Model;
using Dorc.PersistentData.Sources.Interfaces;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NSubstitute;

namespace Dorc.Core.Tests
{
    [TestClass]
    public class DeployLibraryTests
    {
        private IProjectsPersistentSource? _projectsPersistentSource = null;
        private IComponentsPersistentSource? _componentsPersistentSource = null;
        private IEnvironmentsPersistentSource? _environmentsPersistentSource = null;
        private IRequestsPersistentSource? _requestsPersistentSource = null;
        private IBuildServerClientFactory? _buildServerClientFactory = null;
        private DeployLibrary? _deployLibrary = null;

        [TestInitialize]
        public void Setup()
        {
            _projectsPersistentSource = Substitute.For<IProjectsPersistentSource>();
            _componentsPersistentSource = Substitute.For<IComponentsPersistentSource>();
            _environmentsPersistentSource = Substitute.For<IEnvironmentsPersistentSource>();
            _requestsPersistentSource = Substitute.For<IRequestsPersistentSource>();
            _buildServerClientFactory = Substitute.For<IBuildServerClientFactory>();

            _deployLibrary = new DeployLibrary(
                _projectsPersistentSource,
                _componentsPersistentSource,
                Substitute.For<IManageProjectsPersistentSource>(),
                _environmentsPersistentSource,
                Substitute.For<Microsoft.Extensions.Logging.ILoggerFactory>(),
                _requestsPersistentSource,
                Substitute.For<Dorc.PersistentData.IClaimsPrincipalReader>(),
                Substitute.For<Dorc.Core.Interfaces.IDeploymentEventsPublisher>(),
                _buildServerClientFactory
            );
        }

        [TestMethod]
        public void AddComponent_WhenIsEnabledIsFalse_IncludesComponentForDisabledResult()
        {
            // Arrange
            var componentNames = new List<string>();
            var component = new ComponentApiModel
            {
                ComponentId = 1,
                ComponentName = "DisabledComponent",
                ScriptPath = "deploy.ps1",
                IsEnabled = false,
                Children = new List<ComponentApiModel>()
            };

            // Act
            InvokeAddComponent(componentNames, component);

            // Assert
            Assert.AreEqual(1, componentNames.Count, "Disabled leaf should be persisted as a disabled result");
            Assert.AreEqual("DisabledComponent", componentNames[0]);
        }

        [TestMethod]
        public void AddComponent_WhenIsEnabledIsTrue_IncludesComponent()
        {
            // Arrange
            var componentNames = new List<string>();
            var component = new ComponentApiModel
            {
                ComponentId = 1,
                ComponentName = "EnabledComponent",
                ScriptPath = "deploy.ps1",
                IsEnabled = true,
                Children = new List<ComponentApiModel>()
            };

            // Act
            InvokeAddComponent(componentNames, component);

            // Assert
            Assert.AreEqual(1, componentNames.Count, "Component with IsEnabled=true should be included");
            Assert.AreEqual("EnabledComponent", componentNames[0]);
        }

        [TestMethod]
        public void AddComponent_WithHierarchy_IncludesAllLeafComponents()
        {
            // Arrange
            var componentNames = new List<string>();

            var enabledLeaf1 = new ComponentApiModel
            {
                ComponentId = 3,
                ComponentName = "EnabledLeaf1",
                ScriptPath = "leaf1.ps1",
                IsEnabled = true,
                Children = new List<ComponentApiModel>()
            };

            var disabledLeaf = new ComponentApiModel
            {
                ComponentId = 4,
                ComponentName = "DisabledLeaf",
                ScriptPath = "leaf2.ps1",
                IsEnabled = false,
                Children = new List<ComponentApiModel>()
            };

            var enabledLeaf2 = new ComponentApiModel
            {
                ComponentId = 5,
                ComponentName = "EnabledLeaf2",
                ScriptPath = "leaf3.ps1",
                IsEnabled = true,
                Children = new List<ComponentApiModel>()
            };

            // Parent container
            var parentContainer = new ComponentApiModel
            {
                ComponentId = 1,
                ComponentName = "ParentContainer",
                ScriptPath = "",
                IsEnabled = true,
                Children = new List<ComponentApiModel> { enabledLeaf1, disabledLeaf, enabledLeaf2 }
            };

            _componentsPersistentSource!.LoadChildren(Arg.Any<ComponentApiModel>());


            // Act
            InvokeAddComponent(componentNames, parentContainer);

            // Assert
            Assert.AreEqual(3, componentNames.Count, "All leaf components should have deployment results");
            Assert.IsTrue(componentNames.Contains("EnabledLeaf1"), "EnabledLeaf1 should be included");
            Assert.IsTrue(componentNames.Contains("EnabledLeaf2"), "EnabledLeaf2 should be included");
            Assert.IsTrue(componentNames.Contains("DisabledLeaf"), "DisabledLeaf should be included as disabled");
            Assert.IsFalse(componentNames.Contains("ParentContainer"), "ParentContainer should never be deployed");
        }

        [TestMethod]
        public void AddComponent_WhenDisabledParent_DeploysEnabledChildren()
        {
            // Arrange
            var componentNames = new List<string>();

            var enabledLeaf = new ComponentApiModel
            {
                ComponentId = 2,
                ComponentName = "EnabledLeaf",
                ScriptPath = "deploy.ps1",
                IsEnabled = true,
                Children = new List<ComponentApiModel>()
            };

            var disabledParent = new ComponentApiModel
            {
                ComponentId = 1,
                ComponentName = "DisabledParent",
                ScriptPath = "",
                IsEnabled = false,
                Children = new List<ComponentApiModel> { enabledLeaf }
            };

            _componentsPersistentSource!.LoadChildren(Arg.Any<ComponentApiModel>());

            // Act
            InvokeAddComponent(componentNames, disabledParent);

            // Assert
            Assert.AreEqual(1, componentNames.Count, "Enabled children of disabled parent should be deployed");
            Assert.AreEqual("EnabledLeaf", componentNames[0]);
        }

        [TestMethod]
        public void AddComponent_WhenNestedParentIsDisabled_DoesNotIncludeItsChildren()
        {
            // Arrange
            var componentNames = new List<string>();
            var enabledLeaf = new ComponentApiModel
            {
                ComponentId = 3,
                ComponentName = "EnabledLeaf",
                ScriptPath = "deploy.ps1",
                IsEnabled = true,
                Children = new List<ComponentApiModel>()
            };
            var disabledParent = new ComponentApiModel
            {
                ComponentId = 2,
                ComponentName = "DisabledParent",
                IsEnabled = false,
                Children = new List<ComponentApiModel> { enabledLeaf }
            };
            var root = new ComponentApiModel
            {
                ComponentId = 1,
                ComponentName = "Root",
                IsEnabled = true,
                Children = new List<ComponentApiModel> { disabledParent }
            };

            _componentsPersistentSource!.LoadChildren(Arg.Any<ComponentApiModel>());

            // Act
            InvokeAddComponent(componentNames, root);

            // Assert
            Assert.AreEqual(0, componentNames.Count,
                "Children of a disabled nested container should not be deployed");
        }

        [TestMethod]
        public void AddComponent_WhenIsEnabledIsFalse_IncludesLeafComponent()
        {
            // Arrange
            var componentNames = new List<string>();
            var component = new ComponentApiModel
            {
                ComponentId = 1,
                ComponentName = "DisabledComponent",
                ScriptPath = "deploy.ps1",
                IsEnabled = false,
                Children = new List<ComponentApiModel>()
            };

            // Act
            InvokeAddComponent(componentNames, component);

            // Assert
            Assert.AreEqual(1, componentNames.Count, "Disabled leaf component should be persisted");
            Assert.AreEqual("DisabledComponent", componentNames[0]);
        }

        [TestMethod]
        public void SubmitRequest_CatalogMode_SkipsArtifactResolutionForArtifactConfiguredProject()
        {
            // Arrange: a GitHub-configured project whose ArtefactsUrl would
            // normally send CreateRequestAsync down the artifact-resolution
            // branch (which needs a real BuildUrl and would otherwise fail
            // for a catalog request that has none).
            var user = new ClaimsPrincipal();
            StubArtifactConfiguredGitHubProject(user);

            _requestsPersistentSource!.SubmitRequest(Arg.Any<DeploymentRequest>()).Returns(555);

            // Act
            var requestId = _deployLibrary!.SubmitRequest(
                "MyProject", "MyEnv", uri: "", buildDefinitionName: "",
                new List<string> { "Comp1" }, new List<RequestProperty>(), user, isCatalog: true);

            // Assert
            Assert.AreEqual(555, requestId, "Catalog-mode request should be persisted and its id returned");
            _buildServerClientFactory!.DidNotReceive().Create(Arg.Any<SourceControlType>());
        }

        [TestMethod]
        public void SubmitRequest_NonCatalogMode_AttemptsArtifactResolutionForArtifactConfiguredProject()
        {
            // Negative control for the catalog short-circuit: the same
            // GitHub-configured project WITHOUT isCatalog goes through the
            // GitHub artifact-resolution branch.
            var user = new ClaimsPrincipal();
            StubArtifactConfiguredGitHubProject(user);

            var gitHubClient = Substitute.For<IBuildServerClient>();
            gitHubClient.GetBuildArtifactDownloadUrlAsync(
                    Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>())
                .Returns("https://api.github.com/repos/owner/repo/actions/artifacts/1/zip");
            _buildServerClientFactory!.Create(SourceControlType.GitHub).Returns(gitHubClient);

            _requestsPersistentSource!.SubmitRequest(Arg.Any<DeploymentRequest>()).Returns(556);

            // Act
            var requestId = _deployLibrary!.SubmitRequest(
                "MyProject", "MyEnv", uri: "12345", buildDefinitionName: "build.yml",
                new List<string> { "Comp1" }, new List<RequestProperty>(), user, isCatalog: false);

            // Assert
            Assert.AreEqual(556, requestId);
            _buildServerClientFactory.Received(1).Create(SourceControlType.GitHub);
            gitHubClient.Received(1).GetBuildArtifactDownloadUrlAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>());
        }

        [TestMethod]
        public void SubmitRequest_BundleBuild_ExpandsBundleJsonIntoIndividualComponentRequests()
        {
            // Arrange
            var user = new ClaimsPrincipal();
            var project = new ProjectApiModel
            {
                ProjectId = 10,
                ProjectName = "SSISRunner",
                ArtefactsUrl = "https://dev.azure.com/sefe",
                ArtefactsSubPaths = "ETRMs",
                ArtefactsBuildRegex = "^SsisRunner\\..*",
                SourceControlType = SourceControlType.AzureDevOps
            };
            _projectsPersistentSource!.GetProject("SSISRunner").Returns(project);
            _environmentsPersistentSource!.GetEnvironment("SSISRunner-UT-01", Arg.Any<IPrincipal>())
                .Returns(new EnvironmentApiModel { EnvironmentId = 20, EnvironmentName = "SSISRunner-UT-01" });
            _environmentsPersistentSource.EnvironmentIsProd("SSISRunner-UT-01").Returns(false);
            _environmentsPersistentSource.EnvironmentIsSecure("SSISRunner-UT-01").Returns(false);

            var compDb = new ComponentApiModel
            {
                ComponentId = 1,
                ComponentName = "010 - SSIS Runner Database Deploy Dacpac",
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

            _componentsPersistentSource!.GetComponentByName(compDb.ComponentName).Returns(compDb);
            _componentsPersistentSource.GetComponentByName(compWebApi.ComponentName).Returns(compWebApi);
            _componentsPersistentSource.GetComponentByName(compWeb.ComponentName).Returns(compWeb);

            var adoClient = Substitute.For<IBuildServerClient>();
            _buildServerClientFactory!.Create(SourceControlType.AzureDevOps).Returns(adoClient);

            // Bundle build validation and artifact download URL (container zip)
            const string bundleVstfsUri = "vstfs:///Build/Build/1019619";
            const string bundleZipUrl = "https://dev.azure.com/sefe/fab6bb12/_apis/build/builds/1019619/artifacts?artifactName=drop&$format=zip";

            adoClient.ValidateBuildAsync(
                    project.ArtefactsUrl, project.ArtefactsSubPaths, project.ArtefactsBuildRegex,
                    "ETRMs; SSISRunner.Bundle", null, bundleVstfsUri, false)
                .Returns(new BuildServerBuildInfo
                {
                    BuildId = 1019619,
                    BuildNumber = "SSISRunner.Bundle_main_26.11.06.1",
                    BuildUri = bundleVstfsUri,
                    DefinitionName = "SSISRunner.Bundle",
                    ProjectName = "ETRMs"
                });

            adoClient.GetBuildArtifactDownloadUrlAsync(
                    project.ArtefactsUrl, project.ArtefactsSubPaths, project.ArtefactsBuildRegex,
                    "ETRMs; SSISRunner.Bundle", bundleVstfsUri)
                .Returns(bundleZipUrl);

            // Real ETRMs SSISRunner.Bundle shape (lowercase "items")
            const string bundleJson = """
                {
                  "items": [
                    {"Component":"010 - SSIS Runner Database Deploy Dacpac","BuildDefinition":"SsisRunner.Database","Build":"SsisRunner.Database_release.CHG0051437_26.11.06.4"},
                    {"Component":"050 - Application Layer Web API","BuildDefinition":"SsisRunner.WebApi","Build":"SsisRunner.WebApi_release.CHG0051437_26.11.06.4"},
                    {"Component":"060 - Blazor Web Portal","BuildDefinition":"SsisRunner.Web","Build":"SsisRunner.Web_release.CHG0051437_26.11.06.4"}
                  ]
                }
                """;

            adoClient.TryReadArtifactFileTextAsync(bundleZipUrl, "bundle.json").Returns(bundleJson);

            // Component 1: SsisRunner.Database
            adoClient.ValidateBuildAsync(
                    project.ArtefactsUrl, project.ArtefactsSubPaths, project.ArtefactsBuildRegex,
                    "ETRMs; SsisRunner.Database", "SsisRunner.Database_release.CHG0051437_26.11.06.4", null, false)
                .Returns(new BuildServerBuildInfo
                {
                    BuildId = 1019475,
                    BuildNumber = "SsisRunner.Database_release.CHG0051437_26.11.06.4",
                    BuildUri = "vstfs:///Build/Build/1019475",
                    DefinitionName = "SsisRunner.Database",
                    ProjectName = "ETRMs"
                });
            adoClient.GetBuildArtifactDownloadUrlAsync(
                    project.ArtefactsUrl, project.ArtefactsSubPaths, project.ArtefactsBuildRegex,
                    "ETRMs; SsisRunner.Database", "vstfs:///Build/Build/1019475")
                .Returns("file://endfsdv01/endur_dev/TFS/Drops/SsisRunner.Database/SsisRunner.Database_release.CHG0051437_26.11.06.4");

            // Component 2: SsisRunner.WebApi
            adoClient.ValidateBuildAsync(
                    project.ArtefactsUrl, project.ArtefactsSubPaths, project.ArtefactsBuildRegex,
                    "ETRMs; SsisRunner.WebApi", "SsisRunner.WebApi_release.CHG0051437_26.11.06.4", null, false)
                .Returns(new BuildServerBuildInfo
                {
                    BuildId = 1019304,
                    BuildNumber = "SsisRunner.WebApi_release.CHG0051437_26.11.06.4",
                    BuildUri = "vstfs:///Build/Build/1019304",
                    DefinitionName = "SsisRunner.WebApi",
                    ProjectName = "ETRMs"
                });
            adoClient.GetBuildArtifactDownloadUrlAsync(
                    project.ArtefactsUrl, project.ArtefactsSubPaths, project.ArtefactsBuildRegex,
                    "ETRMs; SsisRunner.WebApi", "vstfs:///Build/Build/1019304")
                .Returns("file://endfsdv01/endur_dev/TFS/Drops/SsisRunner.WebApi/SsisRunner.WebApi_release.CHG0051437_26.11.06.4");

            var submittedRequests = new List<DeploymentRequest>();
            var nextId = 900;
            _requestsPersistentSource!.SubmitRequest(Arg.Do<DeploymentRequest>(r => submittedRequests.Add(r)))
                .Returns(_ => ++nextId);

            // Act: user selects only Database and WebApi from the bundle
            var firstRequestId = _deployLibrary!.SubmitRequest(
                "SSISRunner",
                "SSISRunner-UT-01",
                bundleVstfsUri,
                "ETRMs; SSISRunner.Bundle",
                new List<string> { compDb.ComponentName, compWebApi.ComponentName },
                new List<RequestProperty> { new() { PropertyName = "PropA", PropertyValue = "ValA", IsSensitive = true } },
                user);

            // Assert
            Assert.AreEqual(901, firstRequestId);
            Assert.AreEqual(2, submittedRequests.Count, "Bundle should expand into 2 deployment requests for the 2 selected component builds");
            Assert.AreEqual("SsisRunner.Database_release.CHG0051437_26.11.06.4", submittedRequests[0].BuildNumber);
            Assert.AreEqual(compDb.ComponentName, submittedRequests[0].Components);
            Assert.AreEqual("SsisRunner.WebApi_release.CHG0051437_26.11.06.4", submittedRequests[1].BuildNumber);
            Assert.AreEqual(compWebApi.ComponentName, submittedRequests[1].Components);

            var serializer = new DeploymentRequestDetailSerializer();
            var detail0 = serializer.Deserialize(submittedRequests[0].RequestDetails);
            Assert.AreEqual(
                "file://endfsdv01/endur_dev/TFS/Drops/SsisRunner.Database/SsisRunner.Database_release.CHG0051437_26.11.06.4",
                detail0.BuildDetail.DropLocation);
            Assert.AreEqual(1, detail0.Properties.Count);
            Assert.IsTrue(detail0.Properties[0].IsSensitive);
        }

        [TestMethod]
        public void SubmitRequest_BundleBuild_ThrowsWhenReferencedComponentBuildIsMissing()
        {
            // Arrange
            var user = new ClaimsPrincipal();
            var project = new ProjectApiModel
            {
                ProjectId = 10,
                ProjectName = "SSISRunner",
                ArtefactsUrl = "https://dev.azure.com/sefe",
                ArtefactsSubPaths = "ETRMs",
                ArtefactsBuildRegex = "^SsisRunner\\..*",
                SourceControlType = SourceControlType.AzureDevOps
            };
            _projectsPersistentSource!.GetProject("SSISRunner").Returns(project);
            _environmentsPersistentSource!.GetEnvironment("SSISRunner-UT-01", Arg.Any<IPrincipal>())
                .Returns(new EnvironmentApiModel { EnvironmentId = 20, EnvironmentName = "SSISRunner-UT-01" });

            var compDb = new ComponentApiModel
            {
                ComponentId = 1,
                ComponentName = "010 - SSIS Runner Database Deploy Dacpac",
                ScriptPath = "DeployDatabase.ps1",
                IsEnabled = true,
                Children = new List<ComponentApiModel>()
            };
            _componentsPersistentSource!.GetComponentByName(compDb.ComponentName).Returns(compDb);

            var adoClient = Substitute.For<IBuildServerClient>();
            _buildServerClientFactory!.Create(SourceControlType.AzureDevOps).Returns(adoClient);

            const string bundleVstfsUri = "vstfs:///Build/Build/1019619";
            const string bundleZipUrl = "https://dev.azure.com/sefe/fab6bb12/_apis/build/builds/1019619/artifacts?artifactName=drop&$format=zip";

            adoClient.ValidateBuildAsync(
                    project.ArtefactsUrl, project.ArtefactsSubPaths, project.ArtefactsBuildRegex,
                    "ETRMs; SSISRunner.Bundle", null, bundleVstfsUri, false)
                .Returns(new BuildServerBuildInfo
                {
                    BuildId = 1019619,
                    BuildNumber = "SSISRunner.Bundle_main_26.11.06.1",
                    BuildUri = bundleVstfsUri,
                    DefinitionName = "SSISRunner.Bundle",
                    ProjectName = "ETRMs"
                });

            adoClient.GetBuildArtifactDownloadUrlAsync(
                    project.ArtefactsUrl, project.ArtefactsSubPaths, project.ArtefactsBuildRegex,
                    "ETRMs; SSISRunner.Bundle", bundleVstfsUri)
                .Returns(bundleZipUrl);

            adoClient.TryReadArtifactFileTextAsync(bundleZipUrl, "bundle.json")
                .Returns("""{"items":[{"Component":"010 - SSIS Runner Database Deploy Dacpac","BuildDefinition":"SsisRunner.Database","Build":"Purged_Build_1.0"}]}""");

            // Referenced component build returns null (purged/missing)
            adoClient.ValidateBuildAsync(
                    project.ArtefactsUrl, project.ArtefactsSubPaths, project.ArtefactsBuildRegex,
                    "ETRMs; SsisRunner.Database", "Purged_Build_1.0", null, false)
                .Returns((BuildServerBuildInfo?)null);

            // Act & Assert
            var ex = Assert.ThrowsExactly< System.InvalidOperationException>(() =>
                _deployLibrary!.SubmitRequest(
                    "SSISRunner",
                    "SSISRunner-UT-01",
                    bundleVstfsUri,
                    "ETRMs; SSISRunner.Bundle",
                    new List<string> { compDb.ComponentName },
                    new List<RequestProperty>(),
                    user));

            StringAssert.Contains(ex.Message, "Purged_Build_1.0");
            _requestsPersistentSource!.DidNotReceive().SubmitRequest(Arg.Any<DeploymentRequest>());
        }

        [TestMethod]
        public void TryExtractTextFromZipOrRawStream_ReadsNestedBundleJsonEntry()
        {
            // Arrange: build an in-memory zip matching ADO Container artifact structure ("drop/bundle.json")
            using var zipStream = new System.IO.MemoryStream();
            using (var archive = new System.IO.Compression.ZipArchive(zipStream, System.IO.Compression.ZipArchiveMode.Create, leaveOpen: true))
            {
                var entry = archive.CreateEntry("drop/bundle.json");
                using var writer = new System.IO.StreamWriter(entry.Open());
                writer.Write("""{"items":[{"Component":"Comp1","BuildDefinition":"Def1","Build":"Build_1"}]}""");
            }
            zipStream.Position = 0;

            // Act
            var extracted = Dorc.Core.AzureDevOpsServer.AzureDevOpsServerWebClient.TryExtractTextFromZipOrRawStream(zipStream, "bundle.json");

            // Assert
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
            _projectsPersistentSource!.GetProject(Arg.Any<string>()).Returns(project);

            var environment = new EnvironmentApiModel
            {
                EnvironmentId = 1,
                EnvironmentName = "MyEnv"
            };
            _environmentsPersistentSource!.GetEnvironment(Arg.Any<string>(), Arg.Any<IPrincipal>())
                .Returns(environment);

            var component = new ComponentApiModel
            {
                ComponentId = 1,
                ComponentName = "Comp1",
                ScriptPath = "deploy.ps1",
                IsEnabled = true,
                Children = new List<ComponentApiModel>()
            };
            _componentsPersistentSource!.GetComponentByName("Comp1").Returns(component);
        }

        private void InvokeAddComponent(List<string> componentNames, ComponentApiModel component)
        {
            var addComponentMethod = typeof(DeployLibrary).GetMethod("AddComponent",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            addComponentMethod?.Invoke(_deployLibrary, new object[] { componentNames, component });
        }
    }
}