using System.Security.Principal;
using Dorc.ApiModel;

namespace Dorc.PersistentData.Sources.Interfaces
{
    public interface IApisPersistentSource
    {
        IEnumerable<ApiApiModel> GetApisForEnvId(int environmentId);

        ApiApiModel? GetApi(int id);

        ApiApiModel AddApi(int environmentId, ApiApiModel model, IPrincipal user);

        ApiApiModel? UpdateApi(ApiApiModel model, IPrincipal user);

        bool DeleteApi(int id, IPrincipal user);
    }
}
