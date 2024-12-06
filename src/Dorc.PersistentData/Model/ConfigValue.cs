namespace Dorc.PersistentData.Model
{
    public class ConfigValue
    {
        public int Id { get; set; }
        public string Key { get; set; } = null!;
        public string? Value { get; set; }
        public bool Secure { get; set; }
    }
}