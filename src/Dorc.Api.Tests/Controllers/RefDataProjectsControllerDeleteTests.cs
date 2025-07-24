using Dorc.Api.Controllers;
using Dorc.ApiModel;
using Dorc.Core.Interfaces;
using Dorc.PersistentData;
using Dorc.PersistentData.Sources.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using System.Security.Claims;

namespace Dorc.Api.Tests.Controllers
{
    [TestClass]
    public class RefDataProjectsControllerDeleteTests
    {
        private IProjectsPersistentSource _projectsPersistentSource;
        private IAccessControlPersistentSource _accessControlPersistentSource;
        private IActiveDirectorySearcher _activeDirectorySearcher;
        private IRolePrivilegesChecker _rolePrivilegesChecker;
        private ISecurityPrivilegesChecker _securityPrivilegesChecker;
        private IClaimsPrincipalReader _claimsPrincipalReader;
        private RefDataProjectsController _controller;
        private ClaimsPrincipal _user;

        [TestInitialize]
        public void Setup()
        {
            _projectsPersistentSource = Substitute.For<IProjectsPersistentSource>();
            _accessControlPersistentSource = Substitute.For<IAccessControlPersistentSource>();
            _activeDirectorySearcher = Substitute.For<IActiveDirectorySearcher>();
            _rolePrivilegesChecker = Substitute.For<IRolePrivilegesChecker>();
            _securityPrivilegesChecker = Substitute.For<ISecurityPrivilegesChecker>();
            _claimsPrincipalReader = Substitute.For<IClaimsPrincipalReader>();

            _controller = new RefDataProjectsController(
                _projectsPersistentSource,
                _accessControlPersistentSource,
                _activeDirectorySearcher,
                _rolePrivilegesChecker,
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
        public void DeleteProject_PowerUserDeletingExistingProject_Success()
        {
            // Arrange
            var projectId = 123;
            var project = new ProjectApiModel { ProjectId = projectId, ProjectName = "TestProject" };
            
            _rolePrivilegesChecker.IsPowerUser(_user).Returns(true);
            _projectsPersistentSource.GetProject(projectId).Returns(project);
            _securityPrivilegesChecker.CanModifyProject(_user, project.ProjectName).Returns(true);

            // Act
            var result = _controller.Delete(projectId);

            // Assert
            Assert.IsInstanceOfType(result, typeof(ObjectResult));
            var objectResult = (ObjectResult)result;
            Assert.AreEqual(200, objectResult.StatusCode);
            _projectsPersistentSource.Received(1).DeleteProject(projectId);
        }

        [TestMethod]
        public void DeleteProject_RegularUserAttempting_Forbidden()
        {
            // Arrange
            var projectId = 123;
            
            _rolePrivilegesChecker.IsPowerUser(_user).Returns(false);
            _rolePrivilegesChecker.IsAdmin(_user).Returns(false);

            // Act
            var result = _controller.Delete(projectId);

            // Assert
            Assert.IsInstanceOfType(result, typeof(ObjectResult));
            var objectResult = (ObjectResult)result;
            Assert.AreEqual(403, objectResult.StatusCode);
            _projectsPersistentSource.DidNotReceive().DeleteProject(Arg.Any<int>());
        }

        [TestMethod]
        public void DeleteProject_NonExistentProject_NotFound()
        {
            // Arrange
            var projectId = 999;
            
            _rolePrivilegesChecker.IsPowerUser(_user).Returns(true);
            _projectsPersistentSource.GetProject(projectId).Returns((ProjectApiModel?)null);

            // Act
            var result = _controller.Delete(projectId);

            // Assert
            Assert.IsInstanceOfType(result, typeof(ObjectResult));
            var objectResult = (ObjectResult)result;
            Assert.AreEqual(404, objectResult.StatusCode);
            _projectsPersistentSource.DidNotReceive().DeleteProject(Arg.Any<int>());
        }
    }
}