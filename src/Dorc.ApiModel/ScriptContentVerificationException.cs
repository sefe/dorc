using System;

namespace Dorc.ApiModel
{
    /// <summary>
    /// Thrown when a script's content does not match its recorded baseline and verification is
    /// being enforced.
    ///
    /// A type of its own rather than a bare exception so that the refusal is distinguishable
    /// from the script having failed. Those are different outcomes for whoever reads the
    /// deployment result: one says the deployment did not do what it was meant to, the other
    /// says the thing it was about to run was not what it was supposed to be.
    /// </summary>
    public class ScriptContentVerificationException : Exception
    {
        public ScriptContentVerificationException(string message)
            : base(message)
        {
        }

        public ScriptContentVerificationException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}
