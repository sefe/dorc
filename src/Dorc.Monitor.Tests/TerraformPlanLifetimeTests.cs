using Dorc.ApiModel;
using Dorc.ApiModel.MonitorRunnerApi;
using Dorc.Core.AzureStorageAccount;
using Dorc.Core.BuildServer;
using Dorc.Core.Configuration;
using Dorc.Core.Security;
using Dorc.Core.Terraform;
using Dorc.Monitor.Pipes;
using Dorc.Monitor.RunnerProcess;
using Dorc.Monitor.Security;
using Dorc.Monitor.Terraform;
using Dorc.PersistentData.Model;
using Dorc.PersistentData.Security;
using Dorc.PersistentData.Sources;
using Dorc.PersistentData.Sources.Interfaces;
using Microsoft.Extensions.Logging;
using NSubstitute;
using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;

namespace Dorc.Monitor.Tests
{
    /// <summary>
    /// Covers per-result Terraform plan directory confinement (<see cref="RestrictedDirectory"/> +
    /// environment-bound <c>credential.QualifiedAccountName</c>), 7-day stale sweep, junction /
    /// reparse-point refusal, and full <see cref="TerraformDispatcher"/> plan and apply sidecar
    /// retention and expiry lifecycles.
    /// </summary>
    [TestClass]
    public class TerraformPlanLifetimeTests
    {
        private string _scratchBase = null!;
        private string _root = null!;
        private string _victim = null!;
        private ILogger _logger = null!;
        private readonly List<TerraformPlanStore> _trackedStores = new();

        private static readonly ScriptGroupReaderIdentity DeploymentIdentity =
            new("CORP", "svc-dorc-prod");

        [TestInitialize]
        public void Setup()
        {
            _scratchBase = Path.Join(AppContext.BaseDirectory, "dorc-plan-test-" + Guid.NewGuid().ToString("N"));
            _root = Path.Join(_scratchBase, "plans");
            _victim = Path.Join(_scratchBase, "external-victim");
            Directory.CreateDirectory(_victim);
            _logger = Substitute.For<ILogger>();
        }

        [TestCleanup]
        public void Cleanup()
        {
            foreach (var store in _trackedStores)
            {
                store.Dispose();
            }
            _trackedStores.Clear();
            RemoveScratchTreeSafely(_scratchBase);
        }

        private TerraformPlanStore Track(TerraformPlanStore store)
        {
            _trackedStores.Add(store);
            return store;
        }

        private TerraformPlanStore Reserve(int deploymentResultId) =>
            Track(TerraformPlanStore.Reserve(_logger, _root, deploymentResultId, DeploymentIdentity));

        /// <summary>
        /// The shared directory is what made restriction impossible: an access control list over it
        /// would have to admit every identity that deploys on the host. One directory per
        /// deployment dispatch is what lets a single identity be admitted, which is the whole reason
        /// this waited for the execution identity to become environment-dependent.
        /// </summary>
        [TestMethod]
        public void TwoDeploymentsDoNotShareAPlanDirectory()
        {
            var first = Reserve(4001);
            var second = Reserve(4002);

            Assert.AreNotEqual(first.Directory, second.Directory);
            Assert.IsTrue(Directory.Exists(first.Directory));
            Assert.IsTrue(Directory.Exists(second.Directory));
        }

        /// <summary>
        /// Plan and apply are separate dispatches whose environment ExecutionIdentity may change
        /// between creation and confirmation; ApplyPlan downloads the approved .tfplan and
        /// .tfplan.source.zip from Blob Storage rather than trusting leftover local files. Each
        /// dispatch therefore allocates its own fresh {resultId}-{nonce} directory.
        /// </summary>
        [TestMethod]
        public void ThePlanAndTheApplyOfOneDeploymentReceiveFreshIsolatedDirectories()
        {
            var planDispatch = Reserve(4003);
            var applyDispatch = Reserve(4003);

            Assert.AreNotEqual(planDispatch.Directory, applyDispatch.Directory);
            StringAssert.StartsWith(Path.GetFileName(planDispatch.Directory), "4003-");
            StringAssert.StartsWith(Path.GetFileName(applyDispatch.Directory), "4003-");
            Assert.IsTrue(Directory.Exists(planDispatch.Directory));
            Assert.IsTrue(Directory.Exists(applyDispatch.Directory));
        }

        [TestMethod]
        public void ArtefactsAreNamedInsideTheDeploymentsOwnDirectory()
        {
            var store = Reserve(4004);

            Assert.AreEqual(
                Path.Join(store.Directory, "4004.tfplan"),
                store.PathOf("4004.tfplan"));
        }

        /// <summary>
        /// Plan file names are derived from the deployment result identifier rather than supplied,
        /// so this is a guard rather than a live defence — but a name that escaped the directory
        /// would escape its access control list with it, which is exactly the confinement being
        /// bought.
        /// </summary>
        [TestMethod]
        [DataRow("../4005.tfplan")]
        [DataRow("..\\4005.tfplan")]
        [DataRow("nested/4005.tfplan")]
        [DataRow("4005.tfplan:stream")]
        [DataRow("")]
        [DataRow("   ")]
        public void AnArtefactNameThatIsNotABareFileNameIsRefused(string planFileName)
        {
            var store = Reserve(4005);

            Assert.ThrowsExactly<ArgumentException>(() => store.PathOf(planFileName));
        }

        [TestMethod]
        public void ExpiryRemovesTheArtefactsAndTheDirectoryTheyLiveIn()
        {
            var store = Reserve(4006);
            File.WriteAllText(store.PathOf("4006.tfplan"), "binary-plan");
            File.WriteAllText(store.PathOf("4006-content.txt"), "instance_password = \"secret\"");

            store.Expire();

            Assert.IsFalse(Directory.Exists(store.Directory));
        }

        [TestMethod]
        public void ExpiringTwiceIsNotAFailure()
        {
            var store = Reserve(4007);

            store.Expire();
            store.Expire();

            Assert.IsFalse(Directory.Exists(store.Directory));
        }

        [TestMethod]
        public void UnexpectedFailuresDuringExpiryAreNotHidden()
        {
            _logger = new ThrowingDebugLogger();
            var store = Reserve(4017);

            Assert.ThrowsExactly<InvalidOperationException>(() => store.Expire());
        }

        /// <summary>
        /// If blob upload failed, the retained dispatch directory holds the only recovery copy of
        /// that plan — a subsequent dispatch for the same result allocates a fresh directory while
        /// leaving the retained directory intact, and pre-existing dispatch directory names are
        /// refused outright.
        /// </summary>
        [TestMethod]
        public void SubsequentReserveLeavesPriorRetainedDirectoryAloneAndRefusesPreExistingDispatchName()
        {
            var first = Reserve(4008);
            var retained = first.PathOf("4008.tfplan");
            File.WriteAllText(retained, "the-only-copy");
            first.Dispose();

            var second = Reserve(4008);

            Assert.AreNotEqual(first.Directory, second.Directory);
            Assert.IsTrue(File.Exists(retained));
            Assert.AreEqual("the-only-copy", File.ReadAllText(retained));

            var collidingName = Path.GetFileName(first.Directory);
            Assert.ThrowsExactly<IOException>(() =>
                TerraformPlanStore.ReserveWithDirectoryName(_logger, _root, collidingName));
        }

        /// <summary>
        /// The accumulated backlog. Before this change plan artefacts sat directly in the shared
        /// root, so on every existing host that is where the history is; sweeping only the new
        /// per-deployment shape would leave every plan the host had ever produced exactly where it
        /// was.
        /// </summary>
        [TestMethod]
        public void TheBacklogOfPlansWrittenBeforeThisLayoutExistedIsSwept()
        {
            Directory.CreateDirectory(_root);
            var legacy = Path.Join(_root, "1234.tfplan");
            File.WriteAllText(legacy, "an-old-plan");
            File.SetLastWriteTimeUtc(legacy, DateTime.UtcNow.AddDays(-30));

            Reserve(4009);

            Assert.IsFalse(File.Exists(legacy));
        }

        [TestMethod]
        public void AnAbandonedDeploymentsPlanDirectoryIsSwept()
        {
            var abandoned = Reserve(4010);
            var artefact = abandoned.PathOf("4010.tfplan");
            File.WriteAllText(artefact, "abandoned");
            abandoned.Dispose();
            Age(abandoned.Directory, TimeSpan.FromDays(30));

            Reserve(4011);

            Assert.IsFalse(Directory.Exists(abandoned.Directory));
        }

        /// <summary>
        /// A plan awaiting confirmation is live. Retention exists to clear what a deployment left
        /// behind, not to sweep what another deployment is still using.
        /// </summary>
        [TestMethod]
        public void ARecentDeploymentsPlanDirectoryIsLeftWhereItIs()
        {
            var recent = Reserve(4012);
            File.WriteAllText(recent.PathOf("4012.tfplan"), "in-flight");
            recent.Dispose();

            Reserve(4013);

            Assert.IsTrue(Directory.Exists(recent.Directory));
        }

        [TestMethod]
        public void SevenDayRetentionBoundaryKeepsSixDayDirectoryAndSweepsEightDayDirectory()
        {
            var withinRetention = Reserve(4020);
            File.WriteAllText(withinRetention.PathOf("4020.tfplan"), "six-days-old");
            withinRetention.Dispose();
            Age(withinRetention.Directory, TimeSpan.FromDays(6));

            var beyondRetention = Reserve(4021);
            File.WriteAllText(beyondRetention.PathOf("4021.tfplan"), "eight-days-old");
            beyondRetention.Dispose();
            Age(beyondRetention.Directory, TimeSpan.FromDays(8));

            Reserve(4022);

            Assert.IsTrue(Directory.Exists(withinRetention.Directory));
            Assert.IsFalse(Directory.Exists(beyondRetention.Directory));
        }

        /// <summary>
        /// An active in-flight deployment holds a lease in ActiveDirectories until Dispose(), so
        /// RemoveOrphanedPlans never sweeps it even if a long-running deployment or host clock skew
        /// pushes its timestamps past the 7-day cutoff. Once the lease is released, a later sweep
        /// removes it normally.
        /// </summary>
        [TestMethod]
        public void AnActiveInFlightDeploymentDirectorySurvivesRetentionSweepEvenWhenAged()
        {
            var activeInFlight = Reserve(4014);
            var inFlightPlan = activeInFlight.PathOf("4014.tfplan");
            File.WriteAllText(inFlightPlan, "still-running");
            Age(activeInFlight.Directory, TimeSpan.FromDays(30));

            Reserve(4024);

            Assert.IsTrue(Directory.Exists(activeInFlight.Directory), "Active lease must protect in-flight dispatch directory from sweep.");
            Assert.IsTrue(File.Exists(inFlightPlan));

            activeInFlight.Dispose();
            Age(activeInFlight.Directory, TimeSpan.FromDays(30));

            Reserve(4025);

            Assert.IsFalse(Directory.Exists(activeInFlight.Directory), "Disposed directory older than retention must be swept.");
        }

