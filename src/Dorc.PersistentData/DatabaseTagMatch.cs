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
    /// Callers must still reject null/empty/whitespace tags at the boundary: an
    /// empty needle is not meaningful and no longer matches anything by accident.
    /// </summary>
    public static class DatabaseTagMatch
    {
        public static Expression<Func<Database, bool>> HasTag(string tag)
        {
            return db => db.TagLinks.Any(t => t.Tag == tag);
        }
    }
}
