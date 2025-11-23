using Dorc.Api.Controllers;
using Dorc.Api.Windows.Interfaces;
using Dorc.ApiModel;
using Dorc.PersistentData.Repositories;
using Dorc.PersistentData.Sources.Interfaces;

namespace Dorc.Api.Windows.Windows.Services
{
    public class ManageUsers : IManageUsers
    {
        private readonly IUsersPersistentSource _usersPersistentSource;
        private readonly IPermissionsPersistentSource _permissionsPersistentSource;
        private readonly IUserPermsPersistentSource _userPermsPersistentSource;
        private readonly IEnvironmentsPersistentSource _environmentsPersistentSource;

        public ManageUsers(IUsersPersistentSource usersPersistentSource,
            IPermissionsPersistentSource permissionsPersistentSource,
            IUserPermsPersistentSource userPermsPersistentSource, IEnvironmentsPersistentSource environmentsPersistentSource)
        {
            _environmentsPersistentSource = environmentsPersistentSource;
            _userPermsPersistentSource = userPermsPersistentSource;
            _permissionsPersistentSource = permissionsPersistentSource;
            _usersPersistentSource = usersPersistentSource;
        }

        public UserApiModel AddUser(UserApiModel model)
        {
            return _usersPersistentSource.AddUser(model);
        }

        public IEnumerable<PermissionDto> GetUserPermissions<T>()
        {
            return _permissionsPersistentSource.GetAllPermissions();
        }

        public IEnumerable<UserPermDto> GetUserPermissions<T>(int userId)
        {
            return _userPermsPersistentSource.GetPermissions(userId);
        }

        public IEnumerable<UserApiModel> GetUsersForEnvironment(int id, UserAccountType type)
        {
            return _usersPersistentSource.GetEnvironmentUsers(id, type);
        }

        public IEnumerable<UserApiModel> GetDatabaseUsers<T>(int databaseId)
        {
            return _usersPersistentSource.GetDatabaseUsers(databaseId);
        }

        public IEnumerable<UserApiModel> GetUsersList<T>(AccountGranularity type = AccountGranularity.UsersAndGroups)
        {
            IEnumerable<UserApiModel> result = null;
            switch (type)
            {
                case AccountGranularity.UsersAndGroups:
                    result = _usersPersistentSource.GetAll();
                    break;
                case AccountGranularity.Users:
                    result = _usersPersistentSource.GetAllUsers();
                    break;
                case AccountGranularity.Groups:
                    result = _usersPersistentSource.GetAllGroups();
                    break;
            }

            return result;
        }
    }
}