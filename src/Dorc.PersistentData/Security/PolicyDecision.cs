namespace Dorc.PersistentData.Security
{
    /// <summary>
    /// The answer a security policy gives about a value: allowed, or refused for a stated
    /// reason.
    ///
    /// The reason is written in terms a caller can show to the user who supplied the value,
    /// and completes a sentence of the form "... cannot be accepted, because {Reason}".
    /// </summary>
    public sealed class PolicyDecision
    {
        private static readonly PolicyDecision Allowed_ = new PolicyDecision(true, string.Empty);

        private PolicyDecision(bool allowed, string reason)
        {
            Allowed = allowed;
            Reason = reason;
        }

        public bool Allowed { get; }

        /// <summary>Why the value was refused. Empty when it was allowed.</summary>
        public string Reason { get; }

        public static PolicyDecision Allow() => Allowed_;

        public static PolicyDecision Refuse(string reason)
        {
            if (string.IsNullOrWhiteSpace(reason))
            {
                throw new ArgumentException("A refusal must say why.", nameof(reason));
            }

            return new PolicyDecision(false, reason);
        }
    }
}
