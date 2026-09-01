using Dorc.PersistentData.Security;
using Microsoft.Extensions.Configuration;

namespace Dorc.Api.Tests
{
    /// <summary>
    /// Three fields name a source of deployable content, all settable at per-project modify
    /// rights: a project's artefacts URL, a project's Terraform repository URL, and a Terraform
    /// component's shared-folder location. Each becomes execution input and two attract a
    /// credential on the way. None was checked beyond a scheme prefix.
    ///
    /// The comparison is on the parsed host, not on the URL text. That distinction is the whole
    /// control: a substring test for "build.corp.example.com" is satisfied by
    /// build.corp.example.com.attacker.net, which is an attacker's host that collects whatever
    /// the substring test was protecting.
    /// </summary>
    [TestClass]
    public class SourceHostAllowListTests
    {
        private const string ArtefactHost = "buildserver.corp.example.com";
        private const string TerraformHost = "github.corp.example.com";

        private static SourceHostAllowList Configured() => Build(
            new Dictionary<string, string?>
            {
                [SourceHostAllowList.ArtefactHostsSetting + ":0"] = ArtefactHost,
                [SourceHostAllowList.TerraformHostsSetting + ":0"] = TerraformHost
            });

        private static SourceHostAllowList Unconfigured() => Build(new Dictionary<string, string?>());

        private static SourceHostAllowList Build(Dictionary<string, string?> settings) =>
            new(new ConfigurationBuilder().AddInMemoryCollection(settings).Build());

        [TestMethod]
        [DataRow("https://buildserver.corp.example.com/drops/app")]
        [DataRow(@"\\buildserver.corp.example.com\drops\app")]
        [DataRow("file://buildserver.corp.example.com/drops/app")]
        [DataRow("HTTPS://BUILDSERVER.CORP.EXAMPLE.COM/drops")]
        public void AcceptsAPermittedArtefactHost(string url)
        {
            Assert.IsTrue(Configured().IsArtefactSourceAllowed(url, out _), url);
        }

        [TestMethod]
        public void AcceptsMultiplePermittedArtefactRootsUsingPathConfinementDelimiters()
        {
            var roots =
                $" https://{ArtefactHost}/drops/one ; ; \\\\{ArtefactHost}\\drops\\two ";

            Assert.IsTrue(Configured().IsArtefactSourceAllowed(roots, out _));
        }

        [TestMethod]
        public void RejectsWhenAnySemicolonDelimitedArtefactRootIsNotPermitted()
        {
            var roots =
                $"https://{ArtefactHost}/drops/one; https://attacker.net/drops/two";

            Assert.IsFalse(Configured().IsArtefactSourceAllowed(roots, out var reason));
            StringAssert.Contains(reason, "attacker.net");
        }

        /// <summary>
        /// The reason the host is parsed rather than matched. Every one of these contains the
        /// permitted host as a substring and none of them is it.
        /// </summary>
        [TestMethod]
        [DataRow("https://buildserver.corp.example.com.attacker.net/drops")]
        [DataRow("https://attacker.net/buildserver.corp.example.com/drops")]
        [DataRow("https://attacker.net/?x=buildserver.corp.example.com")]
        [DataRow("https://attacker.net#buildserver.corp.example.com")]
        [DataRow(@"\\buildserver.corp.example.com.attacker.net\drops")]
        public void RejectsAHostThatMerelyContainsAPermittedOne(string url)
        {
            Assert.IsFalse(Configured().IsArtefactSourceAllowed(url, out _),
                $"'{url}' is not the permitted host, it only mentions it.");
        }

        /// <summary>
        /// Userinfo puts attacker-chosen text before the authority, so a reader scanning
        /// left-to-right sees the permitted host first. The parsed authority does not.
        /// </summary>
        [TestMethod]
        public void RejectsAPermittedHostPlacedInUserInfo()
        {
            Assert.IsFalse(
                Configured().IsArtefactSourceAllowed(
                    "https://buildserver.corp.example.com@attacker.net/drops", out _));
        }

        [TestMethod]
        public void RejectsAnUnrelatedHost()
        {
            Assert.IsFalse(Configured().IsArtefactSourceAllowed("https://attacker.net/drops", out var reason));
            StringAssert.Contains(reason, "attacker.net");
            StringAssert.Contains(reason, SourceHostAllowList.ArtefactHostsSetting);
        }

