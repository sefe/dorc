using Dorc.ApiModel;
using System.Security.Principal;
using Dorc.PersistentData.Model;

namespace Dorc.PersistentData.Sources.Interfaces
{
    public interface IDatabasesPersistentSource
    {
        DatabaseApiModel? GetDatabase(int id);
        IEnumerable<DatabaseApiModel> GetDatabases(string name, string server);
        IEnumerable<DatabaseApiModel> GetDatabases();
        DatabaseApiModel? AddDatabase(DatabaseApiModel db);
        IEnumerable<DatabaseApiModel> GetDatabasesForEnvId(int environmentId);
        IEnumerable<DatabaseApiModel> GetDatabasesForEnvironmentName(string environmentName);
        DatabaseApiModel GetDatabaseByType(EnvironmentApiModel environment, string type);
        DatabaseApiModel? GetDatabaseByType(string envName, string type);
        bool DeleteDatabase(int databaseId);
        DatabaseApiModel? GetApplicationDatabaseForEnvFilter(string filter, string envFilter);
        GetDatabaseApiModelListResponseDto GetDatabaseApiModelByPage(int limit, int page,
            PagedDataOperators operators, IPrincipal user);
        List<String?> GetDatabasServerNameslist();
        public IEnumerable<string> GetEnvironmentNamesForDatabaseId(int serverId);
        DatabaseApiModel? UpdateDatabase(int id, DatabaseApiModel database, IPrincipal user);
        
        void UpdateDatabaseConnectivityStatus(int databaseId, bool isReachable, DateTime lastChecked);
        List<Database> GetAllDatabasesForConnectivityCheck();
        IEnumerable<Database> GetDatabasesForConnectivityCheckBatch(int skip, int take);
        int GetTotalDatabaseCount();
    }
}