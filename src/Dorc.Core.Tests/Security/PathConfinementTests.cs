using Dorc.Core.Security;
using Microsoft.Extensions.Configuration;

namespace Dorc.Core.Tests.Security
{
    /// <summary>
    /// These paths are Windows and UNC paths judged on a Linux test host, which is the
    /// reason PathConfinement normalises them itself rather than through System.IO.Path.
    /// If these tests ever start depending on the host's path semantics they are no longer
    /// testing what production does.
    /// </summary>
    [TestClass]
    public class PathConfinementTests
    {
        private const string Root = @"\\itss.global\prod\GLO\APP\DORC\Scripts\Scripts.PR";

        [TestMethod]
        public void AcceptsAPathBeneathTheRoot()
        {
            Assert.IsTrue(PathConfinement.IsWithin(Root + @"\00 Generic\DeployMSI.ps1", Root));
        }

        [TestMethod]
        public void AcceptsTheRootItself()
        {
            Assert.IsTrue(PathConfinement.IsWithin(Root, Root));
        }

        /// <summary>
        /// The live finding from U-11: registered scripts pointing at a different file
        /// server entirely.
        /// </summary>
        [TestMethod]
        public void RejectsAPathOnAnotherServer()
        {
            Assert.IsFalse(PathConfinement.IsWithin(@"\\trading1\common\jhai\ABissue\SyncIssue.ps1", Root));
        }

        /// <summary>
        /// A bare string-prefix comparison would accept this, because the root is a textual
        /// prefix of an unrelated sibling directory.
        /// </summary>
        [TestMethod]
        public void RejectsASiblingDirectoryWithTheRootAsAPrefix()
        {
            Assert.IsFalse(PathConfinement.IsWithin(Root + @"-archive\x.ps1", Root));
        }

        [TestMethod]
        public void RejectsTraversalOutOfTheRoot()
        {
            Assert.IsFalse(PathConfinement.IsWithin(Root + @"\..\..\Scripts.ST\x.ps1", Root));
        }

        [TestMethod]
        public void ResolvesTraversalThatStaysWithinTheRoot()
        {
            Assert.IsTrue(PathConfinement.IsWithin(Root + @"\00 Generic\..\01 Other\x.ps1", Root));
        }

        [TestMethod]
        public void IsCaseInsensitive()
        {
            Assert.IsTrue(PathConfinement.IsWithin(Root.ToUpperInvariant() + @"\X.PS1", Root.ToLowerInvariant()));
        }

        [TestMethod]
        public void TreatsForwardAndBackSlashesAlike()
        {
            Assert.IsTrue(PathConfinement.IsWithin(
                "//itss.global/prod/GLO/APP/DORC/Scripts/Scripts.PR/00 Generic/x.ps1", Root));
        }

        [TestMethod]
        public void ReducesFileUrisToPathsOnBothSides()
        {
            Assert.IsTrue(PathConfinement.IsWithin(
                "file://itss.global/prod/GLO/APP/DORC/Scripts/Scripts.PR/x.ps1",
                "file://itss.global/prod/GLO/APP/DORC/Scripts/Scripts.PR"));
        }

        [TestMethod]
        public void AcceptsDriveRootedPaths()
        {
            Assert.IsTrue(PathConfinement.IsWithin(@"C:\Builds\app\drop", @"c:\builds"));
            Assert.IsFalse(PathConfinement.IsWithin(@"D:\Builds\app\drop", @"C:\Builds"));
        }

        [TestMethod]
        [DataRow(null)]
        [DataRow("")]
        [DataRow("   ")]
        [DataRow(@"relative\path")]
        [DataRow(@"\\serverOnlyNoShare")]
        public void RejectsAnythingThatCannotBeAnchored(string? candidate)
        {
            Assert.IsFalse(PathConfinement.IsWithin(candidate, Root));
            Assert.IsNull(PathConfinement.Canonicalise(candidate));
        }

        [TestMethod]
        public void RejectsWhenTheRootItselfCannotBeAnchored()
        {
            Assert.IsFalse(PathConfinement.IsWithin(Root + @"\x.ps1", "not-a-root"));
        }

