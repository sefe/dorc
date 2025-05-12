using log4net;
using Microsoft.EntityFrameworkCore;
using System.Security.Principal;
using System.Xml;
using System.Xml.Linq;
using System.Xml.XPath;
using Environment = Dorc.PersistentData.Model.Environment;
using Dorc.ApiModel;
using Dorc.PersistentData.Sources.Interfaces;
using Dorc.PersistentData.Model;
using Dorc.PersistentData.Extensions;
using Dorc.PersistentData.Contexts;

namespace Dorc.PersistentData.Sources
{
    public class EnvironmentsPersistentSource : IEnvironmentsPersistentSource
    {
        private readonly IDeploymentContextFactory contextFactory;
        private readonly ISecurityObjectFilter objectFilter;
        private readonly IRolePrivilegesChecker _rolePrivilegesChecker;
        private readonly IPropertyValuesPersistentSource propertyValuesPersistentSource;
        private readonly ILog logger;
        private readonly IClaimsPrincipalReader _claimsPrincipalReader;

        public EnvironmentsPersistentSource(
            IDeploymentContextFactory contextFactory,
            ISecurityObjectFilter objectFilter,
            IRolePrivilegesChecker rolePrivilegesChecker,
            IPropertyValuesPersistentSource propertyValuesPersistentSource,
            ILog logger,
            IClaimsPrincipalReader claimsPrincipalReader
            )
        {
            this.propertyValuesPersistentSource = propertyValuesPersistentSource;
            _rolePrivilegesChecker = rolePrivilegesChecker;
            this.objectFilter = objectFilter;
            this.contextFactory = contextFactory;
            this.logger = logger;
            this._claimsPrincipalReader = claimsPrincipalReader;
        }

        public EnvironmentApiModel GetEnvironment(string environmentName)
        {
            using (var context = contextFactory.GetContext())
            {
                var environment = EnvironmentUnifier.GetEnvironment(context, environmentName);
                return MapToEnvironmentApiModel(environment, false, false);
            }
        }

        public IEnumerable<string> GetEnvironmentNames(AccessLevel accessLevel, IPrincipal user,
            string thinClientServer, bool excludeProd)
        {
            var environmentNames = new List<string>();
            using (var context = contextFactory.GetContext())
            {
                var allEnvironments = context.Environments.ToList();

                var accessible = new List<Environment>();
                if (_rolePrivilegesChecker.IsAdmin(user))
                {
                    accessible = allEnvironments;
                }
                else
                {
                    string username = _claimsPrincipalReader.GetUserName(user);

                    var ownedEnvDetailNames =
                        allEnvironments
                            .Where(ed => ed.Owner.Contains(username, StringComparison.InvariantCultureIgnoreCase))
                            .Select(ed => ed.Name);
                    accessible.AddRange(allEnvironments.Where(e => ownedEnvDetailNames.Contains(e.Name)));
                }

                foreach (var environment in accessible.Distinct())
                {
                    var environmentDetail = EnvironmentUnifier.GetEnvironment(context, environment.Name);
                    if (environmentDetail == null)
                        continue;

                    if (!environmentDetail.ThinClientServer.Equals(thinClientServer,
                            StringComparison.InvariantCultureIgnoreCase))
                        continue;

                    if (excludeProd && environment.IsProd)
                        continue;

                    environmentNames.Add(environment.Name);
                }
            }

            return environmentNames;
        }

        public IEnumerable<ProjectApiModel> GetMappedProjects(string envName)
        {
            using (var context = contextFactory.GetContext())
            {
                var env = context.Environments
                    .Include(e => e.Projects)
                    .SingleOrDefault(environment =>
                    EF.Functions.Collate(environment.Name, DeploymentContext.CaseInsensitiveCollation)
                        == EF.Functions.Collate(envName, DeploymentContext.CaseInsensitiveCollation));
                return env?.Projects
                    .OrderBy(p => p.Name)
                    .Select(ProjectsPersistentSource.MapToProjectApiModel)
                    .ToList();
            }
        }

