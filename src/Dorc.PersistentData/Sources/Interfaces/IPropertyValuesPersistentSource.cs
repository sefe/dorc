using Dorc.ApiModel;
using Dorc.PersistentData.Contexts;
using System.Security.Principal;
using Environment = Dorc.PersistentData.Model.Environment;

namespace Dorc.PersistentData.Sources.Interfaces
{
    public interface IPropertyValuesPersistentSource
    {
        bool Remove(long? propertyValueId);
        PropertyValueDto Get(long? propertyValueId);
        PropertyValueDto[] GetPropertyValuesByName(string propertyName);
        PropertyValueDto GetCachedPropertyValue(string propertyName);
        void AddEnvironmentFilter(string envName);
        PropertyValueDto[] GetGlobalProperties();
        PropertyValueDto[] GetEnvironmentProperties(string environment);
        IDictionary<string, PropertyValueDto> LoadAllPropertiesIntoCache();
        PropertyValueDto UpdatePropertyValue(long? propertyValueId, string newValue);
        PropertyValueDto AddPropertyValue(PropertyValueDto propertyValueDto);
        void AddFilter(string filterName, string value);
        List<PropertyValueDto> GetPropertyValues(string propertyName, string environmentName, bool decryptProperty = false);
        bool IsCachedPropertySecure(string propertyName);
        bool RemoveByFilterId(long? propertyValueFilterId);
        GetScopedPropertyValuesResponseDto GetPropertyValuesForScopeByPage(int limit, int page,
            PagedDataOperators operators, EnvironmentApiModel scope, IPrincipal principal);
        GetScopedPropertyValuesResponseDto GetPropertyValuesForSearchValueByPage(int limit, int page,
            PagedDataOperators operators, IPrincipal principal);
        PropertyValueDto[] GetPropertyValuesForUser(string? environmentName, string? propertyName, string username, string sidList);
        void ReassignPropertyValues(IDeploymentContext context, Environment oldEnv, string newEnvName);
    }
}