using System;
using System.Collections.Generic;
using System.Linq;

namespace Dorc.ApiModel
{
    /// <summary>
    /// Conversion between a tag set and the semicolon-separated string form.
    ///
    /// Tags are a set: stored as rows in deploy.DatabaseTag / deploy.ServerTag and
    /// carried as arrays through the API. The delimited form survives at three
    /// edges only — the deprecated columns that are dual-written for one release,
    /// the deployment-variable payload whose shape is a contract with PowerShell
    /// deploy scripts, and the usp_Insert_*_Detail parameters.
    ///
    /// Entries are trimmed, empties dropped, order preserved, duplicates deduped
    /// Ordinal keep-first. An individual tag never contains the delimiter.
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
                .Distinct(StringComparer.Ordinal)
                .ToArray();
        }

        /// <summary>
        /// Normalize a tag set: trim, drop empties, dedup Ordinal keeping the first
        /// occurrence, preserve order. Null in, empty out.
        /// </summary>
        public static string[] Normalize(IEnumerable<string> tags)
        {
            if (tags == null)
                return Array.Empty<string>();

            return tags.Where(t => t != null)
                .Select(t => t.Trim())
                .Where(t => t.Length > 0)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
        }

        /// <summary>
        /// Render a tag set in the delimited form the deprecated columns and the
        /// deployment-variable payload still use. Empty renders as null, matching
        /// the column's "no tags" representation.
        /// </summary>
        public static string Join(IEnumerable<string> tags)
        {
            var normalized = Normalize(tags);
            return normalized.Length == 0 ? null : string.Join(Delimiter.ToString(), normalized);
        }

        public static bool HasTag(IEnumerable<string> tags, string tag)
        {
            if (string.IsNullOrWhiteSpace(tag) || tags == null)
                return false;

            var sought = tag.Trim();
            return tags.Any(t => string.Equals(t, sought, StringComparison.Ordinal));
        }
    }
}
