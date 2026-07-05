using Dorc.Monitor.Terraform;

namespace Dorc.Monitor.Tests.Terraform
{
    [TestClass]
    public class TerraformConcurrencyGuardTests
    {
        // Use a fresh guard per test (rather than the static Instance) so
        // tests are isolated from each other's slot state.
        private TerraformConcurrencyGuard NewGuard() => new() { DefaultAcquisitionTimeout = TimeSpan.FromMilliseconds(500) };

        [TestMethod]
        public void Acquire_FirstAttempt_Succeeds()
        {
            var guard = NewGuard();
            using var release = guard.Acquire("env-a", "comp-a", "op-1");
            // No exception = pass; release on scope exit.
        }

        [TestMethod]
        public void Acquire_SameKey_BlocksUntilReleased()
        {
            var guard = NewGuard();
            using var first = guard.Acquire("env-a", "comp-a", "op-1");

            Assert.ThrowsExactly<TerraformConcurrentOperationException>(() =>
            {
                using var second = guard.Acquire("env-a", "comp-a", "op-2", TimeSpan.FromMilliseconds(50));
            });
        }

        [TestMethod]
        public void Acquire_DifferentKeys_DoNotBlock()
        {
            var guard = NewGuard();
            using var held = guard.Acquire("env-a", "comp-a", "op-1");
            // While the (env-a, comp-a) slot is held, acquiring on different
            // (env, component) keys must succeed without blocking. Dispose
            // immediately - we only care that the call returns.
            guard.Acquire("env-a", "comp-b", "op-2").Dispose();
            guard.Acquire("env-b", "comp-a", "op-3").Dispose();
        }

        [TestMethod]
        public void Acquire_AfterRelease_LetsNextWaiterIn()
        {
            var guard = NewGuard();
            // First acquire+release pattern: we only need the disposer to fire,
            // we never read the value.
            guard.Acquire("env-a", "comp-a", "op-1").Dispose();
            using var next = guard.Acquire("env-a", "comp-a", "op-2");
            // Re-acquisition succeeds = pass.
        }

        [TestMethod]
        public void Acquire_KeyComparisonIsCaseInsensitive()
        {
            var guard = NewGuard();
            using var first = guard.Acquire("ENV-A", "Comp-A", "op-1");

            Assert.ThrowsExactly<TerraformConcurrentOperationException>(() =>
            {
                using var second = guard.Acquire("env-a", "comp-a", "op-2", TimeSpan.FromMilliseconds(50));
            });
        }

        [TestMethod]
        public void Acquire_SpaceAndHyphenEnvironments_AreDistinctSlots()
        {
            var guard = NewGuard();
            // "Prod EU" and "Prod-EU" are distinct environment names and map
            // to distinct terraform states (the state-key sanitizer hex-escapes
            // the space), so holding one slot must not block the other.
            using var withSpace = guard.Acquire("Prod EU", "comp-a", "op-1");
            guard.Acquire("Prod-EU", "comp-a", "op-2", TimeSpan.FromMilliseconds(50)).Dispose();
        }

        [TestMethod]
        public void Exception_CarriesContendingOperationId()
        {
            var guard = NewGuard();
            using var first = guard.Acquire("env-a", "comp-a", "op-running");

            var ex = Assert.ThrowsExactly<TerraformConcurrentOperationException>(() =>
            {
                using var second = guard.Acquire("env-a", "comp-a", "op-pending", TimeSpan.FromMilliseconds(50));
            });
            Assert.AreEqual("env-a", ex.Environment);
            Assert.AreEqual("comp-a", ex.Component);
            Assert.AreEqual("op-running", ex.ContendingOperationId);
        }
    }
}
