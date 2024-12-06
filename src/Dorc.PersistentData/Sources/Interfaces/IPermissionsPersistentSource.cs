using Dorc.ApiModel;

namespace Dorc.PersistentData.Sources.Interfaces
{
    public interface IPermissionsPersistentSource
    {
        IEnumerable<PermissionDto> GetAllPermissions();
        PermissionDto GetPermissions(int userId);
        PermissionDto UpdatePermission(int id, PermissionDto perm);
        void DeletePermission(PermissionDto perm);
        void DeletePermission(int permId);
        PermissionDto CreatePermission(PermissionDto perm);
    }
}