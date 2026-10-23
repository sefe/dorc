namespace Dorc.ApiModel
{
    public class EnvironmentContentBuildsApiModel
    {
        public int? ComponentId { get; set; }
        public int? ProjectId { get; set; }
        public string DefinitionRevision { get; set; }
        public string SourceRevisionLabel { get; set; }
        public string ComponentName { set; get; }
        public string RequestBuildNum { set; get; }
        public int RequestId { set; get; }
        public string UpdateDate { set; get; }
        public string State { set; get; }
    }
}