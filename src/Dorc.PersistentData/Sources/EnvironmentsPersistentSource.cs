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
using System.Security.Claims;

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
        private readonly IAccessControlPersistentSource _accessControlPersistentSource;

        public EnvironmentsPersistentSource(
            IDeploymentContextFactory contextFactory,
            ISecurityObjectFilter objectFilter,
            IRolePrivilegesChecker rolePrivilegesChecker,
            IPropertyValuesPersistentSource propertyValuesPersistentSource,
            ILog logger,
            IClaimsPrincipalReader claimsPrincipalReader,
            IAccessControlPersistentSource accessControlPersistentSource
            )
        {
            this.propertyValuesPersistentSource = propertyValuesPersistentSource;
            _rolePrivilegesChecker = rolePrivilegesChecker;
            this.objectFilter = objectFilter;
            this.contextFactory = contextFactory;
            this.logger = logger;
            this._claimsPrincipalReader = claimsPrincipalReader;
            this._accessControlPersistentSource = accessControlPersistentSource;
        }

        public EnvironmentApiModel GetEnvironment(string environmentName)
        {
            using (var context = contextFactory.GetContext())
            {
                var environment = EnvironmentUnifier.GetEnvironment(context, environmentName);
                return MapToEnvironmentApiModel(environment, false, false);
            }
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

        public string GetEnvironmentOwnerId(int envId)
        {
            using (var context = contextFactory.GetContext())
            {
                var env = EnvironmentUnifier.GetEnvironment(context, envId);

                if (env == null) return string.Empty;

                var permissions = _accessControlPersistentSource.GetAccessControls(env.ObjectId);
                var ownerAccess = permissions.FirstOrDefault(p => p.Allow.HasAccessLevel(AccessLevel.Owner));

                var owner = ownerAccess?.Pid ?? ownerAccess?.Sid;

                return owner;
            }
        }

        public bool IsEnvironmentOwner(string envName, ClaimsPrincipal user)
        {
            using (var context = contextFactory.GetContext())
            {
                var env = EnvironmentUnifier.GetEnvironment(context, envName);
                if (env == null) return false;

                var permissions = _accessControlPersistentSource.GetAccessControls(env.ObjectId);
                var ownerAccess = permissions.FirstOrDefault(p => p.Allow.HasAccessLevel(AccessLevel.Owner));

                string userId = _claimsPrincipalReader.GetUserId(user);
                string userlogin = _claimsPrincipalReader.GetUserLogin(user);

                return ownerAccess?.Pid == userId || // 1. oauth user, permission was added via oauth with pid
                    ownerAccess?.Sid == userId ||    // 2. winauth user, permission  was added via AD with sid (compatibility with WinAuth)
                    ownerAccess?.Sid == userlogin;   // 3. oauth user, permission was added via AD and sid was set as loginId (migration from old AD users)
            }
        }

        public bool SetEnvironmentOwner(IPrincipal updatedBy, int envId, UserElementApiModel user)
        {
            if (string.IsNullOrEmpty(user.Username) || envId <= 0) return false;

            using (var context = contextFactory.GetContext())
            {
                var envDetail = EnvironmentUnifier.GetEnvironment(context, envId);

                if (envDetail == null)
                    return false;
                var existingAccessControl = getOwnerAccessControl(envDetail);

                string userFullDomainName = _claimsPrincipalReader.GetUserFullDomainName(updatedBy);
                EnvironmentHistoryPersistentSource.AddHistory(envDetail, string.Empty,
                    "Owner Updated to " + user.DisplayName + " from " + existingAccessControl?.Name,
                    userFullDomainName, "Env Owner Update", context);

                if (existingAccessControl != null)
                {
                    // Remove only the Owner flag for old user while preserving all other access levels
                    existingAccessControl.Allow &= ~(int)AccessLevel.Owner;

                    // If no access levels remain, remove the AccessControl record
                    if (existingAccessControl.Allow == 0 && existingAccessControl.Deny == 0)
                    {
                        context.AccessControls.Remove(existingAccessControl);
                    }
                }

                // Check if new owner already has an AccessControl record
                var newOwnerAccessControl = context.AccessControls.FirstOrDefault(ac =>
                    ac.ObjectId == envDetail.ObjectId &&
                    ((!string.IsNullOrEmpty(user.Sid) && ac.Sid == user.Sid) || (!string.IsNullOrEmpty(user.Pid) && ac.Pid == user.Pid)));

                if (newOwnerAccessControl != null)
                {
                    // Add Owner flag to existing access levels
                    newOwnerAccessControl.Allow |= (int)AccessLevel.Owner;
                    newOwnerAccessControl.Name = user.DisplayName;
                }
                else
                {
                    // Create new access control entry if none exists
                    newOwnerAccessControl = new AccessControl
                    {
                        ObjectId = envDetail.ObjectId,
                        Name = user.DisplayName,
                        Sid = user.Sid,
                        Pid = user.Pid,
                        Allow = (int)AccessLevel.Owner
                    };
                    context.AccessControls.Add(newOwnerAccessControl);
                }

                context.SaveChanges();

                return true;
            }
        }

        public EnvironmentApiModel AttachServerToEnv(int envId, int serverId, ClaimsPrincipal user)
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

        public EnvironmentApiModel DetachServerFromEnv(int envId, int serverId, ClaimsPrincipal user)
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

        public EnvironmentApiModel AttachDatabaseToEnv(int envId, int databaseId, ClaimsPrincipal user)
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

        public EnvironmentApiModel DetachDatabaseFromEnv(int envId, int databaseId, ClaimsPrincipal user)
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
            string username = _claimsPrincipalReader.GetUserLogin(user);
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
            string username = _claimsPrincipalReader.GetUserLogin(user);
            var userSids = _claimsPrincipalReader.GetSidsForUser(user);
            var isAdmin = _rolePrivilegesChecker.IsAdmin(user);
            var sidSet = new HashSet<string>(userSids);

            var output = (
                from project in context.Projects.Include(p => p.Environments).ThenInclude(e => e.AccessControls)
                from environment in project.Environments
                join ac in context.AccessControls on environment.ObjectId equals ac.ObjectId
                    into accessControlEnvironments
                from allAccessControlEnvironments in accessControlEnvironments.DefaultIfEmpty()
                where project.Name == projectName
                let isDelegate =
                    (from env in context.Environments
                     where env.Name == environment.Name && environment.Users.Select(u => u.LoginId).Contains(username)
                     select environment.Name).Any()
                let permissions = (from env in context.Environments
                                   join ac in context.AccessControls on env.ObjectId equals ac.ObjectId
                                   where env.Name == environment.Name && (sidSet.Contains(ac.Sid) || ac.Pid != null && sidSet.Contains(ac.Pid)) &&
                                 ac.Allow != 0
                                   select ac.Allow).ToList()
                let hasPermission = permissions.Any(a => (a & (int)accessLevel) != 0)
                let isOwner = permissions.Any(a => (a & (int)AccessLevel.Owner) != 0)
                select new EnvironmentData
                {
                    Environment = environment,
                    UserEditable = isOwner || hasPermission || isDelegate || isAdmin,
                    IsDelegate = isDelegate,
                    IsModify = hasPermission,
                    IsOwner = isOwner,
                });

            var environmentData = output.ToList().DistinctBy(e => e.Environment.Id);

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
            string username = _claimsPrincipalReader.GetUserLogin(user);
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

        public EnvironmentApiModel GetEnvironment(int environmentId, ClaimsPrincipal user)
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

        public EnvironmentApiModel CreateEnvironment(EnvironmentApiModel env, ClaimsPrincipal user)
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
                    var e = new Environment()
                    {
                        ObjectId = Guid.NewGuid()
                    };

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
                    .Include(e => e.AccessControls)
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

                environment.AccessControls.Clear();
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
                var env = EnvironmentUnifier.GetEnvironment(context, envName);
                return env != null && env.IsProd;
            }
        }

        public bool EnvironmentIsSecure(string envName)
        {
            using (var context = contextFactory.GetContext())
            {
                var env = EnvironmentUnifier.GetEnvironment(context, envName);
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
            return context.Environments
                .Include(e => e.AccessControls)
                .Select(env => new EnvironmentData { Environment = env, UserEditable = true });
        }

        private static IQueryable<EnvironmentData> AccessibleEnvironmentAdmin(IDeploymentContext context,
            string environmentName)
        {
            return from env in context.Environments.Include(e => e.AccessControls).Include(e => e.ParentEnvironment).Include(e => e.ChildEnvironments)
                   where env.Name == environmentName
                   select new EnvironmentData
                   { Environment = env, UserEditable = true };
        }

        private static IQueryable<EnvironmentData> AccessibleEnvironmentsAccessLevel(IDeploymentContext context,
            ICollection<string> userSids, string username)
        {
            var sidSet = userSids.ToHashSet();

            var output = (from environment in context.Environments.Include(e => e.AccessControls)
                          join ac in context.AccessControls on environment.ObjectId equals ac.ObjectId into
                              accessControlEnvironments
                          from allAccessControlEnvironments in accessControlEnvironments.DefaultIfEmpty()
                          let isDelegate =
                              (from env in context.Environments
                               where env.Name == environment.Name && env.Users.Select(u => u.LoginId).Contains(username)
                               select env.Name).Any()
                          let permissions = (from env in context.Environments
                                             join ac in context.AccessControls on env.ObjectId equals ac.ObjectId
                                             where env.Name == environment.Name && (sidSet.Contains(ac.Sid) || ac.Pid != null && sidSet.Contains(ac.Pid))
                                             select ac.Allow).ToList()
                          let isModify = permissions.Any(p => (p & (int)(AccessLevel.Write | AccessLevel.Owner)) != 0)
                          let isOwner = permissions.Any(a => (a & (int)AccessLevel.Owner) != 0)
                          select new EnvironmentData
                          {
                              Environment = environment,
                              UserEditable = isOwner || isModify || isDelegate,
                              IsDelegate = isDelegate,
                              IsModify = isModify,
                              IsOwner = isOwner
                          });

            return output;
        }

        private static IQueryable<EnvironmentData> AccessibleEnvironmentAccessLevel(IDeploymentContext context,
            ICollection<string> userSids, string username, string environmentName)
        {
            var sidSet = userSids.ToHashSet(); // HashSet for faster lookup

            var output = from
                environment in context.Environments.Include(e => e.AccessControls).Include(e => e.ParentEnvironment).Include(e => e.ChildEnvironments)
                         join ac in context.AccessControls on environment.ObjectId equals ac.ObjectId into
                             accessControlEnvironments
                         from allAccessControlEnvironments in accessControlEnvironments.DefaultIfEmpty()
                         where environment.Name == environmentName
                         let isDelegate =
                             (from env in context.Environments
                              where env.Name == environment.Name && environment.Users.Select(u => u.LoginId).Contains(username)
                              select environment.Name).Any()
                         let permissions = (from env in context.Environments
                                            join ac in context.AccessControls on env.ObjectId equals ac.ObjectId
                                            where env.Name == environment.Name && (sidSet.Contains(ac.Sid) || ac.Pid != null && sidSet.Contains(ac.Pid))
                                            select ac.Allow).ToList()
                         let isModify = permissions.Any(p => (p & (int)(AccessLevel.Write | AccessLevel.Owner)) != 0)
                         let isOwner = permissions.Any(a => (a & (int)AccessLevel.Owner) != 0)
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

        /// <summary>
        /// Maps the EnvironmentApiModel to the Environment entity. Does not updates ownership, but creates for new environment
        /// </summary>
        /// <param name="env">model to set values from</param>
        /// <param name="e">model to set values to. DTO for DB</param>
        private static void MapToEnvironment(EnvironmentApiModel env, Environment e)
        {
            e.Name = env.EnvironmentName;
            e.Secure = env.EnvironmentSecure;
            e.IsProd = env.EnvironmentIsProd;

            e.Name = env.EnvironmentName;
            e.Description = env.Details.Description;
            e.FileShare = env.Details.FileShare;
            e.LastUpdate = !string.IsNullOrEmpty(env.Details.LastUpdated)
                ? DateTime.Parse(env.Details.LastUpdated)
                : DateTime.Now;
            e.ThinClientServer = env.Details.ThinClient;
            e.RestoredFromBackup = env.Details.RestoredFromSourceDb;
            e.EnvNote = env.Details.Notes;
            e.ParentId = env.ParentId;

            var ownerAccess = e.AccessControls.FirstOrDefault(ac => ac.Allow.HasAccessLevel(AccessLevel.Owner));

            // for new environment we have to add owner access control, for old env ownership is not changing via update method
            if (ownerAccess == null)
            {
                // TODO: remove this together with supporting AD and Sids
                bool isSid = !string.IsNullOrEmpty(env.Details.EnvironmentOwnerId) &&
                env.Details.EnvironmentOwnerId.StartsWith("S-1-5-");

                e.AccessControls.Add(new AccessControl
                {
                    ObjectId = e.ObjectId,
                    Name = env.Details.EnvironmentOwner,
                    Pid = isSid ? null : env.Details.EnvironmentOwnerId,
                    Sid = isSid ? env.Details.EnvironmentOwnerId : null,
                    Allow = (int)AccessLevel.Owner
                });
            }
        }

        private EnvironmentApiModel MapToEnvironmentApiModel(EnvironmentData ed)
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

        public EnvironmentApiModel MapToEnvironmentApiModel(Environment env,
            bool userEditable, bool isOwner)
        {
            if (env == null)
                return null;

            var resEnv = MapToEnvironmentApiModel(env);
            resEnv.UserEditable = userEditable;
            resEnv.IsOwner = isOwner;

            return resEnv;
        }

        public EnvironmentApiModel MapToEnvironmentApiModel(Environment? env)
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

        public EnvironmentDetailsApiModel MapToEnvironmentDetailsApiModel(Environment details)
        {
            if (details == null)
                return null;

            AccessControl ownerAc = getOwnerAccessControl(details);

            return new EnvironmentDetailsApiModel
            {
                Description = details.Description,
                EnvironmentOwner = ownerAc?.Name,
                EnvironmentOwnerId = ownerAc?.Pid ?? ownerAc?.Sid,
                FileShare = details.FileShare,
                LastUpdated = details.LastUpdate.ToString(),
                ThinClient = details.ThinClientServer,
                RestoredFromSourceDb = details.RestoredFromBackup,
                Notes = details.EnvNote
            };
        }

        private AccessControl? getOwnerAccessControl(Environment env)
        {
            var ownerAc = env.AccessControls.FirstOrDefault(ac => ac.Allow.HasAccessLevel(AccessLevel.Owner));
            if (ownerAc == null)
                logger.Warn($"Owner access control was not found for Environment '{env.Name}', ObjecId:{env.ObjectId}. Check that code has Include(e => e.AccessControls) and Distinct() is not used upper in IQueryable for unnamed object containing Environment");
            return ownerAc;
        }

        public IEnumerable<EnvironmentApiModel> GetPossibleEnvironmentChildren(int id, IPrincipal user)
        {
            using (var context = contextFactory.GetContext())
            {
                var userSids = _claimsPrincipalReader.GetSidsForUser(user);
                var sidSet = new HashSet<string>(userSids);

                var accessLevelRequired = AccessLevel.Write | AccessLevel.Owner;

                var allRelatedEnvs = context.Environments
                    .Include(e => e.AccessControls)
                    .Include(e => e.Projects)
                    .ThenInclude(p => p.Environments)
                    .Where(e => e.Id == id)
                    .SelectMany(e => e.Projects.SelectMany(p => p.Environments)) // Get all environments from all related projects
                    .Where(e => e.Id != id && e.ParentId == null); // Exclude the parent and all children

                var filteredByAccessLevelEnvs = allRelatedEnvs
                    .Join(context.AccessControls, // Join with AccessControls to filter by user access
                          environment => environment.ObjectId,
                          ac => ac.ObjectId,
                          (environment, ac) => new { environment, ac })
                    .Where(joined => (sidSet.Contains(joined.ac.Sid) || joined.ac.Pid != null && sidSet.Contains(joined.ac.Pid)) && (joined.ac.Allow & (int)accessLevelRequired) != 0)
                    .Select(joined => joined.environment);

                var mappedEnvironments = _rolePrivilegesChecker.IsAdmin(user) ? allRelatedEnvs.ToList() : filteredByAccessLevelEnvs.ToList();

                var envChain = context.GetFullEnvironmentChain(id);

                mappedEnvironments = mappedEnvironments.Where(e => !envChain.Any(ec => ec.Id == e.Id)).ToList().DistinctBy(e => e.Id).ToList(); // filter all envs already in chain

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