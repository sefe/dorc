using Dorc.Api.Controllers;
using Dorc.ApiModel;
using Dorc.Core.Interfaces;
using Dorc.PersistentData.Model;
using Dorc.PersistentData.Sources.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using System.Security.Claims;
using Environment = Dorc.PersistentData.Model.Environment;

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

        private void StubResolvedObject(string name, Guid objectId)
        {
            _accessControlPersistentSource.GetSecurableObjects<Environment>(Arg.Any<ClaimsPrincipal>(), name)
                .Returns(new[] { new SecurityObject { ObjectId = objectId, Name = name } });
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
            StubResolvedObject(accessControl.Name, objectId);
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
            StubResolvedObject(accessControl.Name, objectId);
            _accessControlPersistentSource.GetAccessControls(objectId).Returns(currentOwners);
            _accessControlPersistentSource.UpdateAccessControl(Arg.Any<AccessControlApiModel>(), Arg.Any<Guid>(), Arg.Any<ClaimsPrincipal>()).Returns(ci => ci.Arg<AccessControlApiModel>());

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
            StubResolvedObject(accessControl.Name, objectId);
            _accessControlPersistentSource.GetAccessControls(objectId).Returns(currentOwners);
            _accessControlPersistentSource.AddAccessControl(Arg.Any<AccessControlApiModel>(), Arg.Any<Guid>(), Arg.Any<ClaimsPrincipal>()).Returns(ci => ci.Arg<AccessControlApiModel>());

            // Act
            var result = _controller.Put(accessControl);

            // Assert
            Assert.IsInstanceOfType(result, typeof(ObjectResult));
            var objectResult = (ObjectResult)result;
            Assert.AreEqual(StatusCodes.Status200OK, objectResult.StatusCode);
        }

        [TestMethod]
        public void Put_WritesToObjectResolvedFromName_NotClientSuppliedObjectId()
        {
            // Arrange: caller is authorized on "EnvA" (resolves to resolvedObjectId) but supplies
            // the ObjectId of a different object they do not own (spoofedObjectId).
            var resolvedObjectId = Guid.NewGuid();
            var spoofedObjectId = Guid.NewGuid();

            _securityPrivilegesChecker.CanModifyEnvironment(_user, "EnvA").Returns(true);
            _securityPrivilegesChecker.CanReadSecrets(_user, "EnvA").Returns(true);
            StubResolvedObject("EnvA", resolvedObjectId);
            _accessControlPersistentSource.GetAccessControls(Arg.Any<Guid>()).Returns(new List<AccessControlApiModel>());

            var accessControl = new AccessSecureApiModel
            {
                Type = AccessControlType.Environment,
                Name = "EnvA",
                ObjectId = spoofedObjectId,
                Privileges = new List<AccessControlApiModel>
                {
                    new AccessControlApiModel { Id = 0, Allow = 4, Name = "NewOwner", Pid = "newowner" }
                }
            };

            // Act
            var result = _controller.Put(accessControl);

            // Assert: the grant lands on the resolved object, never the spoofed one.
            _accessControlPersistentSource.Received().AddAccessControl(Arg.Any<AccessControlApiModel>(), resolvedObjectId, Arg.Any<ClaimsPrincipal>());
            _accessControlPersistentSource.DidNotReceive().AddAccessControl(Arg.Any<AccessControlApiModel>(), spoofedObjectId, Arg.Any<ClaimsPrincipal>());
            _accessControlPersistentSource.DidNotReceive().DeleteAccessControl(Arg.Any<int>(), spoofedObjectId, Arg.Any<ClaimsPrincipal>());
            var objectResult = (ObjectResult)result;
            Assert.AreEqual(StatusCodes.Status200OK, objectResult.StatusCode);
        }

        [TestMethod]
        public void Put_UnauthorizedOnName_Forbidden()
        {
            _securityPrivilegesChecker.CanModifyEnvironment(Arg.Any<ClaimsPrincipal>(), Arg.Any<string>()).Returns(false);

            var accessControl = new AccessSecureApiModel
            {
                Type = AccessControlType.Environment,
                Name = "EnvA",
                ObjectId = Guid.NewGuid(),
                Privileges = new List<AccessControlApiModel>()
            };

            var result = _controller.Put(accessControl);

            var objectResult = (ObjectResult)result;
            Assert.AreEqual(StatusCodes.Status403Forbidden, objectResult.StatusCode);
            _accessControlPersistentSource.DidNotReceive().AddAccessControl(Arg.Any<AccessControlApiModel>(), Arg.Any<Guid>(), Arg.Any<ClaimsPrincipal>());
        }

        [TestMethod]
        public void Put_NameDoesNotResolve_BadRequest()
        {
            _securityPrivilegesChecker.CanModifyEnvironment(_user, "Ghost").Returns(true);
            _securityPrivilegesChecker.CanReadSecrets(_user, "Ghost").Returns(true);
            _accessControlPersistentSource.GetSecurableObjects<Environment>(Arg.Any<ClaimsPrincipal>(), "Ghost")
                .Returns(new List<SecurityObject>());

            var accessControl = new AccessSecureApiModel
            {
                Type = AccessControlType.Environment,
                Name = "Ghost",
                ObjectId = Guid.NewGuid(),
                Privileges = new List<AccessControlApiModel>()
            };

            var result = _controller.Put(accessControl);

            var objectResult = (ObjectResult)result;
            Assert.AreEqual(StatusCodes.Status400BadRequest, objectResult.StatusCode);
            _accessControlPersistentSource.DidNotReceive().AddAccessControl(Arg.Any<AccessControlApiModel>(), Arg.Any<Guid>(), Arg.Any<ClaimsPrincipal>());
        }
    }
}
