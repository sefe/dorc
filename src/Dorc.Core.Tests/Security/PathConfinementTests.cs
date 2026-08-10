using Dorc.Core.Security;

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
    }
}
