using Dorc.Monitor.Terraform;

namespace Dorc.Monitor.Tests.Terraform
{
    [TestClass]
    [DoNotParallelize]
    public class TerraformConcurrencyGuardTests
    {
        // Slots are process-wide; each test must release all of its leases.
        private TerraformConcurrencyGuard NewGuard() => new() { DefaultAcquisitionTimeout = TimeSpan.FromMilliseconds(500) };

        [TestMethod]
        public async Task Acquire_CancelledWaiter_DoesNotReleaseOwnerOrRetainSlot()
        {
            var guard = NewGuard();
            var before = guard.RetainedKeyCount;
            using (guard.Acquire("project", "cancel-env", "component", "owner"))
            {
                using var cancellation = new CancellationTokenSource();
                var waiting = Task.Run(() => guard.Acquire("project", "cancel-env", "component", "waiter",
                    TimeSpan.FromSeconds(30), cancellation.Token));
                cancellation.CancelAfter(30);
                await Assert.ThrowsAsync<OperationCanceledException>(
                    async () => await waiting.WaitAsync(TimeSpan.FromSeconds(5)));
                Assert.Throws<TerraformConcurrentOperationException>(() =>
                    guard.Acquire("project", "cancel-env", "component", "contender", TimeSpan.Zero));
                Assert.AreEqual(before + 1, guard.RetainedKeyCount);
            }
            Assert.AreEqual(before, guard.RetainedKeyCount);
        }

        [TestMethod]
        public async Task Acquire_ConcurrentWaitersAndRetirement_NeverSplitTheLock()
        {
            var guard = NewGuard();
            var before = guard.RetainedKeyCount;
            var executing = 0;
            var overlap = 0;
            var operations = Enumerable.Range(0, 12).Select(index => Task.Run(async () =>
            {
                for (var round = 0; round < 8; round++)
                {
                    using (guard.Acquire("project", "churn-env", "component", index.ToString(), TimeSpan.FromSeconds(10)))
                    {
                        if (Interlocked.Increment(ref executing) != 1) Interlocked.Increment(ref overlap);
                        await Task.Yield();
                        Interlocked.Decrement(ref executing);
                    }
                }
            }));
            await Task.WhenAll(operations);
            Assert.AreEqual(0, overlap);
            Assert.AreEqual(before, guard.RetainedKeyCount);
        }

        [TestMethod]
        public async Task Release_ConcurrentDisposal_RetiresExactlyOnce()
        {
            var guard = NewGuard();
            var before = guard.RetainedKeyCount;
            var lease = guard.Acquire("project", "dispose-env", "component", "first");
            await Task.WhenAll(Task.Run(lease.Dispose), Task.Run(lease.Dispose));
            Assert.AreEqual(before, guard.RetainedKeyCount);
            using (guard.Acquire("project", "dispose-env", "component", "second"))
                Assert.Throws<TerraformConcurrentOperationException>(() =>
                    guard.Acquire("project", "dispose-env", "component", "third", TimeSpan.Zero));
            Assert.AreEqual(before, guard.RetainedKeyCount);
        }

        [TestMethod]
        public void Acquire_FirstAttempt_Succeeds()
        {
            var guard = NewGuard();
            using var release = guard.Acquire("proj-a", "env-a", "comp-a", "op-1");
            // No exception = pass; release on scope exit.
        }

        [TestMethod]
        public void Acquire_SameKey_BlocksUntilReleased()
        {
            var guard = NewGuard();
            using var first = guard.Acquire("proj-a", "env-a", "comp-a", "op-1");

            Assert.ThrowsExactly<TerraformConcurrentOperationException>(() =>
            {
                using var second = guard.Acquire("proj-a", "env-a", "comp-a", "op-2", TimeSpan.FromMilliseconds(50));
            });
        }

        [TestMethod]
        public void Acquire_DifferentKeys_DoNotBlock()
        {
            var guard = NewGuard();
            using var held = guard.Acquire("proj-a", "env-a", "comp-a", "op-1");
            // While the (env-a, comp-a) slot is held, acquiring on different
            // (env, component) keys must succeed without blocking. Dispose
            // immediately - we only care that the call returns.
            guard.Acquire("proj-a", "env-a", "comp-b", "op-2").Dispose();
            guard.Acquire("proj-a", "env-b", "comp-a", "op-3").Dispose();
        }

        [TestMethod]
        public void Acquire_AfterRelease_LetsNextWaiterIn()
        {
            var guard = NewGuard();
            // First acquire+release pattern: we only need the disposer to fire,
            // we never read the value.
            guard.Acquire("proj-a", "env-a", "comp-a", "op-1").Dispose();
            using var next = guard.Acquire("proj-a", "env-a", "comp-a", "op-2");
            // Re-acquisition succeeds = pass.
        }

        [TestMethod]
        public void Acquire_KeyComparisonIsCaseInsensitive()
        {
            var guard = NewGuard();
            using var first = guard.Acquire("proj-a", "ENV-A", "Comp-A", "op-1");

            Assert.ThrowsExactly<TerraformConcurrentOperationException>(() =>
            {
                using var second = guard.Acquire("proj-a", "env-a", "comp-a", "op-2", TimeSpan.FromMilliseconds(50));
            });
        }

        [TestMethod]
        public void Acquire_SpaceAndHyphenEnvironments_AreDistinctSlots()
        {
            var guard = NewGuard();
            // "Prod EU" and "Prod-EU" are distinct environment names and map
            // to distinct terraform states (the state-key sanitizer hex-escapes
            // the space), so holding one slot must not block the other.
            using var withSpace = guard.Acquire("proj-a", "Prod EU", "comp-a", "op-1");
            guard.Acquire("proj-a", "Prod-EU", "comp-a", "op-2", TimeSpan.FromMilliseconds(50)).Dispose();
        }

        [TestMethod]
        public void Acquire_SameComponentAndEnvironmentInDifferentProjects_AreDistinctSlots()
        {
            var guard = NewGuard();
            // Environments are shared across projects and component names are
            // not globally unique: two projects deploying the same stock
            // module (default component name) to the same-named environment
            // are DIFFERENT terraform states and must not contend.
            using var projectA = guard.Acquire("proj-a", "Prod", "sql-database", "op-1");
            guard.Acquire("proj-b", "Prod", "sql-database", "op-2", TimeSpan.FromMilliseconds(50)).Dispose();
        }

        [TestMethod]
        public void Exception_CarriesContendingOperationId()
        {
            var guard = NewGuard();
            using var first = guard.Acquire("proj-a", "env-a", "comp-a", "op-running");

            var ex = Assert.ThrowsExactly<TerraformConcurrentOperationException>(() =>
            {
                using var second = guard.Acquire("proj-a", "env-a", "comp-a", "op-pending", TimeSpan.FromMilliseconds(50));
            });
            Assert.AreEqual("env-a", ex.Environment);
            Assert.AreEqual("comp-a", ex.Component);
            Assert.AreEqual("op-running", ex.ContendingOperationId);
        }
    }
}