        public IEnumerable<string> GetEnvironmentNames(IPrincipal principal)
        {
            return GetEnvironments(principal).Select(e => e.EnvironmentName);
        }

        public bool EnvironmentExists(string rowEnvironment)
        {
            using (var context = contextFactory.GetContext())
            {
                return context.Environments.Any(x => x.Name == rowEnvironment);
            }
        }

        public string GetEnvironmentOwner(int envId)
        {
            using (var context = contextFactory.GetContext())
            {
                var env = EnvironmentUnifier.GetEnvironment(context, envId);
                if (env == null) return string.Empty;

                var owner = env.Owner;

                return owner;
            }
        }

        public bool IsEnvironmentOwner(string envName, IPrincipal user)
        {
            using (var context = contextFactory.GetContext())
            {
                var env = EnvironmentUnifier.GetEnvironment(context, envName);
                if (env == null) return false;

                string username = _claimsPrincipalReader.GetUserName(user);

                return env.Owner.Equals(username, StringComparison.InvariantCultureIgnoreCase);
            }
        }

        public bool SetEnvironmentOwner(IPrincipal updatedBy, int envId, ActiveDirectoryElementApiModel user)
        {
            if (string.IsNullOrEmpty(user.Username) || envId <= 0) return false;

            using (var context = contextFactory.GetContext())
            {
                var envDetail = EnvironmentUnifier.GetEnvironment(context, envId);

                if (envDetail == null)
                    return false;

                string userFullDomainName = _claimsPrincipalReader.GetUserFullDomainName(updatedBy);
                EnvironmentHistoryPersistentSource.AddHistory(envDetail, string.Empty,
                    "Owner Updated to " + user.DisplayName + " from " + envDetail.Owner,
                    userFullDomainName, "Env Owner Update", context);

                envDetail.Owner = user.Username;

                context.SaveChanges();

                return true;
            }
        }

        public EnvironmentApiModel AttachServerToEnv(int envId, int serverId, IPrincipal user)
        {
            using (var context = contextFactory.GetContext())
            {
                try
                {
                    var envDetail = EnvironmentUnifier.GetEnvironment(context, envId);
                    var server = context.Servers
                        .Include(s => s.Environments)
                        .FirstOrDefault(s => s.Id == serverId);

                    if (server == null)
                        throw new ArgumentOutOfRangeException(nameof(serverId), "Invalid or unknown server Id specified.");

                    server.Environments.Add(envDetail);

                    string username = _claimsPrincipalReader.GetUserName(user);
                    EnvironmentHistoryPersistentSource.AddHistory(envDetail, string.Empty,
                        "Server " + server.Name + " attached to environment ",
                        username, "Attach Server To Env", context);

                    context.SaveChanges();
                    return GetEnvironment(envId, user);
                }
                catch
                {
                    return new EnvironmentApiModel();
                }
            }
        }

        public EnvironmentApiModel DetachServerFromEnv(int envId, int serverId, IPrincipal user)
        {
            using (var context = contextFactory.GetContext())
            {
                try
                {
                    var envDetail = EnvironmentUnifier.GetEnvironment(context, envId);
                    var server = context.Servers
                        .Include(s => s.Environments)
                        .FirstOrDefault(s => s.Id == serverId);

                    if (server == null)
                        throw new ArgumentOutOfRangeException(nameof(serverId), "Invalid or unknown server Id specified.");

                    server.Environments.Remove(envDetail);

                    string username = _claimsPrincipalReader.GetUserFullDomainName(user);
                    EnvironmentHistoryPersistentSource.AddHistory(envDetail, string.Empty,
                        "Server " + server.Name + " detached from environment ",
                        username, "Detach Server From Env", context);

                    context.SaveChanges();
                    return GetEnvironment(envId, user);
                }
                catch
                {
                    return new EnvironmentApiModel();
                }
            }
        }

