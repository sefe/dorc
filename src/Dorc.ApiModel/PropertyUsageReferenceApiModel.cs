namespace Dorc.ApiModel
{
    public class PropertyUsageReferenceApiModel
    {
        public string PropertyName { get; set; } = string.Empty;
        public string Scope { get; set; } = "Default";
        public string PropertyValueFilter { get; set; } = string.Empty;
        public string RawValueSnippet { get; set; } = string.Empty;
    }
}
