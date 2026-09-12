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
            Assert.IsTrue(Configured().CheckArtefactSource(url).Allowed, url);
        }

        [TestMethod]
        public void AcceptsMultiplePermittedArtefactRootsUsingPathConfinementDelimiters()
        {
            var roots =
                $" https://{ArtefactHost}/drops/one ; ; \\\\{ArtefactHost}\\drops\\two ";

            Assert.IsTrue(Configured().CheckArtefactSource(roots).Allowed);
        }

        [TestMethod]
        public void RejectsWhenAnySemicolonDelimitedArtefactRootIsNotPermitted()
        {
            var roots =
                $"https://{ArtefactHost}/drops/one; https://attacker.net/drops/two";

            var decision = Configured().CheckArtefactSource(roots);

            Assert.IsFalse(decision.Allowed);
            StringAssert.Contains(decision.Reason, "attacker.net");
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
            Assert.IsFalse(Configured().CheckArtefactSource(url).Allowed,
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
                Configured().CheckArtefactSource(
                    "https://buildserver.corp.example.com@attacker.net/drops").Allowed);
        }

        [TestMethod]
        public void RejectsAnUnrelatedHost()
        {
            var decision = Configured().CheckArtefactSource("https://attacker.net/drops");

            Assert.IsFalse(decision.Allowed);
            StringAssert.Contains(decision.Reason, "attacker.net");
            StringAssert.Contains(decision.Reason, SourceHostAllowList.ArtefactHostsSetting);
        }

        /// <summary>
        /// The two lists are separate: permission to drop build artefacts somewhere is not
        /// permission to clone and execute infrastructure code from it.
        /// </summary>
        [TestMethod]
        public void KeepsTheArtefactAndTerraformListsApart()
        {
            var allowList = Configured();

            Assert.IsTrue(allowList.CheckArtefactSource($"https://{ArtefactHost}/drops").Allowed);
            Assert.IsFalse(allowList.CheckTerraformSource($"https://{ArtefactHost}/repo.git").Allowed);

            Assert.IsTrue(allowList.CheckTerraformSource($"https://{TerraformHost}/repo.git").Allowed);
            Assert.IsFalse(allowList.CheckArtefactSource($"https://{TerraformHost}/drops").Allowed);
        }

        [TestMethod]
        public void RejectsAValueWithNoIdentifiableHost()
        {
            var allowList = Configured();

            // A local path on the API host is not somewhere deployable content is fetched from.
            var local = allowList.CheckArtefactSource("file:///C:/local/drops");

            Assert.IsFalse(local.Allowed);
            Assert.IsFalse(allowList.CheckArtefactSource("not a url at all").Allowed);

            StringAssert.Contains(local.Reason, "does not name a host");
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
            Assert.IsTrue(allowList.CheckArtefactSource("https://anywhere.example.com/drops").Allowed);
            Assert.IsTrue(allowList.CheckTerraformSource(@"\\anywhere\share").Allowed);
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
            Assert.IsFalse(artefactsOnly.CheckArtefactSource("https://attacker.net/drops").Allowed);
            Assert.IsTrue(artefactsOnly.CheckTerraformSource("https://attacker.net/repo.git").Allowed);
        }

        [TestMethod]
        public void IgnoresBlankEntriesAndSurroundingSpace()
        {
            var padded = Build(new Dictionary<string, string?>
            {
                [SourceHostAllowList.ArtefactHostsSetting + ":0"] = "  " + ArtefactHost + " ",
                [SourceHostAllowList.ArtefactHostsSetting + ":1"] = "   "
            });

            Assert.IsTrue(padded.CheckArtefactSource($"https://{ArtefactHost}/drops").Allowed);
        }

        [TestMethod]
        public void AdmitsAnAbsentValue()
        {
            var allowList = Configured();

            Assert.IsTrue(allowList.CheckTerraformSource(null).Allowed);
            Assert.IsTrue(allowList.CheckTerraformSource("   ").Allowed,
                "Whether the field may be empty at all is asked elsewhere.");
        }

        /// <summary>
        /// The host is read by one parser for every form a source is written in, and that
        /// parser gives the same answer on every platform .NET 8 runs on.
        /// </summary>
        [TestMethod]
        [DataRow(@"\\buildserver\drops\app")]
        [DataRow("//buildserver/drops/app")]
        [DataRow("file://buildserver/drops/app")]
        [DataRow("https://buildserver/drops/app")]
        [DataRow("  https://buildserver/drops/app  ")]
        public void ReadsTheHostOfEveryFormASourceIsWrittenIn(string source)
        {
            Assert.AreEqual("buildserver", SourceHostAllowList.HostOf(source));
        }

        [TestMethod]
        [DataRow(@"relative\path")]
        [DataRow("file:///C:/local/drops")]
        [DataRow("not a url at all")]
        [DataRow("")]
        [DataRow(null)]
        public void ReadsNoHostWhereNoneIsNamed(string? source)
        {
            Assert.IsNull(SourceHostAllowList.HostOf(source));
        }

        /// <summary>
        /// The configuration binder returns null for a list written as a single value, which
        /// would read as "not configured" and admit every host. A list that is present but
        /// unreadable is an error at start-up, not a silently permissive allow-list.
        /// </summary>
        [TestMethod]
        public void RefusesAListWrittenAsASingleValue()
        {
            var refusal = Assert.ThrowsExactly<InvalidOperationException>(() => Build(new Dictionary<string, string?>
            {
                [SourceHostAllowList.ArtefactHostsSetting] = ArtefactHost + ";" + TerraformHost
            }));

            StringAssert.Contains(refusal.Message, SourceHostAllowList.ArtefactHostsSetting);
        }

        [TestMethod]
        public void TreatsABlankValueAsUnconfigured()
        {
            var blank = Build(new Dictionary<string, string?>
            {
                [SourceHostAllowList.ArtefactHostsSetting] = "   "
            });

            Assert.IsTrue(blank.IsUnconfigured);
        }
    }
}
