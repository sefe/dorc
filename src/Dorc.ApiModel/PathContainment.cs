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

            // Path.Combine lets an absolute or rooted relativePath override the root entirely,
            // so the canonicalised result is what must be validated, not the raw inputs.
            var resolved = Path.GetFullPath(Path.Combine(rootFull, relativePath));

            if (string.Equals(resolved, rootFull, StringComparison.OrdinalIgnoreCase))
                return resolved;

            if (!resolved.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
                throw new UnauthorizedAccessException(
                    "Resolved path '" + resolved + "' escapes the permitted root directory '" + rootFull + "'.");

            return resolved;
        }
    }
}