        public EnvironmentApiModel AttachDatabaseToEnv(int envId, int databaseId, IPrincipal user)
        {
            using (var context = contextFactory.GetContext())
            {
                GetEnvironmentAndDatabase(context, envId, databaseId, out var envDetail, out var db);
                if (db.Environments.Any(e => e.Id == envId))
                {
                    throw new ArgumentException(
                        "The specified database is already attached to the specified environment.");
                }

                string username = _claimsPrincipalReader.GetUserFullDomainName(user);
                envDetail.Databases.Add(db);
                EnvironmentHistoryPersistentSource.AddHistory(envDetail, string.Empty,
                    "Database " + db.ServerName + ":" + db.Name + " attached to environment ",
                    username, "Attach Database To Env", context);

                context.SaveChanges();
                return GetEnvironment(envId, user);
            }
        }

        public EnvironmentApiModel DetachDatabaseFromEnv(int envId, int databaseId, IPrincipal user)
        {
            using (var context = contextFactory.GetContext())
            {
                GetEnvironmentAndDatabase(context, envId, databaseId, out var envDetail, out var db);
                var envToRemove = db.Environments.FirstOrDefault(e => e.Id == envId);
                if (envToRemove is null)
                {
                    throw new ArgumentException(
                        "The specified database is not attached to the specified environment.");

                }

                db.Environments.Remove(envToRemove);
                string username = _claimsPrincipalReader.GetUserFullDomainName(user);
                EnvironmentHistoryPersistentSource.AddHistory(envDetail, string.Empty,
                    "Database " + db.ServerName + ":" + db.Name + " detached from environment ",
                    username, "Detach Database From Env", context);

                context.SaveChanges();
                return GetEnvironment(envId, user);
            }
        }

        public IEnumerable<EnvironmentApiModel> GetEnvironments(IPrincipal user)
        {
            string username = _claimsPrincipalReader.GetUserName(user);
            var userSids = _claimsPrincipalReader.GetSidsForUser(user);
            using (var context = contextFactory.GetContext())
            {
                var accessibleEnvNames = _rolePrivilegesChecker.IsAdmin(user)
                    ? AccessibleEnvironmentsAdmin(context)
                    : AccessibleEnvironmentsAccessLevel(context, userSids, username);

                var environments = from environment in accessibleEnvNames.ToList().GroupBy(e => e.Environment.Name)
                        .Select(x => x.FirstOrDefault())
                                   select MapToEnvironmentApiModel(environment);

                return environments.OrderBy(env => env.EnvironmentName).ToList();
            }
        }

        public IEnumerable<EnvironmentData> AccessibleEnvironmentsAccessLevel(IDeploymentContext context,
            string projectName, IPrincipal user, AccessLevel accessLevel)
        {
            string username = _claimsPrincipalReader.GetUserName(user);
            var userSids = _claimsPrincipalReader.GetSidsForUser(user);
            var isAdmin = _rolePrivilegesChecker.IsAdmin(user);

            var output = (
                from project in context.Projects
                from environment in project.Environments
                join ac in context.AccessControls on environment.ObjectId equals ac.ObjectId
                    into accessControlEnvironments
                from allAccessControlEnvironments in accessControlEnvironments.DefaultIfEmpty()
                where project.Name == projectName
                let isOwner = environment.Owner == username
                let isDelegate =
                    (from env in context.Environments
                     where env.Name == environment.Name && environment.Users.Select(u => u.LoginId).Contains(username)
                     select environment.Name).Any()
                let hasPermission = (from env in context.Environments
                                     join ac in context.AccessControls on env.ObjectId equals ac.ObjectId
                                     where env.Name == environment.Name && userSids.Contains(ac.Sid) &&
                                           (ac.Allow & (int)accessLevel) != 0
                                     select env.Name).Any()
                select new EnvironmentData
                {
                    Environment = environment,
                    UserEditable = isOwner || hasPermission || isDelegate || isAdmin,
                    IsDelegate = isDelegate,
                    IsModify = hasPermission,
                    IsOwner = isOwner
                }).Distinct();

            var environmentData = output.ToList();

            if (isAdmin)
                return environmentData;

            return environmentData.Where(e =>
            {
                switch (accessLevel)
                {
                    case AccessLevel.ReadSecrets:
                        return e.IsModify;
                    case AccessLevel.Write:
                        return e.UserEditable;
                    case AccessLevel.None:
                    default:
                        return true;
                }
            });
        }

