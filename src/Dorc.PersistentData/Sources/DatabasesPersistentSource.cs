using Dorc.ApiModel;
using Dorc.PersistentData.Contexts;
using Dorc.PersistentData.Extensions;
using Dorc.PersistentData.Model;
using Dorc.PersistentData.Sources.Interfaces;
using Microsoft.EntityFrameworkCore;
using System.Linq.Expressions;
using System.Security.Principal;
using Database = Dorc.PersistentData.Model.Database;

namespace Dorc.PersistentData.Sources
{
    public class DatabasesPersistentSource : PagingPersistentSourceBase, IDatabasesPersistentSource
    {
        private readonly IDeploymentContextFactory _contextFactory;
        private readonly IRolePrivilegesChecker _rolePrivilegesChecker;
        private readonly IClaimsPrincipalReader _claimsPrincipalReader;

        public DatabasesPersistentSource(
            IDeploymentContextFactory contextFactory,
            IRolePrivilegesChecker rolePrivilegesChecker,
            IClaimsPrincipalReader claimsPrincipalReader
            )
        {
            _rolePrivilegesChecker = rolePrivilegesChecker;
            _contextFactory = contextFactory;
            _claimsPrincipalReader = claimsPrincipalReader;
        }

        public DatabaseApiModel? GetDatabase(int id)
        {
            using (var context = _contextFactory.GetContext())
            {
                return MapToDatabaseApiModel(context.Databases.SingleOrDefault(x => x.Id == id));
            }
        }

        public IEnumerable<DatabaseApiModel> GetDatabases(string name, string server)
        {
            using (var context = _contextFactory.GetContext())
            {
                return context.Databases.Include(d => d.Group).Where(d => d.Name.Equals(name) && d.ServerName.Equals(server)).ToList()
                    .Select(MapToDatabaseApiModel).ToList();
            }
        }

        public IEnumerable<DatabaseApiModel> GetDatabases()
        {
            using (var context = _contextFactory.GetContext())
            {
                return context.Databases.Include(d => d.Group).Where(d => d.Name != null).ToList()
                    .Select(MapToDatabaseApiModel).ToList();

            }
        }

        public IEnumerable<string> GetEnvironmentNamesForDatabaseId(int serverId)
        {
            var output = new List<string>();
            using (var context = _contextFactory.GetContext())
            {
                var database = context.Databases.Include(s => s.Environments)
                    .FirstOrDefault(s => s.Id.Equals(serverId));

                if (database == null)
                    return output;

                var envDetailNames = database.Environments.Select(s => s.Name);

                foreach (var envName in envDetailNames)
                {
                    var environment = EnvironmentUnifier.GetEnvironment(context, envName);
                    if (environment != null)
                        output.Add(environment.Name);
                }

                return output;
            }
        }

        public DatabaseApiModel? AddDatabase(DatabaseApiModel db)
        {
            using (var context = _contextFactory.GetContext())
            {
                var currentDatabase = GetDatabase(db, context);

                if (currentDatabase != null)
                {
                    throw new ArgumentException($"Database already exists {db.ServerName}:{db.Name}");
                }

                var newDatabase = MapToDatabase(db);

                var adGroup = context.AdGroups
                    .FirstOrDefault(g => g.Name == db.AdGroup);

                newDatabase.Group = adGroup;

                context.Databases.Add(newDatabase);

                context.SaveChanges();

                return GetDatabase(db, context);
            }
        }

        public DatabaseApiModel? GetDatabaseByType(string envName, string type)
        {
            using (var context = _contextFactory.GetContext())
            {
                var dbDetails = context.Environments
                    .Include(env => env.Databases)
                    .Single(e => e.Name == envName)
                    .Databases.SingleOrDefault(x => x.Type == type);
                return dbDetails != null ? MapToDatabaseApiModel(dbDetails) : null;
            }
        }

        private DatabaseApiModel? GetDatabase(DatabaseApiModel db, IDeploymentContext context)
        {
            var database =
                MapToDatabaseApiModel(
                context.Databases.SingleOrDefault(d => d.Name.Equals(db.Name)
                                                       &&
                                                       d.ServerName.Equals(db.ServerName)));

            return database;
        }

        public bool DeleteDatabase(int databaseId)
        {
            using (var context = _contextFactory.GetContext())
            {
                var entity = context.Databases.Find(databaseId);
                if (entity == null)
                    return false;

                context.Databases.Remove(entity);
                context.SaveChanges();
                return true;
            }
        }

