using Dorc.ApiModel;

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
        private const string ScriptBody = "Write-Host 'deploy'";

        private static string BaselineOfContent => ScriptContentHash.ComputeFromScriptText(ScriptBody);

        private const string BaselineOfSomethingElse =
            "9f86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a08";

        [TestMethod]
        [DataRow(ScriptContentVerificationMode.Report)]
        [DataRow(ScriptContentVerificationMode.Enforce)]
        public void UnmodifiedContentExecutes(ScriptContentVerificationMode mode)
        {
            var decision = ScriptContentGate.Evaluate(mode, BaselineOfContent, BaselineOfContent);

            Assert.IsTrue(decision.MayExecute);
            Assert.AreEqual(ScriptContentVerdict.Matched, decision.Verdict);
        }

        [TestMethod]
        public void UppercaseObservedReceiptDigestMatchesLowercaseBaseline()
        {
            var decision = ScriptContentGate.Evaluate(
                ScriptContentVerificationMode.Enforce,
                BaselineOfContent,
                PowerShellInput.Fingerprint(ScriptBody));

            Assert.IsTrue(decision.MayExecute);
            Assert.AreEqual(ScriptContentVerdict.Matched, decision.Verdict);
        }

        [TestMethod]
        public void ModifiedContentDoesNotExecuteWhenEnforcing()
        {
            var decision = ScriptContentGate.EvaluateText(
                ScriptContentVerificationMode.Enforce, BaselineOfSomethingElse, ScriptBody);

            Assert.IsFalse(decision.MayExecute);
            Assert.AreEqual(ScriptContentVerdict.Mismatched, decision.Verdict);
            Assert.IsTrue(decision.Explanation.Contains("has not been executed"));
        }

        /// <summary>
        /// Reporting is the default because enforcing on the day of the release would fail every
        /// deployment whose script had legitimately changed since its baseline was taken, all at
        /// once. The mismatch is still a mismatch — it is recorded, and execution is permitted.
        /// </summary>
        [TestMethod]
        public void ModifiedContentExecutesWhenOnlyReporting()
        {
            var decision = ScriptContentGate.EvaluateText(
                ScriptContentVerificationMode.Report, BaselineOfSomethingElse, ScriptBody);

            Assert.IsTrue(decision.MayExecute);
            Assert.AreEqual(ScriptContentVerdict.Mismatched, decision.Verdict);
            StringAssert.Contains(decision.Explanation, "Execution is permitted in report mode");
            StringAssert.Contains(decision.Explanation, "report rather than enforce");
            Assert.IsFalse(decision.Explanation.Contains("has been executed", StringComparison.Ordinal));
        }

        /// <summary>
        /// In Report mode, an unrecorded script logs and permits execution so the estate can
        /// populate baselines without a flag day.
        /// </summary>
        [TestMethod]
        [DataRow(null)]
        [DataRow("")]
        [DataRow("   ")]
        public void UnrecordedBaselineExecutesInReportMode(string? baseline)
        {
            var decision = ScriptContentGate.Evaluate(
                ScriptContentVerificationMode.Report, baseline!, BaselineOfContent);

            Assert.IsTrue(decision.MayExecute);
            Assert.AreEqual(ScriptContentVerdict.Unrecorded, decision.Verdict);
            StringAssert.Contains(decision.Explanation, "execution is permitted in report mode");
            Assert.IsFalse(decision.Explanation.Contains("has been executed", StringComparison.Ordinal));
        }

        /// <summary>
        /// In Enforce mode, an unrecorded baseline must refuse execution so unbaselined scripts,
        /// unreadable Monitor shares, or path-reset scripts cannot execute tampered share files.
        /// </summary>
        [TestMethod]
        [DataRow(null)]
        [DataRow("")]
        [DataRow("   ")]
        public void UnrecordedBaselineRefusesInEnforceMode(string? baseline)
        {
            var decision = ScriptContentGate.Evaluate(
                ScriptContentVerificationMode.Enforce, baseline!, BaselineOfContent);

            Assert.IsFalse(decision.MayExecute);
            Assert.AreEqual(ScriptContentVerdict.Unrecorded, decision.Verdict);
            Assert.IsTrue(decision.Explanation.Contains("has not been executed"));
        }

        [TestMethod]
        public void UnknownVerificationModeFailsClosed()
        {
            Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
                ScriptContentGate.Evaluate(
                    (ScriptContentVerificationMode)99,
                    BaselineOfContent,
                    BaselineOfContent));

            Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
                ScriptContentGate.EvaluateText(
                    (ScriptContentVerificationMode)(-1),
                    BaselineOfContent,
                    ScriptBody));
        }

        [TestMethod]
        public void MalformedNonEmptyRecordedBaselineRefusesWhenEnforcing()
        {
            var decision = ScriptContentGate.Evaluate(
                ScriptContentVerificationMode.Enforce,
                "not-a-valid-sha256",
                BaselineOfContent);

            Assert.IsFalse(decision.MayExecute);
            Assert.AreEqual(ScriptContentVerdict.Mismatched, decision.Verdict);
        }

        [TestMethod]
        public void InvalidObservedHashFailsClosed()
        {
            Assert.ThrowsExactly<ArgumentNullException>(
                () => ScriptContentGate.Evaluate(
                    ScriptContentVerificationMode.Report, BaselineOfContent, null!));

            Assert.ThrowsExactly<ArgumentException>(
                () => ScriptContentGate.Evaluate(
                    ScriptContentVerificationMode.Report, BaselineOfContent, "invalid-digest"));
        }

        [TestMethod]
        public void NothingIsComparedWhenVerificationIsOff()
        {
            var decision = ScriptContentGate.Evaluate(
                ScriptContentVerificationMode.Off, BaselineOfSomethingElse, "not-evaluated");

            Assert.IsTrue(decision.MayExecute);
            Assert.AreEqual(ScriptContentVerdict.NotVerified, decision.Verdict);
        }

        /// <summary>
        /// A mismatch report is written into the log of a deployment that refused to run the
        /// script. Quoting the difference would put the modified script's content — which is
        /// whatever an attacker chose to write — into that log.
        /// </summary>
        [TestMethod]
        public void TheExplanationCarriesHashesAndNoScriptContent()
        {
            var explanation = ScriptContentGate.EvaluateText(
                ScriptContentVerificationMode.Enforce,
                BaselineOfSomethingElse,
                "Invoke-Expression $env:PAYLOAD").Explanation;

            Assert.IsFalse(explanation.Contains("Invoke-Expression"));
            Assert.IsTrue(explanation.Contains(BaselineOfSomethingElse));
        }
    }
}
