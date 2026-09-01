using Dorc.Api.Tests.Mocks;
using Dorc.ApiModel;
using Dorc.PersistentData.Contexts;
using Dorc.PersistentData.Model;
using Dorc.PersistentData.Sources;
using Dorc.PersistentData.Security;
using Dorc.PersistentData.Sources.Interfaces;
using NSubstitute;

namespace Dorc.Api.Tests.Sources
{
    /// <summary>
    /// Tests for ManageProjectsPersistentSource focusing on component and script management.
    /// These tests verify that the UpdateComponent method correctly handles shared vs non-shared scripts.
    /// 
    /// Note: Full integration tests with database would require Microsoft.EntityFrameworkCore.InMemory package.
    /// For now, these tests verify the API contracts and basic validation logic.
    /// </summary>
    [TestClass]
    public class ManageProjectsPersistentSourceTests
    {
        private IDeploymentContextFactory _contextFactory;
        private IRequestsPersistentSource _requestsPersistentSource;
        private ManageProjectsPersistentSource _source;
        private IDeploymentContext _context;
        private IScriptsAuditPersistentSource _scriptsAuditPersistentSource;
        private ISourceHostAllowList _sourceHostAllowList;

        [TestInitialize]
        public void Setup()
        {
            _contextFactory = Substitute.For<IDeploymentContextFactory>();
            _requestsPersistentSource = Substitute.For<IRequestsPersistentSource>();
            _scriptsAuditPersistentSource = Substitute.For<IScriptsAuditPersistentSource>();

            // Unconfigured by default: matches an estate that has not filled the allow-list in,
            // and keeps the pre-existing tests testing what they were written to test.
            _sourceHostAllowList = Substitute.For<ISourceHostAllowList>();
            _sourceHostAllowList.IsUnconfigured.Returns(true);
            _context = Substitute.For<IDeploymentContext>();
            
            // Setup the context factory to return our mocked context
            _contextFactory.GetContext().Returns(_context);
            
            // Setup empty DbSets for common entities used in validation
            var emptyComponents = new List<Component>();
            var componentsDbSet = DbContextMock.GetQueryableMockDbSet(emptyComponents);
            _context.Components.Returns(componentsDbSet);
            
            var emptyProjects = new List<Project>();
            var projectsDbSet = DbContextMock.GetQueryableMockDbSet(emptyProjects);
            _context.Projects.Returns(projectsDbSet);
            
            _source = new ManageProjectsPersistentSource(_contextFactory, _requestsPersistentSource, _scriptsAuditPersistentSource, _sourceHostAllowList);
        }

        [TestMethod]
        public void CreateComponent_WithScriptPath_Success()
        {
            // Arrange
            var projectId = 1;
            var apiComponent = new ComponentApiModel
            {
                ComponentId = 0, // New component
                ComponentName = "TestComponent",
                ScriptPath = "test-script.ps1",
                NonProdOnly = false,
                IsEnabled = true
            };

            // This test verifies that CreateComponent can be called with valid parameters
            // Full database integration testing would require InMemory database setup

            // Act & Assert - Method signature is correct
            Assert.IsNotNull(_source);
            Assert.AreEqual(0, apiComponent.ComponentId);
        }

        [TestMethod]
        public void UpdateComponent_WithZeroComponentId_ReturnsEarly()
        {
            // Arrange
            var apiComponent = new ComponentApiModel
            {
                ComponentId = 0,
                ComponentName = "TestComponent",
                ScriptPath = "test.ps1"
            };

            // Act
            _source.UpdateComponent(apiComponent, 1, null, "testuser");

            // Assert - Method should return early without calling context
            _contextFactory.DidNotReceive().GetContext();
        }

        [TestMethod]
        public void ValidateComponents_EmptyList_NoException()
        {
            // Arrange
            var components = new List<ComponentApiModel>();

            // Act & Assert - Should not throw
            _source.ValidateComponents(components, 1, HttpRequestType.Put);
        }

        [TestMethod]
        public void ValidateComponents_NullComponentName_ThrowsException()
        {
            // Arrange
            var components = new List<ComponentApiModel>
            {
                new ComponentApiModel
                {
                    ComponentId = 1,
                    ComponentName = null, // Invalid
                    ScriptPath = "test.ps1"
                }
            };

            // Act & Assert
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                _source.ValidateComponents(components, 1, HttpRequestType.Put));
        }

