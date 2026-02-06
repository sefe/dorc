namespace Dorc.ApiModel
{
    /// <summary>
    /// Request model for cloning an environment including its variables/properties
    /// </summary>
    public class CloneEnvironmentRequest
    {
        /// <summary>
        /// The ID of the source environment to clone from
        /// </summary>
        public int SourceEnvironmentId { get; set; }

        /// <summary>
        /// The name for the new cloned environment
        /// </summary>
        public string NewEnvironmentName { get; set; }

        /// <summary>
        /// Whether to copy the property values (variables) from the source environment
        /// </summary>
        public bool CopyPropertyValues { get; set; } = true;

        /// <summary>
        /// Whether to copy the server mappings from the source environment
        /// </summary>
        public bool CopyServerMappings { get; set; } = true;

        /// <summary>
        /// Whether to copy the database mappings from the source environment
        /// </summary>
        public bool CopyDatabaseMappings { get; set; } = true;

        /// <summary>
        /// Whether to copy the project mappings from the source environment
        /// </summary>
        public bool CopyProjectMappings { get; set; } = true;

        /// <summary>
        /// Whether to copy the access controls (permissions) from the source environment
        /// </summary>
        public bool CopyAccessControls { get; set; } = false;
    }
}
