using Dorc.ApiModel;
using Dorc.PersistentData.Contexts;
using Dorc.PersistentData.Model;
using Dorc.PersistentData.Repositories;
using Dorc.PersistentData.Sources.Interfaces;
using Microsoft.EntityFrameworkCore;
using System.Security.Principal;

namespace Dorc.PersistentData.Sources
{
    public class UsersPersistentSource : IUsersPersistentSource
    {
        private readonly IDeploymentContextFactory _contextFactory;
        private readonly IClaimsPrincipalReader _claimsPrincipalReader;

        public UsersPersistentSource(
            IDeploymentContextFactory contextFactory,
            IClaimsPrincipalReader claimsPrincipalReader
            )
        {
            _contextFactory = contextFactory;
            _claimsPrincipalReader = claimsPrincipalReader;
        }

        public UserApiModel GetUser(string lanId)
        {
            using (var context = _contextFactory.GetContext())
            {
                var first = context.Users.First(u => u.LanId.Equals(lanId));
                return MapToUserApiModel(first);
            }
        }

        public UserApiModel AddUser(UserApiModel user)
        {
            using (var context = _contextFactory.GetContext())
            {
                context.Users.Add(MapToUser(user));
                context.SaveChanges();
                return MapToUserApiModel(context.Users.First(u => u.LanId == user.LanId));
            }
        }

        public IEnumerable<UserApiModel> GetEnvironmentUsers(int envId, UserAccountType type)
        {
            using (var context = _contextFactory.GetContext())
            {
                var environmentDetail = EnvironmentUnifier.GetEnvironment(context, envId);
                if (environmentDetail == null)
                    return new List<UserApiModel>();

                switch (type)
                {
                    case UserAccountType.Endur:
                        {
                            var result = from user in context.Users
                                         from eu in context.EnvironmentUsers
                                         from env in context.Environments
                                         from db in context.Databases
                                         where env.Id == environmentDetail.Id &&
                                               db.Environments.Any(e => e.Id == environmentDetail.Id) && db.Type == "Endur" &&
                                               eu.DbId == db.Id && user.Id == eu.UserId
                                         select user;
                            var users = result.ToList();

                            return users.Select(MapToUserApiModel).ToList();
                        }
                    default:
                        {
                            return new List<UserApiModel>();
                        }
                }
            }
        }

        public IEnumerable<UserApiModel> GetDatabaseUsers(int databaseId)
        {
            using (var context = _contextFactory.GetContext())
            {
                var dbUsers = context.Databases.Include(d => d.EnvironmentUsers).First(d => d.Id == databaseId).EnvironmentUsers;
                var users = new List<User>();
                foreach (var databaseUser in dbUsers)
                {
                    var user = context.Users.Find(databaseUser.UserId);
                    if (!users.Contains(user))
                        users.Add(user);
                }

                return users.Select(MapToUserApiModel).ToList();
            }
        }


        public IEnumerable<UserApiModel> GetAllUsers()
        {
            using (var context = _contextFactory.GetContext())
            {
                var result = context.Users
                    .Where(u =>
                        EF.Functions.Collate(u.LanIdType, DeploymentContext.CaseInsensitiveCollation)
                            == EF.Functions.Collate("user", DeploymentContext.CaseInsensitiveCollation))
                    .OrderBy(u => u.DisplayName);

                return result.Select(MapToUserApiModel).ToList();
            }
        }

        public IEnumerable<UserApiModel> GetAll()
        {
            using (var context = _contextFactory.GetContext())
            {
                var result = context.Users
                    .OrderBy(u => u.DisplayName);

                return result.Select(MapToUserApiModel).ToList();
            }
        }

        public IEnumerable<UserApiModel> GetAllGroups()
        {
            using (var context = _contextFactory.GetContext())
            {
                var result = context.Users
                    .Where(u =>
                        EF.Functions.Collate(u.LanIdType, DeploymentContext.CaseInsensitiveCollation)
                            == EF.Functions.Collate("Group", DeploymentContext.CaseInsensitiveCollation))
                    .OrderBy(u => u.DisplayName);

                return result.Select(MapToUserApiModel).ToList();
            }
        }

        private UserApiModel MapToUserApiModel(User user)
        {
            if (user == null) return null;

            return new UserApiModel
            {
                DisplayName = user.DisplayName,
                Id = user.Id,
                LanId = user.LanId,
                LoginType = user.LoginType,
                Team = user.Team,
                LoginId = user.LoginId,
                LanIdType = user.LanIdType
            };
        }

        private User MapToUser(UserApiModel user)
        {
            return new User
            {
                DisplayName = user.DisplayName,
                Id = user.Id,
                LanId = user.LanId,
                LoginType = user.LoginType,
                Team = user.Team,
                LoginId = user.LoginId,
                LanIdType = user.LanIdType
            };
        }
    }
}