        public EnvironmentApiModel GetEnvironment(string environmentName, IPrincipal user)
        {
            string username = _claimsPrincipalReader.GetUserName(user);
            var userSids = _claimsPrincipalReader.GetSidsForUser(user);
            using (var context = contextFactory.GetContext())
            {
                var accessibleEnvNames = _rolePrivilegesChecker.IsAdmin(user)
                    ? AccessibleEnvironmentAdmin(context, environmentName)
                    : AccessibleEnvironmentAccessLevel(context, userSids, username, environmentName);
                var environments = from environment in accessibleEnvNames.ToList()
                                   select MapToEnvironmentApiModel(environment);

                return environments.FirstOrDefault();
            }
        }

        public EnvironmentApiModel GetEnvironment(int environmentId, IPrincipal user)
        {
            using (var context = contextFactory.GetContext())
            {
                var environment = EnvironmentUnifier.GetFullEnvironment(context, environmentId);

                return MapToEnvironmentApiModel(environment, objectFilter.HasPrivilege(environment, user, AccessLevel.Write),
                    IsEnvironmentOwner(environment.Name, user));
            }
        }

        public IEnumerable<EnvironmentContentBuildsApiModel> GetEnvironmentComponentStatuses(string environmentName,
            DateTime cutoffDate)
        {
            var result = new List<EnvironmentContentBuildsApiModel>();

            using (var context = contextFactory.GetContext())
            {
                context.Database.SetCommandTimeout(0);

                var request = context.EnvironmentComponentStatuses
                    .Include("Component")
                    .Include("Environment")
                    .Include("DeploymentRequest")
                    .Where(e => e.Environment.Name == environmentName && e.DeploymentRequest.CompletedTime < cutoffDate)
                    .OrderByDescending(e => e.UpdateDate)
                    .Select(o => new
                    {
                        ComponentName = o.Component.Name,
                        RequestDetailsXml = o.DeploymentRequest.RequestDetails,
                        o.UpdateDate,
                        o.Status,
                        o.DeploymentRequest.Id
                    });
                foreach (var build in request)
                    try
                    {
                        result.Add(new EnvironmentContentBuildsApiModel
                        {
                            State = build.Status,
                            UpdateDate = build.UpdateDate.DateTime.ToString("o"),
                            ComponentName = build.ComponentName,
                            RequestBuildNum = xPathToString(build.RequestDetailsXml),
                            RequestId = build.Id
                        });
                    }
                    catch
                    {
                    }

                return result;
            }
        }

        public IEnumerable<EnvironmentComponentStatusModel> GetEnvironmentComponentStatuses(int environmentId)
        {
            using (var context = contextFactory.GetContext())
            {
                var environmentComponentStatus = context.EnvironmentComponentStatuses.Include(e => e.Environment)
                    .Include(cs => cs.Component)
                    .Include(c => c.DeploymentRequest).Where(e => e.Environment.Id == environmentId);
                return environmentComponentStatus.ToList().Select(MapToEnvironmentComponentStatusModel).ToList();
            }
        }

        public Environment GetSecurityObject(string environmentName)
        {
            using (var context = contextFactory.GetContext())
            {
                return context.Environments.FirstOrDefault(environment => environment.Name.Equals(environmentName));
            }
        }

        public EnvironmentApiModel CreateEnvironment(EnvironmentApiModel env, IPrincipal user)
        {
            using (var context = contextFactory.GetContext())
            {
                if (string.IsNullOrWhiteSpace(env.EnvironmentName))
                {
                    throw new ArgumentException("EnvironmentName not set");
                }

                var environment = EnvironmentUnifier.GetEnvironment(context, env.EnvironmentName);
                if (environment == null)
                {
                    var e = new Environment();
                    MapToEnvironment(env, e);

                    context.Environments.Add(e);

                    string username = _claimsPrincipalReader.GetUserFullDomainName(user);
                    EnvironmentHistoryPersistentSource.AddHistory(e, string.Empty,
                        "Environment created",
                        username, "Create Environment", context);

                    context.SaveChanges();
                }

                return GetEnvironment(env.EnvironmentName, user);
            }
        }

