using Dorc.ApiModel;
using Dorc.PersistentData.Contexts;
using Dorc.PersistentData.Model;
using Dorc.PersistentData.Sources.Interfaces;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Dorc.PersistentData.Sources
{
    public class UserPermsPersistentSource : IUserPermsPersistentSource
    {
        private readonly IDeploymentContextFactory _contextFactory;

        public UserPermsPersistentSource(IDeploymentContextFactory contextFactory)
        {
            _contextFactory = contextFactory;
        }

        public IEnumerable<UserPermDto> GetPermissions(int databaseId)
        {
            using (var context = _contextFactory.GetContext())
            {
                var result = context.EnvironmentUsers
                    .Include(environmentUser => environmentUser.Database)
                    .Include(environmentUser => environmentUser.User)
                    .Include(environmentUser => environmentUser.Permission)
                    .Where(environmentUser => environmentUser.DbId == databaseId)
                    .Select(MapToUserPermDto).ToList();
                return result;
            }
        }

        public IEnumerable<UserPermDto> GetPermissions(int userId, int databaseId)
        {
            using (var context = _contextFactory.GetContext())
            {
                return context.EnvironmentUsers
                    .Include(environmentUser => environmentUser.Database)
                    .Include(environmentUser => environmentUser.User)
                    .Include(environmentUser => environmentUser.Permission)
                    .Where(environmentUser => environmentUser.UserId == userId
                        && environmentUser.Database.Id == databaseId)
                    .Select(MapToUserPermDto).ToList();
            }
        }

        public IList<UserDbPermissionApiModel> GetUserDbPermissions(string serverName, string dbName, string? dbType = null)
        {
            using (var context = _contextFactory.GetContext())
            {
                var result = (from usr in context.Users
                              join dbusermap in context.EnvironmentUsers on usr.Id equals dbusermap.UserId
                              join db in context.Databases on dbusermap.DbId equals db.Id
                              join perm in context.Permissions on dbusermap.PermissionId equals perm.Id
                              where db.ServerName == serverName && db.Name == dbName
                              select new UserDbPermissionApiModel
                              {
                                  UserId = usr.Id,
                                  UserLoginId = usr.LoginId,
                                  UserLoginType = usr.LoginType,
                                  PermissionId = perm.Id,
                                  PermissionName = perm.Name ?? string.Empty,
                                  PermissionDisplayName = perm.DisplayName ?? string.Empty,
                                  DbId = db.Id,
                                  DbType = db.Type ?? string.Empty,
                              });

                if (!string.IsNullOrEmpty(dbType))
                {
                    result = result.Where(r => r.DbType == dbType);
                }

                return result.ToList();
            }
        }

        public bool AddUserPermission(int userId, int permissionId, int databaseId)
        {
            const int DuplicateKeyErrorNumber = 2627;      

            EnvironmentUser newEnvironmentUser = new EnvironmentUser
            {
                UserId = userId,
                DbId = databaseId,
                PermissionId = permissionId
            };

            using (var context = _contextFactory.GetContext())
            {
                context.EnvironmentUsers.Add(newEnvironmentUser);

                try
                {
                    context.SaveChanges();
                }
                catch (DbUpdateException dbUpdateException)
                {
                    var sqlException = dbUpdateException.GetBaseException() as SqlException;
                    if (sqlException != null
                        && sqlException.Number == DuplicateKeyErrorNumber)
                    {
                        Console.Error.WriteLine("An attempt is made to insert duplicate EnvironmentUser. Exception: " + sqlException);
                        return false;
                    }
                    throw;
                }
                return true;
            }
        }

        public bool DeleteUserPermission(int userId, int permissionId, int databaseId)
        {
            using (var context = _contextFactory.GetContext())
            {
                int deletedEnvironmentUserCount = context.EnvironmentUsers
                    .Where(environmentUser =>
                        environmentUser.UserId == userId
                        && environmentUser.DbId == databaseId
                        && environmentUser.PermissionId == permissionId)
                    .ExecuteDelete();

                if (deletedEnvironmentUserCount == 0)
                {
                    Console.Error.WriteLine("An attempt is made to deplete non-existing EnvironmentUser.");
                    return false;
                }

                return true;
            }
        }

        UserPermDto MapToUserPermDto(EnvironmentUser environmentUser)
        {
            return new UserPermDto
            {
                Database = environmentUser.Database.Name,
                Id = environmentUser.PermissionId,
                Role = environmentUser.Permission.Name,
                User = environmentUser.User.LanId
            };
        }
    }
}