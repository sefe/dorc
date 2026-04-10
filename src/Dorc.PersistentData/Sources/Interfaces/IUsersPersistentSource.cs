using Dorc.ApiModel;
using Dorc.PersistentData.Repositories;
using System.Security.Principal;

namespace Dorc.PersistentData.Sources.Interfaces
{
    public interface IUsersPersistentSource
    {
        UserApiModel GetUser(string lanId);
        UserApiModel AddUser(UserApiModel user);
        IEnumerable<UserApiModel> GetEnvironmentUsers(int envId, UserAccountType type);
        IEnumerable<UserApiModel> GetDatabaseUsers(int databaseId);
        IEnumerable<UserApiModel> GetAllUsers();
        IEnumerable<UserApiModel> GetAllGroups();
        IEnumerable<UserApiModel> GetAll();
    }
}