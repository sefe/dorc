using System;
using System.Linq.Expressions;
using Dorc.PersistentData.Model;

namespace Dorc.PersistentData
{
    /// <summary>
    /// EF-translatable tag membership over Database.Type (docs/database-tags HLPS §3):
    /// the delimiter-wrap pattern matches an exact semicolon-separated entry, never a
    /// substring. A null Type concatenates to ";;" (EF wraps the column in COALESCE;
    /// LINQ-to-Objects concatenates null as empty), so it matches no non-empty tag —
    /// the same outcome as the old whole-string equality. Callers must reject
    /// null/empty/whitespace and ';'-bearing tags at the boundary; entries in stored
    /// values are normalized (trimmed) on write, and legacy padded rows are covered by
    /// the one-time normalization script (U-2).
    /// </summary>
    public static class DatabaseTagMatch
    {
        public static Expression<Func<Database, bool>> HasTag(string tag)
        {
            return db => (";" + db.Type + ";").Contains(";" + tag + ";");
        }
    }
}
