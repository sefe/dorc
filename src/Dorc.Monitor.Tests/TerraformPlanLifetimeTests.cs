using Dorc.Monitor.Pipes;
using Dorc.Monitor.Terraform;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Dorc.Monitor.Tests
{
    /// <summary>
    /// Terraform plan artefacts embed the variable values the plan was made with — the binary plan
    /// carries them and the rendered content lists them in plain text. They used to be written
    /// into one shared directory created with inherited permissions and were never removed, so a
    /// long-lived deployment host accumulated every plan it had ever produced, readable by any
    /// authenticated user on it.
    ///
    /// What is covered here is the layout and the lifetime: a directory per deployment result, and
    /// removal once the artefacts have a durable copy elsewhere. The access control list itself is
    /// not — applying one needs a Windows security authority, and these tests run wherever the
    /// build does. That is the same boundary the Runner process descriptor tests draw, and it is
    /// why the store keeps the two concerns separable: the layout decides WHO could be admitted,
    /// and the layout is what is testable here.
    ///
    /// The dispatcher's half of the lifetime — expire after upload on the plan path, expire
    /// unconditionally on the apply path — is not reachable from a test host either: dispatch
    /// builds a Windows process security context from a real account before it ever touches the
    /// plan store. The asymmetry is stated at both call sites rather than asserted.
    /// </summary>
    [TestClass]
    public class TerraformPlanLifetimeTests
    {
        private string _root = null!;
        private ILogger _logger = null!;

        private static readonly ScriptGroupReaderIdentity DeploymentIdentity =
            new("CORP", "svc-dorc-prod");

        [TestInitialize]
        public void Setup()
        {
            _root = Path.Join(Path.GetTempPath(), "dorc-plan-test-" + Guid.NewGuid().ToString("N"));
            _logger = Substitute.For<ILogger>();
        }

        [TestCleanup]
        public void Cleanup()
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }

        private TerraformPlanStore Reserve(int deploymentResultId) =>
            TerraformPlanStore.Reserve(_logger, _root, deploymentResultId, DeploymentIdentity);

        /// <summary>
        /// The shared directory is what made restriction impossible: an access control list over it
        /// would have to admit every identity that deploys on the host. One directory per
        /// deployment result is what lets a single identity be admitted, which is the whole reason
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
        /// The plan and the apply that confirms it are separate dispatches, minutes or days apart,
        /// and the apply downloads the plan blob by a name derived from the same identifier. If
        /// they did not agree on the directory the apply would download into a place the Runner was
        /// not told to look.
        /// </summary>
        [TestMethod]
        public void ThePlanAndTheApplyOfOneDeploymentShareADirectory()
        {
            Assert.AreEqual(Reserve(4003).Directory, Reserve(4003).Directory);
        }

        [TestMethod]
        public void ArtefactsAreNamedInsideTheDeploymentsOwnDirectory()
        {
            var store = Reserve(4004);

            Assert.AreEqual(
                Path.Combine(store.Directory, "4004.tfplan"),
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
        [DataRow("nested/4005.tfplan")]
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

        /// <summary>
        /// This is the asymmetry the plan path depends on. If the blob upload failed, the local
        /// copy is the only copy of that plan — so a later deployment reserving the same directory
        /// must re-apply its access control list without touching what is inside it.
        /// </summary>
        [TestMethod]
        public void ReservingADirectoryThatAlreadyHoldsAPlanLeavesItAlone()
        {
            var first = Reserve(4008);
            var retained = first.PathOf("4008.tfplan");
            File.WriteAllText(retained, "the-only-copy");

            Reserve(4008);

            Assert.IsTrue(File.Exists(retained));
            Assert.AreEqual("the-only-copy", File.ReadAllText(retained));
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
            var legacy = Path.Combine(_root, "1234.tfplan");
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

            Reserve(4013);

            Assert.IsTrue(Directory.Exists(recent.Directory));
        }

        /// <summary>
        /// The directory being reserved is never swept, however old it looks. An apply confirmed
        /// after the retention window has passed reserves a directory older than the cutoff, and
        /// removing it would delete the plan the apply is about to read back into it.
        /// </summary>
        [TestMethod]
        public void TheDirectoryThisDeploymentIsReservingSurvivesItsOwnRetention()
        {
            var store = Reserve(4014);
            var retained = store.PathOf("4014.tfplan");
            File.WriteAllText(retained, "confirmed-late");
            Age(store.Directory, TimeSpan.FromDays(30));

            var reReserved = Reserve(4014);

            Assert.AreEqual(store.Directory, reReserved.Directory);
            Assert.IsTrue(File.Exists(retained));
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
            Directory.SetLastWriteTimeUtc(store.Directory, DateTime.UtcNow.AddDays(-30));

            Reserve(4016);

            Assert.IsTrue(Directory.Exists(store.Directory));
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
    }
}
