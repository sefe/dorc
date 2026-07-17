namespace Dorc.PersistentData.Model
{
    public class CloudResource
    {
        public int Id { get; set; }

        public string Name { get; set; } = null!;

        public string Provider { get; set; } = null!;

        public string ResourceType { get; set; } = null!;

        public string ResourceIdentifier { get; set; } = null!;

        public string? Subscription { get; set; }

        public string? Tags { get; set; }

        public ICollection<Environment> Environments { get; set; } = new List<Environment>();
    }
}
