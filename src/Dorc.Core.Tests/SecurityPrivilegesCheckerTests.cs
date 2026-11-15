using Dorc.PersistentData;
using Dorc.PersistentData.Model;
using Dorc.PersistentData.Sources.Interfaces;
using NSubstitute;
using System.Security.Claims;
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
        public void CanReadSecrets_RequiresMachinePrincipalAndExplicitReadSecrets_NeverImpliedByAdminOrOwner()
        {
            var objectId = Guid.NewGuid();
            var env = new Environment { Name = "ProdEnv", ObjectId = objectId };
            _environmentsPersistentSource.GetSecurityObject("ProdEnv").Returns(env);

            var accessControlPersistentSource = Substitute.For<IAccessControlPersistentSource>();
            var claimsReader = Substitute.For<IClaimsPrincipalReader>();

            var humanAdminIdentity = new ClaimsIdentity(
                new[] { new Claim(ClaimTypes.Name, "AdminUser"), new Claim(ClaimTypes.Role, "Admin") },
                "TestAuth",
                ClaimTypes.Name,
                ClaimTypes.Role);
            var humanAdmin = new ClaimsPrincipal(humanAdminIdentity);

            var humanOwner = new ClaimsPrincipal(new ClaimsIdentity(
                new[] { new Claim(ClaimTypes.Name, "OwnerUser") },
                "TestAuth"));

            var groupOwner = new ClaimsPrincipal(new ClaimsIdentity(
                new[] { new Claim(ClaimTypes.Name, "GroupOwnerUser") },
                "TestAuth"));

            var machinePrincipal = new ClaimsPrincipal(new ClaimsIdentity(
                new[] { new Claim(ClaimTypes.Name, "DeployMp"), new Claim(ClaimTypes.Role, "Admin") },
                "TestAuth",
                ClaimTypes.Name,
                ClaimTypes.Role));

            var nonAdminMachineReader = new ClaimsPrincipal(new ClaimsIdentity(
                new[] { new Claim(ClaimTypes.Name, "NonAdminMp") },
                "TestAuth"));

            var writerUser = new ClaimsPrincipal(new ClaimsIdentity(
                new[] { new Claim(ClaimTypes.Name, "WriterUser") },
                "TestAuth"));

            var humanSecretHolder = new ClaimsPrincipal(new ClaimsIdentity(
                new[] { new Claim(ClaimTypes.Name, "LegacySecretHolder") },
                "TestAuth"));

            claimsReader.GetSidsForUser(humanAdmin).Returns(new List<string> { "admin-sid" });
            claimsReader.GetSidsForUser(humanOwner).Returns(new List<string> { "owner-sid" });
            claimsReader.GetSidsForUser(groupOwner).Returns(new List<string> { "group-owner-sid" });
            claimsReader.GetSidsForUser(machinePrincipal).Returns(new List<string> { "mp-sid" });
            claimsReader.GetSidsForUser(nonAdminMachineReader).Returns(new List<string> { "nonadmin-mp-sid" });
            claimsReader.GetSidsForUser(writerUser).Returns(new List<string> { "writer-sid" });
            claimsReader.GetSidsForUser(humanSecretHolder).Returns(new List<string> { "secret-holder-sid" });

            claimsReader.IsMachinePrincipal(humanAdmin).Returns(false);
            claimsReader.IsMachinePrincipal(humanOwner).Returns(false);
            claimsReader.IsMachinePrincipal(groupOwner).Returns(false);
            claimsReader.IsMachinePrincipal(machinePrincipal).Returns(true);
            claimsReader.IsMachinePrincipal(nonAdminMachineReader).Returns(true);
            claimsReader.IsMachinePrincipal(writerUser).Returns(false);
            claimsReader.IsMachinePrincipal(humanSecretHolder).Returns(false);

            var realFilter = new SecurityObjectFilter(accessControlPersistentSource, claimsReader);
            var realSut = new SecurityPrivilegesChecker(
                _projectsPersistentSource,
                _environmentsPersistentSource,
                realFilter,
                _rolePrivilegesChecker,
                claimsReader);

            _rolePrivilegesChecker.IsAdmin(humanAdmin).Returns(true);
            _environmentsPersistentSource.IsEnvironmentOwner("ProdEnv", humanOwner).Returns(true);
            _environmentsPersistentSource.IsEnvironmentOwner("ProdEnv", groupOwner).Returns(true);

            // 1. Human Admin and Human Owner are ALWAYS denied CanReadSecrets, even if granted explicit ReadSecrets on ACL
            accessControlPersistentSource.GetAccessControls(objectId).Returns(new[]
            {
                new Dorc.ApiModel.AccessControlApiModel
                {
                    Id = 1,
                    Sid = "admin-sid",
                    Allow = (int)(AccessLevel.Owner | AccessLevel.Write | AccessLevel.ReadSecrets),
                    Deny = 0
                },
                new Dorc.ApiModel.AccessControlApiModel
                {
                    Id = 2,
                    Sid = "owner-sid",
                    Allow = (int)(AccessLevel.Owner | AccessLevel.Write | AccessLevel.ReadSecrets),
                    Deny = 0
                }
            });

            Assert.IsFalse(realSut.CanReadSecrets(humanAdmin, "ProdEnv"));
            Assert.IsFalse(realSut.CanReadSecrets(humanOwner, "ProdEnv"));

            // 2. CanGrantReadSecrets IS allowed ONLY for Admins and Environment Owners (via IsEnvironmentOwner),
            //    and NEVER for non-owners even when they hold explicit ReadSecrets (human or machine).
            accessControlPersistentSource.GetAccessControls(objectId).Returns(new[]
            {
                new Dorc.ApiModel.AccessControlApiModel
                {
                    Id = 2,
                    Sid = "group-owner-sid",
                    Allow = (int)AccessLevel.Owner,
                    Deny = 0
                },
                new Dorc.ApiModel.AccessControlApiModel
                {
                    Id = 3,
                    Sid = "writer-sid",
                    Allow = (int)AccessLevel.Write,
                    Deny = 0
                },
                new Dorc.ApiModel.AccessControlApiModel
                {
                    Id = 4,
                    Sid = "secret-holder-sid",
                    Allow = (int)(AccessLevel.Write | AccessLevel.ReadSecrets),
                    Deny = 0
                },
                new Dorc.ApiModel.AccessControlApiModel
                {
                    Id = 5,
                    Sid = "nonadmin-mp-sid",
                    Allow = (int)(AccessLevel.Write | AccessLevel.ReadSecrets),
                    Deny = 0
                }
            });

            Assert.IsTrue(realSut.CanGrantReadSecrets(humanAdmin, "ProdEnv"));
            Assert.IsTrue(realSut.CanGrantReadSecrets(humanOwner, "ProdEnv"));
            Assert.IsTrue(realSut.CanGrantReadSecrets(groupOwner, "ProdEnv"));
            Assert.IsFalse(realSut.CanGrantReadSecrets(writerUser, "ProdEnv"));
            Assert.IsFalse(realSut.CanGrantReadSecrets(humanSecretHolder, "ProdEnv"));
            Assert.IsTrue(realSut.CanReadSecrets(nonAdminMachineReader, "ProdEnv"));
            Assert.IsFalse(realSut.CanGrantReadSecrets(nonAdminMachineReader, "ProdEnv"));

            // 3. Machine principal WITHOUT explicit ReadSecrets grant (even with Admin role + Owner + Write) is denied CanReadSecrets
            accessControlPersistentSource.GetAccessControls(objectId).Returns(new[]
            {
                new Dorc.ApiModel.AccessControlApiModel
                {
                    Id = 1,
                    Sid = "mp-sid",
                    Allow = (int)(AccessLevel.Owner | AccessLevel.Write),
                    Deny = 0
                }
            });

            Assert.IsFalse(realSut.CanReadSecrets(machinePrincipal, "ProdEnv"));

            // 4. Machine principal WITH explicit ReadSecrets (2) on the ACL satisfies CanReadSecrets
            accessControlPersistentSource.GetAccessControls(objectId).Returns(new[]
            {
                new Dorc.ApiModel.AccessControlApiModel
                {
                    Id = 1,
                    Sid = "mp-sid",
                    Allow = (int)AccessLevel.ReadSecrets,
                    Deny = 0
                },
                new Dorc.ApiModel.AccessControlApiModel
                {
                    Id = 2,
                    Sid = "nonadmin-mp-sid",
                    Allow = (int)AccessLevel.ReadSecrets,
                    Deny = 0
                }
            });

            Assert.IsTrue(realSut.CanReadSecrets(machinePrincipal, "ProdEnv"));
            Assert.IsTrue(realSut.CanReadSecrets(nonAdminMachineReader, "ProdEnv"));
            Assert.IsFalse(realSut.CanGrantReadSecrets(nonAdminMachineReader, "ProdEnv"));

            // 5. When a matching ACL row has Deny != 0, HasPrivilege returns false even if Allow has ReadSecrets
            accessControlPersistentSource.GetAccessControls(objectId).Returns(new[]
            {
                new Dorc.ApiModel.AccessControlApiModel
                {
                    Id = 1,
                    Sid = "mp-sid",
                    Allow = (int)(AccessLevel.Owner | AccessLevel.Write | AccessLevel.ReadSecrets),
                    Deny = (int)AccessLevel.ReadSecrets
                },
                new Dorc.ApiModel.AccessControlApiModel
                {
                    Id = 2,
                    Pid = "mp-sid",
                    Allow = (int)AccessLevel.Write,
                    Deny = 0
                }
            });
            Assert.IsFalse(realSut.CanReadSecrets(machinePrincipal, "ProdEnv"));

            // 6. Admin role still bypasses non-ReadSecrets checks in HasPrivilege, but NEVER bypasses ReadSecrets
            Assert.IsTrue(realFilter.HasPrivilege(env, humanAdmin, AccessLevel.Write));
            Assert.IsFalse(realFilter.HasPrivilege(env, humanAdmin, AccessLevel.ReadSecrets));

            // 7. When environment does not exist, neither CanReadSecrets nor CanGrantReadSecrets admits it
            _environmentsPersistentSource.GetSecurityObject("MissingEnv").Returns((Environment)null!);
            Assert.IsFalse(realSut.CanReadSecrets(machinePrincipal, "MissingEnv"));
            Assert.IsFalse(realSut.CanGrantReadSecrets(humanAdmin, "MissingEnv"));

            // 8. DirectToolClaimsPrincipalReader reports itself as a machine principal
            var directToolReader = new Dorc.Core.Security.DirectToolClaimsPrincipalReader();
            Assert.IsTrue(directToolReader.IsMachinePrincipal(_user));
        }

        [TestMethod]
        public void CanGrantReadSecrets_WhenOAuthCallerHasGroupOwnerGrantAndExplicitOidDeny_ReturnsFalse()
        {
            var objectId = Guid.NewGuid();
            var env = new Environment { Name = "ProdEnv", ObjectId = objectId };
            _environmentsPersistentSource.GetSecurityObject("ProdEnv").Returns(env);

            var accessControlPersistentSource = Substitute.For<IAccessControlPersistentSource>();
            var claimsReader = Substitute.For<IClaimsPrincipalReader>();

            var oauthCaller = new ClaimsPrincipal(new ClaimsIdentity(
                new[]
                {
                    new Claim(ClaimTypes.Name, "jdoe"),
                    new Claim("oid", "oauth-caller-oid"),
                    new Claim("samAccountName", "jdoe")
                },
                "Bearer"));

            // With IsUseAdSidsForAccessControl enabled, GetSidsForUser returns only AD SIDs (not Entra oid)
            claimsReader.GetSidsForUser(oauthCaller).Returns(new List<string> { "S-1-5-21-USER", "S-1-5-21-TEAM-OWNERS" });
            claimsReader.IsMachinePrincipal(oauthCaller).Returns(false);

            accessControlPersistentSource.GetAccessControls(objectId).Returns(new[]
            {
                new Dorc.ApiModel.AccessControlApiModel
                {
                    Id = 1,
                    Sid = "S-1-5-21-TEAM-OWNERS",
                    Allow = (int)AccessLevel.Owner,
                    Deny = 0
                },
                new Dorc.ApiModel.AccessControlApiModel
                {
                    Id = 2,
                    Pid = "oauth-caller-oid",
                    Allow = 0,
                    Deny = (int)AccessLevel.Owner
                }
            });

            var realFilter = new SecurityObjectFilter(accessControlPersistentSource, claimsReader);
            var realSut = new SecurityPrivilegesChecker(
                _projectsPersistentSource,
                _environmentsPersistentSource,
                realFilter,
                _rolePrivilegesChecker,
                claimsReader);

            _rolePrivilegesChecker.IsAdmin(oauthCaller).Returns(false);
            // IsEnvironmentOwner inspects user.FindFirst("oid") directly and rejects the explicit Deny
            _environmentsPersistentSource.IsEnvironmentOwner("ProdEnv", oauthCaller).Returns(false);

            // SecurityObjectFilter only sees AD SIDs and would return true; CanGrantReadSecrets must NOT fall back to it
            Assert.IsTrue(realFilter.HasPrivilege(env, oauthCaller, AccessLevel.Owner));
            Assert.IsFalse(realSut.CanGrantReadSecrets(oauthCaller, "ProdEnv"));
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
