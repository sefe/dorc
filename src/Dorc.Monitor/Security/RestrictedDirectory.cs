using Microsoft.Win32.SafeHandles;
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
        internal const string ArtifactsDirectoryName = "artifacts";

        internal const FileSystemRights ProgramDataRootAuthenticatedUsersRights =
            FileSystemRights.Traverse
            | FileSystemRights.ListDirectory
            | FileSystemRights.ReadAttributes
            | FileSystemRights.ReadExtendedAttributes
            | FileSystemRights.CreateDirectories
            | FileSystemRights.Synchronize;

        /// <summary>
        /// Applies the restriction to <paramref name="path"/>, creating it if it is not there.
        ///
        /// The DACL is re-applied on every call rather than only at creation. A directory that
        /// an administrator deleted and re-created by hand, or restored from a backup, is back
        /// to inheriting %ProgramData%'s permissions, and every artefact written into it
        /// afterwards would inherit them too.
        /// </summary>
        public static DirectoryInfo Ensure(string path, params IdentityReference[] additionalModifyIdentities) =>
            EnsureWithOwners(
                path,
                PrivilegedIdentities().ToArray(),
                additionalModifyIdentities);

        internal static string EnsureAndPin(
            string path,
            FileIdentity? expectedParentIdentity,
            out FileIdentity directoryIdentity,
            out SafeFileHandle pinnedHandle,
            params IdentityReference[] additionalModifyIdentities) =>
            EnsureWithOwnersAndPin(
                path,
                expectedParentIdentity,
                PrivilegedIdentities().ToArray(),
                out directoryIdentity,
                out pinnedHandle,
                additionalModifyIdentities);

        internal static DirectoryInfo EnsureWithOwners(
            string path,
            IReadOnlyCollection<SecurityIdentifier> privilegedOwners,
            params IdentityReference[] additionalModifyIdentities)
        {
            EnsureWithOwnersAndPin(
                path,
                expectedParentIdentity: null,
                privilegedOwners,
                out _,
                out var pinnedHandle,
                additionalModifyIdentities);
            pinnedHandle.Dispose();
            return new DirectoryInfo(path);
        }

        internal static string EnsureWithOwnersAndPin(
            string path,
            FileIdentity? expectedParentIdentity,
            IReadOnlyCollection<SecurityIdentifier> privilegedOwners,
            out FileIdentity directoryIdentity,
            out SafeFileHandle pinnedHandle,
            params IdentityReference[] additionalModifyIdentities)
        {
            var security = BuildSecurity(additionalModifyIdentities);

            if (File.Exists(path))
            {
                throw new IOException($"Cannot create restricted directory '{path}' because a file already exists at that path.");
            }

            if (IsExistingReparsePoint(path))
            {
                // Setting an ACL or creating through a junction/symlink acts on its target
                // rather than on the link, which would let a local principal redirect the
                // Monitor's privileged DACL rewrite onto an arbitrary directory.
                throw new IOException($"Refusing to create or modify restricted directory at reparse point '{path}'.");
            }

            if (!Directory.Exists(path))
            {
                // FileSystemAclExtensions: creates the directory with the supplied DACL
                // atomically, so it never exists with the ACL it would have inherited.
                security.CreateDirectory(path);
            }

            // Re-open by handle with FILE_FLAG_BACKUP_SEMANTICS | FILE_FLAG_OPEN_REPARSE_POINT,
            // verify canonical path, refuse any directory owned by a non-privileged principal
            // (never adopting in-place, because an attacker who pre-created the directory could
            // retain a pre-opened WRITE_DAC handle across an owner/DACL rewrite), and apply the
            // protected DACL directly on the non-FileShare.Delete pinned handle.
            using (var inspectHandle = ReparseFreeHandles.OpenVerifiedDirectoryHandle(
                path,
                ReparseFreeHandles.FileReadAttributes | ReparseFreeHandles.ReadControl))
            {
                VerifyCanonicalChildOfParent(inspectHandle, path, expectedParentIdentity);
                directoryIdentity = ReparseFreeHandles.GetFileIdentity(inspectHandle, path);

                var currentOwner = ReparseFreeHandles.GetOwnerSid(inspectHandle, path);
                if (!privilegedOwners.Contains(currentOwner))
                {
                    throw new IOException(
                        $"Refusing to use directory '{path}' because its NTFS owner '{currentOwner}' is not a privileged account.");
                }
            }

            pinnedHandle = ReparseFreeHandles.OpenPinnedDirectoryHandle(
                path,
                ReparseFreeHandles.FileListDirectory
                | ReparseFreeHandles.FileReadAttributes
                | ReparseFreeHandles.ReadControl
                | ReparseFreeHandles.WriteDac);
            try
            {
                VerifyCanonicalChildOfParent(pinnedHandle, path, expectedParentIdentity);
                ReparseFreeHandles.VerifyFileIdentity(pinnedHandle, directoryIdentity, path);

                var pinnedOwner = ReparseFreeHandles.GetOwnerSid(pinnedHandle, path);
                if (!privilegedOwners.Contains(pinnedOwner))
                {
                    throw new IOException(
                        $"Refusing to use directory '{path}' because its NTFS owner '{pinnedOwner}' is not a privileged account.");
                }

                ReparseFreeHandles.ApplyProtectedDacl(pinnedHandle, security, path);
                return ReparseFreeHandles.GetCanonicalPath(pinnedHandle, path);
            }
            catch
            {
                pinnedHandle.Dispose();
                throw;
            }
        }

        /// <summary>
        /// Ensures <c>%ProgramData%\dorc</c> exists, is owned by a privileged Monitor/SYSTEM/Admin
        /// principal, carries a protected DACL that removes <c>FILE_DELETE_CHILD</c>,
        /// <c>Delete</c>, <c>ChangePermissions</c>, and <c>TakeOwnership</c> from all non-privileged
        /// principals while permitting runner accounts non-inheriting subdirectory creation and traversal,
        /// and pre-creates <c>terraform-plans</c> beneath it as privileged-owned so no non-privileged
        /// principal can squat it.
        /// </summary>
        public static DirectoryInfo EnsureProgramDataRoot(string path)
        {
            var info = EnsureProgramDataRootAndPin(path, out _, out var pinnedHandle);
            pinnedHandle.Dispose();
            return info;
        }

        internal static DirectoryInfo EnsureProgramDataRootAndPin(
            string path,
            out FileIdentity directoryIdentity,
            out SafeFileHandle pinnedHandle) =>
            EnsureProgramDataRootWithOwners(
                path,
                PrivilegedIdentities().ToArray(),
                out directoryIdentity,
                out pinnedHandle);

        internal static DirectoryInfo EnsureProgramDataRootWithOwners(
            string path,
            IReadOnlyCollection<SecurityIdentifier> privilegedOwners,
            out FileIdentity directoryIdentity,
            out SafeFileHandle pinnedHandle)
        {
            var security = BuildProgramDataRootSecurity();

            if (File.Exists(path))
            {
                throw new IOException($"Cannot create ProgramData root directory '{path}' because a file already exists at that path.");
            }

            if (IsExistingReparsePoint(path))
            {
                throw new IOException($"Refusing to create or modify ProgramData root directory at reparse point '{path}'.");
            }

            if (!Directory.Exists(path))
            {
                security.CreateDirectory(path);
            }

            bool needsDaclHardening;
            using (var inspectHandle = ReparseFreeHandles.OpenVerifiedDirectoryHandle(
                path,
                ReparseFreeHandles.FileReadAttributes | ReparseFreeHandles.ReadControl))
            {
                VerifyCanonicalChildOfParent(inspectHandle, path, expectedParentIdentity: null);
                directoryIdentity = ReparseFreeHandles.GetFileIdentity(inspectHandle, path);

                var currentOwner = ReparseFreeHandles.GetOwnerSid(inspectHandle, path);
                if (!privilegedOwners.Contains(currentOwner))
                {
                    throw new IOException(
                        $"Refusing to use directory '{path}' because its NTFS owner '{currentOwner}' is not a privileged account.");
                }

                var currentSecurity = ReparseFreeHandles.GetDirectorySecurity(inspectHandle, path);
                needsDaclHardening = NeedsProgramDataRootDaclHardening(currentSecurity);
            }

            var pinDesiredAccess = ReparseFreeHandles.FileListDirectory
                | ReparseFreeHandles.FileReadAttributes
                | ReparseFreeHandles.ReadControl
                | (needsDaclHardening ? ReparseFreeHandles.WriteDac : 0u);
            pinnedHandle = ReparseFreeHandles.OpenPinnedDirectoryHandle(path, pinDesiredAccess);
            try
            {
                VerifyCanonicalChildOfParent(pinnedHandle, path, expectedParentIdentity: null);
                ReparseFreeHandles.VerifyFileIdentity(pinnedHandle, directoryIdentity, path);

                var pinnedOwner = ReparseFreeHandles.GetOwnerSid(pinnedHandle, path);
                if (!privilegedOwners.Contains(pinnedOwner))
                {
                    throw new IOException(
                        $"Refusing to use directory '{path}' because its NTFS owner '{pinnedOwner}' is not a privileged account.");
                }

                if (needsDaclHardening)
                {
                    ReparseFreeHandles.ApplyProtectedDacl(pinnedHandle, security, path);
                }

                // Immediately ensure terraform-plans and artifacts exist under %ProgramData%\dorc
                // as privileged-owned restricted directories while holding pinnedHandle, preventing
                // any Authenticated User from squatting them via CreateDirectories. Note that
                // artifacts itself admits only Monitor/SYSTEM/Administrators; each downloaded
                // per-request GitHub build drop directory beneath it receives an explicit
                // ReadAndExecute grant scoped solely to that request's execution identity.
                var terraformPlansPath = Path.Join(path, Terraform.TerraformPlanStore.DirectoryName);
                EnsureWithOwnersAndPin(
                    terraformPlansPath,
                    directoryIdentity,
                    privilegedOwners,
                    out _,
                    out var terraformPlansPinHandle);
                terraformPlansPinHandle.Dispose();

                var artifactsPath = Path.Join(path, ArtifactsDirectoryName);
                EnsureWithOwnersAndPin(
                    artifactsPath,
                    directoryIdentity,
                    privilegedOwners,
                    out _,
                    out var artifactsPinHandle);
                artifactsPinHandle.Dispose();

                return new DirectoryInfo(path);
            }
            catch
            {
                pinnedHandle.Dispose();
                throw;
            }
        }

        /// <summary>
        /// Creates a brand-new restricted directory at <paramref name="path"/>, failing atomically
        /// if any file, directory, junction, or symlink already exists at that path, and returns
        /// its kernel-normalized canonical path.
        /// </summary>
        public static string CreateNew(string path, params IdentityReference[] additionalModifyIdentities) =>
            CreateNew(path, out _, additionalModifyIdentities);

        internal static string CreateNew(
            string path,
            out FileIdentity directoryIdentity,
            params IdentityReference[] additionalModifyIdentities)
        {
            var canonicalPath = CreateNew(
                path,
                expectedParentIdentity: null,
                out directoryIdentity,
                out var pinnedDirectoryHandle,
                additionalModifyIdentities);
            pinnedDirectoryHandle.Dispose();
            return canonicalPath;
        }

        internal static string CreateNew(
            string path,
            FileIdentity? expectedParentIdentity,
            out FileIdentity directoryIdentity,
            out SafeFileHandle pinnedDirectoryHandle,
            params IdentityReference[] additionalModifyIdentities)
        {
            var trimmedFullPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
            var parentPath = Path.GetDirectoryName(trimmedFullPath)
                ?? throw new IOException($"Cannot create restricted directory '{path}' without a parent directory.");
            var leafName = Path.GetFileName(trimmedFullPath);
            if (string.IsNullOrEmpty(leafName))
            {
                throw new IOException($"Cannot create restricted directory '{path}' without a directory name.");
            }

            using var parentHandle = ReparseFreeHandles.OpenVerifiedDirectoryHandle(
                parentPath,
                ReparseFreeHandles.FileReadAttributes);
            if (expectedParentIdentity.HasValue)
            {
                ReparseFreeHandles.VerifyFileIdentity(
                    parentHandle,
                    expectedParentIdentity.Value,
                    parentPath);
            }

            var expectedCanonical = Path.Join(
                ReparseFreeHandles.GetCanonicalPath(parentHandle, parentPath),
                leafName);

            var security = BuildSecurity(additionalModifyIdentities);
            var actualCanonical = ReparseFreeHandles.CreateNewDirectoryWithSecurity(
                trimmedFullPath,
                security,
                out directoryIdentity,
                out pinnedDirectoryHandle);
            if (!string.Equals(actualCanonical, expectedCanonical, StringComparison.OrdinalIgnoreCase))
            {
                pinnedDirectoryHandle.Dispose();
                throw new IOException(
                    $"Newly created restricted directory '{path}' resolved to '{actualCanonical}' instead of '{expectedCanonical}'.");
            }

            return actualCanonical;
        }

        private static void VerifyCanonicalChildOfParent(
            SafeFileHandle handle,
            string path,
            FileIdentity? expectedParentIdentity)
        {
            var trimmedFullPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
            var parentPath = Path.GetDirectoryName(trimmedFullPath);
            var leafName = Path.GetFileName(trimmedFullPath);
            if (string.IsNullOrEmpty(parentPath) || string.IsNullOrEmpty(leafName))
            {
                return;
            }

            using var parentHandle = ReparseFreeHandles.OpenVerifiedDirectoryHandle(
                parentPath,
                ReparseFreeHandles.FileReadAttributes);
            if (expectedParentIdentity.HasValue)
            {
                ReparseFreeHandles.VerifyFileIdentity(
                    parentHandle,
                    expectedParentIdentity.Value,
                    parentPath);
            }

            var expectedCanonical = Path.Join(
                ReparseFreeHandles.GetCanonicalPath(parentHandle, parentPath),
                leafName);
            var actualCanonical = ReparseFreeHandles.GetCanonicalPath(handle, path);
            if (!string.Equals(actualCanonical, expectedCanonical, StringComparison.OrdinalIgnoreCase))
            {
                throw new IOException(
                    $"Refusing to apply a restricted ACL to '{path}' because it resolved to '{actualCanonical}' instead of '{expectedCanonical}'.");
            }
        }

        internal static bool IsExistingReparsePoint(string path)
        {
            try
            {
                return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
            }
            catch (FileNotFoundException)
            {
                return false;
            }
            catch (DirectoryNotFoundException)
            {
                return false;
            }
        }

        internal const string OwnerRightsSidValue = "S-1-3-4";

        public static DirectorySecurity BuildProgramDataRootSecurity()
        {
            var security = new DirectorySecurity();
            security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);

            foreach (var identity in PrivilegedIdentities().Distinct())
            {
                Grant(security, identity, FileSystemRights.FullControl);
            }

            Grant(
                security,
                new SecurityIdentifier(OwnerRightsSidValue),
                FileSystemRights.ReadPermissions);

            // Authenticated Users receive ONLY non-inheriting traversal/listing/subdirectory-creation
            // on %ProgramData%\dorc so per-dispatch Dorc.TerraformRunner scratch directories can be
            // created directly beneath %ProgramData%\dorc while ZERO non-privileged principal holds
            // FILE_DELETE_CHILD, Delete, ChangePermissions, TakeOwnership, or CreateFiles on
            // %ProgramData%\dorc itself.
            security.AddAccessRule(new FileSystemAccessRule(
                new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null),
                ProgramDataRootAuthenticatedUsersRights,
                InheritanceFlags.None,
                PropagationFlags.None,
                AccessControlType.Allow));

            return security;
        }

        private static bool NeedsProgramDataRootDaclHardening(DirectorySecurity currentSecurity)
        {
            if (!currentSecurity.AreAccessRulesProtected)
            {
                return true;
            }

            var privilegedIdentities = PrivilegedIdentities().ToHashSet();
            var ownerRightsSid = new SecurityIdentifier(OwnerRightsSidValue);
            var authenticatedUsersSid = new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null);

            var rules = currentSecurity
                .GetAccessRules(includeExplicit: true, includeInherited: true, typeof(SecurityIdentifier))
                .Cast<FileSystemAccessRule>()
                .ToList();

            var seenPrivileged = new HashSet<SecurityIdentifier>();
            var hasOwnerRights = false;
            var hasAuthenticatedUsers = false;

            const InheritanceFlags fullInheritance =
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;

            foreach (var rule in rules)
            {
                if (rule.IsInherited || rule.AccessControlType != AccessControlType.Allow)
                {
                    return true;
                }

                var sid = (SecurityIdentifier)rule.IdentityReference;
                if (privilegedIdentities.Contains(sid))
                {
                    if (rule.FileSystemRights != FileSystemRights.FullControl
                        || rule.InheritanceFlags != fullInheritance
                        || rule.PropagationFlags != PropagationFlags.None)
                    {
                        return true;
                    }

                    seenPrivileged.Add(sid);
                }
                else if (sid == ownerRightsSid)
                {
                    if ((rule.FileSystemRights & ~FileSystemRights.Synchronize) != FileSystemRights.ReadPermissions
                        || rule.InheritanceFlags != fullInheritance
                        || rule.PropagationFlags != PropagationFlags.None)
                    {
                        return true;
                    }

                    hasOwnerRights = true;
                }
                else if (sid == authenticatedUsersSid)
                {
                    if (rule.FileSystemRights != ProgramDataRootAuthenticatedUsersRights
                        || rule.InheritanceFlags != InheritanceFlags.None
                        || rule.PropagationFlags != PropagationFlags.None)
                    {
                        return true;
                    }

                    hasAuthenticatedUsers = true;
                }
                else
                {
                    return true;
                }
            }

            return !seenPrivileged.SetEquals(privilegedIdentities)
                || !hasOwnerRights
                || !hasAuthenticatedUsers;
        }

        public static DirectorySecurity BuildSecurity(IEnumerable<IdentityReference> additionalModifyIdentities) =>
            BuildSecurity(additionalModifyIdentities, inheritToChildren: true);

        public static DirectorySecurity BuildSecurity(
            IEnumerable<IdentityReference> additionalModifyIdentities,
            bool inheritToChildren)
        {
            var security = new DirectorySecurity();
            security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
            var inheritance = inheritToChildren
                ? InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit
                : InheritanceFlags.None;

            foreach (var identity in PrivilegedIdentities())
            {
                Grant(security, identity, FileSystemRights.FullControl, inheritance);
            }

            // Explicit OWNER RIGHTS (S-1-3-4) with ReadPermissions only suppresses NTFS's implicit
            // Owner WRITE_DAC, preventing a Runner-owned child file or subdirectory from rewriting
            // its own DACL to re-grant access after Monitor revokes the Runner ACE.
            Grant(
                security,
                new SecurityIdentifier(OwnerRightsSidValue),
                FileSystemRights.ReadPermissions,
                inheritance);

            foreach (var identity in additionalModifyIdentities)
            {
                // Modify, not FullControl: a deployment principal has to create, read and
                // replace its own artefacts, but it has no reason to be able to rewrite the
                // directory's ACL and re-open it to the host.
                Grant(security, identity, FileSystemRights.Modify | FileSystemRights.Synchronize, inheritance);
            }

            return security;
        }

        public static DirectorySecurity BuildReadAndExecuteDirectorySecurity(
            IEnumerable<IdentityReference> additionalReadIdentities,
            bool inheritToChildren = true)
        {
            var security = new DirectorySecurity();
            security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
            var privilegedSet = PrivilegedIdentities().ToHashSet();
            var inheritance = inheritToChildren
                ? InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit
                : InheritanceFlags.None;

            foreach (var identity in privilegedSet)
            {
                Grant(security, identity, FileSystemRights.FullControl, inheritance);
            }

            Grant(
                security,
                new SecurityIdentifier(OwnerRightsSidValue),
                FileSystemRights.ReadPermissions,
                inheritance);

            foreach (var identity in additionalReadIdentities.Distinct())
            {
                if (identity is SecurityIdentifier sid && privilegedSet.Contains(sid))
                {
                    continue;
                }

                Grant(
                    security,
                    identity,
                    FileSystemRights.ReadAndExecute | FileSystemRights.Synchronize,
                    inheritance);
            }

            return security;
        }

        public static FileSecurity BuildReadAndExecuteFileSecurity(IEnumerable<IdentityReference> additionalReadIdentities)
        {
            var security = new FileSecurity();
            security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
            var privilegedSet = PrivilegedIdentities().ToHashSet();

            foreach (var identity in privilegedSet)
            {
                security.AddAccessRule(new FileSystemAccessRule(
                    identity,
                    FileSystemRights.FullControl,
                    InheritanceFlags.None,
                    PropagationFlags.None,
                    AccessControlType.Allow));
            }

            security.AddAccessRule(new FileSystemAccessRule(
                new SecurityIdentifier(OwnerRightsSidValue),
                FileSystemRights.ReadPermissions,
                InheritanceFlags.None,
                PropagationFlags.None,
                AccessControlType.Allow));

            foreach (var identity in additionalReadIdentities.Distinct())
            {
                if (identity is SecurityIdentifier sid && privilegedSet.Contains(sid))
                {
                    continue;
                }

                security.AddAccessRule(new FileSystemAccessRule(
                    identity,
                    FileSystemRights.ReadAndExecute | FileSystemRights.Synchronize,
                    InheritanceFlags.None,
                    PropagationFlags.None,
                    AccessControlType.Allow));
            }

            return security;
        }

        /// <summary>
        /// Confines an extracted per-request build drop directory and all of its extracted
        /// subdirectories and single-link files to Monitor, SYSTEM, and Administrators
        /// (<see cref="FileSystemRights.FullControl"/>), while granting explicit
        /// <see cref="FileSystemRights.ReadAndExecute"/> (with <c>ContainerInherit | ObjectInherit</c>
        /// on directories) only to <paramref name="additionalReadIdentities"/>.
        /// </summary>
        public static void RestrictReadTree(
            string directoryPath,
            IEnumerable<IdentityReference> additionalReadIdentities)
        {
            var readers = additionalReadIdentities.Distinct().ToArray();
            var dirSecurity = BuildReadAndExecuteDirectorySecurity(readers, inheritToChildren: true);
            var fileSecurity = BuildReadAndExecuteFileSecurity(readers);
            var privilegedOwners = PrivilegedIdentities().ToHashSet();

            using var rootHandle = ReparseFreeHandles.OpenVerifiedDirectoryHandle(
                directoryPath,
                ReparseFreeHandles.FileListDirectory
                | ReparseFreeHandles.FileReadAttributes
                | ReparseFreeHandles.ReadControl
                | ReparseFreeHandles.WriteDac);
            VerifyCanonicalChildOfParent(rootHandle, directoryPath, expectedParentIdentity: null);

            var currentOwner = ReparseFreeHandles.GetOwnerSid(rootHandle, directoryPath);
            if (!privilegedOwners.Contains(currentOwner))
            {
                throw new IOException(
                    $"Refusing to grant artefact read access on '{directoryPath}' because its NTFS owner '{currentOwner}' is not a privileged account.");
            }

            var canonicalRoot = ReparseFreeHandles.GetCanonicalPath(rootHandle, directoryPath);
            ReparseFreeHandles.ApplyProtectedDacl(rootHandle, dirSecurity, canonicalRoot);
            RestrictReadTreeChildren(canonicalRoot, canonicalRoot, dirSecurity, fileSecurity);
        }

        private static void RestrictReadTreeChildren(
            string parentCanonicalDirectory,
            string treeCanonicalRoot,
            DirectorySecurity dirSecurity,
            FileSecurity fileSecurity)
        {
            var rootPrefix = treeCanonicalRoot.EndsWith(Path.DirectorySeparatorChar)
                ? treeCanonicalRoot
                : treeCanonicalRoot + Path.DirectorySeparatorChar;

            foreach (var entry in new DirectoryInfo(parentCanonicalDirectory).EnumerateFileSystemInfos())
            {
                if ((entry.Attributes & FileAttributes.ReparsePoint) != 0)
                {
                    throw new IOException(
                        $"Refusing to apply artefact read ACL to reparse point '{entry.FullName}'.");
                }

                var expectedChildCanonical = Path.Join(parentCanonicalDirectory, entry.Name);
                if ((entry.Attributes & FileAttributes.Directory) != 0)
                {
                    using var childDirHandle = ReparseFreeHandles.OpenVerifiedDirectoryHandle(
                        expectedChildCanonical,
                        ReparseFreeHandles.FileListDirectory
                        | ReparseFreeHandles.FileReadAttributes
                        | ReparseFreeHandles.ReadControl
                        | ReparseFreeHandles.WriteDac);
                    var actualChildCanonical = ReparseFreeHandles.GetCanonicalPath(childDirHandle, expectedChildCanonical);
                    if (!string.Equals(actualChildCanonical, expectedChildCanonical, StringComparison.OrdinalIgnoreCase)
                        || !actualChildCanonical.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new IOException(
                            $"Refusing to apply artefact read ACL to '{expectedChildCanonical}' because its canonical path '{actualChildCanonical}' escaped '{treeCanonicalRoot}'.");
                    }

                    ReparseFreeHandles.ApplyProtectedDacl(childDirHandle, dirSecurity, actualChildCanonical);
                    RestrictReadTreeChildren(actualChildCanonical, treeCanonicalRoot, dirSecurity, fileSecurity);
                }
                else
                {
                    using var fileHandle = ReparseFreeHandles.OpenVerifiedRegularFileForDacl(
                        expectedChildCanonical,
                        expectedChildCanonical);
                    ReparseFreeHandles.ApplyProtectedDacl(fileHandle, fileSecurity, expectedChildCanonical);
                }
            }
        }

        public static FileSecurity BuildFileSecurity(IEnumerable<IdentityReference> additionalModifyIdentities)
        {
            var security = new FileSecurity();
            security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);

            foreach (var identity in PrivilegedIdentities())
            {
                security.AddAccessRule(new FileSystemAccessRule(
                    identity,
                    FileSystemRights.FullControl,
                    InheritanceFlags.None,
                    PropagationFlags.None,
                    AccessControlType.Allow));
            }

            security.AddAccessRule(new FileSystemAccessRule(
                new SecurityIdentifier(OwnerRightsSidValue),
                FileSystemRights.ReadPermissions,
                InheritanceFlags.None,
                PropagationFlags.None,
                AccessControlType.Allow));

            foreach (var identity in additionalModifyIdentities)
            {
                security.AddAccessRule(new FileSystemAccessRule(
                    identity,
                    FileSystemRights.Modify | FileSystemRights.Synchronize,
                    InheritanceFlags.None,
                    PropagationFlags.None,
                    AccessControlType.Allow));
            }

            return security;
        }

        private static void Grant(
            DirectorySecurity security,
            IdentityReference identity,
            FileSystemRights rights,
            InheritanceFlags inheritance = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit) =>
            security.AddAccessRule(new FileSystemAccessRule(
                identity,
                rights,
                inheritance,
                PropagationFlags.None,
                AccessControlType.Allow));

        internal static IEnumerable<SecurityIdentifier> PrivilegedIdentities()
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
