using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;

namespace Dorc.Monitor.Security
{
    /// <summary>
    /// Creates - and keeps - the directories the Monitor writes deployment artefacts into.
    ///
    /// Everything written to these directories is derived from resolved deployment properties:
    /// the script group bundle carries them outright, secrets included, and a Terraform plan
    /// carries whatever of them the configuration interpolates. Created by
    /// <see cref="Directory.CreateDirectory(string)"/> under %ProgramData% they inherit an ACL
    /// that admits every authenticated user on the host, and the artefacts inherit it in turn.
    ///
    /// The DACL applied here is protected, so nothing is inherited from the parent, and names
    /// only the account the Monitor runs as, SYSTEM and Administrators - plus whatever
    /// per-deployment principal the caller has to admit for the artefact to be usable at all.
    /// </summary>
    [SupportedOSPlatform("windows")]
    internal static class RestrictedDirectory
    {
        /// <summary>
        /// Applies the restriction to <paramref name="path"/>, creating it if it is not there.
        ///
        /// The DACL is re-applied on every call rather than only at creation. A directory that
        /// an administrator deleted and re-created by hand, or restored from a backup, is back
        /// to inheriting %ProgramData%'s permissions, and every artefact written into it
        /// afterwards would inherit them too.
        /// </summary>
        public static DirectoryInfo Ensure(string path, params IdentityReference[] additionalModifyIdentities)
        {
            var security = BuildSecurity(additionalModifyIdentities);

            if (!Directory.Exists(path))
            {
                // FileSystemAclExtensions: creates the directory with the supplied DACL
                // atomically, so it never exists with the ACL it would have inherited.
                return security.CreateDirectory(path);
            }

            var existing = new DirectoryInfo(path);
            existing.SetAccessControl(security);
            return existing;
        }

        public static DirectorySecurity BuildSecurity(IEnumerable<IdentityReference> additionalModifyIdentities)
        {
            var security = new DirectorySecurity();
            security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);

            foreach (var identity in PrivilegedIdentities())
            {
                Grant(security, identity, FileSystemRights.FullControl);
            }

            foreach (var identity in additionalModifyIdentities)
            {
                // Modify, not FullControl: a deployment principal has to create, read and
                // replace its own artefacts, but it has no reason to be able to rewrite the
                // directory's ACL and re-open it to the host.
                Grant(security, identity, FileSystemRights.Modify | FileSystemRights.Synchronize);
            }

            return security;
        }

        private static void Grant(DirectorySecurity security, IdentityReference identity, FileSystemRights rights) =>
            security.AddAccessRule(new FileSystemAccessRule(
                identity,
                rights,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                PropagationFlags.None,
                AccessControlType.Allow));

        private static IEnumerable<IdentityReference> PrivilegedIdentities()
        {
            // Only the service account writing the artefact plus SYSTEM and
            // BUILTIN\Administrators. Everything else - including authenticated interactive
            // users on the Monitor host - is denied by the absence of an inherited Users ACE.
            yield return WindowsIdentity.GetCurrent().User!;
            yield return new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
            yield return new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
        }
    }
}
