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
                var first = context.Users.First(u => u.LanId != null && u.LanId.Equals(lanId));
                return MapToUserApiModel(first)!;
            }
        }

        public UserApiModel AddUser(UserApiModel user)
        {
            using (var context = _contextFactory.GetContext())
            {
                context.Users.Add(MapToUser(user));
                context.SaveChanges();
                return MapToUserApiModel(context.Users.First(u => u.LanId == user.LanId))!;
            }
        }

        public IEnumerable<UserApiModel> GetEnvironmentUsers(int envId)
        {
            using (var context = _contextFactory.GetContext())
            {
                var env = EnvironmentUnifier.GetEnvironment(context, envId);
                if (env == null)
                    return new List<UserApiModel>();

                var result = context.Users
                    .Where(e => e.Environments.Any(en => en.Id == env.Id))
                    .Select(u => u);
                return result.ToList().Select(MapToUserApiModel).Where(u => u != null).Cast<UserApiModel>().ToList();
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

                            return users.Select(MapToUserApiModel).Where(u => u != null).Cast<UserApiModel>().ToList();
                        }
                    default:
                        {
                            return GetEnvironmentUsers(envId);
                        }
                }
            }
        }

        public bool IsDelegatedUser(int envId, IPrincipal user)
        {
            using (var context = _contextFactory.GetContext())
            {
                var env = EnvironmentUnifier.GetEnvironment(context, envId);
                if (env == null)
                    return false;

                var users = context.Users
                    .Where(e => e.Environments.Any(en => en.Id == env.Id))
                    .Select(u => u);

                string username = _claimsPrincipalReader.GetUserLogin(user);

                return users.Any(user =>
                    EF.Functions.Collate(user.LanId, DeploymentContext.CaseInsensitiveCollation)
                    == EF.Functions.Collate(username, DeploymentContext.CaseInsensitiveCollation));
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
                    if (user != null && !users.Contains(user))
                        users.Add(user);
                }

                return users.Select(MapToUserApiModel).Where(u => u != null).Cast<UserApiModel>().ToList();
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

                return result.Select(MapToUserApiModel).Where(u => u != null).Cast<UserApiModel>().ToList();
            }
        }

        public IEnumerable<UserApiModel> GetAll()
        {
            using (var context = _contextFactory.GetContext())
            {
                var result = context.Users
                    .OrderBy(u => u.DisplayName);

                return result.Select(MapToUserApiModel).Where(u => u != null).Cast<UserApiModel>().ToList();
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

                return result.Select(MapToUserApiModel).Where(u => u != null).Cast<UserApiModel>().ToList();
            }
        }

        public IEnumerable<UserApiModel> GetUnallocatedUsers(string envName)
        {
            using (var context = _contextFactory.GetContext())
            {
                var environmentDetails = context.Environments.Include(e => e.Users)
                    .First(ed => ed.Name.Equals(envName));

                var delegatedUsers = environmentDetails.Users.Select(u => u.Id).ToList();

                var users = from user in context.Users
                            where user.LoginType == "ENDUR" && user.LanIdType == "USER" && !delegatedUsers.Contains(user.Id)
                            orderby user.DisplayName
                            select user;

                return users.ToList().Select(MapToUserApiModel).Where(u => u != null).Cast<UserApiModel>().ToList();
            }
        }

        public UserApiModel? AddDelegatedUser(int userId, string envName, IPrincipal principal)
        {
            using (var context = _contextFactory.GetContext())
            {
                var environmentDetails = context.Environments.Include(e => e.Users)
                    .First(ed => ed.Name.Equals(envName));

                var user = context.Users.FirstOrDefault(u => u.Id == userId);
                if (user == null)
                    return null;

                environmentDetails.Users.Add(user);

                var username = _claimsPrincipalReader.GetUserFullDomainName(principal);
                EnvironmentHistoryPersistentSource.AddHistory(environmentDetails, string.Empty,
                    "Adding Delegated user " + user.DisplayName,
                    username, "Add Delegated User", context);

                context.SaveChanges();
                return MapToUserApiModel(user);
            }
        }

        public bool DeleteDelegatedUser(int userId, string envName, IPrincipal principal)
        {
            using (var context = _contextFactory.GetContext())
            {
                var environmentDetails = context.Environments.Include(e => e.Users)
                    .First(ed => ed.Name.Equals(envName));

                var user = environmentDetails.Users.FirstOrDefault(u => u.Id == userId);
                if (user == null)
                    return false;

                string username = _claimsPrincipalReader.GetUserFullDomainName(principal);
                EnvironmentHistoryPersistentSource.AddHistory(environmentDetails, string.Empty,
                    "Removing Delegated user " + user.DisplayName,
                    username, "Remove Delegated User", context);

                environmentDetails.Users.Remove(user);
                context.SaveChanges();
                return true;
            }
        }

        private UserApiModel? MapToUserApiModel(User? user)
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
