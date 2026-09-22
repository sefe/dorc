using System;
using System.Linq;
using System.Linq.Expressions;
using Dorc.PersistentData.Model;

namespace Dorc.PersistentData
{
    /// <summary>
    /// EF-translatable tag membership over deploy.DatabaseTag.
    ///
    /// Translates to EXISTS against the join table, which seeks IX_DatabaseTag_Tag.
    /// The delimited column this replaced could only be matched by wrapping it in
    /// delimiters and testing LIKE '%;tag;%' — a leading wildcard, so every lookup
    /// scanned the whole table, and whitespace in the stored value silently made a
    /// row unmatchable.
    ///
    /// The needle is trimmed here rather than only at the API boundary. Stored tags
    /// are trimmed on write and on migration, so an untrimmed needle would silently
    /// match nothing — and the estate contains real values that carried trailing
    /// whitespace under the old delimited column, where SQL's '=' forgave it.
    ///
    /// Callers must still reject null/empty/whitespace tags: an empty needle is not
    /// meaningful and no longer matches anything by accident.
    /// </summary>
    public static class DatabaseTagMatch
    {
        public static Expression<Func<Database, bool>> HasTag(string tag)
        {
            // Trimmed before the expression is built, so this is a parameter value
            // rather than an LTRIM/RTRIM the server would have to evaluate per row.
            var sought = tag?.Trim();

            return db => db.TagLinks.Any(t => t.Tag == sought);
        }
    }
}
