using Dorc.ApiModel;
using Dorc.ApiModel.MonitorRunnerApi;
using Dorc.Monitor.Pipes;
using Dorc.Monitor.Security;
using Microsoft.Extensions.Logging;
using NSubstitute;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;

namespace Dorc.Monitor.Tests
{
    /// <summary>
    /// Every local artefact of a deployment - the script group bundle, the Terraform working
    /// directory, and the Terraform plan staged beside it - is derived from resolved
    /// deployment properties, and every one of them was created under %ProgramData% with an
    /// inherited ACL that admits any authenticated user on the host.
    ///
    /// These cover the directory restriction itself, and the plan directory that had been left
    /// out of it: the plan file embeds the variable values the configuration was rendered with,
    /// and the rendering beside it spells them out in clear.
    /// </summary>
    [TestClass]
    [SupportedOSPlatform("windows")]
    public class DeploymentArtefactDirectoryTests
    {
        private string _scratch = null!;

        [TestInitialize]
        public void Setup()
        {
            if (!OperatingSystem.IsWindows())
            {
                Assert.Inconclusive("Windows ACLs are the subject of these tests.");
            }

            // Under the test output rather than the machine's temp directory, so a failed run
            // leaves its evidence with the run that produced it.
            _scratch = Path.Combine(AppContext.BaseDirectory, "artefact-acl-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_scratch);
        }

        [TestCleanup]
        public void Cleanup()
        {
            if (_scratch is not null && Directory.Exists(_scratch))
            {
                Directory.Delete(_scratch, recursive: true);
            }
        }

        private static AuthorizationRuleCollection RulesOf(string path) =>
            new DirectoryInfo(path)
                .GetAccessControl()
                .GetAccessRules(includeExplicit: true, includeInherited: true, typeof(SecurityIdentifier));

        private static bool IsProtected(string path) =>
            new DirectoryInfo(path).GetAccessControl().AreAccessRulesProtected;

        private static IEnumerable<FileSystemAccessRule> RulesFor(string path, SecurityIdentifier sid) =>
            RulesOf(path).Cast<FileSystemAccessRule>().Where(rule => rule.IdentityReference.Equals(sid));

        private static SecurityIdentifier Everyone => new(WellKnownSidType.WorldSid, null);

        private static SecurityIdentifier CurrentUser => WindowsIdentity.GetCurrent().User!;

        private static string CurrentAccountName => WindowsIdentity.GetCurrent().Name;

        [TestMethod]
        public void ARestrictedDirectoryInheritsNothingFromProgramData()
        {
            var path = Path.Combine(_scratch, "created");

            RestrictedDirectory.Ensure(path);

            Assert.IsTrue(Directory.Exists(path));
            Assert.IsTrue(IsProtected(path), "The DACL must be protected, or %ProgramData%'s Users ACE is inherited.");
            Assert.IsFalse(RulesOf(path).Cast<FileSystemAccessRule>().Any(rule => rule.IsInherited));
            Assert.IsEmpty(RulesFor(path, Everyone));
        }

        /// <summary>
        /// The artefacts are what actually has to be unreadable; the directory is only the
        /// means. Without inheritable ACEs a file created inside it carries no restriction of
        /// its own.
        /// </summary>
        [TestMethod]
        public void TheRestrictionIsInheritedByTheArtefactsWrittenIntoIt()
        {
            var path = Path.Combine(_scratch, "inheritable");

            RestrictedDirectory.Ensure(path);

            var writer = RulesFor(path, CurrentUser).Single();
            Assert.AreEqual(
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                writer.InheritanceFlags);
            Assert.AreEqual(FileSystemRights.FullControl, writer.FileSystemRights & FileSystemRights.FullControl);

            var artefact = Path.Combine(path, "artefact.json");
            File.WriteAllText(artefact, "{}");

            var artefactRules = new FileInfo(artefact)
                .GetAccessControl()
                .GetAccessRules(includeExplicit: true, includeInherited: true, typeof(SecurityIdentifier))
                .Cast<FileSystemAccessRule>()
                .ToList();
            Assert.IsEmpty(artefactRules.Where(rule => rule.IdentityReference.Equals(Everyone)));
            Assert.IsNotEmpty(artefactRules.Where(rule => rule.IdentityReference.Equals(CurrentUser)));
        }

        /// <summary>
        /// A directory an administrator deleted and re-created by hand is back to inheriting
        /// %ProgramData%. Applying the restriction only at creation would leave every artefact
        /// written afterwards readable by the host.
        /// </summary>
        [TestMethod]
        public void AnExistingDirectoryIsRestrictedAgainRatherThanLeftAsFound()
        {
            var path = Path.Combine(_scratch, "loosened");
            Directory.CreateDirectory(path);

            var loosened = new DirectoryInfo(path).GetAccessControl();
            loosened.SetAccessRuleProtection(isProtected: false, preserveInheritance: true);
            loosened.AddAccessRule(new FileSystemAccessRule(
                Everyone,
                FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                PropagationFlags.None,
                System.Security.AccessControl.AccessControlType.Allow));
            new DirectoryInfo(path).SetAccessControl(loosened);
            Assert.IsNotEmpty(RulesFor(path, Everyone), "The test has not reproduced the exposure it is about.");

            RestrictedDirectory.Ensure(path);

            Assert.IsTrue(IsProtected(path));
            Assert.IsEmpty(RulesFor(path, Everyone));
        }

        /// <summary>
        /// The Runner writes the plan as the deployment account, so that account has to be
        /// admitted - but only far enough to write its artefacts, not far enough to re-open the
        /// directory to the host by rewriting the ACL.
        /// </summary>
        [TestMethod]
        public void AnAdmittedDeploymentAccountGetsModifyAndNotOwnershipOfTheAcl()
        {
            var path = Path.Combine(_scratch, "admitted");
            var deploymentAccount = new SecurityIdentifier(WellKnownSidType.NetworkServiceSid, null);

            RestrictedDirectory.Ensure(path, deploymentAccount);

            var granted = RulesFor(path, deploymentAccount).Single();
            Assert.AreEqual(FileSystemRights.Modify, granted.FileSystemRights & FileSystemRights.Modify);
            Assert.AreNotEqual(
                FileSystemRights.ChangePermissions,
                granted.FileSystemRights & FileSystemRights.ChangePermissions);
            Assert.AreNotEqual(
                FileSystemRights.TakeOwnership,
                granted.FileSystemRights & FileSystemRights.TakeOwnership);
        }

        [TestMethod]
        public void ThePlanDirectoryIsRestrictedAndAdmitsTheAccountThatWritesThePlan()
        {
            var planDirectory = TerraformPlanStorage.EnsureRestricted(
                _scratch,
                new ScriptGroupReaderIdentity(domain: null, CurrentAccountName),
                Substitute.For<ILogger>());

            Assert.AreEqual(Path.Join(_scratch, "terraform-plans"), planDirectory);
            Assert.IsTrue(IsProtected(planDirectory));
            Assert.IsEmpty(RulesFor(planDirectory, Everyone));
            Assert.IsNotEmpty(RulesFor(planDirectory, CurrentUser));
        }

        /// <summary>
        /// The confinement is the directory ACL and it is applied regardless. Admitting the
        /// deployment account only ever WIDENS access, so a name the local authority cannot
        /// resolve - an unreachable domain controller, a host that is not joined - leaves the
        /// plan unwritable, which fails the deployment loudly, rather than leaving it readable.
        /// </summary>
        [TestMethod]
        public void APlanDirectoryIsStillRestrictedWhenTheDeploymentAccountCannotBeResolved()
        {
            var logger = Substitute.For<ILogger>();

            var planDirectory = TerraformPlanStorage.EnsureRestricted(
                _scratch,
                new ScriptGroupReaderIdentity("NO-SUCH-DOMAIN", $"no-such-account-{Guid.NewGuid():N}"),
                logger);

            Assert.IsTrue(IsProtected(planDirectory));
            Assert.IsEmpty(RulesFor(planDirectory, Everyone));
        }

        /// <summary>
        /// A deployment account misconfigured to a broad group would publish every plan staged
        /// on the host to it.
        /// </summary>
        [TestMethod]
        public void APlanDirectoryRefusesADeploymentAccountThatNamesTheWholeHost()
        {
            var everyoneAccountName = ((NTAccount)Everyone.Translate(typeof(NTAccount))).Value;

            var planDirectory = TerraformPlanStorage.EnsureRestricted(
                _scratch,
                new ScriptGroupReaderIdentity(domain: null, everyoneAccountName),
                Substitute.For<ILogger>());

            Assert.IsTrue(IsProtected(planDirectory));
            Assert.IsEmpty(RulesFor(planDirectory, Everyone));
        }
    }

