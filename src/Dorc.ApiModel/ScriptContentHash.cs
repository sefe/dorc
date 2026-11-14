using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Dorc.ApiModel
{
    /// <summary>
    /// The canonical identity of a PowerShell script's content, as recorded and as verified.
    ///
    /// Lives in the shared <c>netstandard2.0</c> model assembly so the API, Monitor, .NET 8
    /// PowerShell runner, and .NET Framework 4.8 PowerShell runner share a single definition.
    ///
    /// Canonical form: 64-character lower-case hexadecimal SHA-256 over
    /// <c>Encoding.UTF8.GetBytes(scriptText)</c>, where <c>scriptText</c> is the decoded script
    /// text produced by <see cref="File.ReadAllText(string)"/> (which consumes any UTF-8 or
    /// UTF-16 byte-order mark preamble) and handed directly to <c>PowerShell.AddScript</c>.
    /// This aligns identically with <see cref="PowerShellInput.Fingerprint"/> on
    /// <c>PowerShellExecutionReceipt.TextSha256</c> (modulo case) and allows the runner to read
    /// a network-share script once via <see cref="ReadAndHash"/>, verify its baseline, pass the
    /// in-memory text to <c>AddScript</c>, and record execution evidence without a second read.
    /// </summary>
    public static class ScriptContentHash
    {
        /// <summary>
        /// Computes the lower-case 64-character hexadecimal SHA-256 of the UTF-8 encoding of
        /// the decoded script text that <c>PowerShell.AddScript</c> executes.
        /// </summary>
        public static string ComputeFromScriptText(string scriptText)
        {
            if (scriptText == null)
            {
                throw new ArgumentNullException(nameof(scriptText));
            }

            using (var sha256 = SHA256.Create())
            {
                return ToHex(sha256.ComputeHash(Encoding.UTF8.GetBytes(scriptText)));
            }
        }

        /// <summary>
        /// Reads <paramref name="filePath"/> once (decoding any UTF-8 or UTF-16 BOM preamble)
        /// and returns both the exact decoded script text and its canonical lower-case SHA-256
        /// content hash, avoiding a second share read between verification,
        /// <c>PowerShell.AddScript</c>, and execution receipt capture.
        /// </summary>
        public static (string ScriptText, string Hash) ReadAndHash(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                throw new ArgumentException("A script file path is required.", nameof(filePath));
            }

            if (filePath.IndexOf('\0') >= 0)
            {
                throw new ArgumentException("Script file path must not contain NUL characters.", nameof(filePath));
            }

            var directoryPath = Path.GetDirectoryName(filePath);
            var fileName = Path.GetFileName(filePath);
            if (string.IsNullOrEmpty(directoryPath)
                || string.IsNullOrEmpty(fileName)
                || fileName == "."
                || fileName == ".."
                || directoryPath.Contains("..")
                || !IsFullyQualifiedPath(directoryPath))
            {
                throw new ArgumentException(
                    "Script file path must be a fully qualified path without parent-directory traversal segments.",
                    nameof(filePath));
            }

            var baseFull = Path.GetFullPath(directoryPath);
            var canonicalPath = Path.GetFullPath(Path.Combine(baseFull, fileName));
            var prefix = baseFull.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!canonicalPath.StartsWith(prefix, StringComparison.Ordinal))
            {
                throw new ArgumentException("Invalid file path", nameof(filePath));
            }

            using (var stream = new FileStream(canonicalPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true))
            {
                var scriptText = reader.ReadToEnd();
                return (scriptText, ComputeFromScriptText(scriptText));
            }
        }

        private static bool IsFullyQualifiedPath(string path)
        {
            if (Path.DirectorySeparatorChar == '/')
            {
                return path.Length >= 1 && path[0] == '/';
            }

            // Windows UNC or extended/device path (\\server\share\..., \\?\C:\..., \??\UNC\...)
            if ((path.Length >= 2 && path[0] == '\\' && path[1] == '\\')
                || (path.Length >= 4 && path[0] == '\\' && path[1] == '?' && path[2] == '?' && path[3] == '\\'))
            {
                return true;
            }

            // Windows drive-qualified path (C:\... or C:/...)
            return path.Length >= 3
                && ((path[0] >= 'A' && path[0] <= 'Z') || (path[0] >= 'a' && path[0] <= 'z'))
                && path[1] == ':'
                && (path[2] == '\\' || path[2] == '/');
        }

        /// <summary>
        /// Reads <paramref name="filePath"/> once via <see cref="File.ReadAllText(string)"/> and
        /// returns its canonical lower-case 64-character hexadecimal SHA-256 content hash.
        /// </summary>
        public static string ComputeFromFile(string filePath)
        {
            return ReadAndHash(filePath).Hash;
        }

        /// <summary>
        /// Whether a recorded hash and a computed one name the same script content.
        ///
        /// Case-insensitive ordinal comparison over well-formed 64-character hexadecimal SHA-256
        /// digests; returns <c>false</c> when either side is absent or malformed.
        /// </summary>
        public static bool Matches(string recorded, string computed)
        {
            if (!IsWellFormed(recorded) || !IsWellFormed(computed))
            {
                return false;
            }

            return string.Equals(recorded.Trim(), computed.Trim(), StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Whether a value is strictly a 64-character hexadecimal SHA-256 digest (ignoring
        /// surrounding whitespace).
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

        /// <summary>
        /// Normalizes <paramref name="value"/> to lower-case 64-character hexadecimal, or to
        /// <c>null</c> when <paramref name="value"/> is null, empty, or whitespace. Returns
        /// <c>false</c> when a non-empty value is not a well-formed SHA-256 hex digest.
        /// </summary>
        public static bool TryNormalize(string value, out string normalized)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                normalized = null;
                return true;
            }

            if (!IsWellFormed(value))
            {
                normalized = null;
                return false;
            }

            normalized = value.Trim().ToLowerInvariant();
            return true;
        }

        /// <summary>
        /// Preserves <paramref name="recordedHash"/> across an in-place script edit or
        /// copy-on-write clone only when the script path is unchanged under exact ordinal
        /// comparison; clears to <c>null</c> whenever the path changes so a new target file
        /// never inherits the old file's baseline.
        /// </summary>
        public static string RetainForPath(string recordedHash, string previousPath, string nextPath)
        {
            if (string.IsNullOrWhiteSpace(recordedHash)
                || string.IsNullOrWhiteSpace(previousPath)
                || string.IsNullOrWhiteSpace(nextPath))
            {
                return null;
            }

            if (!string.Equals(previousPath, nextPath, StringComparison.Ordinal)
                || !string.Equals(previousPath.Trim(), nextPath.Trim(), StringComparison.Ordinal))
            {
                return null;
            }

            return recordedHash;
        }

        private static string ToHex(byte[] hash)
        {
            // Convert.ToHexString does not exist on .NET Framework 4.8, which this assembly
            // compiles for, but BitConverter does everywhere.
            return BitConverter.ToString(hash).Replace("-", string.Empty).ToLowerInvariant();
        }
    }
}
