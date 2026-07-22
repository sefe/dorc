using System;
using System.Collections.Generic;
using System.Linq;

namespace Dorc.ApiModel
{
    /// <summary>
    /// The semicolon-separated tag string convention (docs/database-tags HLPS §3):
    /// entries are trimmed, empties dropped, order preserved, duplicates deduped
    /// Ordinal keep-first. A null/empty/whitespace value means "no tags" and matches
    /// nothing; an individual tag never contains the delimiter.
    /// </summary>
    public static class TagString
    {
        public const char Delimiter = ';';

        public static string[] Split(string joined)
        {
            if (string.IsNullOrWhiteSpace(joined))
                return Array.Empty<string>();
            return joined.Split(Delimiter)
                .Select(t => t.Trim())
                .Where(t => t.Length > 0)
                .ToArray();
        }

        public static bool HasTag(string joined, string tag)
        {
            if (string.IsNullOrWhiteSpace(tag))
                return false;
            var sought = tag.Trim();
            return Split(joined).Any(t => string.Equals(t, sought, StringComparison.Ordinal));
        }

        public static string Normalize(string joined)
        {
            // Distinct is documented to keep the first occurrence in source order,
            // preserving the keep-first, order-stable dedup contract.
            var seen = Split(joined).Distinct(StringComparer.Ordinal).ToArray();

            return seen.Length == 0 ? null : string.Join(Delimiter.ToString(), seen);
        }
    }
}
