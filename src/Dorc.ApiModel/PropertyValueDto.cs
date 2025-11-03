namespace Dorc.ApiModel
{
    public class PropertyValueDto
    {
        public long Id { get; set; }
        public string Value { get; set; }
        public string MaskedValue { get; set; }
        public PropertyApiModel Property { get; set; }
        public string PropertyValueFilter { get; set; }
        public long? PropertyValueFilterId { get; set; }
        public int Priority { get; set; }
        public bool DefaultValue { get; set; }
        public bool UserEditable { get; set; }
    }
}