using System;

namespace Dorc.ApiModel
{
    /// <summary>
    /// What verification found. Four outcomes rather than a boolean, because three of them are
    /// not failures and treating them as one would either refuse legitimate deployments or hide
    /// the case that matters.
    /// </summary>
    public enum ScriptContentVerdict
    {
        /// <summary>Verification is switched off. Nothing was computed and nothing was compared.</summary>
        NotVerified,

        /// <summary>
        /// No baseline had been recorded for this script. Permitted under
        /// <see cref="ScriptContentVerificationMode.Report"/> and refused under
        /// <see cref="ScriptContentVerificationMode.Enforce"/>.
        /// </summary>
        Unrecorded,

        /// <summary>The bytes about to execute are the bytes that were recorded.</summary>
        Matched,

        /// <summary>
        /// The bytes about to execute are NOT the bytes that were recorded. The script changed
        /// on the share after its baseline was taken — which is either a promotion nobody
        /// recorded, or the thing this exists to catch.
        /// </summary>
        Mismatched
    }

    /// <summary>
    /// What the gate decided about the content a runner has just read, and why.
    /// </summary>
    public sealed class ScriptContentDecision
    {
        internal ScriptContentDecision(bool mayExecute, ScriptContentVerdict verdict, string explanation)
        {
            MayExecute = mayExecute;
            Verdict = verdict;
            Explanation = explanation;
        }

        /// <summary>False only when the content must not execute.</summary>
        public bool MayExecute { get; }

        public ScriptContentVerdict Verdict { get; }

        /// <summary>
        /// What to log, in every outcome but <see cref="ScriptContentVerdict.Matched"/>.
        /// Contains hashes and no script content: a mismatch report that quoted the difference
        /// would put the modified script into the log of a deployment that refused to run it.
        /// </summary>
        public string Explanation { get; }
    }

    /// <summary>
    /// Decides whether the content a runner has just read may be executed.
    ///
    /// One decision, shared. The two PowerShell runners compile for different frameworks and
    /// share no code but this assembly; two implementations of "may this run" is two answers
    /// waiting to diverge, and the divergence would be silent — one runner refusing what the
    /// other executes, on the same script, depending only on which PowerShell version a
    /// component happens to be pinned to.
    ///
    /// The gate is given the CONTENT, not a path. Re-reading the file to hash it would reopen
    /// the window the whole step exists to close: the caller hashes and executes the same read.
    /// </summary>
    public static class ScriptContentGate
    {
        /// <summary>
        /// Evaluates the hash computed from the single script read against its recorded baseline.
        /// </summary>
        /// <param name="mode">How far verification is taken.</param>
        /// <param name="recordedHash">The baseline, or null when none has been recorded.</param>
        /// <param name="actualHash">The SHA-256 digest of the UTF-8 decoded script text from the single read.</param>
        public static ScriptContentDecision Evaluate(
            ScriptContentVerificationMode mode,
            string recordedHash,
            string actualHash)
        {
            ValidateMode(mode);

            if (mode == ScriptContentVerificationMode.Off)
            {
                return new ScriptContentDecision(
                    true,
                    ScriptContentVerdict.NotVerified,
                    "Script content verification is switched off.");
            }

            if (actualHash == null)
            {
                throw new ArgumentNullException(nameof(actualHash));
            }

            if (!ScriptContentHash.IsWellFormed(actualHash))
            {
                throw new ArgumentException(
                    "The observed script content hash must be a 64-character SHA-256 hex digest.",
                    nameof(actualHash));
            }

            if (string.IsNullOrWhiteSpace(recordedHash))
            {
                if (mode == ScriptContentVerificationMode.Enforce)
                {
                    return new ScriptContentDecision(
                        false,
                        ScriptContentVerdict.Unrecorded,
                        "No content baseline has been recorded for this script, and script content"
                        + " verification is set to enforce so the script has not been executed."
                        + " Record a baseline via PUT /RefDataScripts/{id}/content-hash (or capture"
                        + " baselines in Report mode) before enforcing.");
                }

                return new ScriptContentDecision(
                    true,
                    ScriptContentVerdict.Unrecorded,
                    "No content baseline had been recorded for this script, so there was nothing to"
                    + " verify against; execution is permitted in report mode. Record a baseline via"
                    + " PUT /RefDataScripts/{id}/content-hash or verify that the Monitor can read"
                    + " the confined script share to capture a first-seen baseline.");
            }

            var computed = actualHash.Trim().ToLowerInvariant();
            var baseline = recordedHash.Trim();

            if (ScriptContentHash.Matches(baseline, computed))
            {
                return new ScriptContentDecision(true, ScriptContentVerdict.Matched, string.Empty);
            }

            if (mode == ScriptContentVerificationMode.Enforce)
            {
                return new ScriptContentDecision(
                    false,
                    ScriptContentVerdict.Mismatched,
                    "The script's content does not match its recorded baseline and has not been"
                    + $" executed. Recorded '{baseline}', found '{computed}'. Either the script"
                    + " was promoted without its baseline being re-recorded, or it was modified on"
                    + " the share.");
            }

            return new ScriptContentDecision(
                true,
                ScriptContentVerdict.Mismatched,
                "The script's content does not match its recorded baseline. Execution is permitted"
                + " in report mode because script content verification is set to report rather than"
                + $" enforce. Recorded '{baseline}', found '{computed}'.");
        }

        /// <summary>
        /// Evaluates the decoded script text from a single read against its recorded baseline.
        /// </summary>
        public static ScriptContentDecision EvaluateText(
            ScriptContentVerificationMode mode,
            string recordedHash,
            string scriptText)
        {
            ValidateMode(mode);

            if (mode == ScriptContentVerificationMode.Off)
            {
                return new ScriptContentDecision(
                    true,
                    ScriptContentVerdict.NotVerified,
                    "Script content verification is switched off.");
            }

            if (scriptText == null)
            {
                throw new ArgumentNullException(nameof(scriptText));
            }

            return Evaluate(mode, recordedHash, ScriptContentHash.ComputeFromScriptText(scriptText));
        }

        private static void ValidateMode(ScriptContentVerificationMode mode)
        {
            if (mode != ScriptContentVerificationMode.Off
                && mode != ScriptContentVerificationMode.Report
                && mode != ScriptContentVerificationMode.Enforce)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(mode),
                    mode,
                    "Unknown script content verification mode.");
            }
        }
    }
}
