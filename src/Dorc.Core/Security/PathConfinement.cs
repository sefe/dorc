using System.Text;

namespace Dorc.Core.Security
{
    /// <summary>
    /// Decides whether a path lies beneath a permitted root.
    ///
    /// Several places in DOrc take a caller-supplied path, combine it with a configured root
    /// and then execute or read from the result. Path.Combine does not confine: given a
    /// rooted second argument it discards the first entirely, so a value such as
    /// \\elsewhere\share\x.ps1 escapes the root while appearing to be relative to it. String
    /// prefix comparison does not confine either - it accepts traversal segments and is
    /// defeated by a root that is a textual prefix of an unrelated sibling directory
    /// (\\host\scripts-archive against a root of \\host\scripts).
    ///
    /// Normalisation is performed here rather than through System.IO.Path, deliberately.
    /// The paths being judged are Windows and UNC paths, but the test suite runs on Linux,
    /// where Path.IsPathRooted rejects \\host\share and GetFullPath leaves backslashes
    /// untouched. A confinement check that answered differently on the test host than in
    /// production would be worse than none, so both separators are treated as separators and
    /// traversal is resolved directly.
    /// </summary>
    public static class PathConfinement
    {
        /// <summary>
        /// Reduces a path or file URI to a comparable absolute form, or null when it cannot
        /// be reduced. A null result means "cannot be shown to be confined" and callers must
        /// treat it as a refusal.
        ///
        /// The result uses backslash separators and retains its root marker: a UNC prefix, a
        /// drive specifier, or a leading slash for POSIX paths.
        /// </summary>
        public static string? Canonicalise(string? pathOrUri)
        {
            if (string.IsNullOrWhiteSpace(pathOrUri))
            {
                return null;
            }

            var value = pathOrUri.Trim();

            // Configured roots are frequently recorded as file:// URIs while the values
            // compared against them are plain UNC or local paths. Reduce both to a path.
            if (Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.IsFile)
            {
                // LocalPath is platform-dependent for UNC, so rebuild from the components.
                value = string.IsNullOrEmpty(uri.Host)
                    ? Uri.UnescapeDataString(uri.AbsolutePath)
                    : @"\\" + uri.Host + Uri.UnescapeDataString(uri.AbsolutePath);
            }

            value = value.Replace('/', '\\');

            var root = ExtractRoot(value, out var remainder);
            if (root == null)
            {
                // Relative, or otherwise not anchored anywhere. Not confinable.
                return null;
            }

            var segments = new List<string>();
            foreach (var segment in remainder.Split('\\', StringSplitOptions.RemoveEmptyEntries))
            {
                if (segment == ".")
                {
                    continue;
                }

                if (segment == "..")
                {
                    if (segments.Count == 0)
                    {
                        // Traversal above the root. Not a path that can be shown to be within
                        // anything.
                        return null;
                    }

                    segments.RemoveAt(segments.Count - 1);
                    continue;
                }

                segments.Add(segment);
            }

            var canonical = new StringBuilder(root);
            for (var i = 0; i < segments.Count; i++)
            {
                if (i > 0 || !root.EndsWith('\\'))
                {
                    canonical.Append('\\');
                }

                canonical.Append(segments[i]);
            }

            return canonical.ToString();
        }

        /// <summary>
        /// Splits the anchoring prefix from the rest of the path. Returns null when the path
        /// is not anchored, which makes it unconfinable.
        /// </summary>
        private static string? ExtractRoot(string value, out string remainder)
        {
            remainder = string.Empty;

            // UNC: \\server\share
            if (value.StartsWith(@"\\", StringComparison.Ordinal))
            {
                var parts = value[2..].Split('\\', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 2)
                {
                    // A server with no share names no location that can contain anything.
                    return null;
                }

                remainder = string.Join('\\', parts.Skip(2));
                return @"\\" + parts[0] + '\\' + parts[1];
            }

            // Drive: X:\ or X:
            if (value.Length >= 2 && char.IsLetter(value[0]) && value[1] == ':')
            {
                remainder = value.Length > 2 ? value[2..] : string.Empty;
                return value[..2].ToUpperInvariant();
            }

            // POSIX absolute, retained so the helper is exercisable on non-Windows hosts.
            if (value.StartsWith('\\'))
            {
                remainder = value[1..];
                return "\\";
            }

            return null;
        }

        /// <summary>
        /// Whether <paramref name="candidate"/> is <paramref name="root"/> itself or lies
        /// beneath it. False when either side cannot be canonicalised.
        /// </summary>
        public static bool IsWithin(string? candidate, string? root)
        {
            var canonicalCandidate = Canonicalise(candidate);
            var canonicalRoot = Canonicalise(root);

            if (canonicalCandidate == null || canonicalRoot == null)
            {
                return false;
            }

            // Windows paths and UNC shares are case-insensitive.
            if (string.Equals(canonicalCandidate, canonicalRoot, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            // Compared with the separator appended so that a root of \\host\scripts does not
            // admit \\host\scripts-archive, which a bare prefix test would.
            var rootWithSeparator = canonicalRoot.EndsWith('\\') ? canonicalRoot : canonicalRoot + '\\';

            return canonicalCandidate.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Whether the candidate lies beneath any permitted root. False when the set is empty
        /// - an absent allow-list confines nothing and must not admit everything.
        /// </summary>
        public static bool IsWithinAny(string? candidate, IEnumerable<string>? roots)
        {
            return roots != null && roots.Any(root => IsWithin(candidate, root));
        }

        /// <summary>
        /// Splits a semicolon-delimited list of roots, as stored on a project's artefacts URL.
        /// </summary>
        public static IEnumerable<string> SplitRoots(string? delimitedRoots)
        {
            if (string.IsNullOrWhiteSpace(delimitedRoots))
            {
                return Array.Empty<string>();
            }

            return delimitedRoots
                .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }
    }
}
