namespace Dorc.PersistentData.Model
{
    public class Container
    {
        public int Id { get; set; }

        public string Name { get; set; } = null!;

        public string Image { get; set; } = null!;

        public string? Registry { get; set; }

        public string? HostServerName { get; set; }

        public string? Tags { get; set; }

        public ICollection<Environment> Environments { get; set; } = new List<Environment>();
    }
}
