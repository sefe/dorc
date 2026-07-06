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
        public void AddComponent_WhenIsEnabledIsFalse_ExcludesComponent()
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
            Assert.AreEqual(0, componentNames.Count, "Component with IsEnabled=false should be excluded");
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
        public void AddComponent_WithHierarchy_OnlyIncludesEnabledLeafComponents()
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
            Assert.AreEqual(2, componentNames.Count, "Should only include enabled leaf components");
            Assert.IsTrue(componentNames.Contains("EnabledLeaf1"), "EnabledLeaf1 should be included");
            Assert.IsTrue(componentNames.Contains("EnabledLeaf2"), "EnabledLeaf2 should be included");
            Assert.IsFalse(componentNames.Contains("DisabledLeaf"), "DisabledLeaf should be excluded");
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
        public void AddComponent_WhenIsEnabledIsFalse_ExcludesLeafComponent()
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
            Assert.AreEqual(0, componentNames.Count, "Disabled leaf component should be excluded");
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