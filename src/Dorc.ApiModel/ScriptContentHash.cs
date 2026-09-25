using System;
using System.Linq;
using System.Security.Cryptography;

namespace Dorc.ApiModel
{
    /// <summary>
    /// The identity of a script's content, as recorded and as verified.
    ///
    /// This lives in the shared model assembly because both ends need it and they do not share
    /// anything else: the API records a hash, and two PowerShell runners on two different
    /// frameworks - .NET 8 and .NET Framework 4.8 - verify one. Two implementations of "the
    /// hash of this file" is two answers waiting to disagree, and the failure mode of a
    /// disagreement is a refusal to run a script that was never modified.
    ///
    /// Computed over the exact bytes, never over decoded text. Decoding introduces the runner's
    /// encoding assumptions into the answer, and those are precisely what differs between the
    /// two frameworks. The bytes are what execute, so the bytes are what is hashed.
    /// </summary>
    public static class ScriptContentHash
    {
        /// <summary>
        /// The hash of the content that is about to be executed.
        /// </summary>
        /// <param name="content">
        /// The bytes as read. The caller must hash and execute the SAME read - re-reading the
        /// file to hash it reopens the window this check exists to close.
        /// </param>
        public static string Of(byte[] content)
        {
            if (content == null)
            {
                throw new ArgumentNullException(nameof(content));
            }

            using (var sha256 = SHA256.Create())
            {
                return ToHex(sha256.ComputeHash(content));
            }
        }

        /// <summary>
        /// Whether a recorded hash and a computed one name the same content.
        ///
        /// Case-insensitive: a hash recorded by a pipeline is as likely to arrive upper-case as
        /// lower, and a comparison that refused a script because someone's tool shouted its hex
        /// would be a self-inflicted outage rather than a security property.
        /// </summary>
        public static bool Matches(string recorded, string computed)
        {
            if (string.IsNullOrWhiteSpace(recorded) || string.IsNullOrWhiteSpace(computed))
            {
                return false;
            }

            return string.Equals(recorded.Trim(), computed.Trim(), StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Whether a value is shaped like a SHA-256 hash. Used at the write path so that a
        /// mistyped or truncated hash is refused when it is recorded rather than discovered as a
        /// mismatch on the next deployment, when the report says the script changed and it did
        /// not.
        /// </summary>
        public static bool IsWellFormed(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            var candidate = value.Trim();

            if (candidate.Length != 64)
            {
                return false;
            }

            return candidate.All(Uri.IsHexDigit);
        }

        private static string ToHex(byte[] hash)
        {
            // Convert.ToHexString does not exist on .NET Framework 4.8, which this assembly
            // compiles for, but BitConverter does everywhere.
            return BitConverter.ToString(hash).Replace("-", string.Empty).ToLowerInvariant();
        }
    }
}
