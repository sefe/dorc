using Dorc.ApiModel;

namespace Dorc.PersistentData.Sources.Interfaces
{
    public interface IUserPermsPersistentSource
    {
        IEnumerable<UserPermDto> GetPermissions(int userId);
        IEnumerable<UserPermDto> GetPermissions(int userId, int databaseId);
        bool AddUserPermission(int userId, int permissionId, int dbId);
        bool DeleteUserPermission(int userId, int permissionId, int dbId);
        IList<UserDbPermissionApiModel> GetUserDbPermissions(string serverName, string dbName, string? dbType = null);
    }
}