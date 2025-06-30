using Dorc.Api.Controllers;
using Dorc.ApiModel;
using Dorc.Core.Interfaces;
using Dorc.PersistentData.Sources.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using System.Security.Claims;

namespace Dorc.Api.Tests.Controllers
{
    [TestClass]
    public class AccessControlControllerTests
    {
        private IAccessControlPersistentSource _accessControlPersistentSource;
        private IActiveDirectorySearcher _adSearcher;
        private ISecurityPrivilegesChecker _securityPrivilegesChecker;
        private AccessControlController _controller;
        private ClaimsPrincipal _user;

        [TestInitialize]
        public void Setup()
        {
            _accessControlPersistentSource = Substitute.For<IAccessControlPersistentSource>();
            _adSearcher = Substitute.For<IActiveDirectorySearcher>();
            _securityPrivilegesChecker = Substitute.For<ISecurityPrivilegesChecker>();
            _controller = new AccessControlController(_accessControlPersistentSource, _adSearcher, _securityPrivilegesChecker)
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
        public void Put_WhenRemovingLastOwner_ReturnsBadRequest()
        {
            // Arrange
            var objectId = Guid.NewGuid();
            var currentOwners = new List<AccessControlApiModel>
            {
                new AccessControlApiModel { Id = 1, Allow = 4, Name = "Owner1", Pid = "owner1" } // Owner flag (4)
            };
            var newPrivileges = new List<AccessControlApiModel>
            {
                new AccessControlApiModel { Id = 1, Allow = 1, Name = "Owner1", Pid = "owner1" } // Write flag (1)
            };

            var accessControl = new AccessSecureApiModel
            {
                Type = AccessControlType.Environment,
                Name = "TestEnv",
                ObjectId = objectId,
                Privileges = newPrivileges
            };

            _securityPrivilegesChecker.CanModifyEnvironment(_user, accessControl.Name).Returns(true);
            _accessControlPersistentSource.GetAccessControls(objectId).Returns(currentOwners);

            // Act
            var result = _controller.Put(accessControl);

            // Assert
            Assert.IsInstanceOfType(result, typeof(ObjectResult));
            var badRequestResult = (ObjectResult)result;
            Assert.AreEqual(StatusCodes.Status400BadRequest, badRequestResult.StatusCode);
        }

        [TestMethod]
        public void Put_WhenKeepingAtLeastOneOwner_ReturnsOk()
        {
            // Arrange
            var objectId = Guid.NewGuid();
            var currentOwners = new List<AccessControlApiModel>
            {
                new AccessControlApiModel { Id = 1, Allow = 4, Name = "Owner1", Pid = "owner1" }, // Owner flag (4)
                new AccessControlApiModel { Id = 2, Allow = 4, Name = "Owner2", Pid = "owner2" }  // Owner flag (4)
            };
            var newPrivileges = new List<AccessControlApiModel>
            {
                new AccessControlApiModel { Id = 1, Allow = 1, Name = "Owner1", Pid = "owner1" }, // Write flag (1)
                new AccessControlApiModel { Id = 2, Allow = 4, Name = "Owner2", Pid = "owner2" }  // Owner flag (4)
            };

            var accessControl = new AccessSecureApiModel
            {
                Type = AccessControlType.Environment,
                Name = "TestEnv",
                ObjectId = objectId,
                Privileges = newPrivileges
            };

            _securityPrivilegesChecker.CanModifyEnvironment(_user, accessControl.Name).Returns(true);
            _accessControlPersistentSource.GetAccessControls(objectId).Returns(currentOwners);
            _accessControlPersistentSource.UpdateAccessControl(Arg.Any<AccessControlApiModel>()).Returns(ci => ci.Arg<AccessControlApiModel>());

            // Act
            var result = _controller.Put(accessControl);

            // Assert
            Assert.IsInstanceOfType(result, typeof(ObjectResult));
            var objectResult = (ObjectResult)result;
            Assert.AreEqual(StatusCodes.Status200OK, objectResult.StatusCode);
        }

        [TestMethod]
        public void Put_WhenNoCurrentOwners_AllowsUpdate()
        {
            // Arrange
            var objectId = Guid.NewGuid();
            var currentOwners = new List<AccessControlApiModel>(); // No current owners
            var newPrivileges = new List<AccessControlApiModel>
            {
                new AccessControlApiModel { Id = 1, Allow = 4, Name = "NewOwner", Pid = "newowner" } // Owner flag (4)
            };

            var accessControl = new AccessSecureApiModel
            {
                Type = AccessControlType.Environment,
                Name = "TestEnv",
                ObjectId = objectId,
                Privileges = newPrivileges
            };

            _securityPrivilegesChecker.CanModifyEnvironment(_user, accessControl.Name).Returns(true);
            _accessControlPersistentSource.GetAccessControls(objectId).Returns(currentOwners);
            _accessControlPersistentSource.AddAccessControl(Arg.Any<AccessControlApiModel>(), Arg.Any<Guid>()).Returns(ci => ci.Arg<AccessControlApiModel>());

            // Act
            var result = _controller.Put(accessControl);

            // Assert
            Assert.IsInstanceOfType(result, typeof(ObjectResult));
            var objectResult = (ObjectResult)result;
            Assert.AreEqual(StatusCodes.Status200OK, objectResult.StatusCode);
        }
    }
} 