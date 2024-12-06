namespace Dorc.Core.VariableResolution
{
    public class PropertyValueNotFound
    {
        public PropertyValueNotFound(string propertyName)
        {
            PropertyName = propertyName;
        }

        public string PropertyName { get; set; }
    }
}