    /// <summary>
    /// The bundle's file name is composed from HostInstanceId, which can be set from the
    /// environment, and it is not only written but DELETED. Nothing that reaches the writer
    /// today carries a separator - but the writer cannot check that its caller is trustworthy,
    /// only that the name it was given names one file in the directory it is meant to name it
    /// in.
    /// </summary>
    [TestClass]
    [SupportedOSPlatform("windows")]
    public class ScriptGroupBundleNameTests
    {
        private static ScriptGroupFileWriter NewWriter() =>
            new(Substitute.For<ILogger<ScriptGroupFileWriter>>());

        [TestMethod]
        [DataRow(@"..\..\Windows\System32\config\evil")]
        [DataRow("../../evil")]
        [DataRow(@"sub\DOrcMonitor-host-42")]
        [DataRow("sub/DOrcMonitor-host-42")]
        [DataRow(@"c:\Windows\Temp\DOrcMonitor-host-42")]
        [DataRow(@"\\other-host\share\DOrcMonitor-host-42")]
        [DataRow("..")]
        [DataRow("")]
        [DataRow("   ")]
        public void ABundleNameThatEscapesTheBundleDirectoryIsRefused(string pipeName)
        {
            Assert.ThrowsExactly<ArgumentException>(() => NewWriter().Start(
                pipeName,
                new ScriptGroup(),
                new ScriptGroupReaderIdentity("CORP", "svc-dorc"),
                CancellationToken.None));
        }

        /// <summary>
        /// Expire is called from a finally block on the deployment path. A name it refuses must
        /// leave the deployment's own outcome standing, not replace it with an argument
        /// failure raised while cleaning up.
        /// </summary>
        [TestMethod]
        public void ARefusedBundleNameDoesNotThrowOutOfExpiry()
        {
            NewWriter().Expire(@"..\..\evil");
        }
    }
}
