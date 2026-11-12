using System.Security.Claims;
using Dorc.Core.Interfaces;
using Dorc.PersistentData;
using Dorc.PersistentData.Model;
using Dorc.PersistentData.Repositories;
using Dorc.PersistentData.Sources.Interfaces;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NSubstitute;
using Environment = Dorc.PersistentData.Model.Environment;

namespace Dorc.Core.Tests
{
    [TestClass]
    public class SecurityPrivilegesCheckerTests
    {
        private IProjectsPersistentSource _projectsPersistentSource = null!;
        private IEnvironmentsPersistentSource _environmentsPersistentSource = null!;
        private ISecurityObjectFilter _securityObjectFilter = null!;
        private IRolePrivilegesChecker _rolePrivilegesChecker = null!;
        private IClaimsPrincipalReader _claimsPrincipalReader = null!;
        private SecurityPrivilegesChecker _sut = null!;
        private ClaimsPrincipal _user = null!;

        [TestInitialize]
        public void Setup()
        {
            _projectsPersistentSource = Substitute.For<IProjectsPersistentSource>();
            _environmentsPersistentSource = Substitute.For<IEnvironmentsPersistentSource>();
            _securityObjectFilter = Substitute.For<ISecurityObjectFilter>();
            _rolePrivilegesChecker = Substitute.For<IRolePrivilegesChecker>();
            _claimsPrincipalReader = Substitute.For<IClaimsPrincipalReader>();

            _sut = new SecurityPrivilegesChecker(
                _projectsPersistentSource,
                _environmentsPersistentSource,
                _securityObjectFilter,
                _rolePrivilegesChecker,
                _claimsPrincipalReader);

            _user = new ClaimsPrincipal(new ClaimsIdentity());
        }

        [TestMethod]
        public void CanModifyPropertyValue_WhenEnvironmentNameIsNullOrEmpty_UsesCanModifyPropertyForPowerUser()
        {
            _rolePrivilegesChecker.IsAdmin(_user).Returns(false);
            _rolePrivilegesChecker.IsPowerUser(_user).Returns(true);

            Assert.IsTrue(_sut.CanModifyPropertyValue(_user, null!));
            Assert.IsTrue(_sut.CanModifyPropertyValue(_user, string.Empty));
        }

        [TestMethod]
        public void CanModifyPropertyValue_WhenEnvironmentNameIsProvided_UsesCanModifyEnvironment()
        {
            var env = new Environment { Name = "DevEnv" };
            _environmentsPersistentSource.GetSecurityObject("DevEnv").Returns(env);
            _rolePrivilegesChecker.IsPowerUser(_user).Returns(true);
            _rolePrivilegesChecker.IsAdmin(_user).Returns(false);
            _securityObjectFilter.HasPrivilege(env, _user, AccessLevel.Write | AccessLevel.Owner).Returns(false);
            _environmentsPersistentSource.IsEnvironmentOwner("DevEnv", _user).Returns(false);

            Assert.IsFalse(_sut.CanModifyPropertyValue(_user, "DevEnv"));
        }

        [TestMethod]
        public void CanModifyProject_AcceptsWriteOrOwnerAccessLevel()
        {
            var project = new Project { Id = 7, Name = "TestProject" };
            _projectsPersistentSource.GetSecurityObject("TestProject").Returns(project);
            _projectsPersistentSource.GetSecurityObject(7).Returns(project);
            _securityObjectFilter.HasPrivilege(project, _user, AccessLevel.Write | AccessLevel.Owner).Returns(true);

            Assert.IsTrue(_sut.CanModifyProject(_user, "TestProject"));
            Assert.IsTrue(_sut.CanModifyProject(_user, 7));
        }

