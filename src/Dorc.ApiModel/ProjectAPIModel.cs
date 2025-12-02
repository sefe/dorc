namespace Dorc.ApiModel
{
    public class ProjectApiModel
    {
        public int ProjectId { get; set; }
        public string ProjectName { get; set; }
        public string ProjectDescription { get; set; }
        public string ArtefactsUrl { get; set; }
        public string ArtefactsSubPaths { get; set; }
        public string ArtefactsBuildRegex { get; set; }
        public string TerraformGitRepoUrl { get; set; }
        public string TerraformSubPath { get; set; }
        public DatabaseApiModel SourceDatabase { get; set; }
    }
}
