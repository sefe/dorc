using System.Collections.Generic;
using System.Reflection;
using Dorc.ApiModel;
using Dorc.Core.BuildServer;
using Dorc.Core.Interfaces;
using Dorc.PersistentData;
using Dorc.PersistentData.Sources.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NSubstitute;

namespace Dorc.Core.Tests
{
    [TestClass]
    public class ComponentHierarchyTraversalTests
    {
        private IComponentsPersistentSource _componentsPersistentSource = null!;
        private DeploymentRequestSubmitter _requestSubmitter = null!;

        [TestInitialize]
        public void Setup()
        {
            _componentsPersistentSource = Substitute.For<IComponentsPersistentSource>();
            _requestSubmitter = new DeploymentRequestSubmitter(
                Substitute.For<IProjectsPersistentSource>(),
                _componentsPersistentSource,
                Substitute.For<IManageProjectsPersistentSource>(),
                Substitute.For<IEnvironmentsPersistentSource>(),
                Substitute.For<ILoggerFactory>(),
                Substitute.For<IRequestsPersistentSource>(),
                Substitute.For<IClaimsPrincipalReader>(),
                Substitute.For<IDeploymentEventsPublisher>(),
                Substitute.For<IBuildServerClientFactory>());
        }

        [TestMethod]
        public void AddComponent_WhenIsEnabledIsFalse_IncludesComponentForDisabledResult()
        {
            var componentNames = new List<string>();
            var component = new ComponentApiModel
            {
                ComponentId = 1,
                ComponentName = "DisabledComponent",
                ScriptPath = "deploy.ps1",
                IsEnabled = false,
                Children = new List<ComponentApiModel>()
            };

            InvokeAddComponent(componentNames, component);

            Assert.AreEqual(1, componentNames.Count, "Disabled leaf should be persisted as a disabled result");
            Assert.AreEqual("DisabledComponent", componentNames[0]);
        }

        [TestMethod]
        public void AddComponent_WhenIsEnabledIsTrue_IncludesComponent()
        {
            var componentNames = new List<string>();
            var component = new ComponentApiModel
            {
                ComponentId = 1,
                ComponentName = "EnabledComponent",
                ScriptPath = "deploy.ps1",
                IsEnabled = true,
                Children = new List<ComponentApiModel>()
            };

            InvokeAddComponent(componentNames, component);

            Assert.AreEqual(1, componentNames.Count, "Component with IsEnabled=true should be included");
            Assert.AreEqual("EnabledComponent", componentNames[0]);
        }

        [TestMethod]
        public void AddComponent_WithHierarchy_IncludesAllLeafComponents()
        {
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

            var parentContainer = new ComponentApiModel
            {
                ComponentId = 1,
                ComponentName = "ParentContainer",
                ScriptPath = "",
                IsEnabled = true,
                Children = new List<ComponentApiModel> { enabledLeaf1, disabledLeaf, enabledLeaf2 }
            };

            _componentsPersistentSource.LoadChildren(Arg.Any<ComponentApiModel>());

            InvokeAddComponent(componentNames, parentContainer);

            Assert.AreEqual(3, componentNames.Count, "All leaf components should have deployment results");
            Assert.IsTrue(componentNames.Contains("EnabledLeaf1"), "EnabledLeaf1 should be included");
            Assert.IsTrue(componentNames.Contains("EnabledLeaf2"), "EnabledLeaf2 should be included");
            Assert.IsTrue(componentNames.Contains("DisabledLeaf"), "DisabledLeaf should be included as disabled");
            Assert.IsFalse(componentNames.Contains("ParentContainer"), "ParentContainer should never be deployed");
        }

        [TestMethod]
        public void AddComponent_WhenDisabledParent_DeploysEnabledChildren()
        {
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

            _componentsPersistentSource.LoadChildren(Arg.Any<ComponentApiModel>());

            InvokeAddComponent(componentNames, disabledParent);

            Assert.AreEqual(1, componentNames.Count, "Enabled children of disabled parent should be deployed");
            Assert.AreEqual("EnabledLeaf", componentNames[0]);
        }

        [TestMethod]
        public void AddComponent_WhenNestedParentIsDisabled_DoesNotIncludeItsChildren()
        {
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

            _componentsPersistentSource.LoadChildren(Arg.Any<ComponentApiModel>());

            InvokeAddComponent(componentNames, root);

            Assert.AreEqual(0, componentNames.Count,
                "Children of a disabled nested container should not be deployed");
        }

        [TestMethod]
        public void AddComponent_WhenIsEnabledIsFalse_IncludesLeafComponent()
        {
            var componentNames = new List<string>();
            var component = new ComponentApiModel
            {
                ComponentId = 1,
                ComponentName = "DisabledComponent",
                ScriptPath = "deploy.ps1",
                IsEnabled = false,
                Children = new List<ComponentApiModel>()
            };

            InvokeAddComponent(componentNames, component);

            Assert.AreEqual(1, componentNames.Count, "Disabled leaf component should be persisted");
            Assert.AreEqual("DisabledComponent", componentNames[0]);
        }

        private void InvokeAddComponent(List<string> componentNames, ComponentApiModel component)
        {
            var addComponentMethod = typeof(DeploymentRequestSubmitter).GetMethod("AddComponent",
                BindingFlags.NonPublic | BindingFlags.Instance);
            addComponentMethod?.Invoke(_requestSubmitter, new object[] { componentNames, component });
        }
    }
}