        public DatabaseApiModel GetDatabaseByType(EnvironmentApiModel environment, string type)
        {
            using (var context = _contextFactory.GetContext())
            {
                var endurDb = context.Databases
                    .Include(d => d.Environments)
                    .Include(d => d.Group)
                    .SingleOrDefault(d =>
                        d.Type == type
                        && d.Environments.FirstOrDefault().Name == environment.EnvironmentName);
                return endurDb != null ? MapToDatabaseApiModel(endurDb) : null;
            }
        }

        public IEnumerable<DatabaseApiModel> GetDatabasesForEnvId(int environmentId)
        {
            using (var context = _contextFactory.GetContext())
            {
                var env = EnvironmentUnifier.GetEnvironment(context, environmentId);
                if (env == null)
                {
                    return new List<DatabaseApiModel>();
                }

                return GetDatabasesForEnvironmentName(env.Name);
            }
        }

        public IEnumerable<DatabaseApiModel> GetDatabasesForEnvironmentName(string environmentName)
        {
            using (var context = _contextFactory.GetContext())
            {
                var result = context.Databases
                    .Include(d => d.Group)
                    .Where(database => database.Environments
                        .Any(env => env.Name == environmentName))
                    .OrderBy(database => database.Name).ToList();
                return result.Select(MapToDatabaseApiModel).ToList();
            }
        }


        public GetDatabaseApiModelListResponseDto GetDatabaseApiModelByPage(int limit, int page,
            PagedDataOperators operators, IPrincipal user)
        {
            PagedModel<Database> output = null;
            using (var context = _contextFactory.GetContext())
            {
                var isAdmin = _rolePrivilegesChecker.IsAdmin(user);

                string username = _claimsPrincipalReader.GetUserName(user);
                var envPrivilegeInfos = GetEnvironmentPrivInfos(username, context);

                var reqStatusesQueryable = context.Databases.Include(database => database.Environments)
                    .Include(database => database.Group).AsQueryable();

                if (operators.Filters != null && operators.Filters.Any())
                {
                    var filterLambdas =
                        new List<Expression<Func<Database, bool>>>();
                    foreach (var pagedDataFilter in operators.Filters)
                    {
                        if (pagedDataFilter == null)
                            continue;

                        if (!string.IsNullOrEmpty(pagedDataFilter.Path) && !string.IsNullOrEmpty(pagedDataFilter.FilterValue))
                        {
                            if (pagedDataFilter.Path == "EnvironmentNames")
                            {
                                filterLambdas.Add(server =>
                                    server.Environments.Any(ed => ed.Name.Contains(pagedDataFilter.FilterValue)));
                            }
                            else
                            {
                                filterLambdas.Add(reqStatusesQueryable.ContainsExpression(pagedDataFilter.Path,
                                    pagedDataFilter.FilterValue));
                            }
                        }
                    }

                    reqStatusesQueryable = WhereAll(reqStatusesQueryable, filterLambdas.ToArray());
                }

                if (operators.SortOrders != null && operators.SortOrders.Any())
                {
                    IOrderedQueryable<Database> orderedQuery = null;

                    for (var i = 0; i < operators.SortOrders.Count; i++)
                    {
                        if (operators.SortOrders[i] == null)
                            continue;
                        if (string.IsNullOrEmpty(operators.SortOrders[i].Path) ||
                            string.IsNullOrEmpty(operators.SortOrders[i].Direction))
                            continue;

                        if (operators.SortOrders[i].Path == "EnvironmentNames")
                        {
                            operators.SortOrders[i].Path = "Environments";
                        }

                        var param = Expression.Parameter(typeof(Database), "Database");
                        var prop = Expression.PropertyOrField(param, operators.SortOrders[i].Path);

                        switch (prop.Type)
                        {
                            case Type boolType when boolType == typeof(bool):
                                {
                                    var expr = GetExpressionForOrdering<Database, bool>(prop, param);
                                    orderedQuery = OrderScripts(operators, i, orderedQuery, reqStatusesQueryable, expr);
                                    break;
                                }
                            case Type stringType when stringType == typeof(string):
                                {
                                    var expr = GetExpressionForOrdering<Database, string>(prop, param);
                                    orderedQuery = OrderScripts(operators, i, orderedQuery, reqStatusesQueryable, expr);
                                    break;
                                }
                            case Type intType when intType == typeof(int):
                                {
                                    var expr = GetExpressionForOrdering<Database, int>(prop, param);
                                    orderedQuery = OrderScripts(operators, i, orderedQuery, reqStatusesQueryable, expr);
                                    break;
                                }
                            case Type datetimeType when datetimeType == typeof(DateTime):
                                {
                                    var expr = GetExpressionForOrdering<Database, DateTime>(prop, param);
                                    orderedQuery = OrderScripts(operators, i, orderedQuery, reqStatusesQueryable, expr);
                                    break;
                                }
                        }
                    }

                    if (orderedQuery != null)
                        output = orderedQuery.AsNoTracking()
                            .Paginate(page, limit);
                }

                if (output == null)
                    output = reqStatusesQueryable.AsNoTracking()
                        .OrderBy(s => s.Id)
                        .Paginate(page, limit);

                return new GetDatabaseApiModelListResponseDto
                {
                    CurrentPage = output.CurrentPage,
                    TotalPages = output.TotalPages,
                    TotalItems = output.TotalItems,
                    Items = output.Items.Select(s => new DatabaseApiModel
                    {
                        Id = s.Id,
                        Name = s.Name,
                        Type = s.Type,
                        ServerName = s.ServerName,
                        AdGroup = s.Group?.Name,
                        ArrayName = s.ArrayName,
                        EnvironmentNames = s.Environments.Select(ed => ed.Name).ToList(),
                        UserEditable = (from environmentDetail in s.Environments
                                        select envPrivilegeInfos[environmentDetail.Name]
                                            into privilegeInfo
                                        where privilegeInfo != null
                                        select privilegeInfo.IsOwner || privilegeInfo.HasPermission || privilegeInfo.IsDelegate ||
                                               isAdmin).All(e => e)
                    }).ToList()
                };
            }
        }

