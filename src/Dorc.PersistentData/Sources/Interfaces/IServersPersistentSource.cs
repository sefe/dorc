using Dorc.ApiModel;
using System.Security.Principal;
using Dorc.PersistentData.Model;

namespace Dorc.PersistentData.Sources.Interfaces
{
    public interface IServersPersistentSource
    {
        ServerApiModel UpdateServer(int id,
            ServerApiModel server, IPrincipal user);
        IEnumerable<ServerApiModel> GetServers(IPrincipal user);
        ServerApiModel? GetServer(string serverName, IPrincipal user);
        ServerApiModel GetServer(int serverId, IPrincipal user);
        ServerApiModel AddServer(ServerApiModel server, IPrincipal user);
        IEnumerable<ServerApiModel> GetServersForEnvId(int environmentId);
        IEnumerable<ServerApiModel> GetEnvContentAppServersForEnvId(int environmentId);
        IEnumerable<string> GetEnvironmentNamesForServerId(int serverId);
        bool DeleteServer(int serverId);

        GetServerApiModelListResponseDto GetServerApiModelByPage(int limit, int page,
            PagedDataOperators operators, IPrincipal user);

        IEnumerable<Server> GetAppServerDetails(string envName);
        
        void UpdateServerConnectivityStatus(int serverId, bool isReachable, DateTime lastChecked);

        /// <summary>
        /// Keyset pagination for connectivity check: returns up to <paramref name="take"/> servers
        /// with Id strictly greater than <paramref name="afterId"/>, ordered by Id ascending.
        /// Pass 0 on the first call. Caller terminates when the result count is less than take.
        /// Stable across concurrent inserts/deletes between batches, unlike OFFSET-based paging.
        /// </summary>
        IEnumerable<Server> GetServersForConnectivityCheckBatchAfter(int afterId, int take);
    }
}