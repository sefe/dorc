using Dorc.ApiModel;

namespace Dorc.PersistentData.Sources.Interfaces
{
    public interface IPropertiesPersistentSource
    {
        bool IsPropertySecure(string propertyName);
        string GetConfigurationFilePath(EnvironmentApiModel environment);
        PropertyApiModel CreateProperty(string propertyName, bool secure, string updatedBy);
        PropertyApiModel CreateProperty(PropertyApiModel property, string updatedBy);
        PropertyApiModel GetProperty(string propertyName);
        IEnumerable<PropertyApiModel> GetAllProperties();
        bool DeleteProperty(string propertyName, string updatedBy);
        bool UpdateProperty(PropertyApiModel prop);
        PropertyApiModel GetProperty(int propertyId);
        IEnumerable<PropertyApiModel> GetPropertiesContaining(string containingText);
    }
}