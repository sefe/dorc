using Dorc.Api.Interfaces;
using Dorc.Api.Security;
using Dorc.Api.Tests.Mocks;
using Dorc.ApiModel;
using Dorc.Core;
using Dorc.Core.Configuration;
using Dorc.Core.Interfaces;
using Dorc.Core.Security;
using Dorc.PersistentData;
using Dorc.PersistentData.Contexts;
using Dorc.PersistentData.Model;
using Dorc.PersistentData.Repositories;
using Dorc.PersistentData.Sources;
using Dorc.PersistentData.Sources.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using NSubstitute;
using System.Security.Claims;
using System.Security.Principal;

namespace Dorc.Api.Tests
{
    /// <summary>
    /// The ReadSecrets privilege exists for service accounts belonging to live running systems.
    /// As implemented it did neither of the two things that requires: it was granted implicitly
    /// to every environment owner, and it could not be restricted to machines at all — so human
    /// owners received decrypted secret property values through the API and the web UI, the
    /// exact outcome it was designed to prevent.
    /// </summary>
    [TestClass]
    public class ReadSecretsRestrictionTests
    {
        private const string EnvName = "SOME-ENV";

        private IEnvironmentsPersistentSource _environments = null!;
        private ISecurityObjectFilter _securityObjectFilter = null!;
        private IRolePrivilegesChecker _rolePrivilegesChecker = null!;
        private IClaimsPrincipalReader _claimsPrincipalReader = null!;
        private ClaimsPrincipal _user = null!;

        [TestInitialize]
        public void Setup()
        {
            _user = new ClaimsPrincipal(new ClaimsIdentity("test"));

            _environments = Substitute.For<IEnvironmentsPersistentSource>();
            _environments.GetSecurityObject(EnvName).Returns(new PersistentData.Model.Environment());

            _securityObjectFilter = Substitute.For<ISecurityObjectFilter>();
            _rolePrivilegesChecker = Substitute.For<IRolePrivilegesChecker>();
            _claimsPrincipalReader = Substitute.For<IClaimsPrincipalReader>();
        }

        private SecurityPrivilegesChecker NewChecker() =>
            new(Substitute.For<IProjectsPersistentSource>(),
                _environments,
                _securityObjectFilter,
                _rolePrivilegesChecker,
                _claimsPrincipalReader);

        private void GrantsOnEnvironment(AccessLevel granted) =>
            _securityObjectFilter
                .HasPrivilege(Arg.Any<PersistentData.Model.Environment>(), Arg.Any<ClaimsPrincipal>(), Arg.Any<AccessLevel>())
                .Returns(call => (call.ArgAt<AccessLevel>(2) & granted) != 0);

        [TestMethod]
        public void AMachinePrincipalWithAnExplicitGrantStillReadsSecrets()
        {
            _claimsPrincipalReader.IsMachinePrincipal(_user).Returns(true);
            GrantsOnEnvironment(AccessLevel.ReadSecrets);

            Assert.IsTrue(NewChecker().CanReadSecrets(_user, EnvName),
                "This is the path that must not break.");
        }

        /// <summary>
        /// The weakness, directly: a person holding the privilege was reading secrets.
        /// </summary>
        [TestMethod]
        public void APersonWithAnExplicitGrantDoesNotReadSecrets()
        {
            _claimsPrincipalReader.IsMachinePrincipal(_user).Returns(false);
            GrantsOnEnvironment(AccessLevel.ReadSecrets);

            Assert.IsFalse(NewChecker().CanReadSecrets(_user, EnvName));
        }

        /// <summary>
        /// The implicit half of the weakness. Ownership conferred the privilege on every
        /// environment owner without anyone granting it, which also made an audit of holders
        /// meaningless.
        /// </summary>
        [TestMethod]
        public void OwnershipNoLongerImpliesThePrivilege()
        {
            _claimsPrincipalReader.IsMachinePrincipal(_user).Returns(true);
            GrantsOnEnvironment(AccessLevel.Owner);

            Assert.IsFalse(NewChecker().CanReadSecrets(_user, EnvName),
                "Owner must no longer imply ReadSecrets, even for a machine.");
        }

        [TestMethod]
        public void AMachinePrincipalWithoutAGrantDoesNotReadSecrets()
        {
            _claimsPrincipalReader.IsMachinePrincipal(_user).Returns(true);
            GrantsOnEnvironment(AccessLevel.Write);

            Assert.IsFalse(NewChecker().CanReadSecrets(_user, EnvName));
        }

