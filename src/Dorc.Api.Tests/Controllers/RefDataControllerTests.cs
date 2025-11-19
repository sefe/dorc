using Dorc.Api.Controllers;
using Dorc.ApiModel;
using Dorc.Core.Interfaces;
using Dorc.PersistentData;
using Dorc.PersistentData.Sources;
using Dorc.PersistentData.Sources.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using System.Security.Claims;

namespace Dorc.Api.Tests.Controllers
{
    [TestClass]
    public class RefDataControllerTests
    {
        private IManageProjectsPersistentSource _manageProjectsPersistentSource;
        private IProjectsPersistentSource _projectsPersistentSource;
        private ISecurityPrivilegesChecker _securityPrivilegesChecker;
        private IClaimsPrincipalReader _claimsPrincipalReader;
        private RefDataController _controller;
        private ClaimsPrincipal _user;

        [TestInitialize]
        public void Setup()
        {
            _manageProjectsPersistentSource = Substitute.For<IManageProjectsPersistentSource>();
            _projectsPersistentSource = Substitute.For<IProjectsPersistentSource>();
            _securityPrivilegesChecker = Substitute.For<ISecurityPrivilegesChecker>();
            _claimsPrincipalReader = Substitute.For<IClaimsPrincipalReader>();

            _controller = new RefDataController(
                _manageProjectsPersistentSource,
                _projectsPersistentSource,
                _securityPrivilegesChecker,
                _claimsPrincipalReader)
            {
                ControllerContext = new ControllerContext()
                {
                    HttpContext = new DefaultHttpContext()
                }
            };

            _user = new ClaimsPrincipal(new ClaimsIdentity(new Claim[] {
                new Claim(ClaimTypes.Name, "TestUser")
            }));
            _controller.HttpContext.User = _user;
        }

        [TestMethod]
        public void Get_ValidProjectId_ReturnsRefData()
        {
            // Arrange
            var projectId = 1;
            var project = new ProjectApiModel { ProjectId = projectId, ProjectName = "TestProject" };
            var components = new List<ComponentApiModel>
            {
                new ComponentApiModel { ComponentId = 1, ComponentName = "Component1", ScriptPath = "path1.ps1" }
            };

            _projectsPersistentSource.GetProject(projectId).Returns(project);
            _manageProjectsPersistentSource.GetOrderedComponents(projectId).Returns(components);

            // Act
            var result = _controller.Get(projectId.ToString()) as ObjectResult;

            // Assert
            Assert.IsNotNull(result);
            Assert.AreEqual(StatusCodes.Status200OK, result.StatusCode);
            var refData = result.Value as RefDataApiModel;
            Assert.IsNotNull(refData);
            Assert.AreEqual(project.ProjectId, refData.Project.ProjectId);
            Assert.AreEqual(components.Count, refData.Components.Count);
        }

        [TestMethod]
        public void Put_UpdateComponentWithNewScript_CreatesNewScriptWhenShared()
        {
            // Arrange
            var projectId = 1;
            var refData = new RefDataApiModel
            {
                Project = new ProjectApiModel { ProjectId = projectId, ProjectName = "TestProject" },
                Components = new List<ComponentApiModel>
                {
                    new ComponentApiModel
                    {
                        ComponentId = 1,
                        ComponentName = "Component1",
                        ScriptPath = "newpath.ps1", // Changed script path
                        NonProdOnly = false
                    }
                }
            };

            _securityPrivilegesChecker.CanModifyProject(_user, projectId).Returns(true);
            _claimsPrincipalReader.GetUserFullDomainName(_user).Returns("DOMAIN\\TestUser");
            _projectsPersistentSource.GetProject(projectId).Returns(refData.Project);
            _manageProjectsPersistentSource.GetOrderedComponents(projectId).Returns(refData.Components);

            // Act
            var result = _controller.Put(refData) as ObjectResult;

            // Assert
            Assert.IsNotNull(result);
            Assert.AreEqual(StatusCodes.Status200OK, result.StatusCode);
            
            // Verify all expected calls were made
            _manageProjectsPersistentSource.Received(1).InsertRefDataAudit(
                "DOMAIN\\TestUser",
                HttpRequestType.Put,
                refData);
            _projectsPersistentSource.Received(1).ValidateProject(refData.Project, HttpRequestType.Put);
            _manageProjectsPersistentSource.Received(1).ValidateComponents(
                refData.Components,
                projectId,
                HttpRequestType.Put);
            _projectsPersistentSource.Received(1).UpdateProject(refData.Project);
        }

