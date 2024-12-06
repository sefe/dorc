namespace Dorc.PersistentData.Model
{
    public class PropertyValue
    {
        public long Id { get; set; }
        public Property? Property { get; set; }
        public string? Value { get; set; }
        public ICollection<PropertyValueFilter> Filters { get; set; } = new List<PropertyValueFilter>();
        public override string ToString()
        {
            return $"Id: {Id}, Value: {Value}, Property.Name: {Property?.Name}";
        }
    }
}