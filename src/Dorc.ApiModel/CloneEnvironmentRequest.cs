namespace Dorc.ApiModel
{
    /// Request model for cloning an environment including its variables/properties
    public class CloneEnvironmentRequest
    {
        public int SourceEnvironmentId { get; set; }
        public string NewEnvironmentName { get; set; }
        public bool CopyPropertyValues { get; set; } = true;
        public bool CopyServerMappings { get; set; } = true;
        public bool CopyDatabaseMappings { get; set; } = true;
        public bool CopyProjectMappings { get; set; } = true;
        public bool CopyAccessControls { get; set; } = false;
    }
}