        public List<String?> GetDatabasServerNameslist()
        {
            using (var context = _contextFactory.GetContext())
            {
                var list = context.Databases
                    .Where(d => d.ServerName != null && d.ServerName != "")
                    .Select(d => d.ServerName)
                    .Distinct().ToList();
                return list ?? new List<String?>();
            }
        }

        public DatabaseApiModel? GetApplicationDatabaseForEnvFilter(string username, string envFilter,
            string envName)
        {
            using (var context = _contextFactory.GetContext())
            {
                var environmentDetails = context.Environments.Include(ed => ed.Databases)
                    .First(environmentDetails =>
                        EF.Functions.Collate(environmentDetails.Name, DeploymentContext.CaseInsensitiveCollation)
                        == EF.Functions.Collate(envName, DeploymentContext.CaseInsensitiveCollation)
                        && EF.Functions.Collate(environmentDetails.ThinClientServer, DeploymentContext.CaseInsensitiveCollation)
                        == EF.Functions.Collate(envFilter, DeploymentContext.CaseInsensitiveCollation));

                var dbIds = environmentDetails.Databases.Select(d => d.Id).ToList();

                var database = context.EnvironmentUsers.Include(eu => eu.Database).Include(eu => eu.User)
                    .Where(eu =>
                        dbIds.Contains(eu.Database.Id) && eu.User.LoginId.Equals(username) &&
                        eu.User.LoginType.Equals(envFilter)).Select(eu => eu.Database).FirstOrDefault();

                return MapToDatabaseApiModel(database);
            }
        }

        public DatabaseApiModel? UpdateDatabase(int id, DatabaseApiModel database, IPrincipal user)
        {
            using (var context = _contextFactory.GetContext())
            {
                var existingDatabase = context.Databases.Find(database.Id);

                if (existingDatabase == null)
                    return null;

                // Check if another database already exists with the same name and server (excluding current database)
                var duplicateDatabase = context.Databases.SingleOrDefault(d => 
                    d.Name.Equals(database.Name) && 
                    d.ServerName.Equals(database.ServerName) && 
                    d.Id != database.Id);

                if (duplicateDatabase != null)
                {
                    throw new ArgumentException($"Database already exists {database.ServerName}:{database.Name}");
                }

                existingDatabase.Name = database.Name;
                existingDatabase.ServerName = database.ServerName;
                existingDatabase.ArrayName = database.ArrayName;
                existingDatabase.Type = database.Type;

                var adGroup = context.AdGroups
                    .FirstOrDefault(g => g.Name == database.AdGroup);
                if (adGroup != null)
                    existingDatabase.Group = adGroup;

                context.SaveChanges();

                var apiServer = MapToDatabaseApiModel(existingDatabase);

                if (apiServer == null)
                    return null;

                apiServer.UserEditable = database.UserEditable;
                return apiServer;
            }
        }

        private Database MapToDatabase(DatabaseApiModel db)
        {
            return new Database
            {
                Id = db.Id,
                Name = db.Name,
                ServerName = db.ServerName,
                Type = db.Type,
                ArrayName = db.ArrayName
            };
        }

        public static DatabaseApiModel? MapToDatabaseApiModel(Database db)
        {
            if (db == null)
                return null;

            return new DatabaseApiModel
            {
                AdGroup = db.Group?.Name,
                Id = db.Id,
                Name = db.Name,
                Type = db.Type,
                ServerName = db.ServerName,
                ArrayName = db.ArrayName
            };
        }
    }
}