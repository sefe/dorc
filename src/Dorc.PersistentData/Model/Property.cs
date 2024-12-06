namespace Dorc.PersistentData.Model
{
    public class Property
    {
        public int Id { get; set; }
        public string Name { get; set; } = null!;
        public string? Description { get; set; }
        public bool Secure { get; set; }
        public bool IsArray { get; set; }
        public ICollection<PropertyValue> PropertyValues { get; set; } = new List<PropertyValue>();
    }
}