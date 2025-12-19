using System.Reflection;
using System.Security.Claims;
using Dorc.Api.Interfaces;
using Dorc.ApiModel;
using Dorc.Core;
using Dorc.Core.Interfaces;
using Dorc.PersistentData.Repositories;
using Dorc.PersistentData.Sources.Interfaces;
using Microsoft.Extensions.Logging;

namespace Dorc.Api.Services
{
    public class ApiServices : IApiServices
    {
        private readonly IManageProjectsPersistentSource _manageProjectsPersistentSource;
        private readonly ILogger _log;
        private readonly IServiceStatus _serviceStatus;
        private readonly IManageUsers _manageUsers;
        private readonly IProjectsPersistentSource _projectsPersistentSource;
        private readonly IDatabasesPersistentSource _databasesPersistentSource;
        private readonly IEnvironmentsPersistentSource _environmentsPersistentSource;
        private readonly IServersPersistentSource _serversPersistentSource;

        public ApiServices(IManageProjectsPersistentSource manageProjectsPersistentSource,
            IServiceStatus serviceStatus, IManageUsers manageUsers,
            IProjectsPersistentSource projectsPersistentSource,
            IDatabasesPersistentSource databasesPersistentSource,
            IEnvironmentsPersistentSource environmentsPersistentSource,
            IServersPersistentSource serversPersistentSource, ILogger<ApiServices> log)
        {
            _serversPersistentSource = serversPersistentSource;
            _environmentsPersistentSource = environmentsPersistentSource;
            _databasesPersistentSource = databasesPersistentSource;
            _projectsPersistentSource = projectsPersistentSource;
            _manageUsers = manageUsers;
            _serviceStatus = serviceStatus;
            _log = log;
            _manageProjectsPersistentSource = manageProjectsPersistentSource;
        }

        public TemplateApiModel<ComponentApiModel> GetComponentsByProject(string projectName)
        {
            try
            {
                var project = _projectsPersistentSource.GetProject(projectName);
                if (project == null)
                    throw new InvalidDataException($"Unable to locate '{projectName}' in DOrc!");

                var components = _projectsPersistentSource.GetComponentsForProject(projectName);

                return new TemplateApiModel<ComponentApiModel> { Project = project, Items = components };
            }
            catch (Exception e)
            {
                _log.LogError(e, "GetComponentsByProject");
                throw;
            }
        }

        public List<ServiceStatusApiModel> GetEnvDaemonsStatuses(string envName, ClaimsPrincipal principal)
        {
            return _serviceStatus.GetServicesAndStatus(envName, principal).Select(MapToServiceStatusApiModel).ToList();
        }

        public List<ServiceStatusApiModel> GetEnvDaemonsStatuses(int envId)
        {
            return _serviceStatus.GetServicesAndStatus(envId).Select(MapToServiceStatusApiModel).ToList();
        }

        public ServiceStatusApiModel? ChangeServiceState(ServiceStatusApiModel daemon, ClaimsPrincipal principal)
        {
            var result = _serviceStatus.ChangeServiceState(MapToServicesAndStatus(daemon), principal);
            return result is null ? null : MapToServiceStatusApiModel(result);
        }

        private ServicesAndStatus MapToServicesAndStatus(ServiceStatusApiModel ss)
        {
            return new ServicesAndStatus
            {
                EnvName = ss.EnvName,
                ServerName = ss.ServerName,
                ServiceName = ss.ServiceName,
                ServiceStatus = ss.ServiceStatus
            };
        }

        private ServiceStatusApiModel MapToServiceStatusApiModel(ServicesAndStatus ss)
        {
            return new ServiceStatusApiModel
            {
                EnvName = ss.EnvName,
                ServerName = ss.ServerName,
                ServiceName = ss.ServiceName,
                ServiceStatus = ss.ServiceStatus
            };
        }

