using Dorc.ApiModel;
using Dorc.PersistentData.Contexts;
using Dorc.PersistentData.Extensions;
using Dorc.PersistentData.Model;
using Dorc.PersistentData.Sources.Interfaces;
using Microsoft.EntityFrameworkCore;
using System.Linq.Expressions;
using System.Security.Principal;
using Server = Dorc.PersistentData.Model.Server;

namespace Dorc.PersistentData.Sources
{
    public class ServersPersistentSource : PagingPersistentSourceBase, IServersPersistentSource
    {
        private readonly IDeploymentContextFactory _contextFactory;
        private readonly IRolePrivilegesChecker _rolePrivilegesChecker;
        private readonly IClaimsPrincipalReader _claimsPrincipalReader;

        public ServersPersistentSource(
            IDeploymentContextFactory contextFactory,
            IRolePrivilegesChecker rolePrivilegesChecker,
            IClaimsPrincipalReader claimsPrincipalReader
            )
        {
            _rolePrivilegesChecker = rolePrivilegesChecker;
            _contextFactory = contextFactory;
            _claimsPrincipalReader = claimsPrincipalReader;
        }

        public ServerApiModel UpdateServer(int id, ServerApiModel server, IPrincipal user)
        {
            using (var context = _contextFactory.GetContext())
            {
                var oldServer = context.Servers.First(s => s.Id == id);
                var newServer = MapToServer(server, oldServer);
                context.SaveChanges();

                var apiServer = MapToServerApiModel(newServer);
                apiServer.UserEditable = server.UserEditable;
                return apiServer;
            }
        }

        public bool DeleteServer(int serverId)
        {
            using (var context = _contextFactory.GetContext())
            {
                var server = context.Servers
                    .Include(s => s.Environments)
                    .FirstOrDefault(s => s.Id == serverId);

                if (server != null)
                {
                    foreach (var environmentDetail in server.Environments.ToList())
                    {
                        server.Environments.Remove(environmentDetail);   
                    }
                    foreach (var daemon in server.Services.ToList())
                    {
                        server.Services.Remove(daemon);   
                    }
                }

                context.Servers.Remove(server);
                context.SaveChanges();
                return true;
            }
        }

        public IEnumerable<Server> GetAppServerDetails(string envName)
        {
            using (var context = _contextFactory.GetContext())
            {
                var envDetails = context.Environments.Include(aps => aps.Servers).Single(x => x.Name == envName);
                var endurAppServers =
                    envDetails.Servers.Where(x => x.ApplicationTags.Contains("appserv")).ToList();
                return endurAppServers;
            }
        }

