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
        /// No baseline had been recorded for this script. Never a refusal: a script executed for
        /// the first time after this shipped has no baseline through nobody's fault.
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
        /// Evaluates the content against its recorded baseline.
        /// </summary>
        /// <param name="mode">How far verification is taken.</param>
        /// <param name="recordedHash">The baseline, or null when none has been recorded.</param>
        /// <param name="content">The exact bytes about to be executed.</param>
        /// <param name="verdict">What was found.</param>
        /// <param name="explanation">
        /// What to log, in every outcome but <see cref="ScriptContentVerdict.Matched"/>.
        /// Contains hashes and no script content: a mismatch report that quoted the difference
        /// would put the modified script into the log of a deployment that refused to run it.
        /// </param>
        /// <returns>False only when the content must not execute.</returns>
        public static bool MayExecute(
            ScriptContentVerificationMode mode,
            string recordedHash,
            byte[] content,
            out ScriptContentVerdict verdict,
            out string explanation)
        {
            explanation = string.Empty;

            if (mode == ScriptContentVerificationMode.Off)
            {
                verdict = ScriptContentVerdict.NotVerified;
                explanation = "Script content verification is switched off.";
                return true;
            }

            if (content == null)
            {
                throw new ArgumentNullException(nameof(content));
            }

            if (string.IsNullOrWhiteSpace(recordedHash))
            {
                verdict = ScriptContentVerdict.Unrecorded;
                explanation =
                    "No content baseline had been recorded for this script, so there was nothing to"
                    + " verify against. The baseline is recorded from this execution and verified"
                    + " from the next one.";
                return true;
            }

            var computed = ScriptContentHash.Of(content);

            if (ScriptContentHash.Matches(recordedHash, computed))
            {
                verdict = ScriptContentVerdict.Matched;
                return true;
            }

            verdict = ScriptContentVerdict.Mismatched;

            if (mode == ScriptContentVerificationMode.Enforce)
            {
                explanation =
                    "The script's content does not match its recorded baseline and has not been"
                    + $" executed. Recorded '{recordedHash}', found '{computed}'. Either the script"
                    + " was promoted without its baseline being re-recorded, or it was modified on"
                    + " the share.";
                return false;
            }

            explanation =
                "The script's content does not match its recorded baseline. It has been executed"
                + " because script content verification is set to report rather than enforce."
                + $" Recorded '{recordedHash}', found '{computed}'.";
            return true;
        }
    }
}