        /// <summary>
        /// An absent allow-list confines nothing. Admitting everything on that basis would
        /// make the check vacuous for exactly the projects least configured.
        /// </summary>
        [TestMethod]
        public void RejectsAgainstAnEmptyRootSet()
        {
            Assert.IsFalse(PathConfinement.IsWithinAny(Root + @"\x.ps1", Array.Empty<string>()));
            Assert.IsFalse(PathConfinement.IsWithinAny(Root + @"\x.ps1", null));
        }

        [TestMethod]
        public void AcceptsAgainstAnyOfSeveralRoots()
        {
            var roots = PathConfinement.SplitRoots($@"\\other\share; {Root} ;\\third\share");

            Assert.IsTrue(PathConfinement.IsWithinAny(Root + @"\x.ps1", roots));
            Assert.IsFalse(PathConfinement.IsWithinAny(@"\\fourth\share\x.ps1", roots));
        }

        [TestMethod]
        public void SplitRootsIgnoresBlankEntriesAndSurroundingSpace()
        {
            CollectionAssert.AreEqual(
                new[] { @"\\a\b", @"\\c\d" },
                PathConfinement.SplitRoots(@" \\a\b ;; \\c\d ; ").ToArray());
        }

        [TestMethod]
        [DataRow("file://itss.global/prod/GLO/APP/DORC/Scripts/Scripts.PR/%2e%2e/Scripts.ST/x.ps1", Root)]
        [DataRow("file://itss.global/prod/GLO/APP/DORC/Scripts/Scripts.PR/%252e%252e/Scripts.ST/x.ps1", Root)]
        [DataRow("file://itss.global/prod/%2e%2e/prod/GLO/APP/DORC/Scripts/Scripts.PR/x.ps1", Root)]
        [DataRow("file://host/drops/team-b/%2f..%2fteam-a", @"\\host\drops\team-a")]
        [DataRow("file://host/drops/team-b/%5c..%5cteam-a", @"\\host\drops\team-a")]
        [DataRow("file://host/drops/team-b/%252e%252e/team-a", @"\\host\drops\team-a")]
        [DataRow("file://host/drops/team-b/%2e%2e/team-a", @"\\host\drops\team-a")]
        [DataRow("file://host/drops/team-b/../team-a", @"\\host\drops\team-a")]
        public void RejectsFileUriTraversalAndEncodedSeparatorsOrDots(string candidate, string root)
        {
            Assert.IsNull(PathConfinement.Canonicalise(candidate));
            Assert.IsFalse(PathConfinement.IsWithin(candidate, root));
        }

        [TestMethod]
        [DataRow(@"\\host\drops\team-b\%2e%2e\team-a", @"\\host\drops\team-a")]
        [DataRow(@"\\host\drops\team-a\%2e%2e\team-b\rogue-drop", @"\\host\drops\team-a")]
        [DataRow(@"\\host\drops\team-a\%2e%2e\%2e%2e\other-share\rogue-drop", @"\\host\drops\team-a")]
        [DataRow(@"\\?\UNC\host\drops\team-a\%2e%2e\team-b\rogue-drop", @"\\host\drops\team-a")]
        [DataRow(@"C:\drops\team-a\%2e%2e\team-b\rogue-drop", @"C:\drops\team-a")]
        [DataRow("file://///?/UNC/host/drops/team-%61/x", @"\\host\drops\team-%61")]
        public void RejectsPlainAndExtendedPathsWithUriDecodedPercentSequences(string candidate, string root)
        {
            Assert.IsNull(PathConfinement.Canonicalise(candidate));
            Assert.IsFalse(PathConfinement.IsWithin(candidate, root));
        }

        [TestMethod]
        public void PreservesPlainPercentAndNonUnreservedPercentOnPlainPaths()
        {
            const string allowedRoot = @"\\host\drops\team-a";
            Assert.IsTrue(PathConfinement.IsWithin(@"\\host\drops\team-a\100%-ready", allowedRoot));
            Assert.IsTrue(PathConfinement.IsWithin(@"\\host\drops\team-a\drop%20name", allowedRoot));
        }

