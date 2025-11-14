namespace Dorc.PersistentData.Model
{
    public class Server
    {
        public int Id { get; set; }

        public string? Name { get; set; }

        public string? OsName { get; set; }

        public string? ApplicationTags { get; set; }

        public DateTime? LastChecked { get; set; }

        public bool? IsReachable { get; set; }

        public ICollection<Daemon> Services { get; set; } = new List<Daemon>();

        public ICollection<Environment> Environments { get; set; } = new List<Environment>();
    }
}