        public EnvironmentApiModel UpdateEnvironment(EnvironmentApiModel env, IPrincipal user)
        {
            using (var context = contextFactory.GetContext())
            {
                var environment = EnvironmentUnifier.GetFullEnvironment(context, env.EnvironmentId);

                if (env.EnvironmentName != environment.Name)
                {
                    string username = _claimsPrincipalReader.GetUserFullDomainName(user);
                    EnvironmentHistoryPersistentSource.AddHistory(environment, string.Empty,
                        "Environment Name Updated to " + env.EnvironmentName + " from " + environment.Name,
                        username, "Env Name Update", context);

                    // Need to also update the env properties
                    propertyValuesPersistentSource.ReassignPropertyValues(context, environment, env.EnvironmentName);
                }

                MapToEnvironment(env, environment);

                context.SaveChanges();

                return GetEnvironment(env.EnvironmentName, user);
            }
        }

        public bool DeleteEnvironment(EnvironmentApiModel env, IPrincipal principal)
        {
            using (var context = contextFactory.GetContext())
            using (var dbContextTransaction = context.Database.BeginTransaction())
            {
                var environment = context.Environments
                    .Include(e => e.Databases)
                    .Include(e => e.ComponentStatus)
                    .Include(e => e.Histories)
                    .Include(e => e.Projects)
                    .Include(e => e.Servers)
                    .Include(e => e.Users)
                    .SingleOrDefault(environment =>
                        EF.Functions.Collate(environment.Name, DeploymentContext.CaseInsensitiveCollation)
                        == EF.Functions.Collate(env.EnvironmentName, DeploymentContext.CaseInsensitiveCollation));

                if (environment == null) return false;

                var propertyValueIds = context.PropertyValueFilters.Where(pvf => pvf.Value.Equals(environment.Name))
                    .Select(pvf => pvf.PropertyValue.Id).ToList();

                context.PropertyValueFilters.Where(pvf => pvf.Value.Equals(environment.Name)).ExecuteDelete();
                context.PropertyValues.Where(pv => propertyValueIds.Contains(pv.Id)).ExecuteDelete();

                environment.Databases.Clear();
                environment.Servers.Clear();
                environment.Users.Clear();

                foreach (var h in environment.Histories.ToList())
                {
                    context.EnvironmentHistories.Remove(h);
                }
                environment.Histories.Clear();

                environment.Projects.Clear();

                foreach (var ecs in environment.ComponentStatus.ToList())
                {
                    context.EnvironmentComponentStatuses.Remove(ecs);
                }
                environment.ComponentStatus.Clear();

                context.Environments.Remove(environment);

                context.SaveChanges();
                dbContextTransaction.Commit();
                return true;
            }
        }

        public bool EnvironmentIsProd(string envName)
        {
            using (var context = contextFactory.GetContext())
            {
                if (!context.Environments.Any(x => x.Name == envName)) return false;
                var env = context.Environments.FirstOrDefault(x => x.Name == envName);
                return env != null && env.IsProd;
            }
        }

        public bool EnvironmentIsSecure(string envName)
        {
            using (var context = contextFactory.GetContext())
            {
                var env = context.Environments
                    .FirstOrDefault(e => e.Name.Equals(envName));
                return !(env is null) && env.Secure;
            }
        }

        private static void GetEnvironmentAndDatabase(IDeploymentContext context, int envId, int databaseId,
            out Environment environment,
            out Database database)
        {
            var envDetail = EnvironmentUnifier.GetFullEnvironment(context, envId);
            if (envDetail is null)
            {
                throw new ArgumentOutOfRangeException(nameof(envId), "Invalid or unknown environment Id specified.");
            }

            var db = context.Databases
                .Include(d => d.Environments)
                .FirstOrDefault(d => d.Id == databaseId);
            if (db == null)
            {
                throw new ArgumentOutOfRangeException(nameof(databaseId), "Invalid or unknown database Id specified.");
            }

            environment = envDetail;
            database = db;
        }

