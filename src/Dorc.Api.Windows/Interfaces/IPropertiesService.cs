using Dorc.ApiModel;
using System.Security.Claims;

namespace Dorc.Api.Windows.Interfaces
{
    public interface IPropertiesService
    {
        PropertyApiModel GetProperty(string propertyName);
        IEnumerable<PropertyApiModel> GetProperties();
        IEnumerable<Response> DeleteProperties(IEnumerable<string> properties, ClaimsPrincipal User);
        IEnumerable<Response> PostProperties(IEnumerable<PropertyApiModel> properties, ClaimsPrincipal User);
        IEnumerable<Response> PutProperties(IDictionary<string, PropertyApiModel> propertiesToUpdate, ClaimsPrincipal User);
    }
}
