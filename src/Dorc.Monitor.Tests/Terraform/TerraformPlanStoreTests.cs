using Dorc.Monitor.Security;
using Dorc.Monitor.Terraform;
using Microsoft.Extensions.Logging.Abstractions;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;

namespace Dorc.Monitor.Tests.Terraform
{
    [TestClass]
    public class TerraformPlanStoreTests
    {
        private string _scratchBase = null!;
        private string _planRoot = null!;

        [TestInitialize]
        public void Setup()
        {
            _scratchBase = Path.Join(
                AppContext.BaseDirectory,
                "dorc-tfplan-store-tests-" + Guid.NewGuid().ToString("N"));
            _planRoot = Path.Join(_scratchBase, "terraform-plans");
            Directory.CreateDirectory(_planRoot);
        }

        [TestCleanup]
        public void Cleanup()
        {
            TerraformPlanLifetimeTests.RemoveScratchTreeSafely(_scratchBase);
        }

        [TestMethod]
        [SupportedOSPlatform("windows")]
        public void SweepConfinesUnexpiredLegacyFlatTfplanFileToHostPrincipalsWhileKeepingItReadableUntilExpiry()
        {
            if (!OperatingSystem.IsWindows())
            {
                return;
            }

            var legacyPlanPath = Path.Join(_planRoot, "7701.tfplan");
            const string legacyPlanPayload = "legacy-pre-upgrade-tfplan-binary-content";
            File.WriteAllText(legacyPlanPath, legacyPlanPayload, Encoding.UTF8);

            var currentUserSid = WindowsIdentity.GetCurrent().User!;
            var systemSid = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
            var adminsSid = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
            var builtinUsersSid = new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null);
            var authenticatedUsersSid = new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null);
            var everyoneSid = new SecurityIdentifier(WellKnownSidType.WorldSid, null);
            var ownerRightsSid = new SecurityIdentifier("S-1-3-4");

            // Simulate a pre-upgrade flat <resultId>.tfplan file carrying permissive Users / Authenticated Users read access.
            var permissiveFileSecurity = new FileSecurity();
            permissiveFileSecurity.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
            foreach (var privilegedSid in new[] { currentUserSid, systemSid, adminsSid })
            {
                permissiveFileSecurity.AddAccessRule(new FileSystemAccessRule(
                    privilegedSid,
                    FileSystemRights.FullControl,
                    InheritanceFlags.None,
                    PropagationFlags.None,
                    AccessControlType.Allow));
            }
            permissiveFileSecurity.AddAccessRule(new FileSystemAccessRule(
                builtinUsersSid,
                FileSystemRights.ReadAndExecute,
                InheritanceFlags.None,
                PropagationFlags.None,
                AccessControlType.Allow));
            permissiveFileSecurity.AddAccessRule(new FileSystemAccessRule(
                authenticatedUsersSid,
                FileSystemRights.ReadAndExecute,
                InheritanceFlags.None,
                PropagationFlags.None,
                AccessControlType.Allow));
            new FileInfo(legacyPlanPath).SetAccessControl(permissiveFileSecurity);

            var recentTimestamp = DateTime.UtcNow.AddDays(-2);
            File.SetLastWriteTimeUtc(legacyPlanPath, recentTimestamp);

            var preSweepRules = GetFileRules(legacyPlanPath);
            Assert.IsTrue(preSweepRules.Any(r => builtinUsersSid.Equals(r.IdentityReference)));
            Assert.IsTrue(preSweepRules.Any(r => authenticatedUsersSid.Equals(r.IdentityReference)));

