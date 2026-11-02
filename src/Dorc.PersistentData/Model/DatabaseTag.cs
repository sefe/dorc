namespace Dorc.PersistentData.Model
{
    /// <summary>
    /// One tag on one database. Tags are rows rather than a delimited string so a
    /// tag-first lookup is an index seek — the delimited form could only ever be
    /// matched with a leading-wildcard LIKE, which forces a scan.
    /// </summary>
    public class DatabaseTag
    {
        public int DatabaseId { get; set; }

        public string Tag { get; set; } = null!;

        public virtual Database Database { get; set; } = null!;
    }
}
