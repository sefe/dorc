using Dorc.Core.Security;

namespace Dorc.Core.Tests.Security
{
    [TestClass]
    public class UserIdentityCanonicaliserTests
    {
        [TestMethod]
        [DataRow(@"CORP\jsmith", "jsmith")]
        [DataRow("jsmith@corp.example.com", "jsmith")]
        [DataRow(@"CORP\jsmith@corp.example.com", "jsmith")]
        [DataRow("  jsmith  ", "jsmith")]
        [DataRow("jsmith", "jsmith")]
        public void Canonicalise_ReducesEachIdentityFormToTheAccountName(string identity, string expected)
        {
            Assert.AreEqual(expected, UserIdentityCanonicaliser.Canonicalise(identity));
        }

        [TestMethod]
        [DataRow(null)]
        [DataRow("")]
        [DataRow("   ")]
        [DataRow(@"CORP\")]
        [DataRow("@corp.example.com")]
        public void Canonicalise_ReturnsEmptyWhenNoAccountNameCanBeEstablished(string? identity)
        {
            Assert.AreEqual(string.Empty, UserIdentityCanonicaliser.Canonicalise(identity));
        }

        /// <summary>
        /// The reason this type exists. DOrc records a submitter with a scheme-dependent
        /// accessor, so the same person appears as DOMAIN\user under Windows authentication
        /// and as an email under OAuth. Comparing the raw strings reports them as two
        /// people, which permits self-approval.
        /// </summary>
        [TestMethod]
        public void SamePrincipal_MatchesTheSameHumanAcrossAuthenticationSchemes()
        {
            Assert.IsTrue(UserIdentityCanonicaliser.SamePrincipal(@"CORP\jsmith", "jsmith@corp.example.com"));
        }

        [TestMethod]
        public void SamePrincipal_IsCaseInsensitive()
        {
            // AD account names and email local parts are case-insensitive.
            Assert.IsTrue(UserIdentityCanonicaliser.SamePrincipal(@"CORP\JSmith", "jsmith@corp.example.com"));
        }

        [TestMethod]
        public void SamePrincipal_DistinguishesDifferentAccounts()
        {
            Assert.IsFalse(UserIdentityCanonicaliser.SamePrincipal(@"CORP\alice", @"CORP\bob"));
        }

        /// <summary>
        /// Deliberate over-match. Two accounts sharing a name across different domains are
        /// reported as the same principal. Where this guards segregation of duties, an
        /// over-match refuses an action and an under-match allows one, so erring toward
        /// "same" is the safe direction.
        /// </summary>
        [TestMethod]
        public void SamePrincipal_ErrsTowardMatchingWhenOnlyTheDomainDiffers()
        {
            Assert.IsTrue(UserIdentityCanonicaliser.SamePrincipal(@"CORP\jsmith", @"OTHERDOMAIN\jsmith"));
        }

        /// <summary>
        /// An unestablished identity is not a match, but callers must not read that as
        /// "different people" - see CanEstablish.
        /// </summary>
        [TestMethod]
        public void SamePrincipal_IsFalseWhenEitherIdentityCannotBeEstablished()
        {
            Assert.IsFalse(UserIdentityCanonicaliser.SamePrincipal(null, @"CORP\bob"));
            Assert.IsFalse(UserIdentityCanonicaliser.SamePrincipal(@"CORP\alice", "   "));
        }

        [TestMethod]
        public void CanEstablish_SeparatesUnknownIdentityFromANonMatch()
        {
            Assert.IsTrue(UserIdentityCanonicaliser.CanEstablish(@"CORP\jsmith"));
            Assert.IsFalse(UserIdentityCanonicaliser.CanEstablish(null));
            Assert.IsFalse(UserIdentityCanonicaliser.CanEstablish(@"CORP\"));
        }

        /// <summary>
        /// Machine-to-machine callers are recorded by client id, which carries neither a
        /// domain nor an at-sign and so passes through unchanged - a service principal
        /// confirming its own request still compares equal.
        /// </summary>
        [TestMethod]
        public void Canonicalise_LeavesMachineClientIdentifiersIntact()
        {
            const string clientId = "b3f1c2a4-7d19-4f0e-9c8b-2a5e6d7f8091";

            Assert.AreEqual(clientId, UserIdentityCanonicaliser.Canonicalise(clientId));
            Assert.IsTrue(UserIdentityCanonicaliser.SamePrincipal(clientId, clientId));
        }
    }
}
