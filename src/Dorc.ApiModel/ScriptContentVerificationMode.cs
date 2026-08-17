namespace Dorc.ApiModel
{
    /// <summary>
    /// How far script content verification is taken.
    ///
    /// Three states rather than a boolean, because the interesting question is not "is it on"
    /// but "does a mismatch stop the deployment". Reporting first and enforcing later is the
    /// same shape as script path confinement, and for the same reason: the population that would
    /// be refused on the day of the release is unknown until something reports it.
    /// </summary>
    public enum ScriptContentVerificationMode
    {
        /// <summary>
        /// No verification and no recording. Exists so an operator can withdraw the control
        /// without a rollback if it misbehaves in a way nobody foresaw. Not a resting state.
        /// </summary>
        Off,

        /// <summary>
        /// Verify, record a first-seen hash, report a mismatch — and execute anyway. The
        /// default. Enforcing on the day of the release would fail every deployment whose script
        /// had legitimately changed since its hash was recorded, all at once, which is the flag
        /// day the sequencing rules exist to prevent.
        /// </summary>
        Report,

        /// <summary>
        /// A mismatch refuses the script. An UNRECORDED hash still does not: a script executed
        /// for the first time after this ships has no recorded hash through nobody's fault, and
        /// refusing it would be arbitrary.
        /// </summary>
        Enforce
    }
}
