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
        /// SHA-256, lower-case 64-character hex, over the UTF-8 bytes of the decoded script text
        /// the runner is expected to execute. Null means unrecorded (initial state and after a path
        /// edit): <c>Off</c> and <c>Report</c> allow execution (<c>Report</c> records on first use),
        /// whereas <c>Enforce</c> (SPEC S-018 §2.3) refuses null without automatic capture.
        /// </summary>
        public string? ContentHash { set; get; }
        public ICollection<Component> Components { set; get; } = new List<Component>();
    }
}