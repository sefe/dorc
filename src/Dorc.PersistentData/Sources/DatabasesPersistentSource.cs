using Dorc.ApiModel;
using Dorc.PersistentData.Contexts;
using Dorc.PersistentData.Extensions;
using Dorc.PersistentData.Model;
using Dorc.PersistentData.Sources.Interfaces;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using System.Linq.Expressions;
using System.Net;
using System.Security.Principal;
using Database = Dorc.PersistentData.Model.Database;

namespace Dorc.PersistentData.Sources
{
    public class DatabasesPersistentSource : PagingPersistentSourceBase, IDatabasesPersistentSource
    {
        private readonly IDeploymentContextFactory _contextFactory;
        private readonly IRolePrivilegesChecker _rolePrivilegesChecker;
        private readonly IClaimsPrincipalReader _claimsPrincipalReader;

        protected override IClaimsPrincipalReader ClaimsPrincipalReader => _claimsPrincipalReader;

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
                return MapToDatabaseApiModel(context.Databases
                    .Include(x => x.TagLinks)
                    .SingleOrDefault(x => x.Id == id));
            }
        }

        public IEnumerable<DatabaseApiModel> GetDatabases(string name, string server)
        {
            using (var context = _contextFactory.GetContext())
            {
                return context.Databases
                    .Include(d => d.Group)
                    .Include(d => d.TagLinks)
                    .Include(d => d.Environments)
                    .Where(d => d.Name.Equals(name) && d.ServerName.Equals(server)).ToList()
                    .Select(MapToDatabaseApiModel).ToList();
            }
        }

        public IEnumerable<DatabaseApiModel> GetDatabases()
        {
            using (var context = _contextFactory.GetContext())
            {
                return context.Databases.Include(d => d.Group).Include(d => d.TagLinks).Where(d => d.Name != null).ToList()
                    .Select(MapToDatabaseApiModel).ToList();

            }
        }

        public IEnumerable<string> GetEnvironmentNamesForDatabaseId(int serverId)
        {
            var output = new List<string>();
            using (var context = _contextFactory.GetContext())
            {
                var database = context.Databases.Include(s => s.TagLinks).Include(s => s.Environments)
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

        private const int DefaultSqlServerPort = 1433;

        private readonly record struct SqlServerEndpoint(
            string NormalizedHost,
            int? ExplicitPort,
            string? InstanceName);

        public IEnumerable<DatabaseApiModel> GetDatabasesForServerName(string serverName)
        {
            if (string.IsNullOrWhiteSpace(serverName))
            {
                return Enumerable.Empty<DatabaseApiModel>();
            }

            using var context = _contextFactory.GetContext();
            var matchingIds = context.Databases
                .Where(d => d.ServerName != null && d.ServerName != "")
                .Select(d => new { d.Id, d.ServerName })
                .AsEnumerable()
                .Where(d => SqlServerInstancesMatch(d.ServerName, serverName))
                .Select(d => d.Id)
                .Distinct()
                .ToList();

            if (matchingIds.Count == 0)
            {
                return Enumerable.Empty<DatabaseApiModel>();
            }

            return context.Databases
                .Include(d => d.Group)
                .Include(d => d.TagLinks)
                .Include(d => d.Environments)
                .Where(d => matchingIds.Contains(d.Id))
                .ToList()
                .Select(MapToDatabaseApiModel)
                .Where(d => d != null)
                .Cast<DatabaseApiModel>()
                .ToList();
        }

        public IEnumerable<DatabaseApiModel> GetDatabasesForServerAndLogin(string serverName, string username)
        {
            if (string.IsNullOrWhiteSpace(serverName) || string.IsNullOrWhiteSpace(username))
            {
                return Enumerable.Empty<DatabaseApiModel>();
            }

            string trimmedUsername = username.Trim();
            using var context = _contextFactory.GetContext();
            var matchingIds = context.EnvironmentUsers
                .Where(eu => eu.Database != null
                    && eu.Database.ServerName != null
                    && eu.Database.ServerName != ""
                    && eu.User != null
                    && EF.Functions.Collate(eu.User.LoginId, DeploymentContext.CaseInsensitiveCollation)
                        == EF.Functions.Collate(trimmedUsername, DeploymentContext.CaseInsensitiveCollation))
                .Select(eu => new { eu.Database.Id, eu.Database.ServerName })
                .AsEnumerable()
                .Where(d => SqlServerInstancesMatch(d.ServerName, serverName))
                .Select(d => d.Id)
                .Distinct()
                .ToList();

            if (matchingIds.Count == 0)
            {
                return Enumerable.Empty<DatabaseApiModel>();
            }

            return context.Databases
                .Include(d => d.Group)
                .Include(d => d.TagLinks)
                .Include(d => d.Environments)
                .Where(d => matchingIds.Contains(d.Id))
                .ToList()
                .Select(MapToDatabaseApiModel)
                .Where(d => d != null)
                .Cast<DatabaseApiModel>()
                .ToList();
        }

        public static bool SqlServerInstancesMatch(string? leftServerName, string? rightServerName)
        {
            if (!TryParseSqlServerEndpoint(leftServerName, out SqlServerEndpoint left)
                || !TryParseSqlServerEndpoint(rightServerName, out SqlServerEndpoint right))
            {
                return false;
            }

            return SqlServerHostsMatch(left.NormalizedHost, right.NormalizedHost)
                && SqlServerEndpointsMatch(left, right);
        }

        private static bool TryParseSqlServerEndpoint(
            string? rawServerName,
            out SqlServerEndpoint endpoint)
        {
            endpoint = default;

            if (string.IsNullOrWhiteSpace(rawServerName))
            {
                return false;
            }

            string candidate = StripSqlProtocolPrefix(rawServerName.Trim());
            if (string.IsNullOrWhiteSpace(candidate))
            {
                return false;
            }

            int? explicitPort = null;
            int commaIndex = candidate.LastIndexOf(',');
            if (commaIndex >= 0)
            {
                string portPart = candidate[(commaIndex + 1)..].Trim();
                candidate = candidate[..commaIndex].Trim();
                if (!int.TryParse(portPart, NumberStyles.None, CultureInfo.InvariantCulture, out int parsedPort)
                    || parsedPort is < 1 or > 65535)
                {
                    return false;
                }

                explicitPort = parsedPort;
            }

            string? instanceName = null;
            int slashIndex = candidate.IndexOf('\\');
            if (slashIndex >= 0)
            {
                string instancePart = candidate[(slashIndex + 1)..].Trim();
                candidate = candidate[..slashIndex].Trim();
                if (string.IsNullOrEmpty(instancePart))
                {
                    return false;
                }

                if (!string.Equals(instancePart, "MSSQLSERVER", StringComparison.OrdinalIgnoreCase))
                {
                    instanceName = instancePart;
                }
            }

            if (string.IsNullOrWhiteSpace(candidate))
            {
                return false;
            }

            string normalizedHost;
            if (IsLocalSqlServerAlias(candidate))
            {
                normalizedHost = "localhost";
            }
            else
            {
                normalizedHost = candidate.TrimEnd('.');
                if (string.IsNullOrWhiteSpace(normalizedHost))
                {
                    return false;
                }
            }

            endpoint = new SqlServerEndpoint(normalizedHost, explicitPort, instanceName);
            return true;
        }

        private static string StripSqlProtocolPrefix(string value)
        {
            ReadOnlySpan<string> prefixes = ["tcp:", "np:", "lpc:", "admin:"];
            foreach (string prefix in prefixes)
            {
                if (value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    return value[prefix.Length..].Trim();
                }
            }

            return value;
        }

        private static bool IsLocalSqlServerAlias(string hostCandidate)
        {
            return string.Equals(hostCandidate, ".", StringComparison.OrdinalIgnoreCase)
                || string.Equals(hostCandidate, "(local)", StringComparison.OrdinalIgnoreCase)
                || string.Equals(hostCandidate, "localhost", StringComparison.OrdinalIgnoreCase)
                || string.Equals(hostCandidate, "localhost.", StringComparison.OrdinalIgnoreCase)
                || string.Equals(hostCandidate, "127.0.0.1", StringComparison.OrdinalIgnoreCase)
                || string.Equals(hostCandidate, "::1", StringComparison.OrdinalIgnoreCase)
                || string.Equals(hostCandidate, "[::1]", StringComparison.OrdinalIgnoreCase);
        }

        private static bool SqlServerHostsMatch(string leftHost, string rightHost)
        {
            if (string.Equals(leftHost, rightHost, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return IsShortHostMatchingFqdn(leftHost, rightHost)
                || IsShortHostMatchingFqdn(rightHost, leftHost);
        }

        private static bool IsShortHostMatchingFqdn(string candidateShortHost, string candidateFqdn)
        {
            if (string.Equals(candidateShortHost, "localhost", StringComparison.OrdinalIgnoreCase)
                || string.Equals(candidateFqdn, "localhost", StringComparison.OrdinalIgnoreCase)
                || candidateShortHost.Contains('.')
                || candidateShortHost.Contains(':')
                || candidateFqdn.Contains(':')
                || IPAddress.TryParse(candidateShortHost, out _)
                || IPAddress.TryParse(candidateFqdn, out _))
            {
                return false;
            }

            return candidateFqdn.Length > candidateShortHost.Length + 1
                && candidateFqdn.StartsWith(candidateShortHost, StringComparison.OrdinalIgnoreCase)
                && candidateFqdn[candidateShortHost.Length] == '.';
        }

        private static bool SqlServerEndpointsMatch(
            SqlServerEndpoint left,
            SqlServerEndpoint right)
        {
            if (left.ExplicitPort.HasValue && right.ExplicitPort.HasValue)
            {
                return left.ExplicitPort.Value == right.ExplicitPort.Value;
            }

            if (left.ExplicitPort.HasValue || right.ExplicitPort.HasValue)
            {
                SqlServerEndpoint explicitEndpoint = left.ExplicitPort.HasValue ? left : right;
                SqlServerEndpoint implicitEndpoint = left.ExplicitPort.HasValue ? right : left;

                if (explicitEndpoint.ExplicitPort!.Value == DefaultSqlServerPort)
                {
                    return string.IsNullOrEmpty(implicitEndpoint.InstanceName);
                }

                // A named instance without an explicit port and a non-default explicit TCP port on
                // the same host may refer to the same SQL Server instance; fail closed.
                return !string.IsNullOrEmpty(implicitEndpoint.InstanceName);
            }

            if (string.IsNullOrEmpty(left.InstanceName) && string.IsNullOrEmpty(right.InstanceName))
            {
                return true;
            }

            if (!string.IsNullOrEmpty(left.InstanceName) && !string.IsNullOrEmpty(right.InstanceName))
            {
                return string.Equals(left.InstanceName, right.InstanceName, StringComparison.OrdinalIgnoreCase);
            }

            return false;
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

        public bool UpdateDatabaseType(int databaseId, DatabaseType databaseType)
        {
            using var context = _contextFactory.GetContext();
            var database = context.Databases.Find(databaseId);
            if (database == null)
                return false;

            database.DatabaseType = databaseType;
            context.SaveChanges();
            return true;
        }

        public DatabaseApiModel? GetDatabaseByTag(string envName, string tag)
        {
            using (var context = _contextFactory.GetContext())
            {
                // Matched in the query rather than over the materialised graph so the
                // comparison keeps the database's case-insensitive collation. Doing it
                // in memory would compare Ordinal, and this overload would then
                // disagree with the DatabaseTagMatch-based one on whether 'endur'
                // finds a database tagged 'Endur'.
                var matches = context.Databases
                    .Include(d => d.TagLinks)
                    .Where(d => d.Environments.Any(e => e.Name == envName))
                    .Where(DatabaseTagMatch.HasTag(tag))
                    .ToList();
                var dbDetails = RequireAtMostOneDatabaseByTag(matches, envName, tag);
                return dbDetails != null ? MapToDatabaseApiModel(dbDetails) : null;
            }
        }

        /// <summary>
        /// Make the database's tag rows match the supplied set, and keep the
        /// deprecated delimited column in step until the follow-up release drops it.
        /// Rows are added and removed rather than cleared and rebuilt, so unchanged
        /// tags keep their identity and the write is a no-op when nothing moved.
        /// </summary>
        private static void SyncTagLinks(Database entity, IEnumerable<string> tags)
        {
            var wanted = TagString.Normalize(tags);

            var toRemove = entity.TagLinks
                .Where(link => !wanted.Contains(link.Tag, TagString.Comparer))
                .ToList();
            foreach (var link in toRemove)
                entity.TagLinks.Remove(link);

            var existing = entity.TagLinks.Select(link => link.Tag).ToArray();
            foreach (var tag in wanted.Where(t => !existing.Contains(t, TagString.Comparer)))
                entity.TagLinks.Add(new DatabaseTag { DatabaseId = entity.Id, Tag = tag });

            entity.Tags = TagString.Join(wanted);
        }

        private DatabaseApiModel? GetDatabase(DatabaseApiModel db, IDeploymentContext context)
        {
            var database =
                MapToDatabaseApiModel(
                context.Databases
                    .Include(d => d.TagLinks)
                    .SingleOrDefault(d => d.Name.Equals(db.Name)
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

        public DatabaseApiModel GetDatabaseByTag(EnvironmentApiModel environment, string tag)
        {
            using (var context = _contextFactory.GetContext())
            {
                var matches = context.Databases
                    .Include(d => d.Environments)
                    .Include(d => d.TagLinks)
                    .Include(d => d.Group)
                    .Where(DatabaseTagMatch.HasTag(tag))
                    .Where(d => d.Environments.Any(e => e.Name == environment.EnvironmentName))
                    .ToList();
                var endurDb = RequireAtMostOneDatabaseByTag(matches, environment.EnvironmentName, tag);
                return endurDb != null ? MapToDatabaseApiModel(endurDb) : null;
            }
        }

        private static Database? RequireAtMostOneDatabaseByTag(IReadOnlyList<Database> matches, string envName, string tag)
        {
            if (matches.Count == 0)
                return null;
            if (matches.Count == 1)
                return matches[0];

            var conflicting = string.Join(", ", matches.Select(d =>
                string.IsNullOrWhiteSpace(d.ServerName)
                    ? $"'{d.Name}'"
                    : $"'{d.Name}' on '{d.ServerName}'"));
            throw new InvalidOperationException(
                $"Environment '{envName}' has multiple databases tagged '{tag}' ({conflicting}). Ensure at most one database on the environment carries the '{tag}' tag.");
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
                    .Include(d => d.TagLinks)
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

                var envPrivilegeInfos = GetEnvironmentPrivInfos(user, context);

                var reqStatusesQueryable = context.Databases.Include(database => database.Environments)
                    .Include(database => database.TagLinks)
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
                            else if (pagedDataFilter.Path == "Tags")
                            {
                                // Matched against the tag rows, not the deprecated
                                // delimited column. Letting ContainsExpression resolve
                                // "Tags" by name would bind to that column, so the grid
                                // would filter a store deployments no longer read from —
                                // and would throw outright once the follow-up release
                                // drops it.
                                filterLambdas.Add(database =>
                                    database.TagLinks.Any(t => t.Tag.Contains(pagedDataFilter.FilterValue)));
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

                        if (operators.SortOrders[i].Path == "Tags")
                        {
                            // Ordered on the tag rows by their alphabetically first tag.
                            // Name-resolving "Tags" would order on the deprecated column,
                            // which sorts by whatever order the tags happen to be joined
                            // in and stops existing when that column is dropped.
                            Expression<Func<Database, string>> firstTag =
                                d => d.TagLinks.Select(t => t.Tag).OrderBy(t => t).FirstOrDefault();
                            orderedQuery = OrderScripts(operators, i, orderedQuery, reqStatusesQueryable, firstTag);
                            continue;
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
                        Tags = s.TagLinks.Select(t => t.Tag).ToArray(),
                        ServerName = s.ServerName,
                        DatabaseType = s.DatabaseType,
                        AdGroup = s.Group?.Name,
                        ArrayName = s.ArrayName,
                        EnvironmentNames = s.Environments.Select(ed => ed.Name).ToList(),
                        UserEditable = (from environmentDetail in s.Environments
                                        select envPrivilegeInfos[environmentDetail.Name]
                                            into privilegeInfo
                                        where privilegeInfo != null
                                        select privilegeInfo.IsOwner || privilegeInfo.HasPermission ||
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

        public DatabaseApiModel? GetApplicationDatabaseForEnvFilter(string username, string envFilter, string envName)
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

                var database = context.EnvironmentUsers
                    .Include(eu => eu.Database).ThenInclude(d => d.TagLinks)
                    .Include(eu => eu.User)
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
                // Include the tag rows: SyncTagLinks reconciles against what is
                // already there, and Find does not populate navigations. Without
                // this it sees an empty set, re-inserts every tag the database
                // already carries (PK violation) and never issues the deletes.
                var existingDatabase = context.Databases
                    .Include(d => d.TagLinks)
                    .FirstOrDefault(d => d.Id == database.Id);

                if (existingDatabase == null)
                    return null;

                // Check if another database already exists with the same name and server (excluding current database)
                var duplicateExists = context.Databases.Any(d =>
                    d.Name.Equals(database.Name) &&
                    d.ServerName.Equals(database.ServerName) &&
                    d.Id != database.Id);

                if (duplicateExists)
                {
                    throw new ArgumentException($"Database already exists {database.ServerName}:{database.Name}");
                }

                existingDatabase.Name = database.Name;
                existingDatabase.ServerName = database.ServerName;
                existingDatabase.DatabaseType = database.DatabaseType ?? existingDatabase.DatabaseType;
                existingDatabase.ArrayName = database.ArrayName;
                SyncTagLinks(existingDatabase, database.Tags);

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
                DatabaseType = db.DatabaseType ?? DatabaseType.SqlServer,
                Tags = TagString.Join(db.Tags),
                TagLinks = TagString.Normalize(db.Tags)
                    .Select(t => new DatabaseTag { Tag = t }).ToList(),
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
                Tags = db.TagLinks != null ? db.TagLinks.Select(t => t.Tag).ToArray() : System.Array.Empty<string>(),
                ServerName = db.ServerName,
                DatabaseType = db.DatabaseType,
                ArrayName = db.ArrayName,
                EnvironmentNames = db.Environments != null ? db.Environments.Select(e => e.Name).ToList() : new List<string>()
            };
        }

        public IEnumerable<DatabaseApiModel> FindDatabases(string name, string? server)
        {
            using (var context = _contextFactory.GetContext())
            {
                var query = context.Databases
                    .Include(d => d.Group)
                    .Include(d => d.TagLinks)
                    .Include(d => d.Environments)
                    .Where(d => d.Name == name);

                if (!string.IsNullOrWhiteSpace(server))
                    query = query.Where(d => d.ServerName == server);

                return query.ToList().Select(MapToDatabaseApiModel).ToList();
            }
        }

        public DatabaseRelatedResourcesApiModel? GetDatabaseRelatedResources(int databaseId)
        {
            using (var context = _contextFactory.GetContext())
            {
                var database = context.Databases
                    .AsNoTracking()
                    .Where(d => d.Id == databaseId)
                    .Select(d => new { d.Id, d.Name, d.ServerName })
                    .SingleOrDefault();

                if (database == null)
                    return null;

                var environments = context.Databases
                    .AsNoTracking()
                    .Where(d => d.Id == databaseId)
                    .SelectMany(d => d.Environments)
                    .Select(e => new EnvironmentShortApiModel
                    {
                        EnvironmentId = e.Id,
                        EnvironmentName = e.Name
                    })
                    .OrderBy(e => e.EnvironmentName)
                    .ToList();

                var servers = context.Databases
                    .AsNoTracking()
                    .Where(d => d.Id == databaseId)
                    .SelectMany(d => d.Environments)
                    .SelectMany(e => e.Servers)
                    .Select(s => new
                    {
                        s.Id,
                        s.Name,
                        s.OsName,
                        Tags = s.TagLinks.Select(t => t.Tag).ToList()
                    })
                    .ToList()
                    .GroupBy(s => s.Id)
                    .Select(g => g.First())
                    .OrderBy(s => s.Name)
                    .Select(s => new ServerApiModel
                    {
                        ServerId = s.Id,
                        Name = s.Name,
                        OsName = s.OsName,
                        Tags = s.Tags.ToArray()
                    })
                    .ToList();

                return new DatabaseRelatedResourcesApiModel
                {
                    DatabaseId = database.Id,
                    DatabaseName = database.Name,
                    ServerName = database.ServerName,
                    Environments = environments,
                    Servers = servers
                };
            }
        }
    }
}