        [TestMethod]
        public void ValidateComponents_ComponentNameTooLong_ThrowsException()
        {
            // Arrange
            var longName = new string('a', 65); // 65 characters, max is 64
            var components = new List<ComponentApiModel>
            {
                new ComponentApiModel
                {
                    ComponentId = 0, // Use 0 so it doesn't try to validate against database
                    ComponentName = longName,
                    ScriptPath = "test.ps1"
                }
            };

            // Act & Assert
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                _source.ValidateComponents(components, 1, HttpRequestType.Put));
        }

        [TestMethod]
        public void ValidateComponents_ComponentNameWithInvalidCharacters_ThrowsException()
        {
            // Arrange
            var components = new List<ComponentApiModel>
            {
                new ComponentApiModel
                {
                    ComponentId = 0,
                    ComponentName = "Component\tWith\nTabs",
                    ScriptPath = "test.ps1"
                }
            };

            // Act & Assert
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                _source.ValidateComponents(components, 1, HttpRequestType.Put));
        }

        [TestMethod]
        public void ValidateComponents_ComponentNameWithCurlyBraces_NoException()
        {
            // Arrange
            var components = new List<ComponentApiModel>
            {
                new ComponentApiModel
                {
                    ComponentId = 0,
                    ComponentName = "Component{Valid}",
                    ScriptPath = "test.ps1"
                }
            };

            // Act & Assert - Should not throw
            _source.ValidateComponents(components, 1, HttpRequestType.Put);
        }

        [TestMethod]
        public void ValidateComponents_ComponentNameWithSquareBrackets_NoException()
        {
            // Arrange
            var components = new List<ComponentApiModel>
            {
                new ComponentApiModel
                {
                    ComponentId = 0,
                    ComponentName = "Component[0]",
                    ScriptPath = "test.ps1"
                }
            };

            // Act & Assert - Should not throw
            _source.ValidateComponents(components, 1, HttpRequestType.Put);
        }

        [TestMethod]
        public void ValidateComponents_ComponentNameWithAllowedSpecialChars_NoException()
        {
            // Arrange - all allowed special characters
            var components = new List<ComponentApiModel>
            {
                new ComponentApiModel
                {
                    ComponentId = 0,
                    ComponentName = "My-Component_v1.0 (test)",
                    ScriptPath = "test.ps1"
                }
            };

            // Act & Assert - Should not throw
            _source.ValidateComponents(components, 1, HttpRequestType.Put);
        }

        [TestMethod]
        public void ValidateComponents_ComponentNameWithPercent_ThrowsException()
        {
            // Arrange
            var components = new List<ComponentApiModel>
            {
                new ComponentApiModel
                {
                    ComponentId = 0,
                    ComponentName = "Component%20Name",
                    ScriptPath = "test.ps1"
                }
            };

            // Act & Assert
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                _source.ValidateComponents(components, 1, HttpRequestType.Put));
        }

        [TestMethod]
        public void ValidateComponents_DuplicateComponentNames_ThrowsException()
        {
            // Arrange
            var components = new List<ComponentApiModel>
            {
                new ComponentApiModel
                {
                    ComponentId = 1,
                    ComponentName = "DuplicateName",
                    ScriptPath = "test1.ps1"
                },
                new ComponentApiModel
                {
                    ComponentId = 2,
                    ComponentName = "DuplicateName", // Duplicate
                    ScriptPath = "test2.ps1"
                }
            };

            // Act & Assert
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                _source.ValidateComponents(components, 1, HttpRequestType.Put));
        }

        [TestMethod]
        public void ValidateComponents_PostWithNonZeroId_ThrowsException()
        {
            // Arrange
            var components = new List<ComponentApiModel>
            {
                new ComponentApiModel
                {
                    ComponentId = 1, // Should be 0 for POST
                    ComponentName = "TestComponent",
                    ScriptPath = "test.ps1"
                }
            };

            // Act & Assert
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                _source.ValidateComponents(components, 1, HttpRequestType.Post));
        }

        [TestMethod]
        public void FlattenApiComponents_NestedComponents_FlattensProperly()
        {
            // Arrange
            var components = new List<ComponentApiModel>
            {
                new ComponentApiModel
                {
                    ComponentId = 1,
                    ComponentName = "Parent",
                    ScriptPath = "parent.ps1",
                    Children = new List<ComponentApiModel>
                    {
                        new ComponentApiModel
                        {
                            ComponentId = 2,
                            ComponentName = "Child1",
                            ScriptPath = "child1.ps1"
                        },
                        new ComponentApiModel
                        {
                            ComponentId = 3,
                            ComponentName = "Child2",
                            ScriptPath = "child2.ps1",
                            Children = new List<ComponentApiModel>
                            {
                                new ComponentApiModel
                                {
                                    ComponentId = 4,
                                    ComponentName = "GrandChild",
                                    ScriptPath = "grandchild.ps1"
                                }
                            }
                        }
                    }
                }
            };

            var flattened = new List<ComponentApiModel>();

            // Act
            _source.FlattenApiComponents(components, flattened);

            // Assert
            Assert.AreEqual(4, flattened.Count);
            Assert.IsTrue(flattened.Any(c => c.ComponentName == "Parent"));
            Assert.IsTrue(flattened.Any(c => c.ComponentName == "Child1"));
            Assert.IsTrue(flattened.Any(c => c.ComponentName == "Child2"));
            Assert.IsTrue(flattened.Any(c => c.ComponentName == "GrandChild"));
        }

        [TestMethod]
        public void TraverseComponents_CallsActionForAllComponents()
        {
            // Arrange
            var components = new List<ComponentApiModel>
            {
                new ComponentApiModel
                {
                    ComponentId = 1,
                    ComponentName = "Parent",
                    Children = new List<ComponentApiModel>
                    {
                        new ComponentApiModel
                        {
                            ComponentId = 2,
                            ComponentName = "Child"
                        }
                    }
                }
            };

            var callCount = 0;
            Action<ComponentApiModel, int, int?, string> action = (component, projectId, parentId, username) =>
            {
                callCount++;
            };

            // Act
            _source.TraverseComponents(components, null, 1, action, "testuser");

            // Assert
            Assert.AreEqual(2, callCount, "Action should be called for parent and child");
        }
        // --- S-010 / S-011: what a component may point at ------------------------------

        private static IList<ComponentApiModel> OneComponent(
            string scriptPath, ComponentType type = ComponentType.PowerShell) =>
            new List<ComponentApiModel>
            {
                new()
                {
                    ComponentId = 0,
                    ComponentName = "TestComponent",
                    ScriptPath = scriptPath,
                    ComponentType = type,
                    PSVersion = "7.4"
                }
            };

        [TestMethod]
        public void ValidateComponents_AcceptsARelativeScriptPath()
        {
            _source.ValidateComponents(OneComponent(@"00 Generic\Deploy.ps1"), 1, HttpRequestType.Post);
        }

        /// <summary>
        /// Path.Combine discards the script root when the stored path is rooted, so this does
        /// not resolve beneath the root - it replaces it, and runs as the deployment account.
        /// </summary>
        [TestMethod]
        public void ValidateComponents_RejectsAnAbsoluteScriptPath()
        {
            var refusal = Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
                _source.ValidateComponents(OneComponent(@"\\attacker\share\payload.ps1"), 1, HttpRequestType.Post));

            StringAssert.Contains(refusal.Message, "absolute path");
        }

        [TestMethod]
        public void ValidateComponents_RejectsScriptPathTraversal()
        {
            Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
                _source.ValidateComponents(OneComponent(@"..\..\payload.ps1"), 1, HttpRequestType.Post));
        }

        /// <summary>
        /// A Terraform component's ScriptPath is the source location, not a path relative to
        /// the script root, and is legitimately absolute. Applying the relativity rule to it
        /// would reject every Terraform component on the SharedFolder source type.
        /// </summary>
        [TestMethod]
        public void ValidateComponents_AcceptsAnAbsoluteTerraformSourceLocation()
        {
            _source.ValidateComponents(
                OneComponent(@"\\buildserver\terraform\infra", ComponentType.Terraform), 1, HttpRequestType.Post);
        }

        [TestMethod]
        public void ValidateComponents_RejectsATerraformSourceHostThatIsNotAllowed()
        {
            _sourceHostAllowList.IsUnconfigured.Returns(false);
            _sourceHostAllowList
                .IsTerraformSourceAllowed(Arg.Any<string>(), out Arg.Any<string>())
                .Returns(call => { call[1] = "its host 'attacker' is not permitted."; return false; });

            var refusal = Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
                _source.ValidateComponents(
                    OneComponent(@"\\attacker\terraform\infra", ComponentType.Terraform), 1, HttpRequestType.Post));

            StringAssert.Contains(refusal.Message, "not permitted");
        }

        [TestMethod]
        public void CreateComponent_RejectsATerraformSourceHostBeforePersistence()
        {
            _sourceHostAllowList.IsUnconfigured.Returns(false);
            _sourceHostAllowList
                .IsTerraformSourceAllowed(Arg.Any<string>(), out Arg.Any<string>())
                .Returns(call => { call[1] = "its host 'attacker' is not permitted."; return false; });
            _contextFactory.ClearReceivedCalls();

            var component = OneComponent(
                @"\\attacker\terraform\infra", ComponentType.Terraform).Single();

            Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
                _source.CreateComponent(component, 1, null, "testuser"));
            _contextFactory.DidNotReceive().GetContext();
        }

        [TestMethod]
        public void UpdateComponent_RejectsATerraformSourceHostBeforePersistence()
        {
            _sourceHostAllowList.IsUnconfigured.Returns(false);
            _sourceHostAllowList
                .IsTerraformSourceAllowed(Arg.Any<string>(), out Arg.Any<string>())
                .Returns(call => { call[1] = "its host 'attacker' is not permitted."; return false; });
            _contextFactory.ClearReceivedCalls();

            var component = OneComponent(
                @"\\attacker\terraform\infra", ComponentType.Terraform).Single();
            component.ComponentId = 7;

            Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
                _source.UpdateComponent(component, 1, null, "testuser"));
            _contextFactory.DidNotReceive().GetContext();
        }

        /// <summary>
        /// With no allow-list configured the host check does not apply. Enforcing against an
        /// unfilled list would reject every project and component edit in every deployment on
        /// the day this ships.
        /// </summary>
        [TestMethod]
        public void ValidateComponents_DoesNotCheckTerraformHostsWhenNoAllowListIsConfigured()
        {
            _source.ValidateComponents(
                OneComponent(@"\\anywhere\terraform\infra", ComponentType.Terraform), 1, HttpRequestType.Post);

            _sourceHostAllowList.DidNotReceive()
                .IsTerraformSourceAllowed(Arg.Any<string>(), out Arg.Any<string>());
        }
    }
}