            using (var active = TerraformPlanStore.Reserve(NullLogger.Instance, _planRoot, 7702))
            {
                Assert.IsTrue(
                    File.Exists(legacyPlanPath),
                    "Unexpired legacy flat .tfplan file must be retained until OrphanedPlanRetention elapses.");

                var confinedAcl = new FileInfo(legacyPlanPath).GetAccessControl();
                Assert.IsTrue(confinedAcl.AreAccessRulesProtected, "Retained legacy flat .tfplan must receive a protected DACL on the first sweep.");

                var postSweepRules = GetFileRules(legacyPlanPath);
                Assert.IsFalse(postSweepRules.Any(r => r.IsInherited), "Retained legacy flat .tfplan must carry zero inherited rules.");
                Assert.IsFalse(postSweepRules.Any(r => builtinUsersSid.Equals(r.IdentityReference)), "BUILTIN\\Users read access must be stripped on the first sweep.");
                Assert.IsFalse(postSweepRules.Any(r => authenticatedUsersSid.Equals(r.IdentityReference)), "Authenticated Users read access must be stripped on the first sweep.");
                Assert.IsFalse(postSweepRules.Any(r => everyoneSid.Equals(r.IdentityReference)), "Everyone access must not be present.");

                foreach (var expectedHostSid in new[] { currentUserSid, systemSid, adminsSid })
                {
                    var hostRule = postSweepRules.Single(r => expectedHostSid.Equals(r.IdentityReference));
                    Assert.AreEqual(AccessControlType.Allow, hostRule.AccessControlType);
                    Assert.AreEqual(FileSystemRights.FullControl, hostRule.FileSystemRights);
                }

                var ownerRule = postSweepRules.Single(r => ownerRightsSid.Equals(r.IdentityReference));
                Assert.AreEqual(
                    FileSystemRights.ReadPermissions,
                    ownerRule.FileSystemRights & ~FileSystemRights.Synchronize);
                Assert.AreEqual(
                    (FileSystemRights)0,
                    ownerRule.FileSystemRights & (FileSystemRights.ReadData | FileSystemRights.ChangePermissions));

                // Host/store remains able to read the confined legacy flat .tfplan file before expiry.
                Assert.AreEqual(legacyPlanPayload, File.ReadAllText(legacyPlanPath, Encoding.UTF8));
                using (var stream = new FileStream(legacyPlanPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (var reader = new StreamReader(stream, Encoding.UTF8))
                {
                    Assert.AreEqual(legacyPlanPayload, reader.ReadToEnd());
                }

                active.Expire();
            }

            // Once the legacy flat .tfplan file ages past OrphanedPlanRetention, the sweep deletes it.
            File.SetLastWriteTimeUtc(
                legacyPlanPath,
                DateTime.UtcNow - TerraformPlanStore.OrphanedPlanRetention - TimeSpan.FromHours(1));

            using (var secondSweep = TerraformPlanStore.Reserve(NullLogger.Instance, _planRoot, 7703))
            {
                Assert.IsFalse(
                    File.Exists(legacyPlanPath),
                    "Expired legacy flat .tfplan file must be deleted once past OrphanedPlanRetention.");
                secondSweep.Expire();
            }
        }

        [TestMethod]
        [SupportedOSPlatform("windows")]
        public void SweepRefusesToRewriteDaclOnUnexpiredHardLinkOrReparseEntriesInPlanRoot()
        {
            if (!OperatingSystem.IsWindows())
            {
                return;
            }

            var builtinUsersSid = new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null);

            var externalFile = Path.Join(_scratchBase, "external-target.tfplan");
            File.WriteAllText(externalFile, "external-file-content");
            var externalFileAcl = new FileInfo(externalFile).GetAccessControl();
            externalFileAcl.AddAccessRule(new FileSystemAccessRule(
                builtinUsersSid,
                FileSystemRights.Read,
                InheritanceFlags.None,
                PropagationFlags.None,
                AccessControlType.Allow));
            new FileInfo(externalFile).SetAccessControl(externalFileAcl);

            var externalDir = Path.Join(_scratchBase, "external-dir");
            Directory.CreateDirectory(externalDir);
            var externalDirFile = Path.Join(externalDir, "secret.txt");
            File.WriteAllText(externalDirFile, "external-dir-content");

            // Plant both a multi-link hard link and a directory junction directly inside _planRoot younger than cutoff.
            var plantedHardLink = Path.Join(_planRoot, "7704.tfplan");
            TerraformPlanLifetimeTests.CreateHardLink(plantedHardLink, externalFile);

            var plantedJunction = Path.Join(_planRoot, "7705.tfplan");
            TerraformPlanLifetimeTests.CreateDirectoryLink(plantedJunction, externalDir);

            using (var active = TerraformPlanStore.Reserve(NullLogger.Instance, _planRoot, 7706))
            {
                var postSweepExternalFileAcl = new FileInfo(externalFile).GetAccessControl();
                Assert.IsFalse(
                    postSweepExternalFileAcl.AreAccessRulesProtected,
                    "Sweep must refuse to rewrite or protect DACL on a multi-link hard link's external target.");
                Assert.IsTrue(
                    GetFileRules(externalFile).Any(r => builtinUsersSid.Equals(r.IdentityReference)),
                    "External hard-link target must keep its original Users ACE.");
                Assert.AreEqual("external-file-content", File.ReadAllText(externalFile));
                Assert.AreEqual("external-dir-content", File.ReadAllText(externalDirFile));

                active.Expire();
            }
        }

        [SupportedOSPlatform("windows")]
        private static List<FileSystemAccessRule> GetFileRules(string filePath) =>
            new FileInfo(filePath)
                .GetAccessControl()
                .GetAccessRules(includeExplicit: true, includeInherited: true, typeof(SecurityIdentifier))
                .Cast<FileSystemAccessRule>()
                .ToList();
    }
}
