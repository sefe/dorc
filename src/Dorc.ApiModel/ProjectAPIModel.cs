namespace Dorc.ApiModel
{
    public class ProjectApiModel
    {
        public string Revision { get; set; }
        public int ProjectId { get; set; }
        public string ProjectName { get; set; }
        public string ProjectDescription { get; set; }
        public string ArtefactsUrl { get; set; }
        public string ArtefactsSubPaths { get; set; }
        public string ArtefactsBuildRegex { get; set; }
        public string TerraformGitRepoUrl { get; set; }
        public System.Collections.Generic.List<TerraformStateBackendApiModel> TerraformStateBackends { get; set; }
        public string LeanIXUrl { get; set; }
        /// <summary>Two-letter business vertical code (e.g. "tr") per the SEFE Kafka messaging standard; seeds catalog input suggestions.</summary>
        public string BusinessVertical { get; set; }
        /// <summary>Three-letter application code per the IT "Application Codes for Naming Standards"; seeds catalog input suggestions.</summary>
        public string ApplicationCode { get; set; }
        public DatabaseApiModel SourceDatabase { get; set; }
        public SourceControlType SourceControlType { get; set; } = SourceControlType.AzureDevOps;
    }
}
