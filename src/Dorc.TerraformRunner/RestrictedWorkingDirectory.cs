using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;

namespace Dorc.TerraformRunner
{
    /// <summary>
    /// Creates the scratch directories Terraform runs in.
    ///
    /// These directories are not incidental: they hold terraform.tfvars, which is every
    /// resolved deployment property written out in plain text, along with the provisioned
    /// infrastructure code and Terraform's own state. Created with inherited permissions under
    /// %ProgramData%, they were readable by any authenticated user on the deployment host for
    /// as long as they survived - and on the failure path they survived indefinitely.
    ///
    /// The DACL is protected so that nothing is inherited from %ProgramData%, and names only
    /// the account the Runner is executing as, SYSTEM and Administrators.
    /// </summary>
    internal static class RestrictedWorkingDirectory
    {
        public static DirectoryInfo Create(string path)
        {
            if (!OperatingSystem.IsWindows())
            {
                // DOrc deploys on Windows; this branch exists so the surrounding logic is
                // exercisable off it. There is no equivalent restriction applied here, so it
                // must never be the production path.
                return Directory.CreateDirectory(path);
            }

            return CreateRestricted(path);
        }

        [SupportedOSPlatform("windows")]
        private static DirectoryInfo CreateRestricted(string path)
        {
            var security = new DirectorySecurity();
            security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);

            foreach (var identity in PrivilegedIdentities())
            {
                security.AddAccessRule(new FileSystemAccessRule(
                    identity,
                    FileSystemRights.FullControl,
                    InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                    PropagationFlags.None,
                    AccessControlType.Allow));
            }

            if (Directory.Exists(path))
            {
                var existing = new DirectoryInfo(path);
                existing.SetAccessControl(security);
                return existing;
            }

            // Creates the directory with the supplied DACL atomically, so it never exists with
            // the permissions it would have inherited.
            return security.CreateDirectory(path);
        }

        [SupportedOSPlatform("windows")]
        private static IEnumerable<IdentityReference> PrivilegedIdentities()
        {
            // The Runner is started as the deployment account, so its own identity is the
            // deployment account - there is no second principal to admit here, unlike the
            // Monitor writing a bundle for a Runner it is about to start.
            yield return WindowsIdentity.GetCurrent().User!;
            yield return new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
            yield return new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
        }
    }
}