        [TestMethod]
        [DataRow("https://itss.global/prod/GLO/APP/DORC/Scripts/Scripts.PR/x.ps1")]
        [DataRow("http://itss.global/prod/x.ps1")]
        [DataRow("dorc-gh://org/repo/run/1")]
        [DataRow("h://itss.global/prod/x.ps1")]
        [DataRow("vstfs:///Build/Build/99")]
        [DataRow(@"C:relative\x.ps1")]
        [DataRow(@"\\.\C:\Scripts\x.ps1")]
        [DataRow(@"\\?\Volume{01234567-89ab-cdef-0123-456789abcdef}\Scripts\x.ps1")]
        [DataRow(@"\??\C:\Scripts\x.ps1")]
        [DataRow(@"\\itss.global@SSL@443\prod\GLO\APP\DORC\Scripts\Scripts.PR\x.ps1")]
        [DataRow("file://itss.global@SSL/prod/GLO/APP/DORC/Scripts/Scripts.PR/x.ps1")]
        public void RejectsNonFilesystemSchemesDriveRelativeAndAmbiguousAuthorities(string candidate)
        {
            Assert.IsNull(PathConfinement.Canonicalise(candidate));
            Assert.IsFalse(PathConfinement.IsWithin(candidate, Root));
            Assert.IsFalse(PathConfinement.IsWithin(candidate, candidate));
        }

        [TestMethod]
        public void NormalisesExtendedUncRootsAndCandidatesConsistently()
        {
            Assert.IsTrue(PathConfinement.IsWithin(
                @"\\?\UNC\itss.global\prod\GLO\APP\DORC\Scripts\Scripts.PR\sub\x.ps1",
                Root));
            Assert.IsTrue(PathConfinement.IsWithin(
                @"\\?\C:\Scripts\sub\x.ps1",
                @"C:\Scripts"));
            Assert.IsFalse(PathConfinement.IsWithin(
                @"\\?\C:\Scripts\x.ps1",
                Root));
            Assert.IsFalse(PathConfinement.IsWithin(
                @"\\?\UNC\attacker.example\share\x.ps1",
                Root));
            Assert.IsFalse(PathConfinement.IsWithin(
                Root + @"\deploy.ps1:hidden",
                Root));
        }

        [TestMethod]
        public void ConfigurationSettingsBindsScriptPathEnforcementFromIConfiguration()
        {
            var defaultConfig = new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build();
            Assert.IsFalse(new Dorc.Core.Configuration.ConfigurationSettings(defaultConfig).GetScriptPathEnforcementEnabled());

            var enabledConfig = new Microsoft.Extensions.Configuration.ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["AppSettings:EnforceScriptPathConfinement"] = "true"
                })
                .Build();
            Assert.IsTrue(new Dorc.Core.Configuration.ConfigurationSettings(enabledConfig).GetScriptPathEnforcementEnabled());
        }

        [TestMethod]
        [DataRow(@"%2e%2e\payload.ps1", "parent directory")]
        [DataRow(@"%252e%252e\payload.ps1", "parent directory")]
        [DataRow(@"%25252e%25252e\payload.ps1", "parent directory")]
        [DataRow(@"%2525252e%2525252e\payload.ps1", "parent directory")]
        [DataRow(@"%252525252e%252525252e\payload.ps1", "percent encoding")]
        [DataRow("""{"ScriptPath":"%2525252e%2525252e\\payload.ps1"}""", "parent directory")]
        [DataRow("""{"ScriptPath":"%252525252e%252525252e\\payload.ps1"}""", "percent encoding")]
        public void ScriptPathConfinementRejectsNestedPercentEncodedTraversalAcrossAllPassDepths(
            string path,
            string expectedReasonFragment)
        {
            var decision = Dorc.PersistentData.Security.ScriptPathConfinement.Check(path);
            Assert.IsFalse(decision.Allowed, $"'{path}' must be refused.");
            StringAssert.Contains(decision.Reason, expectedReasonFragment);
        }
    }
}
