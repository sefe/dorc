namespace Dorc.PersistentData.Model
{
    public class Server
    {
        public int Id { get; set; }

        public string? Name { get; set; }

        public string? OsName { get; set; }

        /// <summary>
        /// DEPRECATED delimited column, dual-written until the follow-up release
        /// drops it. <see cref="TagLinks"/> is the source of truth.
        /// </summary>
        public string? Tags { get; set; }

        public ICollection<ServerTag> TagLinks { get; set; } = new List<ServerTag>();

        public ICollection<Daemon> Daemons { get; set; } = new List<Daemon>();

        public ICollection<Environment> Environments { get; set; } = new List<Environment>();
    }
}