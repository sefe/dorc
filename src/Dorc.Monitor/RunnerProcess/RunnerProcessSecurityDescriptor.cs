using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;

namespace Dorc.Monitor.RunnerProcess
{
    /// <summary>
    /// The security descriptor applied to the Runner process object.
    ///
    /// What it replaces is worth stating precisely, because the previous code did not do what
    /// it was written to do. It called SetSecurityDescriptorDacl with a null access list —
    /// which asks for a descriptor granting every local principal full control over the
    /// process, and a process holding both deployment credential pairs in memory is not
    /// something to hand out. But the call took the descriptor <c>ref</c> as a managed struct,
    /// while the pointer actually passed to CreateProcessAsUser addressed a separate unmanaged
    /// copy that only InitializeSecurityDescriptor had touched. That copy has no access list
    /// PRESENT, which is not the same as a null one: Windows then applies the creating token's
    /// default access list. So the null-access-list intent has never taken effect, and the
    /// processes have been protected by a token default rather than by anything deliberate.
    ///
    /// That makes the marshalling defect load-bearing, which is the worst way for a defect to
    /// be. Repairing the marshalling alone — an obvious tidy-up for anyone reading the
    /// interop — would silently convert an inert mistake into a live one. So the descriptor is
    /// built explicitly here instead, in managed code, and the interop that produced the
    /// hazard is gone.
    /// </summary>
    [SupportedOSPlatform("windows")]
    internal sealed class RunnerProcessSecurityDescriptor : IDisposable
    {
        /// <summary>
        /// PROCESS_ALL_ACCESS. Named here rather than reached for by <c>-1</c> or a partial
        /// mask, because the point of this type is that the rights are stated.
        /// </summary>
        private const int ProcessAllAccess = 0x001FFFFF;

        private IntPtr descriptor;

        private RunnerProcessSecurityDescriptor(IntPtr descriptor, int length)
        {
            this.descriptor = descriptor;
            this.Length = length;
        }

        /// <summary>
        /// Unmanaged, self-relative, valid until this instance is disposed.
        /// </summary>
        public IntPtr Handle => descriptor;

        /// <summary>
        /// Bytes allocated at <see cref="Handle"/>. A self-relative descriptor carries its own
        /// extent, so Windows does not need this - but anything reading the buffer back does,
        /// and reading past it is an access violation waiting to happen.
        /// </summary>
        public int Length { get; }

        /// <summary>
        /// Admits the account the Runner runs as, the Monitor that started it, LocalSystem and
        /// the local administrators — and no-one else.
        ///
        /// The Runner's own account is included deliberately: the token default it replaces
        /// already granted it, so leaving it out would be a regression dressed as hardening.
        /// The Monitor is included because it starts, waits on and terminates the process. The
        /// set is a superset of what the token default gives today and a strict subset of the
        /// "everyone" the old code asked for.
        /// </summary>
        public static RunnerProcessSecurityDescriptor AdmittingOnly(
            SecurityIdentifier runner,
            SecurityIdentifier monitor)
        {
            var dacl = new DiscretionaryAcl(isContainer: false, isDS: false, capacity: 4);

            foreach (var principal in Principals(runner, monitor))
            {
                dacl.AddAccess(
                    AccessControlType.Allow,
                    principal,
                    ProcessAllAccess,
                    InheritanceFlags.None,
                    PropagationFlags.None);
            }

            var securityDescriptor = new CommonSecurityDescriptor(
                isContainer: false,
                isDS: false,
                ControlFlags.DiscretionaryAclPresent | ControlFlags.SelfRelative,
                owner: monitor,
                group: null,
                systemAcl: null,
                discretionaryAcl: dacl);

            var binaryForm = new byte[securityDescriptor.BinaryLength];
            securityDescriptor.GetBinaryForm(binaryForm, 0);

            var unmanaged = Marshal.AllocHGlobal(binaryForm.Length);
            try
            {
                Marshal.Copy(binaryForm, 0, unmanaged, binaryForm.Length);
            }
            catch
            {
                Marshal.FreeHGlobal(unmanaged);
                throw;
            }

            return new RunnerProcessSecurityDescriptor(unmanaged, binaryForm.Length);
        }

        private static IEnumerable<SecurityIdentifier> Principals(
            SecurityIdentifier runner,
            SecurityIdentifier monitor)
        {
            yield return runner;

            if (!monitor.Equals(runner))
            {
                yield return monitor;
            }

            yield return new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
            yield return new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
        }

        public void Dispose()
        {
            if (descriptor != IntPtr.Zero)
            {
                // The previous implementation allocated its descriptor with AllocCoTaskMem and
                // never freed it, leaking one per script group for the life of the service.
                Marshal.FreeHGlobal(descriptor);
                descriptor = IntPtr.Zero;
            }
        }
    }
}