        [TestMethod]
        public void CanReadSecrets_RequiresExplicitReadSecrets_NeverImpliedByAdminOrOwner()
        {
            var objectId = Guid.NewGuid();
            var env = new Environment { Name = "ProdEnv", ObjectId = objectId };
            _environmentsPersistentSource.GetSecurityObject("ProdEnv").Returns(env);

            var accessControlPersistentSource = Substitute.For<IAccessControlPersistentSource>();
            var claimsReader = Substitute.For<IClaimsPrincipalReader>();
            claimsReader.GetSidsForUser(Arg.Any<System.Security.Principal.IPrincipal>())
                .Returns(new List<string> { "admin-sid" });

            // Real SecurityObjectFilter with an Admin user who also has Owner + Write on the ACL, but NOT ReadSecrets (2)
            var adminIdentity = new ClaimsIdentity(
                new[] { new Claim(ClaimTypes.Name, "AdminUser"), new Claim(ClaimTypes.Role, "Admin") },
                "TestAuth",
                ClaimTypes.Name,
                ClaimTypes.Role);
            var adminUser = new ClaimsPrincipal(adminIdentity);

            accessControlPersistentSource.GetAccessControls(objectId).Returns(new[]
            {
                new Dorc.ApiModel.AccessControlApiModel
                {
                    Id = 1,
                    Sid = "admin-sid",
                    Allow = (int)(AccessLevel.Owner | AccessLevel.Write),
                    Deny = 0
                }
            });

            var realFilter = new SecurityObjectFilter(accessControlPersistentSource, claimsReader);
            var realSut = new SecurityPrivilegesChecker(
                _projectsPersistentSource,
                _environmentsPersistentSource,
                realFilter,
                _rolePrivilegesChecker,
                claimsReader);

            _rolePrivilegesChecker.IsAdmin(adminUser).Returns(true);
            _environmentsPersistentSource.IsEnvironmentOwner("ProdEnv", adminUser).Returns(true);

            // Admin + Owner + Write still cannot read secrets without explicit AccessLevel.ReadSecrets
            Assert.IsFalse(realSut.CanReadSecrets(adminUser, "ProdEnv"));

            // Once explicit ReadSecrets (2) is granted on the ACL, CanReadSecrets returns true
            accessControlPersistentSource.GetAccessControls(objectId).Returns(new[]
            {
                new Dorc.ApiModel.AccessControlApiModel
                {
                    Id = 1,
                    Sid = "admin-sid",
                    Allow = (int)(AccessLevel.Owner | AccessLevel.Write | AccessLevel.ReadSecrets),
                    Deny = 0
                }
            });

            Assert.IsTrue(realSut.CanReadSecrets(adminUser, "ProdEnv"));

            // When a matching ACL row has Deny != 0, HasPrivilege returns false even if Allow has ReadSecrets
            accessControlPersistentSource.GetAccessControls(objectId).Returns(new[]
            {
                new Dorc.ApiModel.AccessControlApiModel
                {
                    Id = 1,
                    Sid = "admin-sid",
                    Allow = (int)(AccessLevel.Owner | AccessLevel.Write | AccessLevel.ReadSecrets),
                    Deny = (int)AccessLevel.ReadSecrets
                },
                new Dorc.ApiModel.AccessControlApiModel
                {
                    Id = 2,
                    Pid = "admin-sid",
                    Allow = (int)AccessLevel.Write,
                    Deny = 0
                }
            });
            Assert.IsFalse(realSut.CanReadSecrets(adminUser, "ProdEnv"));

            // Admin role still bypasses non-ReadSecrets checks in HasPrivilege
            Assert.IsTrue(realFilter.HasPrivilege(env, adminUser, AccessLevel.Write));

            // When environment does not exist, CanReadSecrets returns false
            _environmentsPersistentSource.GetSecurityObject("MissingEnv").Returns((Environment)null!);
            Assert.IsFalse(realSut.CanReadSecrets(adminUser, "MissingEnv"));
        }

