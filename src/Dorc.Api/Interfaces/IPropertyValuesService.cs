using Dorc.ApiModel;
using System.Security.Claims;

namespace Dorc.Api.Interfaces
{
    public interface IPropertyValuesService
    {
        IEnumerable<PropertyValueDto> GetPropertyValues(string propertyName, string environmentName, ClaimsPrincipal user);
        IEnumerable<Response> DeletePropertyValues(IEnumerable<PropertyValueDto> propertyValuesToDelete,
            ClaimsPrincipal user);
        IEnumerable<Response> PostPropertyValues(IEnumerable<PropertyValueDto> propertyValuesToCreate, ClaimsPrincipal user);
        IEnumerable<Response> PutPropertyValues(IEnumerable<PropertyValueDto> propertyValuesToUpdate, ClaimsPrincipal user);
    }
}