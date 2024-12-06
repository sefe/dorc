namespace Dorc.PersistentData.Model
{
    public class Script
    {
        public int Id { get; set; }
        public string Name { get; set; } = null!;
        public string Path { get; set; } = null!;
        public bool IsPathJSON { get; set; }
        public bool NonProdOnly { get; set; }
        public string? PowerShellVersionNumber { set; get; }
        public ICollection<Component> Components { set; get; } = new List<Component>();
    }
}