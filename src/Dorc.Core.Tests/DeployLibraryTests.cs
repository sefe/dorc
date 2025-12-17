using System.Collections.Generic;
using Dorc.ApiModel;
using Dorc.PersistentData.Sources.Interfaces;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NSubstitute;

namespace Dorc.Core.Tests
{
    [TestClass]
    public class DeployLibraryTests
    {
        private IComponentsPersistentSource? _componentsPersistentSource = null;
        private DeployLibrary? _deployLibrary = null;

        [TestInitialize]
        public void Setup()
        {
            _componentsPersistentSource = Substitute.For<IComponentsPersistentSource>();

            _deployLibrary = new DeployLibrary(
                Substitute.For<IProjectsPersistentSource>(),
                _componentsPersistentSource,
                Substitute.For<IManageProjectsPersistentSource>(),
                Substitute.For<IEnvironmentsPersistentSource>(),
                Substitute.For<Microsoft.Extensions.Logging.ILoggerFactory>(),
                Substitute.For<IRequestsPersistentSource>(),
                Substitute.For<Dorc.PersistentData.IClaimsPrincipalReader>(),
                Substitute.For<Dorc.Core.Interfaces.IDeploymentEventsPublisher>()
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
            Assert.IsEmpty(componentNames, "Component with IsEnabled=false should be excluded");
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
            Assert.HasCount(1, componentNames, "Component with IsEnabled=true should be included");
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
            Assert.HasCount(2, componentNames, "Should only include enabled leaf components");
            Assert.Contains("EnabledLeaf1", componentNames, "EnabledLeaf1 should be included");
            Assert.Contains("EnabledLeaf2", componentNames, "EnabledLeaf2 should be included");
            Assert.DoesNotContain("DisabledLeaf", componentNames, "DisabledLeaf should be excluded");
            Assert.DoesNotContain("ParentContainer", componentNames, "ParentContainer should never be deployed");
        }

        private void InvokeAddComponent(List<string> componentNames, ComponentApiModel component)
        {
            var addComponentMethod = typeof(DeployLibrary).GetMethod("AddComponent",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            addComponentMethod?.Invoke(_deployLibrary, new object[] { componentNames, component });
        }
    }
}