using Dorc.PersistentData;

namespace Dorc.Core.Tests
{
    [TestClass]
    public class SecurityObjectFilterTests
    {
        // AccessLevel: Write = 1, ReadSecrets = 2, Owner = 4.

        [TestMethod]
        public void IsAllowed_GrantsRequestedLevelWhenAllowedAndNotDenied()
        {
            // Allowed Write | Owner (5), no denies.
            Assert.IsTrue(SecurityObjectFilter.IsAllowed(allowed: 5, denied: 0, AccessLevel.Write));
            Assert.IsTrue(SecurityObjectFilter.IsAllowed(allowed: 5, denied: 0, AccessLevel.Owner));
        }

        [TestMethod]
        public void IsAllowed_DenyOfOneLevelDoesNotBlockAnother()
        {
            // The F-2 bug: user is Owner (allow 4) but has a Deny of Write (1).
            // Old logic returned false for EVERY level because any deny bit was set.
            Assert.IsTrue(SecurityObjectFilter.IsAllowed(allowed: 4, denied: 1, AccessLevel.Owner));
            Assert.IsTrue(SecurityObjectFilter.IsAllowed(allowed: 6, denied: 1, AccessLevel.ReadSecrets));
        }

        [TestMethod]
        public void IsAllowed_DenyOfRequestedLevelBlocks()
        {
            // Explicit deny of the requested level wins over allow.
            Assert.IsFalse(SecurityObjectFilter.IsAllowed(allowed: 1, denied: 1, AccessLevel.Write));
            Assert.IsFalse(SecurityObjectFilter.IsAllowed(allowed: 5, denied: 4, AccessLevel.Owner));
        }

        [TestMethod]
        public void IsAllowed_NotAllowed_ReturnsFalse()
        {
            Assert.IsFalse(SecurityObjectFilter.IsAllowed(allowed: 0, denied: 0, AccessLevel.Write));
            // Allowed only Write; requesting Owner is not granted.
            Assert.IsFalse(SecurityObjectFilter.IsAllowed(allowed: 1, denied: 0, AccessLevel.Owner));
        }

        [TestMethod]
        public void IsAllowed_CombinedRequestedLevels_GrantedWhenAnyBitAllowed()
        {
            // Callers pass combinations such as ReadSecrets | Owner (6).
            var readSecretsOrOwner = AccessLevel.ReadSecrets | AccessLevel.Owner;
            Assert.IsTrue(SecurityObjectFilter.IsAllowed(allowed: 4, denied: 0, readSecretsOrOwner));   // Owner satisfies it
            Assert.IsFalse(SecurityObjectFilter.IsAllowed(allowed: 1, denied: 0, readSecretsOrOwner));  // only Write allowed
            // Deny overlapping the requested combination blocks.
            Assert.IsFalse(SecurityObjectFilter.IsAllowed(allowed: 4, denied: 4, readSecretsOrOwner));
        }
    }
}
