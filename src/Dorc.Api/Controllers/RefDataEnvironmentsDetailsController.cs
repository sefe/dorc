using Dorc.Api.Interfaces;
using Dorc.ApiModel;
using Dorc.Core.Interfaces;
using Dorc.PersistentData.Sources.Interfaces;
using log4net;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace Dorc.Api.Controllers
{
    [Authorize]
    [ApiController]
    [Route("[controller]")]
    public class RefDataEnvironmentsDetailsController : ControllerBase
    {
        private readonly IApiServices apiServices;
        private readonly IServersPersistentSource serversPersistentSource;
        private readonly IDatabasesPersistentSource databasesPersistentSource;
        private readonly IEnvironmentsPersistentSource environmentsPersistentSource;
        private readonly ISecurityPrivilegesChecker securityService;
        private readonly ILog logger;

        public RefDataEnvironmentsDetailsController(
            IApiServices apiServices,
            IServersPersistentSource serversPersistentSource,
            IDatabasesPersistentSource databasesPersistentSource,
            IEnvironmentsPersistentSource environmentsPersistentSource,
            ISecurityPrivilegesChecker securityService,
            ILog logger)
        {
            this.securityService = securityService;
            this.environmentsPersistentSource = environmentsPersistentSource;
            this.databasesPersistentSource = databasesPersistentSource;
            this.serversPersistentSource = serversPersistentSource;
            this.apiServices = apiServices;
            this.logger = logger;
        }

        /// <summary>
        ///     Return detailed information about environment items: db's, apps and etc
        /// </summary>
        /// <param name="id">Environment ID</param>
        /// <returns>Json string with EnvironmentContentApiModel object</returns>
        [SwaggerResponse(StatusCodes.Status200OK, Type = typeof(EnvironmentContentApiModel))]
        [HttpGet("{id:int}")]
        public IActionResult Get(int id)
        {
            var environmentsDetails = apiServices.GetEnvironmentsDetails(id, User);
            return StatusCode(StatusCodes.Status200OK, environmentsDetails);
        }

        /// <summary>
        ///     Return detailed information about component statues for the environment
        /// </summary>
        /// <returns>Json string with EnvironmentContentApiModel object</returns>
        [SwaggerResponse(StatusCodes.Status200OK, Type = typeof(IEnumerable<EnvironmentContentBuildsApiModel>))]
        [HttpGet("GetComponentStatues")]
        public IActionResult GetComponentStatues([FromQuery] string envName, [FromQuery] string cutoffDateTime)
        {
            var env = environmentsPersistentSource.GetEnvironment(envName, User);
            if (env == null)
                return StatusCode(StatusCodes.Status400BadRequest, "No access to specified environment");

            var environmentContentBuildsApiModels = environmentsPersistentSource
                .GetEnvironmentComponentStatuses(envName, DateTime.Parse(cutoffDateTime));
            return StatusCode(StatusCodes.Status200OK, environmentContentBuildsApiModels);
        }

        /// <summary>
        /// Returns Environment components
        /// </summary>
        /// <param name="id">environment ID</param>
        /// <param name="type">component: [database=0|server=1]</param>
        /// <returns></returns>
        [SwaggerResponse(StatusCodes.Status200OK, Type = typeof(EnvironmentComponentsDto<DatabaseApiModel>))]
        [HttpGet]
        public object Get([FromQuery] int id, [FromQuery] int type)
        {
            if (id > 0)
            {
                switch (type)
                {
                    case 0:
                        return new EnvironmentComponentsDto<DatabaseApiModel>
                        { Result = databasesPersistentSource.GetDatabasesForEnvId(id).ToList() };
                    case 1:
                        return new EnvironmentComponentsDto<ServerApiModel>
                        { Result = serversPersistentSource.GetServersForEnvId(id).ToList() };
                }
            }

            return new object();
        }

        /// <summary>
        /// Returns all Environments which could became children of the Environment
        /// </summary>
        /// <param name="id">environment ID</param>
        [SwaggerResponse(StatusCodes.Status200OK, Type = typeof(ICollection<EnvironmentApiModel>))]
        [HttpGet("GetPossibleEnvironmentChildren")]
        public object GetPossibleEnvironmentChildren([FromQuery] int id)
        {
            if (id > 0)
            {
                return environmentsPersistentSource.GetPossibleEnvironmentChildren(id, User);
            }

            return new List<EnvironmentApiModel>();
        }

        /// <summary>
        ///     Add or remove environment components 
        /// </summary>
        /// <param name="envId">Environment ID</param>
        /// <param name="componentId">Component ID</param>
        /// <param name="action">Action can be "attach" or "detach"</param>
        /// <param name="component">Component type: server, database</param>
        /// <returns>Returns EnvironmentAPIModel if action succeeded or empty model if error occurred.</returns>
        [SwaggerResponse(StatusCodes.Status200OK, Type = typeof(ApiBoolResult))]
        [HttpPut]
        public ApiBoolResult Put([FromQuery] int envId, [FromQuery] int componentId, [FromQuery] string action,
            [FromQuery] string component)
        {
            var environment = environmentsPersistentSource.GetEnvironment(envId, User);
            if (environment == null || !securityService.CanModifyEnvironment(User, environment.EnvironmentName))
                return new ApiBoolResult
                { Result = false, Message = "User doesn't have \"Modify\" permission for this action!" };

            try
            {
                var env = apiServices.ChangeEnvComponent<EnvironmentApiModel>(envId, componentId, action, component, User);
                return env.EnvironmentId > 0 ? new ApiBoolResult { Result = true } : new ApiBoolResult { Result = false };
            }
            catch (Exception ex)
            {
                switch (ex)
                {
                    case ArgumentOutOfRangeException:
                    case ArgumentException:
                        return new ApiBoolResult
                        { Result = false, Message = ex.Message };
                    default:
                        return new ApiBoolResult { Result = false };
                }
            }
        }

        /// <summary>
        /// Set or unset the parent for an environment.
        /// </summary>
        /// <param name="parentEnvId">Parent Environment ID or null to detach.</param>
        /// <param name="childEnvId">Child Environment ID.</param>
        /// <returns>Returns ApiBoolResult indicating success or failure.</returns>
        [SwaggerResponse(StatusCodes.Status200OK, Type = typeof(ApiBoolResult))]
        [HttpPut("SetParentForEnvironment")]
        public ApiBoolResult SetParentForEnvironment([FromQuery] int? parentEnvId, [FromQuery] int childEnvId)
        {
            var childEnvironment = environmentsPersistentSource.GetEnvironment(childEnvId, User);

            if (childEnvironment == null)
                return new ApiBoolResult { Result = false, Message = "Environment not found." };

            if (!securityService.CanModifyEnvironment(User, childEnvironment.EnvironmentName))
            {
                return new ApiBoolResult
                { Result = false, Message = "User doesn't have \"Modify\" permission for this action!" };
            }

            try
            {
                environmentsPersistentSource.SetParentForEnvironment(parentEnvId, childEnvId, User);

                return new ApiBoolResult { Result = true };
            }
            catch (Exception ex)
            {
                return new ApiBoolResult { Result = false, Message = ex.Message };
            }
        }
    }
}