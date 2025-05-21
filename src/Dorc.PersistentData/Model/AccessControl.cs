namespace Dorc.PersistentData.Model
{
    public class AccessControl
    {
        public int Id { get; set; }
        public Guid ObjectId { get; set; }
        public string? Name { get; set; }
        public string? Sid { get; set; }
        public int Allow { get; set; }
        public int Deny { get; set; }
        public string? Pid { get; set; }
    }
}