        /// <summary>
        /// The two lists are separate: permission to drop build artefacts somewhere is not
        /// permission to clone and execute infrastructure code from it.
        /// </summary>
        [TestMethod]
        public void KeepsTheArtefactAndTerraformListsApart()
        {
            var allowList = Configured();

            Assert.IsTrue(allowList.IsArtefactSourceAllowed($"https://{ArtefactHost}/drops", out _));
            Assert.IsFalse(allowList.IsTerraformSourceAllowed($"https://{ArtefactHost}/repo.git", out _));

            Assert.IsTrue(allowList.IsTerraformSourceAllowed($"https://{TerraformHost}/repo.git", out _));
            Assert.IsFalse(allowList.IsArtefactSourceAllowed($"https://{TerraformHost}/drops", out _));
        }

        [TestMethod]
        public void RejectsAValueWithNoIdentifiableHost()
        {
            var allowList = Configured();

            // A local path on the API host is not somewhere deployable content is fetched from.
            Assert.IsFalse(allowList.IsArtefactSourceAllowed("file:///C:/local/drops", out var local));
            Assert.IsFalse(allowList.IsArtefactSourceAllowed("not a url at all", out _));

            StringAssert.Contains(local, "does not name a host");
        }

        /// <summary>
        /// An unfilled list is not an empty list. There is no safe built-in default for a host
        /// allow-list, and enforcing against nothing would reject every project edit in every
        /// deployment the day this ships — the flag day the sequencing rules exist to prevent.
        /// The gap is reportable instead, so it can be closed deliberately.
        /// </summary>
        [TestMethod]
        public void DoesNotEnforceWhenNoListIsConfigured()
        {
            var allowList = Unconfigured();

            Assert.IsTrue(allowList.IsUnconfigured);
            Assert.IsTrue(allowList.IsArtefactSourceAllowed("https://anywhere.example.com/drops", out _));
            Assert.IsTrue(allowList.IsTerraformSourceAllowed(@"\\anywhere\share", out _));
        }

        [TestMethod]
        public void ReportsItselfConfiguredWhenEitherListIsFilled()
        {
            var artefactsOnly = Build(new Dictionary<string, string?>
            {
                [SourceHostAllowList.ArtefactHostsSetting + ":0"] = ArtefactHost
            });

            Assert.IsFalse(artefactsOnly.IsUnconfigured);

            // The list that IS filled enforces; the one that is not still admits, because an
            // unfilled list confines nothing and enforcing it would be enforcing against zero
            // permitted hosts.
            Assert.IsFalse(artefactsOnly.IsArtefactSourceAllowed("https://attacker.net/drops", out _));
            Assert.IsTrue(artefactsOnly.IsTerraformSourceAllowed("https://attacker.net/repo.git", out _));
        }

        [TestMethod]
        public void IgnoresBlankEntriesAndSurroundingSpace()
        {
            var padded = Build(new Dictionary<string, string?>
            {
                [SourceHostAllowList.ArtefactHostsSetting + ":0"] = "  " + ArtefactHost + " ",
                [SourceHostAllowList.ArtefactHostsSetting + ":1"] = "   "
            });

            Assert.IsTrue(padded.IsArtefactSourceAllowed($"https://{ArtefactHost}/drops", out _));
        }

        [TestMethod]
        public void AdmitsAnAbsentValue()
        {
            var allowList = Configured();

            Assert.IsTrue(allowList.IsTerraformSourceAllowed(null, out _));
            Assert.IsTrue(allowList.IsTerraformSourceAllowed("   ", out _),
                "Whether the field may be empty at all is asked elsewhere.");
        }

        /// <summary>
        /// UNC paths are not URIs, and a non-Windows host does not parse them as one. The host
        /// has to be read the same way wherever this runs.
        /// </summary>
        [TestMethod]
        public void ReadsTheHostOfAUncPathWithoutRelyingOnUriParsing()
        {
            Assert.AreEqual("buildserver", SourceHostAllowList.HostOf(@"\\buildserver\drops\app"));
            Assert.AreEqual("buildserver", SourceHostAllowList.HostOf("//buildserver/drops/app".Replace('/', '\\')));
            Assert.IsNull(SourceHostAllowList.HostOf(@"relative\path"));
        }
    }
}
