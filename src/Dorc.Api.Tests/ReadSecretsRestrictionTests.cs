using Dorc.Core;
using Dorc.Core.Interfaces;
using Dorc.PersistentData;
using Dorc.PersistentData.Model;
using Dorc.PersistentData.Repositories;
using Dorc.PersistentData.Sources.Interfaces;
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
        private IClaimsPrincipalReader _claimsPrincipalReader = null!;
        private ClaimsPrincipal _user = null!;

        [TestInitialize]
        public void Setup()
        {
            _user = new ClaimsPrincipal(new ClaimsIdentity("test"));

            _environments = Substitute.For<IEnvironmentsPersistentSource>();
            _environments.GetSecurityObject(EnvName).Returns(new PersistentData.Model.Environment());

            _securityObjectFilter = Substitute.For<ISecurityObjectFilter>();
            _claimsPrincipalReader = Substitute.For<IClaimsPrincipalReader>();
        }

        private SecurityPrivilegesChecker NewChecker() =>
            new(Substitute.For<IProjectsPersistentSource>(),
                _environments,
                _securityObjectFilter,
                Substitute.For<IRolePrivilegesChecker>(),
                _claimsPrincipalReader);

        private void GrantsOnEnvironment(AccessLevel granted) =>
            _securityObjectFilter
                .HasPrivilege(Arg.Any<PersistentData.Model.Environment>(), Arg.Any<ClaimsPrincipal>(), Arg.Any<AccessLevel>())
                .Returns(call => (call.ArgAt<AccessLevel>(2) & granted) != 0);

        [TestMethod]
        public void AServicePrincipalWithAnExplicitGrantStillReadsSecrets()
        {
            _claimsPrincipalReader.IsServicePrincipal(_user).Returns(true);
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
            _claimsPrincipalReader.IsServicePrincipal(_user).Returns(false);
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
            _claimsPrincipalReader.IsServicePrincipal(_user).Returns(true);
            GrantsOnEnvironment(AccessLevel.Owner);

            Assert.IsFalse(NewChecker().CanReadSecrets(_user, EnvName),
                "Owner must no longer imply ReadSecrets, even for a machine.");
        }

        [TestMethod]
        public void AServicePrincipalWithoutAGrantDoesNotReadSecrets()
        {
            _claimsPrincipalReader.IsServicePrincipal(_user).Returns(true);
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
            _claimsPrincipalReader.IsServicePrincipal(_user).Returns(false);
            GrantsOnEnvironment(AccessLevel.Owner);

            var checker = NewChecker();

            Assert.IsTrue(checker.CanGrantReadSecrets(_user, EnvName));
            Assert.IsFalse(checker.CanReadSecrets(_user, EnvName));
        }

        [TestMethod]
        public void NeitherPredicateAdmitsAnUnknownEnvironment()
        {
            _claimsPrincipalReader.IsServicePrincipal(_user).Returns(true);
            GrantsOnEnvironment(AccessLevel.ReadSecrets | AccessLevel.Owner);
            _environments.GetSecurityObject("GHOST").Returns((PersistentData.Model.Environment)null!);

            var checker = NewChecker();

            Assert.IsFalse(checker.CanReadSecrets(_user, "GHOST"));
            Assert.IsFalse(checker.CanGrantReadSecrets(_user, "GHOST"));
        }
    }
}
