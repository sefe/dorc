using Dorc.Monitor.RunnerProcess;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;

namespace Dorc.Monitor.Tests
{
    /// <summary>
    /// The Runner process holds both deployment credential pairs and the whole resolved
    /// property set in memory, so who may open it matters.
    ///
    /// The code this replaces asked for a null access list — full control to every local
    /// principal — and failed to deliver it, because the descriptor it modified was a managed
    /// copy and the one it passed to CreateProcessAsUser was a separate unmanaged buffer that
    /// only InitializeSecurityDescriptor had touched. A descriptor with no access list PRESENT
    /// is not one with a null access list: Windows applies the token's default instead. So the
    /// intent never took effect, and repairing the marshalling on its own would have made a
    /// dormant mistake live.
    ///
    /// These assert against the binary form, because that is what the kernel reads. Asserting
    /// on the managed objects used to build it would test the builder against itself.
    /// </summary>
    [TestClass]
    public class RunnerProcessSecurityDescriptorTests
    {
        private const string RunnerSid = "S-1-5-21-1111111111-2222222222-3333333333-1001";
        private const string MonitorSid = "S-1-5-21-1111111111-2222222222-3333333333-1002";

        private const int ProcessAllAccess = 0x001FFFFF;

        private static SecurityIdentifier Runner => new(RunnerSid);
        private static SecurityIdentifier Monitor => new(MonitorSid);

        /// <summary>
        /// Access control lists are a Windows construct and so is every type used to build one.
        /// The build host for this repository is not always Windows, and SecurityIdentifier
        /// refuses to be constructed at all where it is not - so these report inconclusive
        /// rather than failing, and carry their weight on the Windows build where the code they
        /// cover actually runs. They are deliberately NOT rewritten to avoid the platform
        /// types: what is under test is the binary descriptor handed to CreateProcessAsUser,
        /// and a version of it that avoided Windows types would not be that descriptor.
        /// </summary>
        [TestInitialize]
        public void RequireWindows()
        {
            if (!OperatingSystem.IsWindows())
            {
                Assert.Inconclusive("Windows access control types are unavailable on this host.");
            }
        }

        /// <summary>
        /// Reads back what was actually written to unmanaged memory, rather than trusting the
        /// managed objects that produced it.
        /// </summary>
        private static RawSecurityDescriptor Marshalled(RunnerProcessSecurityDescriptor descriptor)
        {
            Assert.AreNotEqual(IntPtr.Zero, descriptor.Handle, "No descriptor was marshalled.");

            var raw = new byte[descriptor.Length];
            Marshal.Copy(descriptor.Handle, raw, 0, raw.Length);

            return new RawSecurityDescriptor(raw, 0);
        }

        /// <summary>
        /// The defect that made the old code dangerous to tidy up. A descriptor whose
        /// DiscretionaryAclPresent flag is clear silently defers to the token default; one
        /// whose flag is set with a null pointer grants everyone everything. Neither is
        /// acceptable, and only an explicit list is distinguishable from both.
        /// </summary>
        [TestMethod]
        public void DeclaresAnAccessListRatherThanDeferringToTheTokenDefault()
        {
            using var descriptor = RunnerProcessSecurityDescriptor.AdmittingOnly(Runner, Monitor);

            var marshalled = Marshalled(descriptor);

            Assert.IsTrue(marshalled.ControlFlags.HasFlag(ControlFlags.DiscretionaryAclPresent));
            Assert.IsTrue(marshalled.ControlFlags.HasFlag(ControlFlags.SelfRelative),
                "CreateProcessAsUser is handed a pointer, so the descriptor must be self-relative.");
            Assert.IsNotNull(marshalled.DiscretionaryAcl,
                "A present-but-null access list is the everyone-full-control case the old code asked for.");
        }

