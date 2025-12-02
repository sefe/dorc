namespace Dorc.PersistentData.Model
{
    public class Project : SecurityObject
    {
        public int Id { get; set; }
        public string? Description { get; set; }
        public string? ArtefactsUrl { get; set; }
        public string? ArtefactsSubPaths { get; set; }
        public string? ArtefactsBuildRegex { get; set; }
        public string? TerraformGitRepoUrl { get; set; }
        public string? TerraformSubPath { get; set; }
        public int? SourceDatabaseId { get; set; }
        public Database? SourceDatabase { get; set; }
        public ICollection<Component> Components { get; set; } = new List<Component>();
        public ICollection<Environment> Environments { get; set; } = new List<Environment>();
        public ICollection<RefDataAudit> RefDataAudits { get; set; } = new List<RefDataAudit>();
    }
}