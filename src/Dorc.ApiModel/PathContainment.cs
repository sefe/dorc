using System;
using System.IO;

namespace Dorc.ApiModel
{
    /// <summary>
    /// Guards file operations against path traversal (CWE-22): resolves a candidate
    /// path against a permitted root directory and rejects any result that escapes it.
    /// Returns the canonical full path when it is safely contained.
    /// </summary>
    public static class PathContainment
    {
        /// <summary>
        /// Resolves <paramref name="relativePath"/> against <paramref name="rootDirectory"/> and
        /// returns the canonical full path, provided it stays within the root. Throws
        /// <see cref="UnauthorizedAccessException"/> if the resolved path escapes the root
        /// (e.g. via "..\" segments or an absolute/rooted path that overrides the root).
        /// </summary>
        public static string ResolveWithinRoot(string rootDirectory, string relativePath)
        {
            if (string.IsNullOrEmpty(rootDirectory))
                throw new ArgumentException("Root directory must be provided.", nameof(rootDirectory));
            if (string.IsNullOrEmpty(relativePath))
                throw new ArgumentException("Path must be provided.", nameof(relativePath));

            var rootFull = Path.GetFullPath(rootDirectory);
            var separator = Path.DirectorySeparatorChar.ToString();
            var rootWithSeparator = rootFull.EndsWith(separator, StringComparison.Ordinal)
                ? rootFull
                : rootFull + separator;

            // A rooted/absolute relativePath would override the root entirely, so reject it
            // explicitly rather than relying on the containment check below to catch the escape.
            if (Path.IsPathRooted(relativePath))
                throw new UnauthorizedAccessException(
                    "Path '" + relativePath + "' must be relative to the root directory '" + rootFull + "'.");

            // relativePath is now guaranteed non-rooted, so concatenate it under the
            // separator-terminated root and let GetFullPath canonicalise any ".." segments. (We
            // avoid Path.Combine — whose rooted-override behaviour static analysis flags — and
            // Path.Join, which is unavailable on this netstandard2.0 / net48 target.)
            var resolved = Path.GetFullPath(rootWithSeparator + relativePath);

            if (string.Equals(resolved, rootFull, StringComparison.OrdinalIgnoreCase))
                return resolved;

            if (!resolved.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
                throw new UnauthorizedAccessException(
                    "Resolved path '" + resolved + "' escapes the permitted root directory '" + rootFull + "'.");

            return resolved;
        }
    }
}