        private static IQueryable<EnvironmentData> AccessibleEnvironmentsAdmin(IDeploymentContext context)
        {
            return (from env in context.Environments
                    select new EnvironmentData
                    { Environment = env, UserEditable = true })
                .Distinct();
        }

        private static IQueryable<EnvironmentData> AccessibleEnvironmentAdmin(IDeploymentContext context,
            string environmentName)
        {
            return from env in context.Environments.Include(e => e.ParentEnvironment).Include(e => e.ChildEnvironments)
                   where env.Name == environmentName
                   select new EnvironmentData
                   { Environment = env, UserEditable = true };
        }

        private static IQueryable<EnvironmentData> AccessibleEnvironmentsAccessLevel(IDeploymentContext context,
            ICollection<string> userSids, string username)
        {
            var output = (from environment in context.Environments
                          join ac in context.AccessControls on environment.ObjectId equals ac.ObjectId into
                              accessControlEnvironments
                          from allAccessControlEnvironments in accessControlEnvironments.DefaultIfEmpty()
                          let isOwner = environment.Owner == username
                          let isDelegate =
                              (from env in context.Environments
                               where env.Name == environment.Name && env.Users.Select(u => u.LoginId).Contains(username)
                               select env.Name).Any()
                          let isModify = (from env in context.Environments
                                          join ac in context.AccessControls on env.ObjectId equals ac.ObjectId
                                          where env.Name == environment.Name && userSids.Contains(ac.Sid) &&
                                                (ac.Allow & (int)AccessLevel.Write) != 0
                                          select env.Name).Any()
                          select new EnvironmentData
                          {
                              Environment = environment,
                              UserEditable = isOwner || isModify || isDelegate,
                              IsDelegate = isDelegate,
                              IsModify = isModify,
                              IsOwner = isOwner
                          }).Distinct();

            return output;
        }

        private static IQueryable<EnvironmentData> AccessibleEnvironmentAccessLevel(IDeploymentContext context,
            ICollection<string> userSids, string username, string environmentName)
        {
            var output = from
                environment in context.Environments.Include(e => e.ParentEnvironment).Include(e => e.ChildEnvironments)
                         join ac in context.AccessControls on environment.ObjectId equals ac.ObjectId into
                             accessControlEnvironments
                         from allAccessControlEnvironments in accessControlEnvironments.DefaultIfEmpty()
                         where environment.Name == environmentName
                         let isOwner = environment.Owner == username
                         let isDelegate =
                             (from env in context.Environments
                              where env.Name == environment.Name && environment.Users.Select(u => u.LoginId).Contains(username)
                              select environment.Name).Any()
                         let isModify = (from env in context.Environments
                                         join ac in context.AccessControls on env.ObjectId equals ac.ObjectId
                                         where env.Name == environment.Name && userSids.Contains(ac.Sid) &&
                                               (ac.Allow & (int)AccessLevel.Write) != 0
                                         select env.Name).Any()
                         select new EnvironmentData
                         {
                             Environment = environment,
                             UserEditable = isOwner || isModify || isDelegate,
                             IsDelegate = isDelegate,
                             IsModify = isModify,
                             IsOwner = isOwner
                         };
            return output;
        }

        private string xPathToString(string value)
        {
            var reader = XmlReader.Create(new StringReader(value));
            var doc = XDocument.Load(reader);
            var query = "/DeploymentRequestDetail//BuildDetail//BuildNumber";
            var xPathSelectElement = doc.XPathSelectElement(query);
            return xPathSelectElement != null ? xPathSelectElement.Value : string.Empty;
        }