        /// <summary>
        /// Administering the privilege is a human activity; exercising it is a machine one. Had
        /// the grant guard kept using the retrieval predicate, no person could ever have granted
        /// ReadSecrets to anyone and it would have become unadministrable.
        /// </summary>
        [TestMethod]
        public void APersonMayStillGrantThePrivilegeTheyCannotExercise()
        {
            _claimsPrincipalReader.IsMachinePrincipal(_user).Returns(false);
            GrantsOnEnvironment(AccessLevel.Owner);
            _environments.IsEnvironmentOwner(EnvName, _user).Returns(true);

            var checker = NewChecker();

            Assert.IsTrue(checker.CanGrantReadSecrets(_user, EnvName));
            Assert.IsFalse(checker.CanReadSecrets(_user, EnvName));

            GrantsOnEnvironment(AccessLevel.None);
            _environments.IsEnvironmentOwner(EnvName, _user).Returns(false);
            _rolePrivilegesChecker.IsAdmin(_user).Returns(true);

            Assert.IsTrue(checker.CanGrantReadSecrets(_user, EnvName));
            Assert.IsFalse(checker.CanReadSecrets(_user, EnvName));
        }

        [TestMethod]
        public void GroupOwnerWithExplicitOAuthOidDenyCannotGrantReadSecretsWhenAdSidsEnabled()
        {
            var envObjectId = Guid.NewGuid();
            var envEntity = new PersistentData.Model.Environment
            {
                Id = 1,
                Name = EnvName,
                ObjectId = envObjectId
            };

            var context = Substitute.For<IDeploymentContext>();
            var envDbSet = DbContextMock.GetQueryableMockDbSet(new List<PersistentData.Model.Environment> { envEntity });
            context.Environments.Returns(envDbSet);
            var contextFactory = Substitute.For<IDeploymentContextFactory>();
            contextFactory.GetContext().Returns(context);

            var accessControlSource = Substitute.For<IAccessControlPersistentSource>();
            var config = Substitute.For<IConfigurationSettings>();
            config.GetIsUseAdSidsForAccessControl().Returns(true);

            var httpContext = new DefaultHttpContext();
            httpContext.Request.Headers["Authorization"] = "Bearer token";
            var httpContextAccessor = Substitute.For<IHttpContextAccessor>();
            httpContextAccessor.HttpContext.Returns(httpContext);

            var adGroupReader = Substitute.For<IUserGroupReader>();
            adGroupReader.GetUserData("jdoe").Returns(new UserElementApiModel { Sid = "S-1-5-21-USER" });
            adGroupReader.GetSidsForUser("jdoe").Returns(new List<string> { "S-1-5-21-USER", "S-1-5-21-TEAM-OWNERS" });

            var oauthGroupReader = Substitute.For<IUserGroupReader>();
            var groupsFactory = Substitute.For<IUserGroupsReaderFactory>();
            groupsFactory.GetWinAuthUserGroupsReader().Returns(adGroupReader);
            groupsFactory.GetOAuthUserGroupsReader().Returns(oauthGroupReader);

            var claimsReaderFactory = new ClaimsPrincipalReaderFactory(config, httpContextAccessor, groupsFactory);
            var realFilter = new SecurityObjectFilter(accessControlSource, claimsReaderFactory);
            var realEnvironments = new EnvironmentsPersistentSource(
                contextFactory,
                realFilter,
                _rolePrivilegesChecker,
                Substitute.For<IPropertyValuesPersistentSource>(),
                Substitute.For<ILogger<EnvironmentsPersistentSource>>(),
                claimsReaderFactory,
                accessControlSource);

            var checker = new SecurityPrivilegesChecker(
                Substitute.For<IProjectsPersistentSource>(),
                realEnvironments,
                realFilter,
                _rolePrivilegesChecker,
                claimsReaderFactory);

            var oauthCaller = new ClaimsPrincipal(new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.Name, "jdoe"),
                new Claim("samAccountName", "jdoe"),
                new Claim("oid", "caller-entra-oid"),
                new Claim("m2m", "false")
            }, "Bearer"));

            _rolePrivilegesChecker.IsAdmin(oauthCaller).Returns(false);

            // Group Owner grant without deny: IsEnvironmentOwner handles group ownership and admits CanGrantReadSecrets
            accessControlSource.GetAccessControls(envObjectId).Returns(new[]
            {
                new AccessControlApiModel
                {
                    Id = 1,
                    Name = "TeamOwnersAdGroup",
                    Sid = "S-1-5-21-TEAM-OWNERS",
                    Allow = (int)AccessLevel.Owner,
                    Deny = 0
                }
            });
            Assert.IsTrue(realEnvironments.IsEnvironmentOwner(EnvName, oauthCaller));
            Assert.IsTrue(checker.CanGrantReadSecrets(oauthCaller, EnvName));

            // Group Owner grant combined with an explicit Deny against the caller's OAuth oid:
            // IsEnvironmentOwner sees the oid deny and rejects; CanGrantReadSecrets must reject too
            accessControlSource.GetAccessControls(envObjectId).Returns(new[]
            {
                new AccessControlApiModel
                {
                    Id = 1,
                    Name = "TeamOwnersAdGroup",
                    Sid = "S-1-5-21-TEAM-OWNERS",
                    Allow = (int)AccessLevel.Owner,
                    Deny = 0
                },
                new AccessControlApiModel
                {
                    Id = 2,
                    Name = "DeniedOAuthCaller",
                    Pid = "caller-entra-oid",
                    Allow = 0,
                    Deny = (int)AccessLevel.Owner
                }
            });
            Assert.IsFalse(realEnvironments.IsEnvironmentOwner(EnvName, oauthCaller));
            Assert.IsTrue(realFilter.HasPrivilege(envEntity, oauthCaller, AccessLevel.Owner));
            Assert.IsFalse(checker.CanGrantReadSecrets(oauthCaller, EnvName));
        }

        [TestMethod]
        public void HoldingReadSecretsDoesNotConferGrantAuthority()
        {
            GrantsOnEnvironment(AccessLevel.Write | AccessLevel.ReadSecrets);
            _rolePrivilegesChecker.IsAdmin(_user).Returns(false);
            _environments.IsEnvironmentOwner(EnvName, _user).Returns(false);

            var checker = NewChecker();

            _claimsPrincipalReader.IsMachinePrincipal(_user).Returns(true);
            Assert.IsTrue(checker.CanReadSecrets(_user, EnvName));
            Assert.IsFalse(checker.CanGrantReadSecrets(_user, EnvName));

            _claimsPrincipalReader.IsMachinePrincipal(_user).Returns(false);
            Assert.IsFalse(checker.CanReadSecrets(_user, EnvName));
            Assert.IsFalse(checker.CanGrantReadSecrets(_user, EnvName));
        }

        [TestMethod]
        public void NeitherPredicateAdmitsAnUnknownEnvironment()
        {
            _claimsPrincipalReader.IsMachinePrincipal(_user).Returns(true);
            GrantsOnEnvironment(AccessLevel.ReadSecrets | AccessLevel.Owner);
            _rolePrivilegesChecker.IsAdmin(_user).Returns(true);
            _environments.IsEnvironmentOwner("GHOST", _user).Returns(true);
            _environments.GetSecurityObject("GHOST").Returns((PersistentData.Model.Environment)null!);

            var checker = NewChecker();

            Assert.IsFalse(checker.CanReadSecrets(_user, "GHOST"));
            Assert.IsFalse(checker.CanGrantReadSecrets(_user, "GHOST"));
        }

        [TestMethod]
        public void ClaimsPrincipalReaders_ClassifyMachineAndPersonCallersCorrectly()
        {
            var userGroupReader = Substitute.For<IUserGroupReader>();
            var oauthReader = new OAuthClaimsPrincipalReader(userGroupReader);
            var winAuthReader = new WinAuthClaimsPrincipalReader(userGroupReader);
            var directToolReader = new DirectToolClaimsPrincipalReader();

            var m2mPrincipal = new ClaimsPrincipal(new ClaimsIdentity(new[]
            {
                new Claim("m2m", "true"),
                new Claim("client_id", "deploy-client")
            }, "Bearer"));
            var humanOAuthPrincipal = new ClaimsPrincipal(new ClaimsIdentity(new[]
            {
                new Claim("m2m", "false"),
                new Claim("email", "dev@example.com")
            }, "Bearer"));
            var winPrincipal = new ClaimsPrincipal(new ClaimsIdentity("Negotiate"));

            Assert.IsTrue(oauthReader.IsMachinePrincipal(m2mPrincipal));
            Assert.IsFalse(oauthReader.IsMachinePrincipal(humanOAuthPrincipal));
            Assert.IsFalse(winAuthReader.IsMachinePrincipal(winPrincipal));
            Assert.IsTrue(directToolReader.IsMachinePrincipal(winPrincipal));

            var httpContext = new DefaultHttpContext();
            var httpContextAccessor = Substitute.For<IHttpContextAccessor>();
            httpContextAccessor.HttpContext.Returns(httpContext);
            var groupsFactory = Substitute.For<IUserGroupsReaderFactory>();
            groupsFactory.GetWinAuthUserGroupsReader().Returns(userGroupReader);
            groupsFactory.GetOAuthUserGroupsReader().Returns(userGroupReader);
            var factory = new ClaimsPrincipalReaderFactory(
                Substitute.For<IConfigurationSettings>(),
                httpContextAccessor,
                groupsFactory);

            httpContext.Request.Headers["Authorization"] = "Bearer token";
            Assert.IsTrue(factory.IsMachinePrincipal(m2mPrincipal));
            Assert.IsFalse(factory.IsMachinePrincipal(humanOAuthPrincipal));

            httpContext.Request.Headers.Remove("Authorization");
            Assert.IsFalse(factory.IsMachinePrincipal(m2mPrincipal));
        }
    }
}