        public GetServerApiModelListResponseDto GetServerApiModelByPage(int limit, int page,
            PagedDataOperators operators, IPrincipal user)
        {
            PagedModel<Server> output = null;
            using (var context = _contextFactory.GetContext())
            {
                var isAdmin = _rolePrivilegesChecker.IsAdmin(user);

                string username = _claimsPrincipalReader.GetUserName(user);
                var envPrivilegeInfos = GetEnvironmentPrivInfos(username, context);
                var reqStatusesQueryable = context.Servers.Include(server => server.Environments).AsQueryable();

                if (operators.Filters != null && operators.Filters.Any())
                {
                    var filterLambdas =
                        new List<Expression<Func<Server, bool>>>();
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
                    IOrderedQueryable<Server> orderedQuery = null;

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

                        var param = Expression.Parameter(typeof(Server), "Server");
                        var prop = Expression.PropertyOrField(param, operators.SortOrders[i].Path);

                        switch (prop.Type)
                        {
                            case Type boolType when boolType == typeof(bool):
                                {
                                    var expr = GetExpressionForOrdering<Server, bool>(prop, param);
                                    orderedQuery = OrderScripts(operators, i, orderedQuery, reqStatusesQueryable, expr);
                                    break;
                                }
                            case Type stringType when stringType == typeof(string):
                                {
                                    var expr = GetExpressionForOrdering<Server, string>(prop, param);
                                    orderedQuery = OrderScripts(operators, i, orderedQuery, reqStatusesQueryable, expr);
                                    break;
                                }
                            case Type intType when intType == typeof(int):
                                {
                                    var expr = GetExpressionForOrdering<Server, int>(prop, param);
                                    orderedQuery = OrderScripts(operators, i, orderedQuery, reqStatusesQueryable, expr);
                                    break;
                                }
                            case Type datetimeType when datetimeType == typeof(DateTime):
                                {
                                    var expr = GetExpressionForOrdering<Server, DateTime>(prop, param);
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

                return new GetServerApiModelListResponseDto
                {
                    CurrentPage = output.CurrentPage,
                    TotalPages = output.TotalPages,
                    TotalItems = output.TotalItems,
                    Items = output.Items.Select(s => new ServerApiModel
                    {
                        EnvironmentNames = s.Environments.Select(ed => ed.Name).ToList(),
                        Name = s.Name,
                        ApplicationTags = s.ApplicationTags,
                        OsName = s.OsName,
                        ServerId = s.Id,
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


        public IEnumerable<ServerApiModel> GetServers(IPrincipal user)
        {
            using (var context = _contextFactory.GetContext())
            {
                return context.Servers.Select(s => new ServerApiModel
                { Name = s.Name, ServerId = s.Id, ApplicationTags = s.ApplicationTags, OsName = s.OsName })
                    .ToList();
            }
        }
        public ServerApiModel? GetServer(string serverName, IPrincipal user)
        {
            using (var context = _contextFactory.GetContext())
            {
                var isAdmin = _rolePrivilegesChecker.IsAdmin(user);
                var servers = context.Servers
                    .Where(server => EF.Functions.Collate(server.Name, DeploymentContext.CaseInsensitiveCollation)
                        == EF.Functions.Collate(serverName, DeploymentContext.CaseInsensitiveCollation)).ToList();
                var svr = servers.FirstOrDefault();
                if (svr == null) return null;

                string username = _claimsPrincipalReader.GetUserName(user);
                var envPrivilegeInfos = GetEnvironmentPrivInfos(username, context, svr.Environments.Select(ed => ed.Name));

                var serverApiModel = new ServerApiModel
                {
                    EnvironmentNames = svr.Environments.Select(ed => ed.Name).ToList(),
                    Name = svr.Name,
                    ApplicationTags = svr.ApplicationTags,
                    OsName = svr.OsName,
                    ServerId = svr.Id,
                };

                var totalEdit = (from environmentDetail in svr.Environments
                                 select envPrivilegeInfos[environmentDetail.Name]
                    into privilegeInfo
                                 where privilegeInfo != null
                                 select privilegeInfo.IsOwner || privilegeInfo.HasPermission || privilegeInfo.IsDelegate ||
                                        isAdmin).ToList();

                serverApiModel.UserEditable = totalEdit.All(e => e);
                return serverApiModel;
            }
        }

        public ServerApiModel GetServer(int serverId, IPrincipal user)
        {
            using (var context = _contextFactory.GetContext())
            {
                var isAdmin = _rolePrivilegesChecker.IsAdmin(user);

                var servers = context.Servers.Where(s => s.Id == serverId).ToList();
                var svr = servers.FirstOrDefault();
                if (svr == null) return null;

                string username = _claimsPrincipalReader.GetUserName(user);
                var envPrivilegeInfos = GetEnvironmentPrivInfos(username, context, svr.Environments.Select(ed => ed.Name));

                var serverApiModel = new ServerApiModel
                {
                    EnvironmentNames = svr.Environments.Select(ed => ed.Name).ToList(),
                    Name = svr.Name,
                    ApplicationTags = svr.ApplicationTags,
                    OsName = svr.OsName,
                    ServerId = svr.Id,
                };

                var totalEdit = (from environmentDetail in svr.Environments
                                 select envPrivilegeInfos[environmentDetail.Name]
                    into privilegeInfo
                                 where privilegeInfo != null
                                 select privilegeInfo.IsOwner || privilegeInfo.HasPermission || privilegeInfo.IsDelegate ||
                                        isAdmin).ToList();

                serverApiModel.UserEditable = totalEdit.All(e => e);
                return serverApiModel;
            }
        }

        public ServerApiModel AddServer(ServerApiModel server, IPrincipal user)
        {
            var svr = MapToServer(server);
            using (var context = _contextFactory.GetContext())
            {
                svr = context.Servers.Add(svr).Entity;
                context.SaveChanges();
            }
            return GetServer(svr.Id, user);
        }

        public IEnumerable<string> GetEnvironmentNamesForServerId(int serverId)
        {
            var output = new List<string>();
            using (var context = _contextFactory.GetContext())
            {
                var server = context.Servers.Include(s => s.Environments)
                    .FirstOrDefault(s => s.Id.Equals(serverId));

                if (server == null)
                    return output;

                var envDetailNames = server.Environments.Select(s => s.Name);

                foreach (var envName in envDetailNames)
                {
                    var environment = EnvironmentUnifier.GetEnvironment(context, envName);
                    if (environment != null)
                        output.Add(environment.Name);
                }

                return output;
            }
        }

        public IEnumerable<ServerApiModel> GetServersForEnvId(int environmentId)
        {
            using (var context = _contextFactory.GetContext())
            {
                var envDetail = EnvironmentUnifier.GetEnvironment(context, environmentId);
                if (envDetail == null) return new List<ServerApiModel>();
                var result = context.Servers
                    .Where(s => s.Environments.Any(e => e.Id == envDetail.Id))
                    .Select(s => s);
                return result.ToList().Select(MapToServerApiModel).ToList();
            }
        }

        public IEnumerable<ServerApiModel> GetEnvContentAppServersForEnvId(int environmentId)
        {
            using (var context = _contextFactory.GetContext())
            {
                var envDetail = EnvironmentUnifier.GetEnvironment(context, environmentId);
                if (envDetail == null) return new List<ServerApiModel>();
                var result = context.Servers
                    .Where(s => s.Environments.Any(e => e.Id == envDetail.Id))
                    .OrderBy(s => s.Name)
                    .Select(s => s);
                return result.ToList().Select(MapToServerApiModel).ToList();
            }
        }


        private Server MapToServer(ServerApiModel server, Server s = null)
        {
            if (s == null)
            {
                return new Server
                {
                    Id = server.ServerId,
                    Name = server.Name,
                    OsName = server.OsName,
                    ApplicationTags = server.ApplicationTags
                };
            }

            s.Id = server.ServerId;
            s.Name = server.Name;
            s.OsName = server.OsName;
            s.ApplicationTags = server.ApplicationTags;
            return s;
        }

        private ServerApiModel MapToServerApiModel(Server server)
        {
            if (server == null) return null;

            return new ServerApiModel
            {
                Name = server.Name,
                ApplicationTags = server.ApplicationTags,
                OsName = server.OsName,
                ServerId = server.Id,
                EnvironmentNames = server.Environments?.Select(ed => ed.Name).ToList()
            };
        }

        private ServerApiModel MapToServerApiModel(ServerData serverData)
        {
            if (serverData == null) return null;

            return new ServerApiModel
            {
                Name = serverData.Server.Name,
                ApplicationTags = serverData.Server.ApplicationTags,
                OsName = serverData.Server.OsName,
                ServerId = serverData.Server.Id,
                EnvironmentNames = serverData.Server.Environments.Select(ed => ed.Name).ToList(),
                UserEditable = serverData.UserEditable
            };
        }
    }
}