        private static EnvironmentComponentStatusModel MapToEnvironmentComponentStatusModel(
            EnvironmentComponentStatus s)
        {
            return new EnvironmentComponentStatusModel
            {
                Component = s.Component.Name,
                Status = s.Status,
                UpdateDate = s.UpdateDate,
                BuildNumber = s.DeploymentRequest.BuildNumber,
                DropLocation = s.DeploymentRequest.DropLocation,
                Uri = s.DeploymentRequest.BuildUri,
                Project = s.DeploymentRequest.Project
            };
        }

        private static void MapToEnvironment(EnvironmentApiModel env, Environment e)
        {
            e.Name = env.EnvironmentName;
            e.Secure = env.EnvironmentSecure;
            e.IsProd = env.EnvironmentIsProd;

            e.Name = env.EnvironmentName;
            e.Description = env.Details.Description;
            e.Owner = env.Details.EnvironmentOwner;
            e.FileShare = env.Details.FileShare;
            e.LastUpdate = !string.IsNullOrEmpty(env.Details.LastUpdated)
                ? DateTime.Parse(env.Details.LastUpdated)
                : DateTime.Now;
            e.ThinClientServer = env.Details.ThinClient;
            e.RestoredFromBackup = env.Details.RestoredFromSourceDb;
            e.EnvNote = env.Details.Notes;
            e.ParentId = env.ParentId;
        }

        private static EnvironmentApiModel MapToEnvironmentApiModel(EnvironmentData ed)
        {
            if (ed.Environment == null)
                return null;

            return MapToEnvironmentApiModel(ed.Environment, ed.UserEditable, ed.IsOwner);
        }

        private static EnvironmentApiModel MapToParentEnvironmentApiModel(Environment? parentEnv)
        {
            if (parentEnv is null)
                return null;

            return new EnvironmentApiModel
            {
                EnvironmentName = parentEnv.Name,
                EnvironmentSecure = parentEnv.Secure,
                EnvironmentIsProd = parentEnv.IsProd,
                EnvironmentId = parentEnv.Id,
                ParentId = parentEnv.ParentId,
                IsParent = true,
                ParentEnvironment = MapToParentEnvironmentApiModel(parentEnv.ParentEnvironment)
            };
        }

        private static EnvironmentApiModel? MapToChildEnvironmentApiModel(Environment? childEnv)
        {
            if (childEnv is null)
                return null;

            return new EnvironmentApiModel
            {
                EnvironmentName = childEnv.Name,
                EnvironmentSecure = childEnv.Secure,
                EnvironmentIsProd = childEnv.IsProd,
                EnvironmentId = childEnv.Id,
                ParentId = childEnv.ParentId,
                IsParent = childEnv.ChildEnvironments.Any(),
                ChildEnvironments = childEnv.ChildEnvironments.Select(MapToChildEnvironmentApiModel).ToList()
            };
        }

        public static EnvironmentApiModel MapToEnvironmentApiModel(Environment env,
            bool userEditable, bool isOwner)
        {
            if (env == null)
                return null;

            var resEnv = MapToEnvironmentApiModel(env);
            resEnv.UserEditable = userEditable;
            resEnv.IsOwner = isOwner;

            return resEnv;
        }

        public static EnvironmentApiModel MapToEnvironmentApiModel(Environment? env)
        {
            if (env is null)
                return null!;

            return new EnvironmentApiModel
            {
                EnvironmentName = env.Name,
                EnvironmentSecure = env.Secure,
                EnvironmentIsProd = env.IsProd,
                EnvironmentId = env.Id,
                Details = MapToEnvironmentDetailsApiModel(env),
                ParentId = env.ParentId,
                IsParent = env.ChildEnvironments.Any(),
                ParentEnvironment = MapToParentEnvironmentApiModel(env.ParentEnvironment),
                ChildEnvironments = env.ChildEnvironments.Select(MapToChildEnvironmentApiModel).ToList()
            };
        }

