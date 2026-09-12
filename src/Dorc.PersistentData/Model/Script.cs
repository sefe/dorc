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

        /// <summary>
        /// SHA-256, lower-case hex, of the bytes the runner is expected to execute. Null means
        /// unrecorded — the state of every row that predates this, and never a refusal on its
        /// own.
        /// </summary>
        public string? ContentHash { set; get; }
        public ICollection<Component> Components { set; get; } = new List<Component>();
    }
}