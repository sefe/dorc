namespace Dorc.ApiModel
{
    public class FlatPropertyValueApiModel
    {
        public int PropertyId { get; set; }
        public string Property { get; set; }
        public string PropertyValueScope { get; set; }
        public long PropertyValueScopeId { get; set; }
        public long PropertyValueId { get; set; }
        public string PropertyValue { get; set; }
        public string MaskedPropertyValue { get; set; }
        public string[] PropertyArrayValue { get; set; }
        public bool Secure { get; set; }
        public bool IsArray { get; set; }
        public bool UserEditable { get; set; }
    }
}
