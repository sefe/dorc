using Dorc.ApiModel;
using System.Text;

namespace Dorc.Api.Tests.Sources
{
    /// <summary>
    /// The decision the runner makes immediately before executing a script.
    ///
    /// It lives in the shared model assembly and is tested here because the two PowerShell
    /// runners compile for different frameworks and share nothing else. Two implementations of
    /// "may this run" would be two answers waiting to diverge, and the divergence would be
    /// silent — one runner refusing what the other executes, on the same script, depending only
    /// on which PowerShell version a component happens to be pinned to.
    /// </summary>
    [TestClass]
    public class ScriptContentGateTests
    {
        private static readonly byte[] Content = Encoding.UTF8.GetBytes("Write-Host 'deploy'");

        private static string BaselineOfContent => ScriptContentHash.Of(Content);

        private const string BaselineOfSomethingElse =
            "9f86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a08";

        [TestMethod]
        [DataRow(ScriptContentVerificationMode.Report)]
        [DataRow(ScriptContentVerificationMode.Enforce)]
        public void UnmodifiedContentExecutes(ScriptContentVerificationMode mode)
        {
            Assert.IsTrue(ScriptContentGate.MayExecute(
                mode, BaselineOfContent, Content, out var verdict, out _));

            Assert.AreEqual(ScriptContentVerdict.Matched, verdict);
        }

        [TestMethod]
        public void ModifiedContentDoesNotExecuteWhenEnforcing()
        {
            Assert.IsFalse(ScriptContentGate.MayExecute(
                ScriptContentVerificationMode.Enforce, BaselineOfSomethingElse, Content,
                out var verdict, out var explanation));

            Assert.AreEqual(ScriptContentVerdict.Mismatched, verdict);
            Assert.IsTrue(explanation.Contains("has not been executed"));
        }

        /// <summary>
        /// Reporting is the default because enforcing on the day of the release would fail every
        /// deployment whose script had legitimately changed since its baseline was taken, all at
        /// once. The mismatch is still a mismatch — it is recorded, and the script runs.
        /// </summary>
        [TestMethod]
        public void ModifiedContentExecutesWhenOnlyReporting()
        {
            Assert.IsTrue(ScriptContentGate.MayExecute(
                ScriptContentVerificationMode.Report, BaselineOfSomethingElse, Content,
                out var verdict, out var explanation));

            Assert.AreEqual(ScriptContentVerdict.Mismatched, verdict);
            Assert.IsTrue(explanation.Contains("report rather than enforce"));
        }

        /// <summary>
        /// A script executed for the first time after this shipped has no baseline through
        /// nobody's fault. Refusing it would be arbitrary, and it is exactly the case that makes
        /// a naive implementation break dispatch on every legitimate update.
        /// </summary>
        [TestMethod]
        [DataRow(ScriptContentVerificationMode.Report, null)]
        [DataRow(ScriptContentVerificationMode.Report, "")]
        [DataRow(ScriptContentVerificationMode.Enforce, null)]
        [DataRow(ScriptContentVerificationMode.Enforce, "   ")]
        public void AnUnrecordedBaselineIsNeverARefusal(
            ScriptContentVerificationMode mode, string? baseline)
        {
            Assert.IsTrue(ScriptContentGate.MayExecute(
                mode, baseline!, Content, out var verdict, out _));

            Assert.AreEqual(ScriptContentVerdict.Unrecorded, verdict);
        }

        [TestMethod]
        public void NothingIsComparedWhenVerificationIsOff()
        {
            Assert.IsTrue(ScriptContentGate.MayExecute(
                ScriptContentVerificationMode.Off, BaselineOfSomethingElse, Content,
                out var verdict, out _));

            Assert.AreEqual(ScriptContentVerdict.NotVerified, verdict);
        }

        /// <summary>
        /// A mismatch report is written into the log of a deployment that refused to run the
        /// script. Quoting the difference would put the modified script's content — which is
        /// whatever an attacker chose to write — into that log.
        /// </summary>
        [TestMethod]
        public void TheExplanationCarriesHashesAndNoScriptContent()
        {
            ScriptContentGate.MayExecute(
                ScriptContentVerificationMode.Enforce, BaselineOfSomethingElse,
                Encoding.UTF8.GetBytes("Invoke-Expression $env:PAYLOAD"),
                out _, out var explanation);

            Assert.IsFalse(explanation.Contains("Invoke-Expression"));
            Assert.IsTrue(explanation.Contains(BaselineOfSomethingElse));
        }
    }

    /// <summary>
    /// Verification and execution have to operate on a single read: reading the file twice, once
    /// to hash and once to run, leaves a window in which the file can be substituted — which is
    /// the window the whole step exists to close. Decoding therefore happens from the bytes that
    /// were verified, and it has to decode them the way File.ReadAllText did before, or scripts
    /// would change behaviour for reasons unrelated to this.
    /// </summary>
    [TestClass]
    public class ScriptTextTests
    {
        [TestMethod]
        public void DecodesUtf8WithoutAByteOrderMark()
        {
            var content = Encoding.UTF8.GetBytes("Write-Host 'héllo'");

            Assert.AreEqual("Write-Host 'héllo'", ScriptText.Decode(content));
        }

        [TestMethod]
        public void HonoursAByteOrderMarkAndDoesNotLeaveItInTheText()
        {
            var content = Encoding.UTF8.GetPreamble()
                .Concat(Encoding.UTF8.GetBytes("Write-Host 'x'"))
                .ToArray();

            Assert.AreEqual("Write-Host 'x'", ScriptText.Decode(content));
        }

        [TestMethod]
        public void DecodesAnAlternativeEncodingAnnouncedByItsMark()
        {
            var content = Encoding.Unicode.GetPreamble()
                .Concat(Encoding.Unicode.GetBytes("Write-Host 'x'"))
                .ToArray();

            Assert.AreEqual("Write-Host 'x'", ScriptText.Decode(content));
        }
    }
}