        /// <summary>
        /// A directory's own write time does not move when a file inside it is written, so age has
        /// to be judged by the newest artefact rather than by the container.
        /// </summary>
        [TestMethod]
        public void ADirectoryHoldingARecentlyWrittenArtefactIsNotSwept()
        {
            var store = Reserve(4015);
            File.WriteAllText(store.PathOf("4015.tfplan"), "written-just-now");
            store.Dispose();
            Directory.SetLastWriteTimeUtc(store.Directory, DateTime.UtcNow.AddDays(-30));

            Reserve(4016);

            Assert.IsTrue(Directory.Exists(store.Directory));
        }

        [TestMethod]
        [SupportedOSPlatform("windows")]
        public void PlanDirectoryGrantsModifyToResolvedNamedExecutionIdentityAndIsolatesEnvironments()
        {
            if (!OperatingSystem.IsWindows())
            {
                return;
            }

            var configValues = Substitute.For<IConfigValuesPersistentSource>();
            configValues.GetConfigValue("DORC_Deploy_payments-env_Username").Returns("LOCAL SERVICE");
            configValues.GetConfigValue("DORC_Deploy_payments-env_Password").Returns("payments-secret");
            configValues.GetConfigValue("DORC_Deploy_trading-env_Username").Returns(@"NT AUTHORITY\NETWORK SERVICE");
            configValues.GetConfigValue("DORC_Deploy_trading-env_Password").Returns("trading-secret");

            var credentialSource = new ConfigValueDeploymentCredentialSource(
                configValues,
                Substitute.For<ILogger<ConfigValueDeploymentCredentialSource>>());
            var adoption = new RequestExecutionIdentityAdoption();

            var paymentsCredential = adoption.Resolve(credentialSource, DeploymentTier.Production, "payments-env")!;
            var tradingCredential = adoption.Resolve(credentialSource, DeploymentTier.Production, "trading-env")!;

            var paymentsQualified = paymentsCredential.QualifiedAccountName("NT AUTHORITY");
            var tradingQualified = tradingCredential.QualifiedAccountName("CORP");
            Assert.AreEqual(@"NT AUTHORITY\LOCAL SERVICE", paymentsQualified);
            Assert.AreEqual(@"NT AUTHORITY\NETWORK SERVICE", tradingQualified);

            var paymentsStore = Track(TerraformPlanStore.Reserve(_logger, _root, 4101, paymentsQualified));
            var tradingStore = Track(TerraformPlanStore.Reserve(_logger, _root, 4102, tradingQualified));

            var localServiceSid = new SecurityIdentifier(WellKnownSidType.LocalServiceSid, null);
            var networkServiceSid = new SecurityIdentifier(WellKnownSidType.NetworkServiceSid, null);
            var builtinUsersSid = new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null);
            var authenticatedUsersSid = new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null);

            // Root directory must be protected and exclude every deployment identity and host Users.
            var rootAcl = new DirectoryInfo(_root).GetAccessControl();
            Assert.IsTrue(rootAcl.AreAccessRulesProtected);
            var rootRules = rootAcl.GetAccessRules(true, true, typeof(SecurityIdentifier))
                .Cast<FileSystemAccessRule>()
                .ToList();
            Assert.IsFalse(rootRules.Any(r => localServiceSid.Equals(r.IdentityReference)));
            Assert.IsFalse(rootRules.Any(r => networkServiceSid.Equals(r.IdentityReference)));
            Assert.IsFalse(rootRules.Any(r => builtinUsersSid.Equals(r.IdentityReference)));
            Assert.IsFalse(rootRules.Any(r => authenticatedUsersSid.Equals(r.IdentityReference)));

            // Payments directory grants Modify | Synchronize (read + write + create + delete,
            // without ChangePermissions or TakeOwnership) ONLY to payments' execution identity.
            var paymentsAcl = new DirectoryInfo(paymentsStore.Directory).GetAccessControl();
            Assert.IsTrue(paymentsAcl.AreAccessRulesProtected);
            var paymentsRules = paymentsAcl.GetAccessRules(true, true, typeof(SecurityIdentifier))
                .Cast<FileSystemAccessRule>()
                .ToList();

            var paymentsRule = paymentsRules.Single(r => localServiceSid.Equals(r.IdentityReference));
            Assert.AreEqual(System.Security.AccessControl.AccessControlType.Allow, paymentsRule.AccessControlType);
            Assert.AreEqual(FileSystemRights.Modify | FileSystemRights.Synchronize, paymentsRule.FileSystemRights);
            Assert.AreEqual(
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                paymentsRule.InheritanceFlags);
            Assert.AreEqual((FileSystemRights)0, paymentsRule.FileSystemRights & FileSystemRights.ChangePermissions);
            Assert.AreEqual((FileSystemRights)0, paymentsRule.FileSystemRights & FileSystemRights.TakeOwnership);
            Assert.IsFalse(paymentsRules.Any(r => networkServiceSid.Equals(r.IdentityReference)));

            // Trading directory admits trading's identity and excludes payments' identity.
            var tradingRules = new DirectoryInfo(tradingStore.Directory)
                .GetAccessControl()
                .GetAccessRules(true, true, typeof(SecurityIdentifier))
                .Cast<FileSystemAccessRule>()
                .ToList();
            Assert.IsTrue(tradingRules.Any(r => networkServiceSid.Equals(r.IdentityReference)));
            Assert.IsFalse(tradingRules.Any(r => localServiceSid.Equals(r.IdentityReference)));

