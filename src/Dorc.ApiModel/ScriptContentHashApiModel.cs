namespace Dorc.ApiModel
{
    /// <summary>
    /// What a caller sends to record a script's content hash.
    ///
    /// A wrapper rather than a bare string body so that the property is named on the wire. A
    /// promotion pipeline posting a 64-character string with no field name around it is a
    /// message whose meaning depends entirely on which endpoint it was sent to, and the cost of
    /// getting that wrong is a script baselined to the wrong content.
    /// </summary>
    public class ScriptContentHashApiModel
    {
        /// <summary>
        /// SHA-256, hexadecimal. Null or empty clears the recorded hash, which means the next
        /// execution records what it finds.
        /// </summary>
        public string ContentHash { get; set; }

        /// <summary>
        /// Optional path the caller expects the script record to still target. When supplied and
        /// non-whitespace, recording is refused with 409 Conflict if the live script path differs,
        /// preventing a promotion for an older path from baselining a retargeted script.
        /// </summary>
        public string ExpectedPath { get; set; }
    }
}