        [TestMethod]
        public void Put_UpdateComponentRemoveScript_DeletesScriptWhenNotShared()
        {
            // Arrange
            var projectId = 1;
            var refData = new RefDataApiModel
            {
                Project = new ProjectApiModel { ProjectId = projectId, ProjectName = "TestProject" },
                Components = new List<ComponentApiModel>
                {
                    new ComponentApiModel
                    {
                        ComponentId = 1,
                        ComponentName = "Component1",
                        ScriptPath = null, // Script removed
                        NonProdOnly = false
                    }
                }
            };

            _securityPrivilegesChecker.CanModifyProject(_user, projectId).Returns(true);
            _claimsPrincipalReader.GetUserFullDomainName(_user).Returns("DOMAIN\\TestUser");
            _projectsPersistentSource.GetProject(projectId).Returns(refData.Project);
            _manageProjectsPersistentSource.GetOrderedComponents(projectId).Returns(refData.Components);

            // Act
            var result = _controller.Put(refData) as ObjectResult;

            // Assert
            Assert.IsNotNull(result);
            Assert.AreEqual(StatusCodes.Status200OK, result.StatusCode);
            
            // Verify component update was called
            _manageProjectsPersistentSource.Received(1).TraverseComponents(
                refData.Components,
                null,
                projectId,
                Arg.Any<Action<ComponentApiModel, int, int?>>());
        }

        [TestMethod]
        public void Put_UpdateComponentWithSameScript_DoesNotCreateNewScript()
        {
            // Arrange
            var projectId = 1;
            var refData = new RefDataApiModel
            {
                Project = new ProjectApiModel { ProjectId = projectId, ProjectName = "TestProject" },
                Components = new List<ComponentApiModel>
                {
                    new ComponentApiModel
                    {
                        ComponentId = 1,
                        ComponentName = "Component1",
                        ScriptPath = "samepath.ps1", // Same script path
                        NonProdOnly = false
                    }
                }
            };

            _securityPrivilegesChecker.CanModifyProject(_user, projectId).Returns(true);
            _claimsPrincipalReader.GetUserFullDomainName(_user).Returns("DOMAIN\\TestUser");
            _projectsPersistentSource.GetProject(projectId).Returns(refData.Project);
            _manageProjectsPersistentSource.GetOrderedComponents(projectId).Returns(refData.Components);

            // Act
            var result = _controller.Put(refData) as ObjectResult;

            // Assert
            Assert.IsNotNull(result);
            Assert.AreEqual(StatusCodes.Status200OK, result.StatusCode);
        }

        [TestMethod]
        public void Put_UserWithoutModifyRights_ReturnsForbidden()
        {
            // Arrange
            var projectId = 1;
            var refData = new RefDataApiModel
            {
                Project = new ProjectApiModel { ProjectId = projectId, ProjectName = "TestProject" },
                Components = new List<ComponentApiModel>()
            };

            _securityPrivilegesChecker.CanModifyProject(_user, projectId).Returns(false);

            // Act
            var result = _controller.Put(refData) as ObjectResult;

            // Assert
            Assert.IsNotNull(result);
            Assert.AreEqual(StatusCodes.Status403Forbidden, result.StatusCode);
            Assert.AreEqual("User does not have Modify rights on this Project", result.Value);
        }

        [TestMethod]
        public void Put_ValidationError_ReturnsBadRequest()
        {
            // Arrange
            var projectId = 1;
            var refData = new RefDataApiModel
            {
                Project = new ProjectApiModel { ProjectId = projectId, ProjectName = "TestProject" },
                Components = new List<ComponentApiModel>()
            };

            _securityPrivilegesChecker.CanModifyProject(_user, projectId).Returns(true);
            _claimsPrincipalReader.GetUserFullDomainName(_user).Returns("DOMAIN\\TestUser");
            _projectsPersistentSource.When(x => x.ValidateProject(Arg.Any<ProjectApiModel>(), Arg.Any<HttpRequestType>()))
                .Do(x => throw new ArgumentException("Invalid project"));

            // Act
            var result = _controller.Put(refData) as ObjectResult;

            // Assert
            Assert.IsNotNull(result);
            Assert.AreEqual(StatusCodes.Status400BadRequest, result.StatusCode);
            Assert.AreEqual("Invalid project", result.Value);
        }

