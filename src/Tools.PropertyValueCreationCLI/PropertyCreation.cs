using Dorc.PersistentData.Sources.Interfaces;

namespace Tools.PropertyValueCreationCLI
{
    internal class PropertyCreation : IPropertyCreation
    {
        private readonly IPropertiesPersistentSource _propertiesPersistentSource;

        public PropertyCreation(IPropertiesPersistentSource propertiesPersistentSource)
        {
            _propertiesPersistentSource = propertiesPersistentSource;
        }

        public void InsertProperty(string propertyName, bool secure, string updatedBy)
        {
            _propertiesPersistentSource.CreateProperty(propertyName, secure, updatedBy);
        }
    }
}