        public static EnvironmentDetailsApiModel MapToEnvironmentDetailsApiModel(Environment details)
        {
            if (details == null)
                return null;

            return new EnvironmentDetailsApiModel
            {
                Description = details.Description,
                EnvironmentOwner = details.Owner,
                FileShare = details.FileShare,
                LastUpdated = details.LastUpdate.ToString(),
                ThinClient = details.ThinClientServer,
                RestoredFromSourceDb = details.RestoredFromBackup,
                Notes = details.EnvNote
            };
        }

        public IEnumerable<EnvironmentApiModel> GetPossibleEnvironmentChildren(int id, IPrincipal user)
        {
            using (var context = contextFactory.GetContext())
            {
                var userSids = _claimsPrincipalReader.GetSidsForUser(user);
                var accessLevelRequired = AccessLevel.Write;

                var allRelatedEnvs = context.Environments
                    .Include(e => e.Projects)
                    .ThenInclude(p => p.Environments)
                    .Where(e => e.Id == id)
                    .SelectMany(e => e.Projects.SelectMany(p => p.Environments)) // Get all environments from all related projects
                    .Where(e => e.Id != id && e.ParentId == null) // Exclude the parent and all children
                    .Distinct();

                var filteredByAccessLevelEnvs = allRelatedEnvs
                    .Join(context.AccessControls, // Join with AccessControls to filter by user access
                          environment => environment.ObjectId,
                          ac => ac.ObjectId,
                          (environment, ac) => new { environment, ac })
                    .Where(joined => userSids.Contains(joined.ac.Sid) && (joined.ac.Allow & (int)accessLevelRequired) != 0)
                    .Select(joined => joined.environment)
                    .Distinct();

                var mappedEnvironments = _rolePrivilegesChecker.IsAdmin(user) ? allRelatedEnvs.ToList() : filteredByAccessLevelEnvs.ToList();

                var envChain = context.GetFullEnvironmentChain(id);

                mappedEnvironments = mappedEnvironments.Where(e => !envChain.Any(ec => ec.Id == e.Id)).ToList(); // filter all envs already in chain

                var possibleChildren = mappedEnvironments.Select(MapToEnvironmentApiModel);
                return possibleChildren;
            }
        }

        public void SetParentForEnvironment(int? parentEnvId, int childEnvId, IPrincipal user)
        {
            using (var context = contextFactory.GetContext())
            {
                var childEnv = EnvironmentUnifier.GetEnvironment(context, childEnvId);

                if (childEnv is null)
                    throw new ArgumentException("Child environment not found.");

                string username = _claimsPrincipalReader.GetUserFullDomainName(user);
                if (parentEnvId.HasValue)
                {
                    var envChain = context.GetFullEnvironmentChain(parentEnvId.Value);

                    var parentEnv = envChain.FirstOrDefault(e => e.Id == parentEnvId);
                    if (parentEnv is null)
                        throw new ArgumentException("Parent environment not found.");

                    if (childEnv.ParentId == parentEnvId)
                    {
                        logger.Debug($"Environment {childEnv.Name} is already a child of {parentEnv.Name}");
                        return;
                    }
                    
                    if (envChain.FirstOrDefault(e => e.Id == childEnvId) is not null)
                    {
                        throw new ArgumentException($"Environment {childEnv.Name} is already in the chain of the environment {parentEnv.Name}.");
                    }

                    if (parentEnv.ParentId is not null)
                    {
                        throw new ArgumentException($"Not allowed to have more than 1 level of environment hierarchy. Environment {parentEnv.Name} is already a child ");
                    }

                    childEnv.ParentId = parentEnvId;
                    EnvironmentHistoryPersistentSource.AddHistory(childEnv, string.Empty,
                        "Attached as a child to parent environment " + parentEnv.Name,
                        username, "Attach Child Environment", context);
                }
                else
                {
                    if (!childEnv.ParentId.HasValue)
                    {
                        logger.Debug($"Environment {childEnv.Name} is not a child");
                        return;
                    }

                    childEnv.ParentId = null;
                    EnvironmentHistoryPersistentSource.AddHistory(childEnv, string.Empty,
                        "Child environment detached from its parent.",
                        username, "Detach Child Environment", context);
                }

                context.SaveChanges();
            }
        }
    }
}