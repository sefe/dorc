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
        List<Server> GetAllServersForConnectivityCheck();
        IEnumerable<Server> GetServersForConnectivityCheckBatch(int skip, int take);
        int GetTotalServerCount();
    }
}