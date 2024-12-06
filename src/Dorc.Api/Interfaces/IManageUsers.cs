using Dorc.Api.Controllers;
using Dorc.ApiModel;
using Dorc.PersistentData.Repositories;

namespace Dorc.Api.Interfaces
{
    public interface IManageUsers
    {
        IEnumerable<PermissionDto> GetUserPermissions<T>();
        IEnumerable<UserPermDto> GetUserPermissions<T>(int userId);
        UserApiModel AddUser(UserApiModel model);
        IEnumerable<UserApiModel> GetUsersList<T>(AccountGranularity type = AccountGranularity.UsersAndGroups);
        IEnumerable<UserApiModel> GetUsersForEnvironment(int id, UserAccountType type);
        IEnumerable<UserApiModel> GetDatabaseUsers<T>(int databaseId);
    }
}
