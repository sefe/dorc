using Dorc.PersistentData.Security;

namespace Dorc.Api.Tests
{
    /// <summary>
    /// A component's script path is stored relative to the script root and joined to it at
    /// dispatch with Path.Combine — which does not join. Given a rooted second argument it
    /// discards the first entirely, so a stored value of \\elsewhere\share\payload.ps1 does not
    /// resolve beneath the root, it replaces it, and the result runs as the deployment account.
    /// Modify rights on one project were enough, and the gated promotion pipeline that protects
    /// the script share was simply stepped around.
    ///
    /// The rule is stated over the stored value rather than the joined result: a relative path
    /// with no traversal cannot escape whatever root it is later joined to. That is equivalent
    /// to canonicalising against the root, and needs no knowledge of where scripts live — which
    /// is what lets it run at the write path.
    /// </summary>
    [TestClass]
    public class ScriptPathConfinementTests
    {
        private static bool IsConfined(string? path) => ScriptPathConfinement.IsConfined(path, out _);

        [TestMethod]
        [DataRow(@"00 Generic\DeployMSI.ps1")]
        [DataRow("00 Generic/DeployMSI.ps1")]
        [DataRow("DeployMSI.ps1")]
        [DataRow(@"a\b\c\d.ps1")]
        [DataRow("..dat.ps1")]
        [DataRow(@"folder\..dat\x.ps1")]
        public void AcceptsARelativePathBeneathTheRoot(string path)
        {
            Assert.IsTrue(IsConfined(path), $"'{path}' cannot resolve outside the root.");
        }

        /// <summary>
        /// The weakness itself: Path.Combine discards the root when handed any of these.
        /// </summary>
        [TestMethod]
        [DataRow(@"\\attacker\share\payload.ps1")]
        [DataRow("//attacker/share/payload.ps1")]
        [DataRow(@"C:\Windows\Temp\payload.ps1")]
        [DataRow(@"\Windows\Temp\payload.ps1")]
        [DataRow("/etc/payload.ps1")]
        [DataRow(@"\\?\C:\payload.ps1")]
        [DataRow(@"\\.\pipe\payload")]
        public void RejectsAnAbsolutePath(string path)
        {
            Assert.IsFalse(IsConfined(path), $"'{path}' replaces the script root rather than resolving beneath it.");
        }

        [TestMethod]
        [DataRow(@"..\..\Scripts.ST\x.ps1")]
        [DataRow("../../x.ps1")]
        [DataRow(@"00 Generic\..\..\x.ps1")]
        [DataRow(@"00 Generic\ .. \x.ps1")]
        public void RejectsTraversal(string path)
        {
            Assert.IsFalse(IsConfined(path));
        }

        /// <summary>
        /// Everything colon-shaped in a Windows path names something other than a file beneath
        /// the root — a drive, a device, or an alternate data stream. A stream in particular
        /// resolves inside the root and still executes content the root does not contain.
        /// </summary>
        [TestMethod]
        [DataRow(@"scripts\deploy.ps1:hidden")]
        [DataRow("D:relative.ps1")]
        public void RejectsAColon(string path)
        {
            Assert.IsFalse(IsConfined(path));
        }

        /// <summary>
        /// Components legitimately carry no script. Rejecting that would fail every update to a
        /// grouping component.
        /// </summary>
        [TestMethod]
        [DataRow(null)]
        [DataRow("")]
        [DataRow("   ")]
        public void AcceptsAnAbsentScriptPath(string? path)
        {
            Assert.IsTrue(IsConfined(path));
        }

        [TestMethod]
        public void SaysWhichRuleWasBroken()
        {
            ScriptPathConfinement.IsConfined(@"\\attacker\share\x.ps1", out var absolute);
            ScriptPathConfinement.IsConfined(@"..\x.ps1", out var traversal);

            StringAssert.Contains(absolute, "absolute path");
            StringAssert.Contains(traversal, "parent directory");
        }

        /// <summary>
        /// The check has to give the same answer wherever it runs. Path.IsPathRooted does not:
        /// on a non-Windows host it considers neither \\host\share nor C:\x rooted, so a
        /// validation built on it would admit exactly the paths that matter and only fail to on
        /// the Windows box that later joins them.
        /// </summary>
        [TestMethod]
        public void DoesNotDependOnTheHostsPathSemantics()
        {
            Assert.IsFalse(Path.IsPathRooted(@"\\attacker\share\x.ps1") && OperatingSystem.IsLinux(),
                "Precondition: the host under test disagrees with Windows about rooted paths.");

            Assert.IsFalse(IsConfined(@"\\attacker\share\x.ps1"));
            Assert.IsFalse(IsConfined(@"C:\Windows\Temp\x.ps1"));
        }

        /// <summary>
        /// A script path is stored either plainly or as a JSON document naming one. Both reach
        /// Path.Combine at dispatch, so checking only the plain form would leave the JSON form
        /// as an open door to exactly the same escape.
        /// </summary>
        [TestMethod]
        public void AcceptsAJsonScriptPathNamingARelativePath()
        {
            Assert.IsTrue(IsConfined("""{"ScriptPath":"00 Generic\\DeployMSI.ps1","GenericArguments":[]}"""));
        }

        [TestMethod]
        [DataRow("""{"ScriptPath":"\\\\attacker\\share\\payload.ps1"}""")]
        [DataRow("""{"ScriptPath":"C:\\Windows\\Temp\\payload.ps1"}""")]
        [DataRow("""{"ScriptPath":"..\\..\\payload.ps1"}""")]
        public void RejectsAJsonScriptPathThatEscapesTheRoot(string path)
        {
            Assert.IsFalse(IsConfined(path), "The JSON form reaches Path.Combine exactly as the plain form does.");
        }

        /// <summary>
        /// A document that announces itself as JSON and then will not yield a path cannot be
        /// shown to be confined, and what cannot be shown to be confined is refused.
        /// </summary>
        [TestMethod]
        [DataRow("""{"ScriptPath":42}""")]
        [DataRow("""{"ScriptPath":{"nested":"x"}}""")]
        public void RejectsAJsonScriptPathWhoseScriptPathCannotBeRead(string path)
        {
            Assert.IsFalse(IsConfined(path));
        }

        /// <summary>
        /// Parsed cleanly, names no script. Dispatch combines an empty path with the root.
        /// </summary>
        [TestMethod]
        public void AcceptsAJsonDocumentThatNamesNoScript()
        {
            Assert.IsTrue(IsConfined("""{"GenericArguments":[]}"""));
        }
    }
}
