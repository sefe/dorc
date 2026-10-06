namespace Dorc.ApiModel
{
    public class CopyEnvBuildDto
    {
        public string SourceEnv { get; set; }
        public string TargetEnv { get; set; }
        public string Project { get; set; }
        public string Components { get; set; }
    }
}