        /// <summary>
        ///     Return detailed information about environment items: db's, apps and etc
        /// </summary>
        /// <param name="id">Environment ID</param>
        /// <param name="user"></param>
        public EnvironmentContentApiModel GetEnvironmentsDetails(int id, ClaimsPrincipal user)
        {
            var result = new EnvironmentContentApiModel();

            var env = _environmentsPersistentSource.GetEnvironment(id, user);

            result.DbServers = GetDbServers(id);
            result.AppServers = _serversPersistentSource.GetEnvContentAppServersForEnvId(id);

            if (env != null)
            {
                result.EnvironmentName = env.EnvironmentName;
                result.FileShare = env.Details?.FileShare;
                result.Description = env.Details?.Description;
                result.Builds =
                    _environmentsPersistentSource.GetEnvironmentComponentStatuses(env.EnvironmentName, DateTime.Now);
                result.EndurUsers = _manageUsers.GetUsersForEnvironment(id, UserAccountType.Endur);
                result.DelegatedUsers = _manageUsers.GetUsersForEnvironment(id, UserAccountType.NotSet);
                result.MappedProjects = _environmentsPersistentSource.GetMappedProjects(env.EnvironmentName);
            }
            else
            {
                result.EnvironmentName = "";
            }

            return result;
        }

        /// <summary>
        ///     Return DB Servers list
        /// </summary>
        /// <param name="id">Environment ID</param>
        /// <returns>List of EnvironmentContentDbServerApiModel</returns>
        public IEnumerable<DatabaseApiModel> GetDbServers(int id)
        {
            var databases = _databasesPersistentSource.GetDatabasesForEnvId(id).ToArray();
            if (databases.Any())
            {
                var dataBasesResult = new List<DatabaseApiModel>();
                foreach (var db in databases)
                    dataBasesResult.Add(
                        new DatabaseApiModel
                        {
                            Id = db.Id,
                            Name = db.Name,
                            ServerName = db.ServerName,
                            Type = db.Type,
                            AdGroup = db.AdGroup,
                            ArrayName = db.ArrayName
                        }
                    );
                return dataBasesResult;
            }

            return new List<DatabaseApiModel>();
        }

        /// <summary>
        ///     Detach or attach component from Environment
        /// </summary>
        /// <param name="envId">Environment ID</param>
        /// <param name="componentId">Component ID</param>
        /// <param name="action">attach or detach</param>
        /// <param name="component">server or database</param>
        /// <param name="user"></param>
        /// <returns></returns>
        public EnvironmentApiModel ChangeEnvComponent<T>(int envId, int componentId, string action,
            string component, ClaimsPrincipal user)
        {
            var actions = PrepareActions(user);
            return actions[component][action].Invoke(envId, componentId);
        }

        public List<ReleaseInformationApiModel> GetReleaseInformation(IEnumerable<int> requestIds)
        {
            var orderedRequestIds = requestIds.OrderBy(x => x);
            var listOfReleaseInformation = new List<ReleaseInformationApiModel>();
            foreach (var id in orderedRequestIds)
                listOfReleaseInformation.Add(_manageProjectsPersistentSource.GetRequestDetails(id));
            return listOfReleaseInformation;
        }

        private Dictionary<string, Dictionary<string, ComponentActions>> PrepareActions(ClaimsPrincipal user)
        {
            var actions = new Dictionary<string, Dictionary<string, ComponentActions>>();
            var server = new Dictionary<string, ComponentActions>
            {
                {
                    "attach",
                    (envId, serverId) => _environmentsPersistentSource.AttachServerToEnv(envId, serverId, user)
                },
                {
                    "detach",
                    (envId, serverId) => _environmentsPersistentSource.DetachServerFromEnv(envId, serverId, user)
                }
            };
            actions.Add("server", server);
            var database = new Dictionary<string, ComponentActions>
            {
                {
                    "attach",
                    (envId, databaseId) => _environmentsPersistentSource.AttachDatabaseToEnv(envId, databaseId, user)
                },
                {
                    "detach",
                    (envId, databaseId) => _environmentsPersistentSource.DetachDatabaseFromEnv(envId, databaseId, user)
                }
            };
            actions.Add("database", database);
            return actions;
        }

        private delegate EnvironmentApiModel ComponentActions(int envId, int componentId);

    }
}