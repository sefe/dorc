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
    /// keep-first. An individual tag never contains the delimiter.
    /// </summary>
    public static class TagString
    {
        public const char Delimiter = ';';

        /// <summary>
        /// Tag identity. Case-insensitive because that is what the storage enforces:
        /// PK_DatabaseTag / PK_ServerTag are keyed on (EntityId, Tag) under the
        /// database's SQL_Latin1_General_CP1_CI_AS collation, so 'Endur' and 'ENDUR'
        /// are one tag there and cannot both exist.
        ///
        /// Comparing Ordinal here instead would let a set like ["Endur","ENDUR"] pass
        /// validation and dedup, then violate the primary key on insert — a 500 the
        /// caller cannot act on. It also means a casing-only edit to an existing tag
        /// is a no-op rather than a delete-and-reinsert of the same key.
        /// </summary>
        public static readonly StringComparer Comparer = StringComparer.OrdinalIgnoreCase;

        public static string[] Split(string joined)
        {
            if (string.IsNullOrWhiteSpace(joined))
                return Array.Empty<string>();

            return joined.Split(Delimiter)
                .Select(t => t.Trim())
                .Where(t => t.Length > 0)
                .Distinct(Comparer)
                .ToArray();
        }

        /// <summary>
        /// Normalize a tag set: trim, drop empties, dedup (see Comparer) keeping the first
        /// occurrence, preserve order. Null in, empty out.
        /// </summary>
        public static string[] Normalize(IEnumerable<string> tags)
        {
            if (tags == null)
                return Array.Empty<string>();

            return tags.Where(t => t != null)
                .Select(t => t.Trim())
                .Where(t => t.Length > 0)
                .Distinct(Comparer)
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

        /// <summary>
        /// Exact set membership: does the set contain this tag?
        /// </summary>
        public static bool HasTag(IEnumerable<string> tags, string tag)
        {
            if (string.IsNullOrWhiteSpace(tag) || tags == null)
                return false;

            var sought = tag.Trim();
            return tags.Any(t => Comparer.Equals(t, sought));
        }

        /// <summary>
        /// Substring membership: does any tag in the set CONTAIN this fragment?
        ///
        /// Deliberately named apart from <see cref="HasTag"/>. While tags were one
        /// delimited string, callers wrote <c>tags.Contains(x)</c> and got substring
        /// matching from string.Contains. As an array the identical expression
        /// compiles to exact equality instead, silently narrowing the match with no
        /// compiler error — which is how the Endur post-restore refresh lost its
        /// matches. Callers that mean substring say so by calling this.
        /// </summary>
        public static bool HasTagContaining(IEnumerable<string> tags, string fragment)
        {
            if (string.IsNullOrEmpty(fragment) || tags == null)
                return false;

            return tags.Any(t => t != null && t.IndexOf(fragment, StringComparison.Ordinal) >= 0);
        }
    }
}