            // Files staged in the dispatch directory inherit Modify | Synchronize for the execution identity.
            var stagedPlan = paymentsStore.PathOf("4101.tfplan");
            File.WriteAllText(stagedPlan, "binary-plan");
            var fileRules = new FileInfo(stagedPlan)
                .GetAccessControl()
                .GetAccessRules(true, true, typeof(SecurityIdentifier))
                .Cast<FileSystemAccessRule>()
                .ToList();
            var inheritedRule = fileRules.Single(r => localServiceSid.Equals(r.IdentityReference));
            Assert.AreEqual(FileSystemRights.Modify | FileSystemRights.Synchronize, inheritedRule.FileSystemRights);
        }

        [TestMethod]
        [SupportedOSPlatform("windows")]
        public void BroadGroupIdentityIsRefusedWithoutOpeningPlanDirectoryToTheHost()
        {
            if (!OperatingSystem.IsWindows())
            {
                return;
            }

            var worldSid = new SecurityIdentifier(WellKnownSidType.WorldSid, null);
            var everyoneName = worldSid.Translate(typeof(NTAccount)).Value;

            var store = Track(TerraformPlanStore.Reserve(_logger, _root, 4103, everyoneName));

            var rules = new DirectoryInfo(store.Directory)
                .GetAccessControl()
                .GetAccessRules(true, true, typeof(SecurityIdentifier))
                .Cast<FileSystemAccessRule>()
                .ToList();
            Assert.IsFalse(rules.Any(r => worldSid.Equals(r.IdentityReference)));
        }

        [TestMethod]
        public void ReserveRefusesPreExistingReparsePointAtPlanDirectoryWithoutTouchingTarget()
        {
            Directory.CreateDirectory(_root);
            var victimFile = Path.Join(_victim, "untouched.txt");
            File.WriteAllText(victimFile, "do-not-touch");

            var plantedReparse = Path.Join(_root, "4201-preexisting");
            CreateDirectoryLink(plantedReparse, _victim);

            Assert.ThrowsExactly<IOException>(() =>
                TerraformPlanStore.ReserveWithDirectoryName(_logger, _root, "4201-preexisting"));
            Assert.IsTrue(File.Exists(victimFile));
            Assert.AreEqual("do-not-touch", File.ReadAllText(victimFile));
        }

        [TestMethod]
        public void OpenVerifiedReadStageAndCancelSignalRefusePlantedJunctionWithoutTouchingExternalTarget()
        {
            var victimFile = Path.Join(_victim, "secret-target.txt");
            File.WriteAllText(victimFile, "external-secret");

            var store = Reserve(4205);
            CreateDirectoryLink(store.PathOf("4205.tfplan"), _victim);
            CreateDirectoryLink(store.PathOf("4205.tfplan.cancel"), _victim);

            Assert.ThrowsExactly<IOException>(() => store.OpenVerifiedRead("4205.tfplan"));
            Assert.ThrowsExactly<IOException>(() => store.StageVerifiedArtefact("4205.tfplan"));
            Assert.ThrowsExactly<IOException>(() => store.TryStageVerifiedArtefact("4205.tfplan", out _));
            Assert.ThrowsExactly<IOException>(() => store.WriteCancelSignal("4205.tfplan.cancel"));

            // Normal regular files in the same store work via verified I/O.
            File.WriteAllText(store.PathOf("4205-content.txt"), "safe-plan-text");
            var stagedPath = store.StageVerifiedArtefact("4205-content.txt");
            Assert.AreEqual("safe-plan-text", File.ReadAllText(stagedPath));

            store.WriteCancelSignal("4205-valid.cancel");
            Assert.IsTrue(File.Exists(store.PathOf("4205-valid.cancel")));
            Assert.AreEqual(0L, new FileInfo(store.PathOf("4205-valid.cancel")).Length);

            Assert.IsTrue(File.Exists(victimFile));
            Assert.AreEqual("external-secret", File.ReadAllText(victimFile));
        }

        [TestMethod]
        public void ExpireUnlinksPlantedJunctionAndDeletesPlanDirectoryWithoutTouchingExternalTarget()
        {
            var victimFile = Path.Join(_victim, "precious.txt");
            File.WriteAllText(victimFile, "keep-safe");

            var store = Reserve(4202);
            var secretPlan = store.PathOf("4202.tfplan");
            var secretText = store.PathOf("4202-content.txt");
            File.WriteAllText(secretPlan, "plan-bytes");
            File.WriteAllText(secretText, "db_password = \"s3cr3t\"");

            var childLink = Path.Join(store.Directory, "escape-junction");
            CreateDirectoryLink(childLink, _victim);

            store.Expire();

            Assert.IsFalse(Directory.Exists(childLink), "Planted junction link itself must be unlinked.");
            Assert.IsFalse(Directory.Exists(store.Directory), "Plan directory must be completely removed after unlinking child junction.");
            Assert.IsTrue(File.Exists(victimFile), "External junction target file must survive intact during Expire.");
            Assert.AreEqual("keep-safe", File.ReadAllText(victimFile));
        }

        [TestMethod]
        public void StaleSweepUnlinksNestedJunctionAndDeletesStaleDirectoryWithoutDeletingTargetFiles()
        {
            var victimFile = Path.Join(_victim, "survives-sweep.txt");
            File.WriteAllText(victimFile, "protected-content");

            var staleStore = Reserve(4203);
            var stalePlan = staleStore.PathOf("4203.tfplan");
            File.WriteAllText(stalePlan, "stale-plan");
            staleStore.Dispose();
            Age(staleStore.Directory, TimeSpan.FromDays(30));

            var nestedLink = Path.Join(staleStore.Directory, "nested-escape");
            CreateDirectoryLink(nestedLink, _victim);
            Directory.SetLastWriteTimeUtc(staleStore.Directory, DateTime.UtcNow.AddDays(-30));

            var topLevelLink = Path.Join(_root, "9998");
            CreateDirectoryLink(topLevelLink, _victim);

            Reserve(4204);

            Assert.IsFalse(Directory.Exists(staleStore.Directory), "Stale plan directory should be completely swept after unlinking nested junction.");
            Assert.IsTrue(File.Exists(victimFile), "Target of top-level and nested junctions must survive sweep.");
            Assert.AreEqual("protected-content", File.ReadAllText(victimFile));
        }

        [TestMethod]
        [SupportedOSPlatform("windows")]
        public void RevokeRunnerRecursivelyStripsRunnerAndPreservesOwnerRightsReadOnlyOnNestedDirectoriesAndFiles()
        {
            if (!OperatingSystem.IsWindows())
            {
                return;
            }

            var store = Track(TerraformPlanStore.Reserve(_logger, _root, 4206, @"NT AUTHORITY\LOCAL SERVICE"));
            var admittedAccountSid = new SecurityIdentifier(WellKnownSidType.LocalServiceSid, null);
            var ownerRightsSid = new SecurityIdentifier("S-1-3-4");

            var nestedDir = Path.Join(store.Directory, "modules", "child");
            Directory.CreateDirectory(nestedDir);
            var nestedFile = Path.Join(nestedDir, "secret.tfvars");
            File.WriteAllText(nestedFile, "token = \"abc\"");

            store.RevokeRunner();

            foreach (var dirPath in new[] { store.Directory, Path.Join(store.Directory, "modules"), nestedDir })
            {
                var rules = new DirectoryInfo(dirPath)
                    .GetAccessControl()
                    .GetAccessRules(true, true, typeof(SecurityIdentifier))
                    .Cast<FileSystemAccessRule>()
                    .ToList();
                Assert.IsFalse(rules.Any(r => admittedAccountSid.Equals(r.IdentityReference)), $"Runner ACE must be revoked on '{dirPath}'.");
                var ownerRights = rules.Single(r => ownerRightsSid.Equals(r.IdentityReference));
                Assert.AreEqual(FileSystemRights.ReadPermissions, ownerRights.FileSystemRights & FileSystemRights.ReadPermissions);
                Assert.AreEqual((FileSystemRights)0, ownerRights.FileSystemRights & FileSystemRights.ChangePermissions);
            }

            var fileRules = new FileInfo(nestedFile)
                .GetAccessControl()
                .GetAccessRules(true, true, typeof(SecurityIdentifier))
                .Cast<FileSystemAccessRule>()
                .ToList();
            Assert.IsFalse(fileRules.Any(r => admittedAccountSid.Equals(r.IdentityReference)));
            var fileOwnerRights = fileRules.Single(r => ownerRightsSid.Equals(r.IdentityReference));
            Assert.AreEqual(FileSystemRights.ReadPermissions, fileOwnerRights.FileSystemRights & FileSystemRights.ReadPermissions);
            Assert.AreEqual((FileSystemRights)0, fileOwnerRights.FileSystemRights & FileSystemRights.ChangePermissions);
        }

        [TestMethod]
        [SupportedOSPlatform("windows")]
        public void PinnedNtfsFileIdRefusesImposterDirectoryAtSamePath()
        {
            if (!OperatingSystem.IsWindows())
            {
                return;
            }

            var store = Track(TerraformPlanStore.Reserve(_logger, _root, 4207));
            Assert.ThrowsExactly<IOException>(() => Directory.Delete(store.Directory, recursive: true));

            store.ReleasePinHandlesForTesting();
            Directory.Delete(store.Directory, recursive: true);
            Directory.CreateDirectory(store.Directory);
            var imposterFile = Path.Join(store.Directory, "4207.tfplan");
            File.WriteAllText(imposterFile, "imposter-plan");

            Assert.ThrowsExactly<IOException>(() => store.AdmitRunner(@"NT AUTHORITY\LOCAL SERVICE"));
            Assert.ThrowsExactly<IOException>(() => store.StageVerifiedArtefact("4207.tfplan"));
            Assert.ThrowsExactly<IOException>(() => store.WriteCancelSignal("4207.cancel"));
            Assert.ThrowsExactly<IOException>(() => store.RevokeRunnerAccessWindows());

            store.Expire();
            Assert.IsTrue(File.Exists(imposterFile), "Expire must refuse to delete a swapped directory whose NTFS FileId differs.");
        }

        [TestMethod]
        [SupportedOSPlatform("windows")]
        public void ActivePlanStoreBlocksDirectoryAndRootRenameWhileAllowingConcurrentStoresAndCleanExpiration()
        {
            if (!OperatingSystem.IsWindows())
            {
                return;
            }

            var store = Track(TerraformPlanStore.Reserve(_logger, _root, 4208, @"NT AUTHORITY\LOCAL SERVICE"));
            var secondStore = Track(TerraformPlanStore.Reserve(_logger, _root, 4209, @"NT AUTHORITY\LOCAL SERVICE"));

            File.WriteAllText(store.PathOf("4208.tfplan"), "plan-bytes");
            var stagedPath = store.StageVerifiedArtefact("4208.tfplan");
            Assert.AreEqual("plan-bytes", File.ReadAllText(stagedPath));
            store.RevokeRunner();

            Assert.ThrowsExactly<IOException>(() =>
                Directory.Move(store.Directory, Path.Join(_scratchBase, "moved-dispatch")));
            Assert.ThrowsExactly<IOException>(() =>
                Directory.Move(_root, Path.Join(_scratchBase, "moved-root")));

            store.Expire();
            store.Dispose();

            Assert.IsFalse(Directory.Exists(store.Directory), "Expire + Dispose must cleanly delete the dispatch directory.");
            Assert.IsFalse(File.Exists(stagedPath), "Dispose must cleanly delete the Monitor staging directory.");

            // Concurrent second store still holds its root pin handle.
            Assert.ThrowsExactly<IOException>(() =>
                Directory.Move(_root, Path.Join(_scratchBase, "moved-root-while-second-active")));

            secondStore.Expire();
            secondStore.Dispose();

            Assert.IsFalse(Directory.Exists(secondStore.Directory));

            var movedRoot = Path.Join(_scratchBase, "moved-root-after-all-disposed");
            Directory.Move(_root, movedRoot);
            Assert.IsTrue(Directory.Exists(movedRoot));
        }

        [TestMethod]
        [SupportedOSPlatform("windows")]
        public void HardLinksAtCancelSignalAndInsideWorkDirectoryAreRefusedWithoutTruncatingOrRewritingTargetDacl()
        {
            if (!OperatingSystem.IsWindows())
            {
                return;
            }

            var cancelVictimFile = Path.Join(_victim, "external-cancel-target.txt");
            File.WriteAllText(cancelVictimFile, "critical-external-config");

            var workVictimFile = Path.Join(_victim, "external-work-target.txt");
            File.WriteAllText(workVictimFile, "external-state-content");

            var localServiceSid = new SecurityIdentifier(WellKnownSidType.LocalServiceSid, null);
            var markerSid = new SecurityIdentifier(WellKnownSidType.NetworkServiceSid, null);

            // Keep workVictimFile's DACL unprotected (inheriting from _victim) with an explicit
            // marker ACE so any directory DACL auto-propagation or file DACL rewrite is detected.
            var initialVictimAcl = new FileInfo(workVictimFile).GetAccessControl();
            Assert.IsFalse(initialVictimAcl.AreAccessRulesProtected);
            initialVictimAcl.AddAccessRule(new FileSystemAccessRule(
                markerSid,
                FileSystemRights.Read,
                InheritanceFlags.None,
                PropagationFlags.None,
                System.Security.AccessControl.AccessControlType.Allow));
            new FileInfo(workVictimFile).SetAccessControl(initialVictimAcl);
            File.SetAttributes(workVictimFile, File.GetAttributes(workVictimFile) | FileAttributes.ReadOnly);

            var store = Track(TerraformPlanStore.Reserve(_logger, _root, 4299, @"NT AUTHORITY\LOCAL SERVICE"));
            var workDir = Path.Join(store.Directory, "work");
            Directory.CreateDirectory(workDir);
            var legitWorkFile = Path.Join(workDir, "legit-output.txt");
            File.WriteAllText(legitWorkFile, "runner-output");

            var cancelLinkPath = store.PathOf("4299.cancel");
            CreateHardLink(cancelLinkPath, cancelVictimFile);

            var workHardLinkPath = Path.Join(workDir, "planted-hardlink.txt");
            CreateHardLink(workHardLinkPath, workVictimFile);

            // 1. Cancel-signal write and overwrite-mode OpenVerifiedRegularFileForWrite must both
            // refuse the hard link BEFORE truncating the external target file.
            Assert.ThrowsExactly<IOException>(() => store.WriteCancelSignal("4299.cancel"));
            Assert.AreEqual("critical-external-config", File.ReadAllText(cancelVictimFile));

            Assert.ThrowsExactly<IOException>(() =>
            {
                using var _ = ReparseFreeHandles.OpenVerifiedRegularFileForWrite(
                    cancelLinkPath,
                    cancelLinkPath,
                    overwriteExisting: true);
            });
            Assert.AreEqual("critical-external-config", File.ReadAllText(cancelVictimFile));

            // 2. Verified artefact read/staging must also refuse a multiply-linked file.
            Assert.ThrowsExactly<IOException>(() => store.StageVerifiedArtefact("4299.cancel"));

            // 3. Post-run DACL lockdown must revoke Runner access on legitimate single-link
            // siblings, refuse the planted hard links, and NEVER rewrite or propagate DACLs
            // onto either external target file.
            Assert.ThrowsExactly<IOException>(() => store.RevokeRunnerAccessWindows());

            var legitRules = new FileInfo(legitWorkFile)
                .GetAccessControl()
                .GetAccessRules(true, true, typeof(SecurityIdentifier))
                .Cast<FileSystemAccessRule>()
                .ToList();
            Assert.IsFalse(
                legitRules.Any(r => localServiceSid.Equals(r.IdentityReference)),
                "Legitimate single-link sibling inside work\\ must still have Runner access revoked.");

            var postRevokeVictimAcl = new FileInfo(workVictimFile).GetAccessControl();
            Assert.IsFalse(
                postRevokeVictimAcl.AreAccessRulesProtected,
                "Directory DACL lockdown on work\\ must not propagate or protect the external target file's DACL.");
            var postRevokeVictimRules = postRevokeVictimAcl
                .GetAccessRules(true, true, typeof(SecurityIdentifier))
                .Cast<FileSystemAccessRule>()
                .ToList();
            Assert.IsTrue(
                postRevokeVictimRules.Any(r => markerSid.Equals(r.IdentityReference)),
                "External hard-link target must retain its original marker ACE.");
            Assert.AreEqual("external-state-content", File.ReadAllText(workVictimFile));

            // 4. Expire must unlink the dispatch directory and planted hard links without
            // deleting, truncating, clearing ReadOnly on, or rewriting DACLs of external targets.
            store.Expire();
            Assert.IsFalse(Directory.Exists(store.Directory));
            Assert.IsTrue(File.Exists(cancelVictimFile));
            Assert.AreEqual("critical-external-config", File.ReadAllText(cancelVictimFile));
            Assert.IsTrue(File.Exists(workVictimFile));
            Assert.AreEqual("external-state-content", File.ReadAllText(workVictimFile));
            Assert.AreNotEqual(
                (FileAttributes)0,
                File.GetAttributes(workVictimFile) & FileAttributes.ReadOnly,
                "Unlinking a planted hard link must not strip ReadOnly from the external target inode.");
            Assert.IsFalse(new FileInfo(workVictimFile).GetAccessControl().AreAccessRulesProtected);

            File.SetAttributes(workVictimFile, File.GetAttributes(workVictimFile) & ~FileAttributes.ReadOnly);
        }

        [SupportedOSPlatform("windows")]
        [DllImport("kernel32.dll", EntryPoint = "CreateHardLinkW", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CreateHardLinkNative(
            string lpFileName,
            string lpExistingFileName,
            IntPtr lpSecurityAttributes);

        [SupportedOSPlatform("windows")]
        internal static void CreateHardLink(string linkPath, string targetFilePath)
        {
            if (!CreateHardLinkNative(linkPath, targetFilePath, IntPtr.Zero))
            {
                throw new IOException(
                    $"CreateHardLinkW failed for '{linkPath}' -> '{targetFilePath}' (Win32 error {Marshal.GetLastWin32Error()}).");
            }
        }

        [TestMethod]
        [SupportedOSPlatform("windows")]
        public void ReserveUnderProgramDataRootHardensParentDaclAndStripsChildDeleteFromNonPrivilegedPrincipals()
        {
            if (!OperatingSystem.IsWindows())
            {
                return;
            }

            var simulatedProgramDataDorc = Path.Join(_scratchBase, "dorc");
            Directory.CreateDirectory(simulatedProgramDataDorc);

            var authenticatedUsersSid = new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null);
            var everyoneSid = new SecurityIdentifier(WellKnownSidType.WorldSid, null);
            var ownerRightsSid = new SecurityIdentifier(RestrictedDirectory.OwnerRightsSidValue);

            var permissiveAcl = new DirectoryInfo(simulatedProgramDataDorc).GetAccessControl();
            permissiveAcl.AddAccessRule(new FileSystemAccessRule(
                authenticatedUsersSid,
                FileSystemRights.Modify | FileSystemRights.Synchronize,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                PropagationFlags.None,
                System.Security.AccessControl.AccessControlType.Allow));
            permissiveAcl.AddAccessRule(new FileSystemAccessRule(
                everyoneSid,
                FileSystemRights.DeleteSubdirectoriesAndFiles | FileSystemRights.Delete,
                InheritanceFlags.None,
                PropagationFlags.None,
                System.Security.AccessControl.AccessControlType.Allow));
            new DirectoryInfo(simulatedProgramDataDorc).SetAccessControl(permissiveAcl);

            var planRoot = Path.Join(simulatedProgramDataDorc, TerraformPlanStore.DirectoryName);
            var store = Track(TerraformPlanStore.ReserveWithDirectoryName(
                _logger,
                planRoot,
                "4210-hardened",
                simulatedProgramDataDorc));

            var hardenedSecurity = new DirectoryInfo(simulatedProgramDataDorc).GetAccessControl();
            Assert.IsTrue(hardenedSecurity.AreAccessRulesProtected, "ProgramData\\dorc DACL must be protected.");

            var rules = hardenedSecurity
                .GetAccessRules(includeExplicit: true, includeInherited: true, typeof(SecurityIdentifier))
                .Cast<FileSystemAccessRule>()
                .ToList();
            Assert.IsFalse(rules.Any(r => r.IsInherited), "ProgramData\\dorc must carry zero inherited ACEs.");
            Assert.IsFalse(rules.Any(r => everyoneSid.Equals(r.IdentityReference)), "Everyone ACE must be removed.");

            var authUsersRule = rules.Single(r => authenticatedUsersSid.Equals(r.IdentityReference));
            Assert.AreEqual(InheritanceFlags.None, authUsersRule.InheritanceFlags);
            Assert.AreEqual(PropagationFlags.None, authUsersRule.PropagationFlags);
            Assert.AreEqual(RestrictedDirectory.ProgramDataRootAuthenticatedUsersRights, authUsersRule.FileSystemRights);
            Assert.AreEqual((FileSystemRights)0, authUsersRule.FileSystemRights & FileSystemRights.DeleteSubdirectoriesAndFiles);
            Assert.AreEqual((FileSystemRights)0, authUsersRule.FileSystemRights & FileSystemRights.Delete);
            Assert.AreEqual((FileSystemRights)0, authUsersRule.FileSystemRights & FileSystemRights.ChangePermissions);
            Assert.AreEqual((FileSystemRights)0, authUsersRule.FileSystemRights & FileSystemRights.TakeOwnership);
            Assert.AreEqual((FileSystemRights)0, authUsersRule.FileSystemRights & FileSystemRights.CreateFiles);

            var ownerRightsRule = rules.Single(r => ownerRightsSid.Equals(r.IdentityReference));
            Assert.AreEqual(
                FileSystemRights.ReadPermissions,
                ownerRightsRule.FileSystemRights & ~FileSystemRights.Synchronize);
            Assert.AreEqual(
                (FileSystemRights)0,
                ownerRightsRule.FileSystemRights & FileSystemRights.ChangePermissions);

            // Open handle on planRoot also prevents renaming its ancestor simulatedProgramDataDorc.
            Assert.ThrowsExactly<IOException>(() =>
                Directory.Move(simulatedProgramDataDorc, Path.Join(_scratchBase, "dorc-renamed")));

            store.Expire();
            store.Dispose();
            Assert.IsFalse(Directory.Exists(store.Directory));
        }

        [TestMethod]
        public void ExpireDeletesReadOnlyFilesAndSubdirectoriesViaHandle()
        {
            var store = Reserve(4208);
            var readOnlySubDir = Path.Join(store.Directory, "ro-sub");
            Directory.CreateDirectory(readOnlySubDir);
            var readOnlyFile = Path.Join(readOnlySubDir, "4208.tfplan");
            File.WriteAllText(readOnlyFile, "readonly-plan");

            File.SetAttributes(readOnlyFile, File.GetAttributes(readOnlyFile) | FileAttributes.ReadOnly);
            var dirInfo = new DirectoryInfo(readOnlySubDir);
            dirInfo.Attributes |= FileAttributes.ReadOnly;

            store.Expire();

            Assert.IsFalse(Directory.Exists(store.Directory));
        }

        [TestMethod]
        public void ExpireRefusesWhileSubdirectoryHasOpenWriteHandleAndNeverUnlinksExternalNestedJunction()
        {
            if (!OperatingSystem.IsWindows())
            {
                return;
            }

            var externalContainer = Path.Join(_scratchBase, "external-container");
            var externalLeafTarget = Path.Join(_scratchBase, "external-leaf-target");
            Directory.CreateDirectory(externalContainer);
            Directory.CreateDirectory(externalLeafTarget);
            var externalNestedJunction = Path.Join(externalContainer, "nested-junction");
            CreateDirectoryLink(externalNestedJunction, externalLeafTarget);

            var store = Reserve(4209);
            var subDir = Path.Join(store.Directory, "held-sub");
            Directory.CreateDirectory(subDir);
            var plantedJunction = Path.Join(store.Directory, "in-slot-junction");
            CreateDirectoryLink(plantedJunction, externalContainer);

            // Simulate a concurrent runner process holding a FILE_WRITE_DATA handle on subDir.
            using (var heldWriteHandle = Dorc.Monitor.Security.ReparseFreeHandles.OpenRawHandle(
                subDir,
                Dorc.Monitor.Security.ReparseFreeHandles.GenericWrite,
                Dorc.Monitor.Security.ReparseFreeHandles.FileShareRead
                | Dorc.Monitor.Security.ReparseFreeHandles.FileShareWrite
                | Dorc.Monitor.Security.ReparseFreeHandles.FileShareDelete,
                Dorc.Monitor.Security.ReparseFreeHandles.OpenExisting,
                out var err))
            {
                Assert.IsFalse(heldWriteHandle.IsInvalid, $"Expected open write handle on subDir (err={err}).");

                // Expire fails closed on the held write handle without touching externalNestedJunction.
                store.Expire();
                Assert.IsTrue(Directory.Exists(subDir));
                Assert.IsTrue((new DirectoryInfo(externalNestedJunction).Attributes & FileAttributes.ReparsePoint) != 0);
                Assert.IsTrue(Directory.Exists(externalLeafTarget));
            }

            // Once the concurrent write handle is closed, Expire removes the slot cleanly and leaves external junctions untouched.
            store.Expire();

            Assert.IsFalse(Directory.Exists(store.Directory));
            Assert.AreNotEqual((FileAttributes)0, new DirectoryInfo(externalNestedJunction).Attributes & FileAttributes.ReparsePoint);
            Assert.IsTrue(Directory.Exists(externalLeafTarget));
        }

        internal static void CreateDirectoryLink(string linkPath, string targetPath)
        {
            if (OperatingSystem.IsWindows())
            {
                using var process = Process.Start(new ProcessStartInfo
                {
                    FileName = System.Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe",
                    Arguments = $"/c mklink /J \"{linkPath}\" \"{targetPath}\"",
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                })!;
                process.WaitForExit();
                if (process.ExitCode != 0)
                {
                    throw new InvalidOperationException($"mklink /J failed: {process.StandardError.ReadToEnd()}");
                }
            }
            else
            {
                Directory.CreateSymbolicLink(linkPath, targetPath);
            }
        }

        internal static void RemoveScratchTreeSafely(string rootPath)
        {
            if (string.IsNullOrWhiteSpace(rootPath) || !Directory.Exists(rootPath))
            {
                return;
            }

            UnlinkReparsePoints(new DirectoryInfo(rootPath));
            Directory.Delete(rootPath, recursive: true);
        }

        private static void UnlinkReparsePoints(DirectoryInfo directory)
        {
            if ((directory.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                Directory.Delete(directory.FullName, recursive: false);
                return;
            }

            foreach (var entry in directory.EnumerateFileSystemInfos())
            {
                if ((entry.Attributes & FileAttributes.ReparsePoint) != 0)
                {
                    if (entry is DirectoryInfo)
                    {
                        Directory.Delete(entry.FullName, recursive: false);
                    }
                    else
                    {
                        entry.Delete();
                    }
                }
                else if (entry is DirectoryInfo subDir)
                {
                    UnlinkReparsePoints(subDir);
                }
            }
        }

        private static void Age(string directory, TimeSpan by)
        {
            var when = DateTime.UtcNow - by;

            foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
            {
                File.SetLastWriteTimeUtc(file, when);
            }

            Directory.SetLastWriteTimeUtc(directory, when);
        }

        private sealed class ThrowingDebugLogger : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                if (logLevel == LogLevel.Debug)
                {
                    throw new InvalidOperationException("Unexpected logger failure.");
                }
            }
        }
    }

    /// <summary>
    /// End-to-end <see cref="TerraformDispatcher"/> verification of plan and apply sidecar
    /// confinement, post-runner ingestion lifetime, expiry after full completion, and failure
    /// retention.
    /// </summary>
    [TestClass]
    public class TerraformDispatcherPlanConfinementTests
    {
        private string _planRoot = null!;
        private string _sharedScriptRoot = null!;
        private IRequestsPersistentSource _requests = null!;
        private IConfigValuesPersistentSource _configValues = null!;
        private IConfigurationSettings _settings = null!;
        private IDeploymentRequestProcessesPersistentSource _processes = null!;
        private IScriptGroupPipeServer _pipeServer = null!;
        private IAzureStorageAccountWorker _blobs = null!;
        private IProjectsPersistentSource _projects = null!;
        private IGitHubHostValidator _gitHubValidator = null!;
        private ICloudResourcesPersistentSource _cloudResources = null!;
        private ICloudResourceAuditPersistentSource _cloudAudit = null!;
        private IEnvironmentsPersistentSource _environments = null!;
        private IDatabasesPersistentSource _databases = null!;
        private IPropertiesPersistentSource _properties = null!;
        private IScriptScopeConfigValues _scriptScopeConfig = null!;
        private IDeploymentCredentialSource _credentialSource = null!;
        private ISourceHostAllowList _sourceHostAllowList = null!;

        [TestInitialize]
        public void Setup()
        {
            var baseDir = Path.Join(AppContext.BaseDirectory, "dorc-dispatcher-plan-test-" + Guid.NewGuid().ToString("N"));
            _planRoot = Path.Join(baseDir, "terraform-plans");
            _sharedScriptRoot = Path.Join(baseDir, "scripts");
            Directory.CreateDirectory(_sharedScriptRoot);

            _requests = Substitute.For<IRequestsPersistentSource>();
            _configValues = Substitute.For<IConfigValuesPersistentSource>();
            _settings = Substitute.For<IConfigurationSettings>();
            _processes = Substitute.For<IDeploymentRequestProcessesPersistentSource>();
            _pipeServer = Substitute.For<IScriptGroupPipeServer>();
            _blobs = Substitute.For<IAzureStorageAccountWorker>();
            _projects = Substitute.For<IProjectsPersistentSource>();
            _gitHubValidator = Substitute.For<IGitHubHostValidator>();
            _cloudResources = Substitute.For<ICloudResourcesPersistentSource>();
            _cloudAudit = Substitute.For<ICloudResourceAuditPersistentSource>();
            _environments = Substitute.For<IEnvironmentsPersistentSource>();
            _databases = Substitute.For<IDatabasesPersistentSource>();
            _properties = Substitute.For<IPropertiesPersistentSource>();
            _scriptScopeConfig = Substitute.For<IScriptScopeConfigValues>();
            _sourceHostAllowList = Substitute.For<ISourceHostAllowList>();

            _settings.GetConfigurationDomainNameIntra().Returns("NT AUTHORITY");
            _configValues.GetConfigValue(PropertyValueScopeOptionsFixed.ScriptRoot, string.Empty).Returns(_sharedScriptRoot);
            _configValues.GetAllConfigValues(Arg.Any<bool>()).Returns(Array.Empty<ConfigValueApiModel>());
            _properties.GetAllProperties().Returns(Array.Empty<PropertyApiModel>());
            _pipeServer.Start(
                    Arg.Any<string>(),
                    Arg.Any<ScriptGroup>(),
                    Arg.Any<ScriptGroupReaderIdentity>(),
                    Arg.Any<CancellationToken>())
                .Returns(Task.CompletedTask);

            _configValues.GetConfigValue("DORC_Deploy_payments-env_Username").Returns("LOCAL SERVICE");
            _configValues.GetConfigValue("DORC_Deploy_payments-env_Password").Returns("payments-secret");
            _credentialSource = new ConfigValueDeploymentCredentialSource(
                _configValues,
                Substitute.For<ILogger<ConfigValueDeploymentCredentialSource>>());

            _environments.GetEnvironment("Payments Prod").Returns(new EnvironmentApiModel
            {
                EnvironmentId = 77,
                EnvironmentName = "Payments Prod"
            });

            _cloudResources.ExecuteMutation(Arg.Any<Func<int>>())
                .Returns(call => call.Arg<Func<int>>()());
            _cloudResources.GetByIdentity(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>())
                .Returns(Array.Empty<CloudResourceApiModel>());
            _cloudResources.Add(Arg.Any<CloudResourceApiModel>())
                .Returns(call =>
                {
                    var model = call.Arg<CloudResourceApiModel>();
                    model.Id = 901;
                    return model;
                });
            _cloudResources.AttachToEnvironment(Arg.Any<int>(), Arg.Any<int>())
                .Returns(EnvironmentAttachmentOutcome.Attached);
        }

        [TestCleanup]
        public void Cleanup()
        {
            var baseDir = Path.GetDirectoryName(_planRoot);
            if (baseDir is not null)
            {
                TerraformPlanLifetimeTests.RemoveScratchTreeSafely(baseDir);
            }
        }

        [TestMethod]
        public void PlanDispatchKeepsPlanDirectoryAliveThroughSidecarUploadsAndReceiptThenExpires()
        {
            const int requestId = 500;
            const int resultId = 5001;
            var (component, result) = SeedSubmittedRequest(requestId, resultId, "tf-comp-plan-ok");
            var uploadedFiles = new List<string>();
            TerraformDeploymentReceipt? capturedReceipt = null;
            string? observedQualifiedAccount = null;
            string? observedPlanDir = null;

            _blobs.When(b => b.SaveFileToBlobs(Arg.Any<string>()))
                .Do(call =>
                {
                    var path = call.Arg<string>();
                    Assert.IsNotNull(observedPlanDir);
                    Assert.IsTrue(Directory.Exists(observedPlanDir), "Plan directory must still exist while uploading artefacts.");
                    Assert.AreNotEqual(observedPlanDir, Path.GetDirectoryName(path), "Uploads must read from the Monitor-only verified staging directory, not the runner-writable directory.");
                    Assert.IsTrue(File.Exists(path), $"Uploaded file '{path}' must still exist on disk during upload.");
                    uploadedFiles.Add(Path.GetFileName(path));
                });

            _requests.When(r => r.SaveTerraformDeploymentReceipt(Arg.Any<TerraformDeploymentReceipt>()))
                .Do(call =>
                {
                    Assert.IsNotNull(observedPlanDir);
                    Assert.IsTrue(Directory.Exists(observedPlanDir), "Plan directory must still exist during receipt persistence.");
                    capturedReceipt = call.Arg<TerraformDeploymentReceipt>();
                });

            var dispatcher = CreateDispatcher((invocation, _) =>
            {
                observedQualifiedAccount = invocation.QualifiedAccountName;
                observedPlanDir = invocation.PlanDirectory;
                StringAssert.StartsWith(Path.GetFileName(observedPlanDir), "5001-");
                Assert.IsTrue(Directory.Exists(invocation.PlanDirectory));

                File.WriteAllText(invocation.PlanFilePath, "binary-plan-payload");
                File.WriteAllText(invocation.PlanContentFilePath, "+ azurerm_resource_group.main");
                WritePlannedSourceZip(invocation.PlanSourceArchiveFilePath, requestId, resultId, "rev-1");
                File.WriteAllText(invocation.LockFilePath, "provider \"registry.terraform.io/hashicorp/azurerm\" {}");
                return 0;
            });

            var succeeded = dispatcher.Dispatch(
                component,
                result,
                new Dictionary<string, VariableValue>(),
                requestId,
                isProduction: true,
                environmentName: "Payments Prod",
                executionIdentityReference: "payments-env",
                identityAdoption: new RequestExecutionIdentityAdoption(),
                resultLogBuilder: new StringBuilder(),
                terreformOperation: TerraformRunnerOperations.CreatePlan,
                cancellationToken: CancellationToken.None);

            Assert.IsTrue(succeeded);
            Assert.IsNotNull(observedPlanDir);
            Assert.AreEqual(@"NT AUTHORITY\LOCAL SERVICE", observedQualifiedAccount);
            CollectionAssert.AreEquivalent(
                new[]
                {
                    resultId.CreateTerraformPlanBlobName(),
                    resultId.CreateTerraformPlanContentBlobName(),
                    resultId.CreateTerraformPlanBlobName() + ".source.zip",
                    $"{resultId}.terraform.lock.hcl"
                },
                uploadedFiles);
            Assert.IsNotNull(capturedReceipt);
            Assert.IsFalse(Directory.Exists(observedPlanDir), "Plan directory must be expired once all uploads and receipt persistence complete.");
            Assert.IsEmpty(Directory.GetDirectories(_planRoot), "Both dispatch and Monitor-only staging directories must be removed on clean completion.");
            _requests.Received(1).UpdateResultStatus(
                result,
                Arg.Is<DeploymentResultStatus>(s => s.Value == DeploymentResultStatus.WaitingConfirmation.Value));
        }

        [TestMethod]
        public void PlanDispatchRetainsPlanDirectoryWhenRunnerOrUploadFails()
        {
            const int requestId = 501;
            const int failedRunnerResultId = 5002;
            var (component1, result1) = SeedSubmittedRequest(requestId, failedRunnerResultId, "tf-comp-plan-fail-runner");
            string? runnerFailDir = null;

            var failingRunnerDispatcher = CreateDispatcher((invocation, _) =>
            {
                runnerFailDir = invocation.PlanDirectory;
                File.WriteAllText(invocation.PlanFilePath, "partial-plan");
                return 1;
            });

            var runnerSucceeded = failingRunnerDispatcher.Dispatch(
                component1,
                result1,
                new Dictionary<string, VariableValue>(),
                requestId,
                isProduction: true,
                environmentName: "Payments Prod",
                executionIdentityReference: "payments-env",
                identityAdoption: new RequestExecutionIdentityAdoption(),
                resultLogBuilder: new StringBuilder(),
                terreformOperation: TerraformRunnerOperations.CreatePlan,
                cancellationToken: CancellationToken.None);

            Assert.IsFalse(runnerSucceeded);
            Assert.IsNotNull(runnerFailDir);
            Assert.IsTrue(Directory.Exists(runnerFailDir), "Failed runner must retain local plan directory for inspection.");
            Assert.IsTrue(File.Exists(Path.Join(runnerFailDir, failedRunnerResultId.CreateTerraformPlanBlobName())));

            const int failedUploadRequestId = 502;
            const int failedUploadResultId = 5003;
            var (component2, result2) = SeedSubmittedRequest(failedUploadRequestId, failedUploadResultId, "tf-comp-plan-fail-upload");
            string? uploadFailDir = null;

            _blobs.When(b => b.SaveFileToBlobs(Arg.Is<string>(p => p.EndsWith(".terraform.lock.hcl", StringComparison.Ordinal))))
                .Do(_ => throw new IOException("Simulated blob storage outage during lockfile upload."));

            var failingUploadDispatcher = CreateDispatcher((invocation, _) =>
            {
                uploadFailDir = invocation.PlanDirectory;
                File.WriteAllText(invocation.PlanFilePath, "binary-plan");
                File.WriteAllText(invocation.PlanContentFilePath, "plan-text");
                WritePlannedSourceZip(invocation.PlanSourceArchiveFilePath, failedUploadRequestId, failedUploadResultId, "rev-1");
                File.WriteAllText(invocation.LockFilePath, "lockfile-bytes");
                return 0;
            });

            Assert.ThrowsExactly<IOException>(() =>
                failingUploadDispatcher.Dispatch(
                    component2,
                    result2,
                    new Dictionary<string, VariableValue>(),
                    failedUploadRequestId,
                    isProduction: true,
                    environmentName: "Payments Prod",
                    executionIdentityReference: "payments-env",
                    identityAdoption: new RequestExecutionIdentityAdoption(),
                    resultLogBuilder: new StringBuilder(),
                    terreformOperation: TerraformRunnerOperations.CreatePlan,
                    cancellationToken: CancellationToken.None));

            Assert.IsNotNull(uploadFailDir);
            Assert.IsTrue(Directory.Exists(uploadFailDir), "Upload failure must retain local plan directory for operator recovery.");
            Assert.IsTrue(File.Exists(Path.Join(uploadFailDir, $"{failedUploadResultId}.terraform.lock.hcl")));
            Assert.IsFalse(
                Directory.GetDirectories(_planRoot).Any(d => Path.GetFileName(d).StartsWith("_staging-", StringComparison.Ordinal)),
                "Monitor-only staging directory must be cleaned up on Dispose even when dispatch fails.");
        }

        [TestMethod]
        [SupportedOSPlatform("windows")]
        public void ApplyDispatchStagesInputsBeforeAdmittingRunnerRevokesAfterRunnerAndExpires()
        {
            if (!OperatingSystem.IsWindows())
            {
                return;
            }

            const int requestId = 503;
            const int resultId = 5004;
            var (component, result) = SeedSubmittedRequest(requestId, resultId, "tf-comp-apply-ok");
            SeedApprovedPlanArtifactsForApply(requestId, resultId, component.ComponentId!.Value);

            var localServiceSid = new SecurityIdentifier(WellKnownSidType.LocalServiceSid, null);
            var runnerAdmittedDuringBlobDownload = false;
            var runnerAdmittedDuringExecution = false;
            var runnerAdmittedOnPreStagedPlanDuringExecution = false;
            var runnerRevokedDuringPostApplyUpload = false;

            var planBlobName = resultId.CreateTerraformPlanBlobName();
            _blobs.When(b => b.DownloadFileFromBlobs(planBlobName, Arg.Any<string>()))
                .Do(call =>
                {
                    if (OperatingSystem.IsWindows())
                    {
                        var dir = Path.GetDirectoryName(call.ArgAt<string>(1))!;
                        runnerAdmittedDuringBlobDownload = HasIdentityRule(new DirectoryInfo(dir).GetAccessControl(), localServiceSid);
                    }
                });

            var sidecarUploads = new List<string>();
            var inventoryRecordedWhileDirAlive = false;
            TerraformDeploymentOutcome? finalOutcome = null;
            string? observedPlanDir = null;

            _blobs.When(b => b.SaveFileToBlobs(Arg.Any<string>()))
                .Do(call =>
                {
                    var path = call.Arg<string>();
                    Assert.IsNotNull(observedPlanDir);
                    Assert.IsTrue(Directory.Exists(observedPlanDir), "Plan directory must still exist during post-apply sidecar upload.");
                    if (OperatingSystem.IsWindows())
                    {
                        runnerRevokedDuringPostApplyUpload = !HasIdentityRule(new DirectoryInfo(observedPlanDir).GetAccessControl(), localServiceSid);
                    }
                    Assert.IsTrue(File.Exists(path), $"Post-apply sidecar '{path}' must still exist when uploaded.");
                    sidecarUploads.Add(Path.GetFileName(path));
                });

            _requests.When(r => r.RecordTerraformInventory(
                    Arg.Any<int>(),
                    Arg.Any<int>(),
                    Arg.Any<int>(),
                    Arg.Any<DateTimeOffset>(),
                    Arg.Any<IReadOnlyList<TerraformInventoryObservation>>(),
                    Arg.Any<bool>()))
                .Do(_ =>
                {
                    Assert.IsNotNull(observedPlanDir);
                    Assert.IsTrue(Directory.Exists(observedPlanDir), "Plan directory must still exist while recording inventory provenance.");
                    inventoryRecordedWhileDirAlive = true;
                });

            _requests.When(r => r.SaveTerraformDeploymentOutcome(Arg.Any<TerraformDeploymentOutcome>()))
                .Do(call =>
                {
                    if (observedPlanDir is not null)
                    {
                        Assert.IsTrue(Directory.Exists(observedPlanDir), "Plan directory must still exist while persisting apply outcome.");
                    }
                    finalOutcome = call.Arg<TerraformDeploymentOutcome>();
                });

            var dispatcher = CreateDispatcher((invocation, _) =>
            {
                observedPlanDir = invocation.PlanDirectory;
                StringAssert.StartsWith(Path.GetFileName(observedPlanDir), "5004-");
                Assert.IsTrue(File.Exists(invocation.PlanFilePath), "Downloaded .tfplan must be staged inside dispatch directory.");
                Assert.IsTrue(File.Exists(invocation.PlanSourceArchiveFilePath), "Downloaded .source.zip must be staged inside dispatch directory.");

                if (OperatingSystem.IsWindows())
                {
                    runnerAdmittedDuringExecution = HasIdentityRule(new DirectoryInfo(invocation.PlanDirectory).GetAccessControl(), localServiceSid);
                    runnerAdmittedOnPreStagedPlanDuringExecution = HasIdentityRule(new FileInfo(invocation.PlanFilePath).GetAccessControl(), localServiceSid);
                }

                File.WriteAllText(
                    invocation.AppliedResourcesFilePath,
                    JsonSerializer.Serialize(new TerraformInventorySnapshot
                    {
                        DeploymentResultId = resultId,
                        IsComplete = true,
                        Resources = new List<CloudResourceApiModel>
                        {
                            new()
                            {
                                Name = "rg-payments-prod",
                                Provider = "Azure",
                                ResourceType = "azurerm_resource_group",
                                ResourceIdentifier = "/subscriptions/11111111-2222-4333-8444-555555555555/resourceGroups/rg-payments-prod",
                                Subscription = "11111111-2222-4333-8444-555555555555"
                            }
                        }
                    }));
                File.WriteAllText(invocation.SourceArchiveFilePath, "deployed-source-zip-bytes");
                return 0;
            });

            var succeeded = dispatcher.Dispatch(
                component,
                result,
                new Dictionary<string, VariableValue>(),
                requestId,
                isProduction: true,
                environmentName: "Payments Prod",
                executionIdentityReference: "payments-env",
                identityAdoption: new RequestExecutionIdentityAdoption(),
                resultLogBuilder: new StringBuilder(),
                terreformOperation: TerraformRunnerOperations.ApplyPlan,
                cancellationToken: CancellationToken.None);

            Assert.IsTrue(succeeded);
            Assert.IsNotNull(observedPlanDir);
            if (OperatingSystem.IsWindows())
            {
                Assert.IsFalse(runnerAdmittedDuringBlobDownload, "ApplyPlan inputs must be downloaded BEFORE deploymentAccount is granted access.");
                Assert.IsTrue(runnerAdmittedDuringExecution, "deploymentAccount must be admitted on dispatch directory while Runner executes.");
                Assert.IsTrue(runnerAdmittedOnPreStagedPlanDuringExecution, "deploymentAccount must be granted read/modify on pre-staged .tfplan while Runner executes.");
                Assert.IsTrue(runnerRevokedDuringPostApplyUpload, "deploymentAccount access must be revoked immediately after Runner exits.");
            }
            Assert.IsTrue(inventoryRecordedWhileDirAlive);
            CollectionAssert.AreEquivalent(
                new[] { $"{resultId}.applied-resources.json", $"{resultId}.terraform-source.zip" },
                sidecarUploads);
            Assert.IsNotNull(finalOutcome);
            Assert.AreEqual("Succeeded", finalOutcome.InventoryOutcome);
            Assert.AreEqual("Succeeded", finalOutcome.ArchiveOutcome);
            Assert.IsFalse(Directory.Exists(observedPlanDir), "Plan directory must expire once post-apply ingestion, uploads, and outcome persistence succeed.");
            _requests.Received(1).UpdateResultStatus(
                result,
                Arg.Is<DeploymentResultStatus>(s => s.Value == DeploymentResultStatus.Complete.Value));
        }

        [TestMethod]
        [SupportedOSPlatform("windows")]
        public void ApplyDispatchRevokesPartialRunnerAdmissionWhenAdmitRunnerThrowsBeforeProcessLaunch()
        {
            if (!OperatingSystem.IsWindows())
            {
                return;
            }

            const int requestId = 509;
            const int resultId = 5010;
            var (component, result) = SeedSubmittedRequest(requestId, resultId, "tf-comp-admit-fail");
            SeedApprovedPlanArtifactsForApply(requestId, resultId, component.ComponentId!.Value);

            var localServiceSid = new SecurityIdentifier(WellKnownSidType.LocalServiceSid, null);
            var externalVictim = Path.Join(_sharedScriptRoot, "external-admit-victim.txt");
            File.WriteAllText(externalVictim, "untouched");

            string? stagedPlanPath = null;
            string? stagedSourcePath = null;
            string? dispatchDirectory = null;

            var planBlobName = resultId.CreateTerraformPlanBlobName();
            var sourceBlobName = planBlobName + ".source.zip";
            _blobs.When(b => b.DownloadFileFromBlobs(planBlobName, Arg.Any<string>()))
                .Do(call =>
                {
                    stagedPlanPath = call.ArgAt<string>(1);
                    dispatchDirectory = Path.GetDirectoryName(stagedPlanPath);
                });
            _blobs.When(b => b.DownloadFileFromBlobs(sourceBlobName, Arg.Any<string>()))
                .Do(call =>
                {
                    stagedSourcePath = call.ArgAt<string>(1);
                });

            // Plant a multi-link hard link sorting AFTER "5010.tfplan*" right before AdmitRunner runs,
            // so AdmitRunner grants runner access to the directory and staged inputs before throwing.
            var plantedLink = false;
            _requests.When(r => r.SaveTerraformDeploymentOutcome(Arg.Any<TerraformDeploymentOutcome>()))
                .Do(_ =>
                {
                    if (!plantedLink && dispatchDirectory is not null)
                    {
                        plantedLink = true;
                        TerraformPlanLifetimeTests.CreateHardLink(
                            Path.Join(dispatchDirectory, "zzz-planted-hardlink.txt"),
                            externalVictim);
                    }
                });

            var runnerExecuted = false;
            var dispatcher = CreateDispatcher((_, _) =>
            {
                runnerExecuted = true;
                return 0;
            });

            var ex = Assert.ThrowsExactly<IOException>(() =>
                dispatcher.Dispatch(
                    component,
                    result,
                    new Dictionary<string, VariableValue>(),
                    requestId,
                    isProduction: true,
                    environmentName: "Payments Prod",
                    executionIdentityReference: "payments-env",
                    identityAdoption: new RequestExecutionIdentityAdoption(),
                    resultLogBuilder: new StringBuilder(),
                    terreformOperation: TerraformRunnerOperations.ApplyPlan,
                    cancellationToken: CancellationToken.None));

            StringAssert.Contains(ex.Message, "2 NTFS links");
            Assert.IsFalse(runnerExecuted, "Runner must never execute when AdmitRunner fails.");
            Assert.IsNotNull(dispatchDirectory);
            Assert.IsNotNull(stagedPlanPath);
            Assert.IsNotNull(stagedSourcePath);
            Assert.IsTrue(Directory.Exists(dispatchDirectory), "Failed dispatch retains Monitor-confined directory for diagnostics.");
            Assert.IsFalse(
                HasIdentityRule(new DirectoryInfo(dispatchDirectory).GetAccessControl(), localServiceSid),
                "RevokeRunner must strip partial runner directory admission when AdmitRunner throws.");
            Assert.IsFalse(
                HasIdentityRule(new FileInfo(stagedPlanPath).GetAccessControl(), localServiceSid),
                "RevokeRunner must strip partial runner file admission on staged .tfplan when AdmitRunner throws.");
            Assert.IsFalse(
                HasIdentityRule(new FileInfo(stagedSourcePath).GetAccessControl(), localServiceSid),
                "RevokeRunner must strip partial runner file admission on staged .source.zip when AdmitRunner throws.");
            Assert.AreEqual("untouched", File.ReadAllText(externalVictim));
        }

        [TestMethod]
        public void ApplyDispatchRetainsPlanDirectoryWhenRunnerOrSidecarArchiveUploadFails()
        {
            const int runnerFailRequestId = 504;
            const int runnerFailResultId = 5005;
            var (component1, result1) = SeedSubmittedRequest(runnerFailRequestId, runnerFailResultId, "tf-comp-apply-fail-runner");
            string? runnerFailDir = null;
            SeedApprovedPlanArtifactsForApply(runnerFailRequestId, runnerFailResultId, component1.ComponentId!.Value);

            var failingRunnerDispatcher = CreateDispatcher((invocation, _) =>
            {
                runnerFailDir = invocation.PlanDirectory;
                return 1;
            });
            var runnerSucceeded = failingRunnerDispatcher.Dispatch(
                component1,
                result1,
                new Dictionary<string, VariableValue>(),
                runnerFailRequestId,
                isProduction: true,
                environmentName: "Payments Prod",
                executionIdentityReference: "payments-env",
                identityAdoption: new RequestExecutionIdentityAdoption(),
                resultLogBuilder: new StringBuilder(),
                terreformOperation: TerraformRunnerOperations.ApplyPlan,
                cancellationToken: CancellationToken.None);

            Assert.IsFalse(runnerSucceeded);
            Assert.IsNotNull(runnerFailDir);
            Assert.IsTrue(Directory.Exists(runnerFailDir), "Failed apply runner must retain local plan directory for recovery.");

            const int archiveFailRequestId = 505;
            const int archiveFailResultId = 5006;
            var (component2, result2) = SeedSubmittedRequest(archiveFailRequestId, archiveFailResultId, "tf-comp-apply-fail-archive");
            string? archiveFailDir = null;
            SeedApprovedPlanArtifactsForApply(archiveFailRequestId, archiveFailResultId, component2.ComponentId!.Value);

            _blobs.When(b => b.SaveFileToBlobs(Arg.Is<string>(p => p.EndsWith(".terraform-source.zip", StringComparison.Ordinal))))
                .Do(_ => throw new IOException("Simulated blob outage uploading deployed source archive."));

            var archiveFailDispatcher = CreateDispatcher((invocation, _) =>
            {
                archiveFailDir = invocation.PlanDirectory;
                File.WriteAllText(
                    invocation.AppliedResourcesFilePath,
                    JsonSerializer.Serialize(new TerraformInventorySnapshot
                    {
                        DeploymentResultId = archiveFailResultId,
                        IsComplete = true,
                        Resources = new List<CloudResourceApiModel>()
                    }));
                File.WriteAllText(invocation.SourceArchiveFilePath, "deployed-source-only-local-copy");
                return 0;
            });

            var archiveApplySucceeded = archiveFailDispatcher.Dispatch(
                component2,
                result2,
                new Dictionary<string, VariableValue>(),
                archiveFailRequestId,
                isProduction: true,
                environmentName: "Payments Prod",
                executionIdentityReference: "payments-env",
                identityAdoption: new RequestExecutionIdentityAdoption(),
                resultLogBuilder: new StringBuilder(),
                terreformOperation: TerraformRunnerOperations.ApplyPlan,
                cancellationToken: CancellationToken.None);

            Assert.IsTrue(archiveApplySucceeded);
            Assert.IsNotNull(archiveFailDir);
            Assert.IsTrue(Directory.Exists(archiveFailDir), "Failed post-apply archive upload must retain dispatch directory on disk for operator recovery.");
            Assert.IsTrue(File.Exists(Path.Join(archiveFailDir, $"{archiveFailResultId}.terraform-source.zip")));
        }

        [TestMethod]
        public void ApplyDispatchRetainsPlanDirectoryOnPartialFailureOfAppliedResourceRegistration()
        {
            const int requestId = 506;
            const int resultId = 5007;
            var (component, result) = SeedSubmittedRequest(requestId, resultId, "tf-comp-apply-partial-failure");
            SeedApprovedPlanArtifactsForApply(requestId, resultId, component.ComponentId!.Value);

            var mutationCallCount = 0;
            _cloudResources.ExecuteMutation(Arg.Any<Func<int>>())
                .Returns(call =>
                {
                    mutationCallCount++;
                    if (mutationCallCount == 2)
                    {
                        throw new InvalidOperationException("Simulated database failure on second cloud resource.");
                    }

                    return call.Arg<Func<int>>()();
                });

            TerraformDeploymentOutcome? finalOutcome = null;
            _requests.When(r => r.SaveTerraformDeploymentOutcome(Arg.Any<TerraformDeploymentOutcome>()))
                .Do(call => finalOutcome = call.Arg<TerraformDeploymentOutcome>());

            string? observedPlanDir = null;
            var dispatcher = CreateDispatcher((invocation, _) =>
            {
                observedPlanDir = invocation.PlanDirectory;
                File.WriteAllText(
                    invocation.AppliedResourcesFilePath,
                    JsonSerializer.Serialize(new TerraformInventorySnapshot
                    {
                        DeploymentResultId = resultId,
                        IsComplete = true,
                        Resources = new List<CloudResourceApiModel>
                        {
                            new()
                            {
                                Name = "rg-ok",
                                Provider = "Azure",
                                ResourceType = "azurerm_resource_group",
                                ResourceIdentifier = "/subscriptions/11111111-2222-4333-8444-555555555555/resourceGroups/rg-ok",
                                Subscription = "11111111-2222-4333-8444-555555555555"
                            },
                            new()
                            {
                                Name = "rg-fail",
                                Provider = "Azure",
                                ResourceType = "azurerm_resource_group",
                                ResourceIdentifier = "/subscriptions/11111111-2222-4333-8444-555555555555/resourceGroups/rg-fail",
                                Subscription = "11111111-2222-4333-8444-555555555555"
                            }
                        }
                    }));
                File.WriteAllText(invocation.SourceArchiveFilePath, "deployed-source-zip-bytes");
                return 0;
            });

            var succeeded = dispatcher.Dispatch(
                component,
                result,
                new Dictionary<string, VariableValue>(),
                requestId,
                isProduction: true,
                environmentName: "Payments Prod",
                executionIdentityReference: "payments-env",
                identityAdoption: new RequestExecutionIdentityAdoption(),
                resultLogBuilder: new StringBuilder(),
                terreformOperation: TerraformRunnerOperations.ApplyPlan,
                cancellationToken: CancellationToken.None);

            Assert.IsTrue(succeeded);
            Assert.IsNotNull(finalOutcome);
            Assert.AreEqual("PartialFailure", finalOutcome.InventoryOutcome);
            Assert.AreEqual(1, finalOutcome.RegisteredCount);
            Assert.AreEqual(1, finalOutcome.FailedCount);
            Assert.IsNotNull(observedPlanDir);
            Assert.IsTrue(Directory.Exists(observedPlanDir), "PartialFailure on resource registration must retain local recovery artefacts.");
            Assert.IsTrue(File.Exists(Path.Join(observedPlanDir, $"{resultId}.applied-resources.json")));
        }

        [TestMethod]
        public void DispatcherRefusesPlantedJunctionsDuringPostRunnerPlanAndApplyStaging()
        {
            var externalTarget = Path.Join(Path.GetDirectoryName(_planRoot)!, "external-sensitive");
            Directory.CreateDirectory(externalTarget);
            var externalSecret = Path.Join(externalTarget, "sensitive.txt");
            File.WriteAllText(externalSecret, "do-not-upload");

            // 1) CreatePlan: runner plants a junction at PlanContentFilePath -> Monitor refuses to stage/upload.
            const int planReqId = 507;
            const int planResId = 5008;
            var (planComp, planRes) = SeedSubmittedRequest(planReqId, planResId, "tf-comp-plan-junction");
            var uploadedDuringJunctionPlan = new List<string>();
            _blobs.When(b => b.SaveFileToBlobs(Arg.Any<string>()))
                .Do(call => uploadedDuringJunctionPlan.Add(call.Arg<string>()));

            var junctionPlanDispatcher = CreateDispatcher((invocation, _) =>
            {
                File.WriteAllText(invocation.PlanFilePath, "binary-plan");
                TerraformPlanLifetimeTests.CreateDirectoryLink(invocation.PlanContentFilePath, externalTarget);
                WritePlannedSourceZip(invocation.PlanSourceArchiveFilePath, planReqId, planResId, "rev-1");
                return 0;
            });

            Assert.ThrowsExactly<IOException>(() =>
                junctionPlanDispatcher.Dispatch(
                    planComp,
                    planRes,
                    new Dictionary<string, VariableValue>(),
                    planReqId,
                    isProduction: true,
                    environmentName: "Payments Prod",
                    executionIdentityReference: "payments-env",
                    identityAdoption: new RequestExecutionIdentityAdoption(),
                    resultLogBuilder: new StringBuilder(),
                    terreformOperation: TerraformRunnerOperations.CreatePlan,
                    cancellationToken: CancellationToken.None));
            Assert.IsEmpty(uploadedDuringJunctionPlan, "No artefact may be uploaded when verified staging rejects a planted junction.");
            Assert.IsTrue(File.Exists(externalSecret));

            // 2) ApplyPlan: runner plants a junction at AppliedResourcesFilePath -> Monitor refuses sidecar and retains directory.
            const int applyReqId = 508;
            const int applyResId = 5009;
            var (applyComp, applyRes) = SeedSubmittedRequest(applyReqId, applyResId, "tf-comp-apply-junction");
            SeedApprovedPlanArtifactsForApply(applyReqId, applyResId, applyComp.ComponentId!.Value);

            TerraformDeploymentOutcome? applyOutcome = null;
            _requests.When(r => r.SaveTerraformDeploymentOutcome(Arg.Any<TerraformDeploymentOutcome>()))
                .Do(call => applyOutcome = call.Arg<TerraformDeploymentOutcome>());

            string? applyPlanDir = null;
            var junctionApplyDispatcher = CreateDispatcher((invocation, _) =>
            {
                applyPlanDir = invocation.PlanDirectory;
                TerraformPlanLifetimeTests.CreateDirectoryLink(invocation.AppliedResourcesFilePath, externalTarget);
                return 0;
            });

            var applySucceeded = junctionApplyDispatcher.Dispatch(
                applyComp,
                applyRes,
                new Dictionary<string, VariableValue>(),
                applyReqId,
                isProduction: true,
                environmentName: "Payments Prod",
                executionIdentityReference: "payments-env",
                identityAdoption: new RequestExecutionIdentityAdoption(),
                resultLogBuilder: new StringBuilder(),
                terreformOperation: TerraformRunnerOperations.ApplyPlan,
                cancellationToken: CancellationToken.None);

            Assert.IsTrue(applySucceeded);
            Assert.IsNotNull(applyOutcome);
            Assert.AreEqual("RecoveryOrProvenanceFailed", applyOutcome.InventoryOutcome);
            Assert.IsNotNull(applyPlanDir);
            Assert.IsTrue(Directory.Exists(applyPlanDir));
            Assert.IsTrue(File.Exists(externalSecret));
            Assert.AreEqual("do-not-upload", File.ReadAllText(externalSecret));
        }

        [SupportedOSPlatform("windows")]
        private static bool HasIdentityRule(FileSystemSecurity security, SecurityIdentifier sid) =>
            security.GetAccessRules(includeExplicit: true, includeInherited: true, typeof(SecurityIdentifier))
                .Cast<FileSystemAccessRule>()
                .Any(rule => sid.Equals(rule.IdentityReference) && rule.AccessControlType == System.Security.AccessControl.AccessControlType.Allow);

        private TerraformDispatcher CreateDispatcher(
            Func<TerraformRunnerInvocation, CancellationToken, uint> executeRunner) =>
            new(
                Substitute.For<ILogger<TerraformDispatcher>>(),
                _requests,
                _configValues,
                _settings,
                _processes,
                _pipeServer,
                _blobs,
                _projects,
                _gitHubValidator,
                _cloudResources,
                _cloudAudit,
                _environments,
                _databases,
                _properties,
                _scriptScopeConfig,
                _credentialSource,
                _sourceHostAllowList,
                _planRoot,
                executeRunner);

        private (ComponentApiModel Component, DeploymentResultApiModel Result) SeedSubmittedRequest(
            int requestId,
            int resultId,
            string componentName)
        {
            var componentId = resultId + 100;
            var componentDir = Path.Join(_sharedScriptRoot, componentName);
            Directory.CreateDirectory(componentDir);

            var component = new ComponentApiModel
            {
                ComponentId = componentId,
                ComponentName = componentName,
                ScriptPath = componentDir,
                TerraformSourceType = TerraformSourceType.SharedFolder
            };
            var project = new ProjectApiModel
            {
                ProjectId = 88,
                ProjectName = "PaymentsProject"
            };
            var request = new DeploymentRequestApiModel
            {
                Id = requestId,
                Project = "PaymentsProject",
                BuildNumber = "2026.11.13.1",
                UserName = "CORP\\operator",
                RequestDetails = string.Empty
            };
            var result = new DeploymentResultApiModel
            {
                Id = resultId
            };

            _requests.GetSubmittedComponents(requestId).Returns(new[] { component });
            _requests.GetSubmittedProject(requestId, componentId).Returns(project);
            _requests.GetRequest(requestId).Returns(request);
            _requests.GetRequestDefinitions(requestId).Returns(new[]
            {
                new DeploymentRequestDefinition
                {
                    DeploymentRequestId = requestId,
                    ComponentId = componentId,
                    DefinitionRevision = "rev-1"
                }
            });

            return (component, result);
        }

        private void SeedApprovedPlanArtifactsForApply(int requestId, int resultId, int componentId)
        {
            var planBytes = Encoding.UTF8.GetBytes($"approved-plan-{resultId}");
            var sourceBytes = BuildPlannedSourceZipBytes(requestId, resultId, "rev-1");
            var planBlobName = resultId.CreateTerraformPlanBlobName();
            var sourceBlobName = planBlobName + ".source.zip";

            _blobs.When(b => b.DownloadFileFromBlobs(planBlobName, Arg.Any<string>()))
                .Do(call => File.WriteAllBytes(call.ArgAt<string>(1), planBytes));
            _blobs.When(b => b.DownloadFileFromBlobs(sourceBlobName, Arg.Any<string>()))
                .Do(call => File.WriteAllBytes(call.ArgAt<string>(1), sourceBytes));

            _requests.GetTerraformPlanApproval(resultId).Returns(new TerraformPlanApproval
            {
                DeploymentRequestId = requestId,
                DeploymentResultId = resultId,
                PlanSha256 = Convert.ToHexString(SHA256.HashData(planBytes)),
                SourceSha256 = Convert.ToHexString(SHA256.HashData(sourceBytes)),
                PlanContentSha256 = TerraformPlanArtifactIntegrity.HashPlanContent("plan-text")
            });

            _requests.GetTerraformDeploymentReceipt(resultId).Returns(new TerraformDeploymentReceipt
            {
                DeploymentResultId = resultId,
                DeploymentRequestId = requestId,
                ComponentId = componentId
            });
        }

        private static void WritePlannedSourceZip(string archivePath, int requestId, int resultId, string revision) =>
            File.WriteAllBytes(archivePath, BuildPlannedSourceZipBytes(requestId, resultId, revision));

        private static byte[] BuildPlannedSourceZipBytes(int requestId, int resultId, string revision)
        {
            using var memory = new MemoryStream();
            using (var archive = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true))
            {
                var entry = archive.CreateEntry("_dorc_provenance.json");
                using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
                writer.Write(JsonSerializer.Serialize(new
                {
                    RequestId = requestId,
                    DeploymentResultId = resultId,
                    DefinitionRevision = revision,
                    ArchivedAtUtc = DateTimeOffset.UtcNow
                }));
            }

            return memory.ToArray();
        }
    }
}
