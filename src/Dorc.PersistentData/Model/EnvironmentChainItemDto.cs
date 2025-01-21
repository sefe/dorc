namespace Dorc.PersistentData.Model
{
    public class EnvironmentChainItemDto
    {
        public int Id { get; set; }
        public int? ParentId { get; set; }
        public string Name { get; set; }
        public bool IsProd { get; set; }
        public bool Secure { get; set; }
        public string Owner { get; set; }
        public Guid ObjectId { get; set; }
    }
}
