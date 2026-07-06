using System.IO.Pipes;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;

namespace Dorc.Monitor.Pipes
{
    // (partial): builds the ACL applied to the named pipe that ferries
    // the ScriptGroup (containing PATs / bearer tokens) from the Monitor to
    // the spawned runner process. Without an explicit ACL the pipe inherits
    // permissive defaults that let any authenticated principal connect and
    // read the payload. We restrict to:
    //   - LocalSystem (FullControl)        so Windows service hosts work
    //   - Current process identity (Full)  so the Monitor itself can manage
    //   - Authenticated Users (Read+Write) so the deployment-context runner
    //                                       can connect; tighter scoping
    //                                       requires the per-request SID and
    //                                       is a follow-up.
    // Anonymous logon is denied implicitly (no rule). Everyone is denied
    // implicitly (no rule).
    [SupportedOSPlatform("windows")]
    public static class TerraformPipeAcl
    {
        public static PipeSecurity Build()
        {
            var ps = new PipeSecurity();

            ps.SetAccessRule(new PipeAccessRule(
                new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
                PipeAccessRights.FullControl,
                AccessControlType.Allow));

            using var current = WindowsIdentity.GetCurrent();
            if (current.User is not null)
            {
                ps.SetAccessRule(new PipeAccessRule(
                    current.User,
                    PipeAccessRights.FullControl,
                    AccessControlType.Allow));
            }

            // Runner connects under the deployment user; we don't have that
            // SID here, so grant AuthenticatedUsers the minimum needed to
            // read+write the pipe payload. Anonymous logon is excluded.
            ps.SetAccessRule(new PipeAccessRule(
                new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null),
                PipeAccessRights.ReadData | PipeAccessRights.WriteData | PipeAccessRights.Synchronize,
                AccessControlType.Allow));

            return ps;
        }
    }
}
