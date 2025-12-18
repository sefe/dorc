using Dorc.ApiModel;
using Dorc.PersistentData.Model;
using System.Security.Claims;

namespace Dorc.PersistentData.Sources.Interfaces
{
    public interface IAccessControlPersistentSource
    {
        IEnumerable<AccessControlApiModel> GetAccessControls();
        IEnumerable<AccessControlApiModel> GetAccessControls(Guid objectId);
        AccessControlApiModel AddAccessControl(AccessControlApiModel accessControl, Guid objectId);
        AccessControlApiModel? UpdateAccessControl(AccessControlApiModel accessControl);
        Guid DeleteAccessControl(int id);
        IEnumerable<SecurityObject> GetSecurableObjects<TEntity>(ClaimsPrincipal user, string accessControlName) where TEntity : SecurityObject;
        IEnumerable<SecurityObject> GetSecurableObjects<TEntity>(Type type, ClaimsPrincipal user) where TEntity : SecurityObject;
    }
}