        [TestMethod]
        public void IsEnvironmentOwnerOrAdmin_And_IsProjectOwnerOrAdmin_CoverAllBranches()
        {
            // IsEnvironmentOwnerOrAdmin: Admin true vs false + Owner true/false
            _rolePrivilegesChecker.IsAdmin(_user).Returns(true);
            Assert.IsTrue(_sut.IsEnvironmentOwnerOrAdmin(_user, "Env1"));

            _rolePrivilegesChecker.IsAdmin(_user).Returns(false);
            _environmentsPersistentSource.IsEnvironmentOwner("Env1", _user).Returns(true);
            Assert.IsTrue(_sut.IsEnvironmentOwnerOrAdmin(_user, "Env1"));
            _environmentsPersistentSource.IsEnvironmentOwner("Env1", _user).Returns(false);
            Assert.IsFalse(_sut.IsEnvironmentOwnerOrAdmin(_user, "Env1"));

            // IsProjectOwnerOrAdmin: project null vs non-null, Admin vs Owner
            _projectsPersistentSource.GetSecurityObject("MissingProj").Returns((Project)null!);
            _rolePrivilegesChecker.IsAdmin(_user).Returns(true);
            Assert.IsTrue(_sut.IsProjectOwnerOrAdmin(_user, "MissingProj"));
            _rolePrivilegesChecker.IsAdmin(_user).Returns(false);
            Assert.IsFalse(_sut.IsProjectOwnerOrAdmin(_user, "MissingProj"));

            var project = new Project { Name = "Proj1" };
            _projectsPersistentSource.GetSecurityObject("Proj1").Returns(project);
            _rolePrivilegesChecker.IsAdmin(_user).Returns(true);
            Assert.IsTrue(_sut.IsProjectOwnerOrAdmin(_user, "Proj1"));

            _rolePrivilegesChecker.IsAdmin(_user).Returns(false);
            _securityObjectFilter.HasPrivilege(project, _user, AccessLevel.Owner).Returns(true);
            Assert.IsTrue(_sut.IsProjectOwnerOrAdmin(_user, "Proj1"));
            _securityObjectFilter.HasPrivilege(project, _user, AccessLevel.Owner).Returns(false);
            Assert.IsFalse(_sut.IsProjectOwnerOrAdmin(_user, "Proj1"));
        }

        [TestMethod]
        public void CanModifyProject_And_CanModifyEnvironment_CoverAllBranches()
        {
            // CanModifyProject with empty name delegates to CanModifyProperty
            _rolePrivilegesChecker.IsAdmin(_user).Returns(false);
            _rolePrivilegesChecker.IsPowerUser(_user).Returns(false);
            Assert.IsFalse(_sut.CanModifyProject(_user, string.Empty));
            _rolePrivilegesChecker.IsAdmin(_user).Returns(true);
            Assert.IsTrue(_sut.CanModifyProject(_user, string.Empty));

            // Missing project throws ApplicationException for both name and ID overloads
            _projectsPersistentSource.GetSecurityObject("Unknown").Returns((Project)null!);
            Assert.Throws<ApplicationException>(() => _sut.CanModifyProject(_user, "Unknown"));
            _projectsPersistentSource.GetSecurityObject(999).Returns((Project)null!);
            Assert.Throws<ApplicationException>(() => _sut.CanModifyProject(_user, 999));

            // Admin short-circuits CanModifyProject(name) and CanModifyProject(id)
            var proj = new Project { Id = 5, Name = "Known" };
            _projectsPersistentSource.GetSecurityObject("Known").Returns(proj);
            _projectsPersistentSource.GetSecurityObject(5).Returns(proj);
            _rolePrivilegesChecker.IsAdmin(_user).Returns(true);
            Assert.IsTrue(_sut.CanModifyProject(_user, "Known"));
            Assert.IsTrue(_sut.CanModifyProject(_user, 5));

            // CanModifyEnvironment: null env, Admin, Owner, Write
            _environmentsPersistentSource.GetSecurityObject("NullEnv").Returns((Environment)null!);
            _rolePrivilegesChecker.IsAdmin(_user).Returns(false);
            Assert.IsFalse(_sut.CanModifyEnvironment(_user, "NullEnv"));

            var env = new Environment { Name = "EnvA" };
            _environmentsPersistentSource.GetSecurityObject("EnvA").Returns(env);
            _rolePrivilegesChecker.IsAdmin(_user).Returns(true);
            Assert.IsTrue(_sut.CanModifyEnvironment(_user, "EnvA"));

            _rolePrivilegesChecker.IsAdmin(_user).Returns(false);
            _environmentsPersistentSource.IsEnvironmentOwner("EnvA", _user).Returns(true);
            _securityObjectFilter.HasPrivilege(env, _user, AccessLevel.Write | AccessLevel.Owner).Returns(false);
            Assert.IsTrue(_sut.CanModifyEnvironment(_user, "EnvA"));

            _environmentsPersistentSource.IsEnvironmentOwner("EnvA", _user).Returns(false);
            _securityObjectFilter.HasPrivilege(env, _user, AccessLevel.Write | AccessLevel.Owner).Returns(true);
            Assert.IsTrue(_sut.CanModifyEnvironment(_user, "EnvA"));
        }
    }
}
