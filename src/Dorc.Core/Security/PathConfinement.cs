using System.Text;
using Dorc.ApiModel;

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
            if (HasNonFileUriScheme(value))
            {
                return null;
            }

            // Configured roots are frequently recorded as file:// URIs while the values
            // compared against them are plain UNC or local paths. Only file: URIs undergo
            // URI parsing and percent-decoding; plain filesystem and UNC paths are never
            // percent-decoded by Windows or SMB, so decoding '%' there would create a
            // parser differential against downstream LocalPath / Win32 consumers.
            if (value.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
            {
                if (!TryConvertFileUriToLocalPath(value, out var converted))
                {
                    return null;
                }

                value = converted;
            }
            else
            {
                if (ContainsPlainPathAmbiguousPercentSequence(value))
                {
                    return null;
                }

                if (SourceHost.IsRemoteOrUnc(value) && !SourceHost.TryExtractRemoteHost(value, out _))
                {
                    return null;
                }

                value = SourceHost.NormalizeExtendedUnc(value);
            }

            if (SourceHost.IsRemoteOrUnc(value) && !SourceHost.TryExtractRemoteHost(value, out _))
            {
                return null;
            }

            value = SourceHost.NormalizeExtendedUnc(value);
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

                if (segment.Contains(':'))
                {
                    // Colon in a non-root segment names a device or alternate data stream.
                    return null;
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

            // Drive: X:\ or bare X:
            if (value.Length >= 2 && char.IsLetter(value[0]) && value[1] == ':')
            {
                if (value.Length > 2 && value[2] != '\\')
                {
                    // Drive-relative (e.g. C:relative\x.ps1), not anchored.
                    return null;
                }

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

        private static bool HasNonFileUriScheme(string value)
        {
            var colonIndex = value.IndexOf(':');
            if (colonIndex <= 0)
            {
                return false;
            }

            if (!char.IsLetter(value[0]))
            {
                return false;
            }

            for (var i = 1; i < colonIndex; i++)
            {
                var ch = value[i];
                if (!char.IsLetterOrDigit(ch) && ch != '+' && ch != '-' && ch != '.')
                {
                    return false;
                }
            }

            var scheme = value[..colonIndex];
            if (string.Equals(scheme, "file", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            // Single-letter 'X:\...' and 'X:/...' are Windows drive paths, whereas 'X://...'
            // is a single-letter URI authority scheme and must be rejected.
            if (scheme.Length == 1)
            {
                return value.Length >= colonIndex + 3
                    && IsSlash(value[colonIndex + 1])
                    && IsSlash(value[colonIndex + 2]);
            }

            return true;
        }

        private static bool TryConvertFileUriToLocalPath(string fileUriText, out string localPath)
        {
            localPath = string.Empty;

            if (ContainsForbiddenFileUriPercentSequence(fileUriText))
            {
                return false;
            }

            if (SourceHost.IsRemoteOrUnc(fileUriText) && !SourceHost.TryExtractRemoteHost(fileUriText, out _))
            {
                return false;
            }

            var normalized = SourceHost.NormalizeExtendedUnc(fileUriText);

            // Extended UNC file: URIs (e.g. file://\\?\UNC\server\share\path) reduce directly
            // to \\server\share\path; reject URI escapes/query/fragment and '..' before accepting.
            if (!normalized.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
            {
                if (fileUriText.Contains('%') || fileUriText.Contains('?') && !ContainsOnlyExtendedUncMarkerQuestionMark(fileUriText) || fileUriText.Contains('#'))
                {
                    return false;
                }

                if (HasDotDotSegment(fileUriText["file:".Length..]) || HasDotDotSegment(normalized))
                {
                    return false;
                }

                localPath = normalized;
                return true;
            }

            if (!Uri.TryCreate(normalized, UriKind.Absolute, out var fileUri)
                || !string.Equals(fileUri.Scheme, Uri.UriSchemeFile, StringComparison.OrdinalIgnoreCase)
                || !fileUri.IsFile)
            {
                return false;
            }

            if (!string.IsNullOrEmpty(fileUri.Query) || !string.IsNullOrEmpty(fileUri.Fragment))
            {
                return false;
            }

            // Reject '..' segments in the raw URI before System.Uri collapses or clamps them.
            var rawAfterScheme = fileUri.OriginalString["file:".Length..];
            var unescapedAfterScheme = Uri.UnescapeDataString(rawAfterScheme);
            if (HasDotDotSegment(rawAfterScheme) || HasDotDotSegment(unescapedAfterScheme))
            {
                return false;
            }

            var resolved = fileUri.LocalPath;
            if (string.IsNullOrWhiteSpace(resolved))
            {
                return false;
            }

            // On non-Windows hosts System.Uri.LocalPath retains a leading '/' before a drive
            // letter (e.g. '/C:/drops/app' for 'file:///C:/drops/app'); strip it so canonical
            // output matches Windows LocalPath ('C:\drops\app') identically on every host OS.
            if (string.IsNullOrEmpty(fileUri.Host)
                && resolved.Length >= 3
                && resolved[0] == '/'
                && char.IsLetter(resolved[1])
                && resolved[2] == ':'
                && (resolved.Length == 3 || IsSlash(resolved[3])))
            {
                resolved = resolved[1..];
            }

            localPath = resolved;
            return true;
        }

        private static bool ContainsForbiddenFileUriPercentSequence(string fileUriText)
        {
            if (fileUriText.Contains("%2f", StringComparison.OrdinalIgnoreCase)
                || fileUriText.Contains("%5c", StringComparison.OrdinalIgnoreCase)
                || fileUriText.Contains("%2e", StringComparison.OrdinalIgnoreCase)
                || fileUriText.Contains("%25", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (!fileUriText.Contains('%'))
            {
                return false;
            }

            var once = Uri.UnescapeDataString(fileUriText);
            var twice = Uri.UnescapeDataString(once);
            return !string.Equals(once, twice, StringComparison.Ordinal);
        }

        /// <summary>
        /// Plain UNC and DOS paths are literal to Win32/SMB, whereas System.Uri implicitly
        /// parses them as file: URIs and percent-decodes all 66 RFC 3986 unreserved octets
        /// (%2D, %2E, %30-%39, %41-%5A, %5F, %61-%7A, %7E). Reject those sequences (plus
        /// encoded path separators) on plain paths so Win32 and Uri consumers cannot diverge.
        /// </summary>
        private static bool ContainsPlainPathAmbiguousPercentSequence(string path)
        {
            for (var i = 0; i <= path.Length - 3; i++)
            {
                if (path[i] != '%')
                {
                    continue;
                }

                if (!TryParseHexByte(path[i + 1], path[i + 2], out var b))
                {
                    continue;
                }

                var ch = (char)b;
                if ((ch >= 'A' && ch <= 'Z')
                    || (ch >= 'a' && ch <= 'z')
                    || (ch >= '0' && ch <= '9')
                    || ch == '-'
                    || ch == '.'
                    || ch == '_'
                    || ch == '~'
                    || ch == '/'
                    || ch == '\\')
                {
                    return true;
                }
            }

            return false;
        }

        private static bool ContainsOnlyExtendedUncMarkerQuestionMark(string fileUriText)
        {
            // An extended-UNC prefix contains exactly one '?' right before '\UNC\' or '/UNC/'.
            var first = fileUriText.IndexOf('?');
            if (first < 0 || fileUriText.IndexOf('?', first + 1) >= 0)
            {
                return false;
            }

            var rest = fileUriText[(first + 1)..];
            return rest.StartsWith(@"\UNC\", StringComparison.OrdinalIgnoreCase)
                || rest.StartsWith("/UNC/", StringComparison.OrdinalIgnoreCase)
                || rest.StartsWith(@"\UNC/", StringComparison.OrdinalIgnoreCase)
                || rest.StartsWith(@"/UNC\", StringComparison.OrdinalIgnoreCase);
        }

        private static bool TryParseHexByte(char high, char low, out byte value)
        {
            value = 0;
            var hi = HexDigitValue(high);
            var lo = HexDigitValue(low);
            if (hi < 0 || lo < 0)
            {
                return false;
            }

            value = (byte)((hi << 4) | lo);
            return true;
        }

        private static int HexDigitValue(char ch) => ch switch
        {
            >= '0' and <= '9' => ch - '0',
            >= 'a' and <= 'f' => ch - 'a' + 10,
            >= 'A' and <= 'F' => ch - 'A' + 10,
            _ => -1
        };

        private static bool HasDotDotSegment(string path)
        {
            return path.Split('/', '\\').Any(segment => segment.Trim() == "..");
        }

        private static bool IsSlash(char ch) => ch == '/' || ch == '\\';

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