        [TestMethod]
        public void AdmitsTheRunnerTheMonitorSystemAndAdministrators()
        {
            using var descriptor = RunnerProcessSecurityDescriptor.AdmittingOnly(Runner, Monitor);

            var admitted = AdmittedPrincipals(Marshalled(descriptor));

            CollectionAssert.AreEquivalent(
                new[]
                {
                    Runner.Value,
                    Monitor.Value,
                    new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null).Value,
                    new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null).Value
                },
                admitted);
        }

        /// <summary>
        /// The account the process runs as was already admitted by the token default this
        /// replaces. Dropping it would be a regression dressed up as hardening.
        /// </summary>
        [TestMethod]
        public void AlwaysAdmitsTheAccountTheRunnerRunsAs()
        {
            using var descriptor = RunnerProcessSecurityDescriptor.AdmittingOnly(Runner, Monitor);

            CollectionAssert.Contains(AdmittedPrincipals(Marshalled(descriptor)), Runner.Value);
        }

        /// <summary>
        /// The whole point: no principal beyond the four is named, so a local user who is not
        /// one of them cannot open the process at all.
        /// </summary>
        [TestMethod]
        public void AdmitsNoBroadPrincipal()
        {
            using var descriptor = RunnerProcessSecurityDescriptor.AdmittingOnly(Runner, Monitor);

            var admitted = AdmittedPrincipals(Marshalled(descriptor));

            foreach (var broad in new[]
            {
                WellKnownSidType.WorldSid,
                WellKnownSidType.AuthenticatedUserSid,
                WellKnownSidType.BuiltinUsersSid,
                WellKnownSidType.InteractiveSid
            })
            {
                CollectionAssert.DoesNotContain(admitted, new SecurityIdentifier(broad, null).Value);
            }
        }

        [TestMethod]
        public void GrantsFullProcessAccessToEveryAdmittedPrincipal()
        {
            using var descriptor = RunnerProcessSecurityDescriptor.AdmittingOnly(Runner, Monitor);

            foreach (CommonAce ace in Marshalled(descriptor).DiscretionaryAcl!)
            {
                Assert.AreEqual(AceType.AccessAllowed, ace.AceType);
                Assert.AreEqual(ProcessAllAccess, ace.AccessMask, $"Unexpected mask for {ace.SecurityIdentifier}.");
            }
        }

        /// <summary>
        /// The Monitor and deployment accounts coincide in the current topology. Emitting the
        /// same principal twice would be harmless but untidy; more to the point, the set must
        /// still admit it.
        /// </summary>
        [TestMethod]
        public void DoesNotDuplicateTheMonitorWhenItIsAlsoTheRunner()
        {
            using var descriptor = RunnerProcessSecurityDescriptor.AdmittingOnly(Runner, Runner);

            var admitted = AdmittedPrincipals(Marshalled(descriptor));

            Assert.AreEqual(1, admitted.Count(sid => sid == Runner.Value));
            Assert.AreEqual(3, admitted.Length);
        }

        /// <summary>
        /// The owner is stated rather than left to the creating token, whose default owner is
        /// BUILTIN\Administrators for any account that belongs to it.
        /// </summary>
        [TestMethod]
        public void NamesTheMonitorAsOwner()
        {
            using var descriptor = RunnerProcessSecurityDescriptor.AdmittingOnly(Runner, Monitor);

            Assert.AreEqual(Monitor.Value, Marshalled(descriptor).Owner!.Value);
        }

        [TestMethod]
        public void ReleasesItsUnmanagedMemoryAndSaysSo()
        {
            var descriptor = RunnerProcessSecurityDescriptor.AdmittingOnly(Runner, Monitor);
            Assert.AreNotEqual(IntPtr.Zero, descriptor.Handle);

            descriptor.Dispose();
            Assert.AreEqual(IntPtr.Zero, descriptor.Handle);

            // Disposing twice must not free the same block twice.
            descriptor.Dispose();
            Assert.AreEqual(IntPtr.Zero, descriptor.Handle);
        }

        private static string[] AdmittedPrincipals(RawSecurityDescriptor marshalled)
        {
            return marshalled.DiscretionaryAcl!
                .Cast<CommonAce>()
                .Where(ace => ace.AceType == AceType.AccessAllowed)
                .Select(ace => ace.SecurityIdentifier.Value)
                .ToArray();
        }
    }
}