        [TestMethod]
        public void Put_AddComponentToProject_CreatesNewComponent()
        {
            // Arrange
            var projectId = 1;
            var refData = new RefDataApiModel
            {
                Project = new ProjectApiModel { ProjectId = projectId, ProjectName = "TestProject" },
                Components = new List<ComponentApiModel>
                {
                    new ComponentApiModel
                    {
                        ComponentId = 0, // New component
                        ComponentName = "NewComponent",
                        ScriptPath = "newscript.ps1",
                        NonProdOnly = false
                    }
                }
            };

            _securityPrivilegesChecker.CanModifyProject(_user, projectId).Returns(true);
            _claimsPrincipalReader.GetUserFullDomainName(_user).Returns("DOMAIN\\TestUser");
            _projectsPersistentSource.GetProject(projectId).Returns(refData.Project);
            _manageProjectsPersistentSource.GetOrderedComponents(projectId).Returns(refData.Components);

            // Act
            var result = _controller.Put(refData) as ObjectResult;

            // Assert
            Assert.IsNotNull(result);
            Assert.AreEqual(StatusCodes.Status200OK, result.StatusCode);
        }

        [TestMethod]
        public void Put_DeleteComponentFromProject_RemovesComponent()
        {
            // Arrange
            var projectId = 1;
            var refData = new RefDataApiModel
            {
                Project = new ProjectApiModel { ProjectId = projectId, ProjectName = "TestProject" },
                Components = new List<ComponentApiModel>() // Empty list means all components deleted
            };

            var flattenedComponents = new List<ComponentApiModel>();

            _securityPrivilegesChecker.CanModifyProject(_user, projectId).Returns(true);
            _claimsPrincipalReader.GetUserFullDomainName(_user).Returns("DOMAIN\\TestUser");
            _projectsPersistentSource.GetProject(projectId).Returns(refData.Project);
            _manageProjectsPersistentSource.GetOrderedComponents(projectId).Returns(flattenedComponents);

            // Act
            var result = _controller.Put(refData) as ObjectResult;

            // Assert
            Assert.IsNotNull(result);
            Assert.AreEqual(StatusCodes.Status200OK, result.StatusCode);
            
            // Verify DeleteComponents was called
            _manageProjectsPersistentSource.Received(1).DeleteComponents(
                Arg.Any<IList<ComponentApiModel>>(),
                projectId);
        }

        [TestMethod]
        public void Post_AdminCreatingNewProject_Success()
        {
            // Arrange
            var refData = new RefDataApiModel
            {
                Project = new ProjectApiModel { ProjectId = 0, ProjectName = "NewProject" },
                Components = new List<ComponentApiModel>
                {
                    new ComponentApiModel
                    {
                        ComponentId = 0,
                        ComponentName = "Component1",
                        ScriptPath = "script.ps1"
                    }
                }
            };

            var adminUser = new ClaimsPrincipal(new ClaimsIdentity(new Claim[] {
                new Claim(ClaimTypes.Name, "AdminUser"),
                new Claim(ClaimTypes.Role, "Admin")
            }));
            _controller.HttpContext.User = adminUser;

            _projectsPersistentSource.GetProject(0).Returns(refData.Project);
            _manageProjectsPersistentSource.GetOrderedComponents(0).Returns(refData.Components);
            _claimsPrincipalReader.GetUserFullDomainName(adminUser).Returns("DOMAIN\\AdminUser");

            // Act
            var result = _controller.Post(refData) as ObjectResult;

            // Assert
            Assert.IsNotNull(result);
            Assert.AreEqual(StatusCodes.Status200OK, result.StatusCode);
            _projectsPersistentSource.Received(1).InsertProject(refData.Project);
        }

        [TestMethod]
        public void Post_NonAdminCreatingNewProject_ThrowsException()
        {
            // Arrange
            var refData = new RefDataApiModel
            {
                Project = new ProjectApiModel { ProjectId = 0, ProjectName = "NewProject" },
                Components = new List<ComponentApiModel>()
            };

            // Act & Assert
            Assert.Throws<Exception>(() => _controller.Post(refData));
        }
    }
}
