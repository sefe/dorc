using Dorc.Api.Interfaces;
using Dorc.ApiModel;
using Dorc.PersistentData.Sources.Interfaces;
using System.Security.Claims;

namespace Dorc.Api.Services
{
    public class EnvironmentMapper : IEnvironmentMapper
    {
        private readonly IEnvironmentsPersistentSource _environmentsPersistentSource;
        private readonly IDatabasesPersistentSource _databasesPersistentSource;

        public EnvironmentMapper(IEnvironmentsPersistentSource environmentsPersistentSource,
            IDatabasesPersistentSource databasesPersistentSource)
        {
            _databasesPersistentSource = databasesPersistentSource;
            _environmentsPersistentSource = environmentsPersistentSource;
        }

        public EnvironmentApiModel? GetEnvironmentByDatabase(int envId, int databaseId, ClaimsPrincipal user)
        {
            if (envId == 0)
                return null;

            var database = _databasesPersistentSource.GetDatabase(databaseId);
            var databasesForEnvId = _databasesPersistentSource.GetDatabasesForEnvId(envId);

            if (database == null || !databasesForEnvId.Select(d => d.Id).Contains(database.Id))
                return null;

            var environmentApiModel = _environmentsPersistentSource.GetEnvironment(envId, user);
            return environmentApiModel;
        }
    }
}