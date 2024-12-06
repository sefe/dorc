namespace Dorc.PersistentData.Model
{
    public class PropertyValueFilter
    {
        public long Id { get; set; }
        public PropertyValue PropertyValue { get; set; } = null!;
        public PropertyFilter PropertyFilter { get; set; } = null!;
        public string Value { get; set; } = null!;
    }
}