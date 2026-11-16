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
            _securityPrivilegesChecker.CanReadSecrets(_user, accessControl.Name).Returns(true);
            _securityPrivilegesChecker.IsEnvironmentOwnerOrAdmin(_user, accessControl.Name).Returns(true);
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
                new AccessControlApiModel { Id = 0, Allow = 4, Name = "NewOwner", Pid = "newowner" } // Owner flag (4)
            };

            var accessControl = new AccessSecureApiModel
            {
                Type = AccessControlType.Environment,
                Name = "TestEnv",
                ObjectId = objectId,
                Privileges = newPrivileges
            };

            _securityPrivilegesChecker.CanModifyEnvironment(_user, accessControl.Name).Returns(true);
            _securityPrivilegesChecker.CanReadSecrets(_user, accessControl.Name).Returns(true);
            _securityPrivilegesChecker.IsEnvironmentOwnerOrAdmin(_user, accessControl.Name).Returns(true);
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
            _securityPrivilegesChecker.IsEnvironmentOwnerOrAdmin(_user, "EnvA").Returns(true);
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

        [TestMethod]
        public void Put_WhenNonOwnerTriesToGrantOwnerOnEnvironmentWithExistingOwner_ReturnsForbidden()
        {
            var objectId = Guid.NewGuid();
            var existingPrivileges = new List<AccessControlApiModel>
            {
                new AccessControlApiModel { Id = 1, Name = "ExistingOwner", Allow = 4, Deny = 0 },
                new AccessControlApiModel { Id = 2, Name = "WriteOnlyUser", Allow = 1, Deny = 0 }
            };
            var newPrivileges = new List<AccessControlApiModel>
            {
                new AccessControlApiModel { Id = 1, Name = "ExistingOwner", Allow = 4, Deny = 0 },
                new AccessControlApiModel { Id = 2, Name = "WriteOnlyUser", Allow = 1 | 4, Deny = 0 }
            };
            var accessControl = new AccessSecureApiModel
            {
                Type = AccessControlType.Environment,
                Name = "TestEnv",
                ObjectId = objectId,
                Privileges = newPrivileges
            };

            _securityPrivilegesChecker.CanModifyEnvironment(_user, accessControl.Name).Returns(true);
            _securityPrivilegesChecker.CanReadSecrets(_user, accessControl.Name).Returns(true);
            _securityPrivilegesChecker.IsEnvironmentOwnerOrAdmin(_user, accessControl.Name).Returns(false);
            StubResolvedObject(accessControl.Name, objectId);
            _accessControlPersistentSource.GetAccessControls(objectId).Returns(existingPrivileges);

            var result = _controller.Put(accessControl) as ObjectResult;

            Assert.IsNotNull(result);
            Assert.AreEqual(StatusCodes.Status403Forbidden, result.StatusCode);
            _accessControlPersistentSource.DidNotReceiveWithAnyArgs().UpdateAccessControl(default!, default, default!);
        }

        [TestMethod]
        public void Put_WhenOwnerWithoutReadSecretsBootstrapsReadSecrets_ReturnsOk()
        {
            var objectId = Guid.NewGuid();
            var existingPrivileges = new List<AccessControlApiModel>
            {
                new AccessControlApiModel { Id = 1, Name = "OwnerUser", Pid = "owner-pid", Allow = 4, Deny = 0 }
            };
            var accessControl = new AccessSecureApiModel
            {
                Type = AccessControlType.Environment,
                Name = "NewEnv",
                ObjectId = objectId,
                Privileges = new List<AccessControlApiModel>
                {
                    new AccessControlApiModel { Id = 1, Name = "OwnerUser", Pid = "owner-pid", Allow = 4 | 2, Deny = 0 }
                }
            };

            _securityPrivilegesChecker.CanModifyEnvironment(_user, accessControl.Name).Returns(true);
            _securityPrivilegesChecker.IsEnvironmentOwnerOrAdmin(_user, accessControl.Name).Returns(true);
            _securityPrivilegesChecker.CanReadSecrets(_user, accessControl.Name).Returns(false);
            StubResolvedObject(accessControl.Name, objectId);
            _accessControlPersistentSource.GetAccessControls(objectId).Returns(existingPrivileges);

            var result = _controller.Put(accessControl) as ObjectResult;

            Assert.IsNotNull(result);
            Assert.AreEqual(StatusCodes.Status200OK, result.StatusCode);
            _accessControlPersistentSource.Received(1).UpdateAccessControl(Arg.Any<AccessControlApiModel>(), objectId, _user);
        }

        [TestMethod]
        public void Put_WhenOwnerWithoutReadSecretsGrantsOwner_ReturnsOk()
        {
            var objectId = Guid.NewGuid();
            var existingPrivileges = new List<AccessControlApiModel>
            {
                new AccessControlApiModel { Id = 1, Name = "OwnerUser", Pid = "owner-pid", Allow = 4, Deny = 0 }
            };
            var accessControl = new AccessSecureApiModel
            {
                Type = AccessControlType.Environment,
                Name = "ProdEnv",
                ObjectId = objectId,
                Privileges = new List<AccessControlApiModel>
                {
                    new AccessControlApiModel { Id = 1, Name = "OwnerUser", Pid = "owner-pid", Allow = 4, Deny = 0 },
                    new AccessControlApiModel { Id = 0, Name = "SecondOwner", Pid = "owner2-pid", Allow = 4, Deny = 0 }
                }
            };

            _securityPrivilegesChecker.CanModifyEnvironment(_user, accessControl.Name).Returns(true);
            _securityPrivilegesChecker.IsEnvironmentOwnerOrAdmin(_user, accessControl.Name).Returns(true);
            _securityPrivilegesChecker.CanReadSecrets(_user, accessControl.Name).Returns(false);
            StubResolvedObject(accessControl.Name, objectId);
            _accessControlPersistentSource.GetAccessControls(objectId).Returns(existingPrivileges);

            var result = _controller.Put(accessControl) as ObjectResult;

            Assert.IsNotNull(result);
            Assert.AreEqual(StatusCodes.Status200OK, result.StatusCode);
        }

        [TestMethod]
        public void Put_WhenPrivilegeIdBelongsToDifferentObject_ReturnsBadRequest()
        {
            var objectId = Guid.NewGuid();
            var existingPrivileges = new List<AccessControlApiModel>
            {
                new AccessControlApiModel { Id = 1, Name = "OwnerUser", Pid = "owner-pid", Allow = 4, Deny = 0 }
            };
            var accessControl = new AccessSecureApiModel
            {
                Type = AccessControlType.Environment,
                Name = "TestEnv",
                ObjectId = objectId,
                Privileges = new List<AccessControlApiModel>
                {
                    new AccessControlApiModel { Id = 1, Name = "OwnerUser", Pid = "owner-pid", Allow = 4, Deny = 0 },
                    new AccessControlApiModel { Id = 999, Name = "ForeignRow", Pid = "attacker-pid", Allow = 4, Deny = 0 }
                }
            };

            _securityPrivilegesChecker.CanModifyEnvironment(_user, accessControl.Name).Returns(true);
            _securityPrivilegesChecker.IsEnvironmentOwnerOrAdmin(_user, accessControl.Name).Returns(true);
            _securityPrivilegesChecker.CanReadSecrets(_user, accessControl.Name).Returns(true);
            StubResolvedObject(accessControl.Name, objectId);
            _accessControlPersistentSource.GetAccessControls(objectId).Returns(existingPrivileges);

            var result = _controller.Put(accessControl) as ObjectResult;

            Assert.IsNotNull(result);
            Assert.AreEqual(StatusCodes.Status400BadRequest, result.StatusCode);
            _accessControlPersistentSource.DidNotReceiveWithAnyArgs().UpdateAccessControl(default!, default, default!);
        }

        [TestMethod]
        public void Put_WhenCallerSwapsPidOnExistingRow_ReturnsBadRequest()
        {
            var objectId = Guid.NewGuid();
            var existingPrivileges = new List<AccessControlApiModel>
            {
                new AccessControlApiModel { Id = 1, Name = "OwnerUser", Pid = "owner-pid", Allow = 4 | 2, Deny = 0 }
            };
            var accessControl = new AccessSecureApiModel
            {
                Type = AccessControlType.Environment,
                Name = "TestEnv",
                ObjectId = objectId,
                Privileges = new List<AccessControlApiModel>
                {
                    new AccessControlApiModel { Id = 1, Name = "Attacker", Pid = "attacker-pid", Allow = 4 | 2, Deny = 0 }
                }
            };

            _securityPrivilegesChecker.CanModifyEnvironment(_user, accessControl.Name).Returns(true);
            _securityPrivilegesChecker.IsEnvironmentOwnerOrAdmin(_user, accessControl.Name).Returns(false);
            _securityPrivilegesChecker.CanReadSecrets(_user, accessControl.Name).Returns(false);
            StubResolvedObject(accessControl.Name, objectId);
            _accessControlPersistentSource.GetAccessControls(objectId).Returns(existingPrivileges);

            var result = _controller.Put(accessControl) as ObjectResult;

            Assert.IsNotNull(result);
            Assert.AreEqual(StatusCodes.Status400BadRequest, result.StatusCode);
            _accessControlPersistentSource.DidNotReceiveWithAnyArgs().UpdateAccessControl(default!, default, default!);
        }

        [TestMethod]
        public void Put_WhenOwnerGrantsMoreThanTwoOwners_AllowsCoOwnership()
        {
            var objectId = Guid.NewGuid();
            var existingPrivileges = new List<AccessControlApiModel>
            {
                new AccessControlApiModel { Id = 1, Name = "Owner1", Allow = 4, Deny = 0 },
                new AccessControlApiModel { Id = 2, Name = "Owner2", Allow = 4, Deny = 0 }
            };
            var newPrivileges = new List<AccessControlApiModel>
            {
                new AccessControlApiModel { Id = 1, Name = "Owner1", Allow = 4, Deny = 0 },
                new AccessControlApiModel { Id = 2, Name = "Owner2", Allow = 4, Deny = 0 },
                new AccessControlApiModel { Id = 0, Name = "TeamGroupOwner", Sid = "S-1-5-21-999", Allow = 4, Deny = 0 }
            };
            var accessControl = new AccessSecureApiModel
            {
                Type = AccessControlType.Environment,
                Name = "TestEnv",
                ObjectId = objectId,
                Privileges = newPrivileges
            };

            _securityPrivilegesChecker.CanModifyEnvironment(_user, accessControl.Name).Returns(true);
            _securityPrivilegesChecker.CanReadSecrets(_user, accessControl.Name).Returns(true);
            _securityPrivilegesChecker.IsEnvironmentOwnerOrAdmin(_user, accessControl.Name).Returns(true);
            StubResolvedObject(accessControl.Name, objectId);
            _accessControlPersistentSource.GetAccessControls(objectId).Returns(existingPrivileges);

            var result = _controller.Put(accessControl) as ObjectResult;

            Assert.IsNotNull(result);
            Assert.AreEqual(StatusCodes.Status200OK, result.StatusCode);
        }

        [TestMethod]
        public void Put_ProjectAcl_AllowsDeletingLegacyRowWithReadSecretsBit()
        {
            var objectId = Guid.NewGuid();
            var existingPrivileges = new List<AccessControlApiModel>
            {
                new AccessControlApiModel { Id = 1, Name = "CreatorOwner", Pid = "creator-pid", Allow = 4 | 1 | 2, Deny = 0 },
                new AccessControlApiModel { Id = 2, Name = "OtherOwner", Pid = "other-pid", Allow = 4 | 1, Deny = 0 }
            };
            var newPrivileges = new List<AccessControlApiModel>
            {
                new AccessControlApiModel { Id = 2, Name = "OtherOwner", Pid = "other-pid", Allow = 4 | 1, Deny = 0 }
            };
            var accessControl = new AccessSecureApiModel
            {
                Type = AccessControlType.Project,
                Name = "TestProject",
                ObjectId = objectId,
                Privileges = newPrivileges
            };

            _securityPrivilegesChecker.CanModifyProject(_user, accessControl.Name).Returns(true);
            _securityPrivilegesChecker.IsProjectOwnerOrAdmin(_user, accessControl.Name).Returns(true);
            _accessControlPersistentSource.GetSecurableObjects<Project>(_user, accessControl.Name)
                .Returns(new[] { new Project { Name = accessControl.Name, ObjectId = objectId } });
            _accessControlPersistentSource.GetAccessControls(objectId).Returns(existingPrivileges);

            var result = _controller.Put(accessControl) as ObjectResult;

            Assert.IsNotNull(result);
            Assert.AreEqual(StatusCodes.Status200OK, result.StatusCode);
            _accessControlPersistentSource.Received(1).DeleteAccessControl(1, objectId, _user);
        }

        [TestMethod]
        public void Put_ProjectAcl_RejectsGrantingReadSecretsBit()
        {
            var objectId = Guid.NewGuid();
            var existingPrivileges = new List<AccessControlApiModel>
            {
                new AccessControlApiModel { Id = 1, Name = "ProjectOwner", Pid = "owner-pid", Allow = 4 | 1, Deny = 0 }
            };
            var newPrivileges = new List<AccessControlApiModel>
            {
                new AccessControlApiModel { Id = 1, Name = "ProjectOwner", Pid = "owner-pid", Allow = 4 | 1 | 2, Deny = 0 }
            };
            var accessControl = new AccessSecureApiModel
            {
                Type = AccessControlType.Project,
                Name = "TestProject",
                ObjectId = objectId,
                Privileges = newPrivileges
            };

            _securityPrivilegesChecker.CanModifyProject(_user, accessControl.Name).Returns(true);
            _securityPrivilegesChecker.IsProjectOwnerOrAdmin(_user, accessControl.Name).Returns(true);
            _accessControlPersistentSource.GetSecurableObjects<Project>(_user, accessControl.Name)
                .Returns(new[] { new Project { Name = accessControl.Name, ObjectId = objectId } });
            _accessControlPersistentSource.GetAccessControls(objectId).Returns(existingPrivileges);

            var result = _controller.Put(accessControl) as ObjectResult;

            Assert.IsNotNull(result);
            Assert.AreEqual(StatusCodes.Status403Forbidden, result.StatusCode);
        }

        [TestMethod]
        public void Get_EnvironmentAndProject_PopulatesSecurityFlagsCorrectly()
        {
            var envObjId = Guid.NewGuid();
            var projObjId = Guid.NewGuid();
            _accessControlPersistentSource.GetSecurableObjects<Dorc.PersistentData.Model.Environment>(_user, "Env1")
                .Returns(new[] { new Dorc.PersistentData.Model.Environment { Name = "Env1", ObjectId = envObjId } });
            _accessControlPersistentSource.GetSecurableObjects<Project>(_user, "Proj1")
                .Returns(new[] { new Project { Name = "Proj1", ObjectId = projObjId } });
            _accessControlPersistentSource.GetAccessControls(Arg.Any<Guid>()).Returns(Array.Empty<AccessControlApiModel>());

            _securityPrivilegesChecker.CanModifyEnvironment(_user, "Env1").Returns(true);
            _securityPrivilegesChecker.IsEnvironmentOwnerOrAdmin(_user, "Env1").Returns(true);
            _securityPrivilegesChecker.CanReadSecrets(_user, "Env1").Returns(false);

            var envGet = (_controller.Get(AccessControlType.Environment, "Env1") as ObjectResult)?.Value as AccessSecureApiModel;
            Assert.IsNotNull(envGet);
            Assert.IsTrue(envGet.UserEditable);
            Assert.IsTrue(envGet.UserIsOwner);
            Assert.IsTrue(envGet.UserCanReadSecrets);

            _securityPrivilegesChecker.IsEnvironmentOwnerOrAdmin(_user, "Env1").Returns(false);
            _securityPrivilegesChecker.CanReadSecrets(_user, "Env1").Returns(true);
            var envNonOwnerReaderGet = (_controller.Get(AccessControlType.Environment, "Env1") as ObjectResult)?.Value as AccessSecureApiModel;
            Assert.IsNotNull(envNonOwnerReaderGet);
            Assert.IsFalse(envNonOwnerReaderGet.UserIsOwner);
            Assert.IsFalse(envNonOwnerReaderGet.UserCanReadSecrets);

            _securityPrivilegesChecker.CanReadSecrets(_user, "Env1").Returns(false);
            var envWriteOnlyGet = (_controller.Get(AccessControlType.Environment, "Env1") as ObjectResult)?.Value as AccessSecureApiModel;
            Assert.IsNotNull(envWriteOnlyGet);
            Assert.IsFalse(envWriteOnlyGet.UserCanReadSecrets);

            _securityPrivilegesChecker.CanModifyProject(_user, "Proj1").Returns(true);
            _securityPrivilegesChecker.IsProjectOwnerOrAdmin(_user, "Proj1").Returns(true);

            var projGet = (_controller.Get(AccessControlType.Project, "Proj1") as ObjectResult)?.Value as AccessSecureApiModel;
            Assert.IsNotNull(projGet);
            Assert.IsTrue(projGet.UserEditable);
            Assert.IsTrue(projGet.UserIsOwner);
            Assert.IsFalse(projGet.UserCanReadSecrets);
        }

        [TestMethod]
        public void Put_ValidationAndDenyGuards_CoverAllBranches()
        {
            var objectId = Guid.NewGuid();
            StubResolvedObject("TestEnv", objectId);
            _securityPrivilegesChecker.CanModifyEnvironment(_user, "TestEnv").Returns(true);
            _securityPrivilegesChecker.IsEnvironmentOwnerOrAdmin(_user, "TestEnv").Returns(false);
            _securityPrivilegesChecker.CanReadSecrets(_user, "TestEnv").Returns(true);

            // 1. Null Privileges -> 400
            var nullPrivs = new AccessSecureApiModel { Type = AccessControlType.Environment, Name = "TestEnv", Privileges = null! };
            Assert.AreEqual(StatusCodes.Status400BadRequest, (_controller.Put(nullPrivs) as ObjectResult)?.StatusCode);

            var existing = new List<AccessControlApiModel>
            {
                new AccessControlApiModel { Id = 1, Name = "Owner1", Sid = "sid-1", Pid = "pid-1", Allow = 4, Deny = 0 },
                new AccessControlApiModel { Id = 2, Name = "DeniedUser", Sid = "sid-2", Pid = "pid-2", Allow = 0, Deny = 1 }
            };
            _accessControlPersistentSource.GetAccessControls(objectId).Returns(existing);

            // 2. Negative privilege.Id -> 400
            var negId = new AccessSecureApiModel
            {
                Type = AccessControlType.Environment,
                Name = "TestEnv",
                Privileges = new List<AccessControlApiModel> { new AccessControlApiModel { Id = -1, Name = "Bad", Allow = 1 } }
            };
            Assert.AreEqual(StatusCodes.Status400BadRequest, (_controller.Put(negId) as ObjectResult)?.StatusCode);

            // 3. Duplicate positive privilege.Id -> 400
            var dupId = new AccessSecureApiModel
            {
                Type = AccessControlType.Environment,
                Name = "TestEnv",
                Privileges = new List<AccessControlApiModel>
                {
                    new AccessControlApiModel { Id = 1, Name = "Owner1", Sid = "sid-1", Pid = "pid-1", Allow = 4 },
                    new AccessControlApiModel { Id = 1, Name = "Owner1", Sid = "sid-1", Pid = "pid-1", Allow = 4 }
                }
            };
            Assert.AreEqual(StatusCodes.Status400BadRequest, (_controller.Put(dupId) as ObjectResult)?.StatusCode);

            // 4. Changing Sid on existing row -> 400
            var swapSid = new AccessSecureApiModel
            {
                Type = AccessControlType.Environment,
                Name = "TestEnv",
                Privileges = new List<AccessControlApiModel>
                {
                    new AccessControlApiModel { Id = 1, Name = "Owner1", Sid = "sid-tampered", Pid = "pid-1", Allow = 4 }
                }
            };
            Assert.AreEqual(StatusCodes.Status400BadRequest, (_controller.Put(swapSid) as ObjectResult)?.StatusCode);

            // 5. Non-owner modifying Deny bit -> 403
            var modifyDeny = new AccessSecureApiModel
            {
                Type = AccessControlType.Environment,
                Name = "TestEnv",
                Privileges = new List<AccessControlApiModel>
                {
                    new AccessControlApiModel { Id = 1, Name = "Owner1", Sid = "sid-1", Pid = "pid-1", Allow = 4, Deny = 1 },
                    new AccessControlApiModel { Id = 2, Name = "DeniedUser", Sid = "sid-2", Pid = "pid-2", Allow = 0, Deny = 1 }
                }
            };
            Assert.AreEqual(StatusCodes.Status403Forbidden, (_controller.Put(modifyDeny) as ObjectResult)?.StatusCode);

            // 6. Non-owner adding new row with Deny != 0 -> 403
            var addDeny = new AccessSecureApiModel
            {
                Type = AccessControlType.Environment,
                Name = "TestEnv",
                Privileges = new List<AccessControlApiModel>
                {
                    new AccessControlApiModel { Id = 1, Name = "Owner1", Sid = "sid-1", Pid = "pid-1", Allow = 4, Deny = 0 },
                    new AccessControlApiModel { Id = 2, Name = "DeniedUser", Sid = "sid-2", Pid = "pid-2", Allow = 0, Deny = 1 },
                    new AccessControlApiModel { Id = 0, Name = "NewDeny", Sid = "sid-3", Allow = 0, Deny = 1 }
                }
            };
            Assert.AreEqual(StatusCodes.Status403Forbidden, (_controller.Put(addDeny) as ObjectResult)?.StatusCode);

            // 7. Non-owner deleting row with Deny != 0 -> 403
            var deleteDeny = new AccessSecureApiModel
            {
                Type = AccessControlType.Environment,
                Name = "TestEnv",
                Privileges = new List<AccessControlApiModel>
                {
                    new AccessControlApiModel { Id = 1, Name = "Owner1", Sid = "sid-1", Pid = "pid-1", Allow = 4, Deny = 0 }
                }
            };
            Assert.AreEqual(StatusCodes.Status403Forbidden, (_controller.Put(deleteDeny) as ObjectResult)?.StatusCode);

            // 8. Removing all owners on Environment -> 400
            _securityPrivilegesChecker.IsEnvironmentOwnerOrAdmin(_user, "TestEnv").Returns(true);
            var removeAllOwners = new AccessSecureApiModel
            {
                Type = AccessControlType.Environment,
                Name = "TestEnv",
                Privileges = new List<AccessControlApiModel>
                {
                    new AccessControlApiModel { Id = 1, Name = "Owner1", Sid = "sid-1", Pid = "pid-1", Allow = 1, Deny = 0 },
                    new AccessControlApiModel { Id = 2, Name = "DeniedUser", Sid = "sid-2", Pid = "pid-2", Allow = 0, Deny = 1 }
                }
            };
            Assert.AreEqual(StatusCodes.Status400BadRequest, (_controller.Put(removeAllOwners) as ObjectResult)?.StatusCode);

            // 9. Deleting a row with ReadSecrets on Environment when Write-only caller lacks CanReadSecrets -> 403
            _securityPrivilegesChecker.IsEnvironmentOwnerOrAdmin(_user, "TestEnv").Returns(false);
            _securityPrivilegesChecker.CanReadSecrets(_user, "TestEnv").Returns(false);
            _accessControlPersistentSource.GetAccessControls(objectId).Returns(new List<AccessControlApiModel>
            {
                new AccessControlApiModel { Id = 1, Name = "Owner1", Sid = "sid-1", Pid = "pid-1", Allow = 4, Deny = 0 },
                new AccessControlApiModel { Id = 3, Name = "SecretReader", Sid = "sid-3", Pid = "pid-3", Allow = 2, Deny = 0 }
            });
            var deleteSecretReaderOnEnv = new AccessSecureApiModel
            {
                Type = AccessControlType.Environment,
                Name = "TestEnv",
                Privileges = new List<AccessControlApiModel>
                {
                    new AccessControlApiModel { Id = 1, Name = "Owner1", Sid = "sid-1", Pid = "pid-1", Allow = 4, Deny = 0 }
                }
            };
            Assert.AreEqual(StatusCodes.Status403Forbidden, (_controller.Put(deleteSecretReaderOnEnv) as ObjectResult)?.StatusCode);

            // 10. Write-only user (neither Owner nor ReadSecrets) on Environment:
            // 10a. Tries to grant ReadSecrets -> 403
            var grantSecretsWithoutPerm = new AccessSecureApiModel
            {
                Type = AccessControlType.Environment,
                Name = "TestEnv",
                Privileges = new List<AccessControlApiModel>
                {
                    new AccessControlApiModel { Id = 1, Name = "Owner1", Sid = "sid-1", Pid = "pid-1", Allow = 4, Deny = 0 },
                    new AccessControlApiModel { Id = 3, Name = "SecretReader", Sid = "sid-3", Pid = "pid-3", Allow = 2, Deny = 0 },
                    new AccessControlApiModel { Id = 0, Name = "UnauthorizedSecretGrant", Sid = "sid-4", Pid = "pid-4", Allow = 2, Deny = 0 }
                }
            };
            Assert.AreEqual(StatusCodes.Status403Forbidden, (_controller.Put(grantSecretsWithoutPerm) as ObjectResult)?.StatusCode);

            // 10b. Adds a Write-only row (Id=0) while preserving existing ReadSecrets row (Id=3) -> 200 OK
            var addWriteRowPreservingSecrets = new AccessSecureApiModel
            {
                Type = AccessControlType.Environment,
                Name = "TestEnv",
                Privileges = new List<AccessControlApiModel>
                {
                    new AccessControlApiModel { Id = 1, Name = "Owner1", Sid = "sid-1", Pid = "pid-1", Allow = 4, Deny = 0 },
                    new AccessControlApiModel { Id = 3, Name = "SecretReader", Sid = "sid-3", Pid = "pid-3", Allow = 2, Deny = 0 },
                    new AccessControlApiModel { Id = 0, Name = "NewWriter", Sid = "sid-4", Pid = "pid-4", Allow = 1, Deny = 0 }
                }
            };
            Assert.AreEqual(StatusCodes.Status200OK, (_controller.Put(addWriteRowPreservingSecrets) as ObjectResult)?.StatusCode);

            // 10c. Tries to add a new unpersisted Owner row (Id=0, Allow=4) -> 403
            var addUnpersistedOwner = new AccessSecureApiModel
            {
                Type = AccessControlType.Environment,
                Name = "TestEnv",
                Privileges = new List<AccessControlApiModel>
                {
                    new AccessControlApiModel { Id = 1, Name = "Owner1", Sid = "sid-1", Pid = "pid-1", Allow = 4, Deny = 0 },
                    new AccessControlApiModel { Id = 3, Name = "SecretReader", Sid = "sid-3", Pid = "pid-3", Allow = 2, Deny = 0 },
                    new AccessControlApiModel { Id = 0, Name = "RogueOwner", Sid = "sid-4", Pid = "pid-4", Allow = 4, Deny = 0 }
                }
            };
            Assert.AreEqual(StatusCodes.Status403Forbidden, (_controller.Put(addUnpersistedOwner) as ObjectResult)?.StatusCode);

            // 11. Non-owner with Write + CanReadSecrets trying to grant ReadSecrets on Environment -> 403 Forbidden
            _securityPrivilegesChecker.IsEnvironmentOwnerOrAdmin(_user, "TestEnv").Returns(false);
            _securityPrivilegesChecker.CanReadSecrets(_user, "TestEnv").Returns(true);
            var nonOwnerSecretReaderGrantsReadSecrets = new AccessSecureApiModel
            {
                Type = AccessControlType.Environment,
                Name = "TestEnv",
                Privileges = new List<AccessControlApiModel>
                {
                    new AccessControlApiModel { Id = 1, Name = "Owner1", Sid = "sid-1", Pid = "pid-1", Allow = 4, Deny = 0 },
                    new AccessControlApiModel { Id = 3, Name = "SecretReader", Sid = "sid-3", Pid = "pid-3", Allow = 2, Deny = 0 },
                    new AccessControlApiModel { Id = 0, Name = "NewSecretReader", Sid = "sid-4", Pid = "pid-4", Allow = 2, Deny = 0 }
                }
            };
            Assert.AreEqual(StatusCodes.Status403Forbidden, (_controller.Put(nonOwnerSecretReaderGrantsReadSecrets) as ObjectResult)?.StatusCode);
        }

        [TestMethod]
        public void RefDataProjectsController_Post_GrantsOwnerAndWriteWithoutReadSecrets()
        {
            var projSource = Substitute.For<IProjectsPersistentSource>();
            var claimsReader = Substitute.For<Dorc.PersistentData.IClaimsPrincipalReader>();
            var adSearcher = Substitute.For<IActiveDirectorySearcher>();
            var roleChecker = Substitute.For<Dorc.PersistentData.IRolePrivilegesChecker>();
            var secChecker = Substitute.For<ISecurityPrivilegesChecker>();
            var defsService = Substitute.For<IDeploymentDefinitions>();

            var controller = new RefDataProjectsController(
                projSource,
                _accessControlPersistentSource,
                adSearcher,
                roleChecker,
                secChecker,
                claimsReader,
                defsService)
            {
                ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = _user } }
            };

            roleChecker.IsPowerUser(_user).Returns(true);
            var model = new ProjectApiModel { ProjectName = "NewSelfServiceProject" };
            projSource.GetProject("NewSelfServiceProject").Returns((ProjectApiModel)null!);
            projSource.ProjectArtifactsUriHttpValid(model).Returns(true);
            claimsReader.GetUserLogin(_user).Returns("puser");
            claimsReader.GetUserFullDomainName(_user).Returns("DOMAIN\\puser");
            adSearcher.GetUserData("puser").Returns(new UserElementApiModel { Sid = "sid-puser", Pid = "pid-puser", DisplayName = "Power User" });

            AccessControlApiModel? capturedAcl = null;
            defsService.CreateProject(model, "DOMAIN\\puser", Arg.Do<AccessControlApiModel>(ac => capturedAcl = ac), Arg.Any<Action>())
                .Returns(model);

            var result = controller.Post(model) as OkObjectResult;
            Assert.IsNotNull(result);
            Assert.IsNotNull(capturedAcl);
            Assert.AreEqual((int)(Dorc.PersistentData.AccessLevel.Owner | Dorc.PersistentData.AccessLevel.Write), capturedAcl.Allow);
            Assert.AreEqual(0, capturedAcl.Allow & (int)Dorc.PersistentData.AccessLevel.ReadSecrets);
        }
    }
}
