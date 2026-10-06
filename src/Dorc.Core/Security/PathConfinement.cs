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
    /// Parsing and normalisation are left to <see cref="Uri"/>, which reads UNC paths, drive
    /// paths and file URIs alike, resolves "." and ".." segments, and does so the same way on
    /// every platform .NET 8 runs on. System.IO.Path is not used because it answers according
    /// to the host it runs on, and these are Windows paths that may be judged on a Linux host.
    /// </summary>
    public static class PathConfinement
    {
        /// <summary>
        /// Reduces a path or file URI to a comparable absolute form, or null when it cannot
        /// be reduced. A null result means "cannot be shown to be confined" and callers must
        /// treat it as a refusal.
        ///
        /// The result is the local path form: a UNC prefix or a drive specifier, backslash
        /// separators, no trailing separator.
        /// </summary>
        public static string? Canonicalise(string? pathOrUri)
        {
            if (string.IsNullOrWhiteSpace(pathOrUri))
            {
                return null;
            }

            if (!Uri.TryCreate(pathOrUri.Trim(), UriKind.Absolute, out var uri) || !uri.IsFile)
            {
                // Relative, or not a file location at all. Not anchored anywhere, so not
                // confinable.
                return null;
            }

            if (uri.IsUnc && uri.AbsolutePath.Trim('/').Length == 0)
            {
                // A server with no share names no location that can contain anything.
                return null;
            }

            // LocalPath has already had "." and ".." resolved - in their percent-encoded forms
            // too - and escapes decoded. A dot segment that nonetheless survives is refused
            // rather than compared, as a backstop: nothing that still reads as traversal is
            // ever shown to be confined.
            var path = uri.LocalPath.Replace('/', '\\').TrimEnd('\\');

            if (path.Split('\\').Any(segment => segment == "." || segment == ".."))
            {
                return null;
            }

            return path;
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
            return canonicalCandidate.StartsWith(canonicalRoot + '\\', StringComparison.OrdinalIgnoreCase);
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
