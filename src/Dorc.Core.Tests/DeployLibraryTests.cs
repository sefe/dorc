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
        public void AddComponent_WhenIsEnabledIsNull_IncludesComponent()
        {
            // Arrange
            var componentNames = new List<string>();
            var component = new ComponentApiModel
            {
                ComponentId = 1,
                ComponentName = "NullEnabledComponent",
                ScriptPath = "deploy.ps1",
                IsEnabled = null,
                Children = new List<ComponentApiModel>()
            };

            // Act
            InvokeAddComponent(componentNames, component);

            // Assert
            Assert.AreEqual(1, componentNames.Count, "Component with IsEnabled=null should be included");
            Assert.AreEqual("NullEnabledComponent", componentNames[0]);
        }

        [TestMethod]
        public void AddComponent_WhenChildIsDisabled_ExcludesChildOnly()
        {
            // Arrange
            var componentNames = new List<string>();
            var disabledChild = new ComponentApiModel
            {
                ComponentId = 2,
                ComponentName = "DisabledChild",
                ScriptPath = "child.ps1",
                IsEnabled = false,
                Children = new List<ComponentApiModel>()
            };

            var parent = new ComponentApiModel
            {
                ComponentId = 1,
                ComponentName = "EnabledParent",
                ScriptPath = "parent.ps1",
                IsEnabled = true,
                Children = new List<ComponentApiModel> { disabledChild }
            };

            // Act
            InvokeAddComponent(componentNames, parent);

            // Assert
            Assert.AreEqual(1, componentNames.Count, "Only parent should be included");
            Assert.IsTrue(componentNames.Contains("EnabledParent"), "Parent should be included");
            Assert.IsFalse(componentNames.Contains("DisabledChild"), "Disabled child should be excluded");
        }

        [TestMethod]
        public void AddComponent_WithMultipleLevels_RespectsEachIsEnabledIndependently()
        {
            // Arrange
            var componentNames = new List<string>();

            var grandChild1 = new ComponentApiModel
            {
                ComponentId = 3,
                ComponentName = "EnabledGrandChild",
                ScriptPath = "grandchild1.ps1",
                IsEnabled = true,
                Children = new List<ComponentApiModel>()
            };

            var grandChild2 = new ComponentApiModel
            {
                ComponentId = 4,
                ComponentName = "DisabledGrandChild",
                ScriptPath = "grandchild2.ps1",
                IsEnabled = false,
                Children = new List<ComponentApiModel>()
            };

            var grandChild3 = new ComponentApiModel
            {
                ComponentId = 5,
                ComponentName = "NullGrandChild",
                ScriptPath = "grandchild3.ps1",
                IsEnabled = null,
                Children = new List<ComponentApiModel>()
            };

            var child = new ComponentApiModel
            {
                ComponentId = 2,
                ComponentName = "EnabledChild",
                ScriptPath = "child.ps1",
                IsEnabled = true,
                Children = new List<ComponentApiModel> { grandChild1, grandChild2, grandChild3 }
            };

            var parent = new ComponentApiModel
            {
                ComponentId = 1,
                ComponentName = "EnabledParent",
                ScriptPath = "parent.ps1",
                IsEnabled = true,
                Children = new List<ComponentApiModel> { child }
            };

            // Act
            InvokeAddComponent(componentNames, parent);

            // Assert
            Assert.AreEqual(4, componentNames.Count, "Should include parent, child, and two grandchildren");
            Assert.IsTrue(componentNames.Contains("EnabledParent"), "Parent should be included");
            Assert.IsTrue(componentNames.Contains("EnabledChild"), "Child should be included");
            Assert.IsTrue(componentNames.Contains("EnabledGrandChild"), "Enabled grandchild should be included");
            Assert.IsFalse(componentNames.Contains("DisabledGrandChild"), "Disabled grandchild should be excluded");
            Assert.IsTrue(componentNames.Contains("NullGrandChild"), "Null grandchild should be included");
        }

        private void InvokeAddComponent(List<string> componentNames, ComponentApiModel component)
        {
            var addComponentMethod = typeof(DeployLibrary).GetMethod("AddComponent",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            addComponentMethod?.Invoke(_deployLibrary, new object[] { componentNames, component });
        }
    }
}