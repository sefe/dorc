using Dorc.ApiModel;
using Dorc.Core.Interfaces;
using Dorc.PersistentData;
using Dorc.PersistentData.Sources.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace Dorc.Api.Controllers
{
    [Authorize]
    [ApiController]
    [Route("[controller]")]
    public class RefDataEnvironmentsController : ControllerBase
    {
        private readonly IEnvironmentsPersistentSource environmentsPersistentSource;
        private readonly ISecurityPrivilegesChecker _securityPrivilegesChecker;
        private readonly IRolePrivilegesChecker _rolePrivilegesChecker;

        public RefDataEnvironmentsController(
            IEnvironmentsPersistentSource environmentsPersistentSource,
            ISecurityPrivilegesChecker securityPrivilegesChecker,
            IRolePrivilegesChecker rolePrivilegesChecker)
        {
            _rolePrivilegesChecker = rolePrivilegesChecker;
            _securityPrivilegesChecker = securityPrivilegesChecker;
            this.environmentsPersistentSource = environmentsPersistentSource;
        }

        /// <summary>
        ///     Returns environment by name
        /// </summary>
        /// <param name="env">Environment Name</param>
        /// <returns>Json string with EnvironmentApiModel object or empty model if error occurred</returns>
        [SwaggerResponse(StatusCodes.Status200OK, Type = typeof(IEnumerable<EnvironmentApiModel>))]
        [HttpGet]
        public IActionResult Get(string env = "")
        {
            if (string.IsNullOrEmpty(env))
                return StatusCode(StatusCodes.Status200OK, environmentsPersistentSource.GetEnvironments(User));

            var environmentApiModel = environmentsPersistentSource.GetEnvironment(env, User);

            return StatusCode(StatusCodes.Status200OK, new List<EnvironmentApiModel?> { environmentApiModel });
        }

        /// <summary>
        ///     Returns environment by name
        /// </summary>
        /// <param name="env">Environment Name</param>
        /// <returns>Json string with EnvironmentApiModel object or empty model if error occurred</returns>
        [SwaggerResponse(StatusCodes.Status200OK, Type = typeof(IEnumerable<string>))]
        [Route("GetAllEnvironmentNames")]
        [HttpGet]
        public IActionResult GetAllEnvironmentNames()
        {
            return Ok(environmentsPersistentSource.GetEnvironmentNames(User));
        }


        /// <summary>
        ///     Returns whether this user is the env owner
        /// </summary>
        /// <param name="envName">Environment Name</param>
        /// <returns>Json string with EnvironmentApiModel object or empty model if error occurred</returns>
        [SwaggerResponse(StatusCodes.Status200OK, Type = typeof(bool))]
        [Route("IsEnvironmentOwner")]
        [HttpGet]
        public IActionResult GetIsEnvironmentOwner(string envName)
        {
            return Ok(environmentsPersistentSource.IsEnvironmentOwner(envName, User));
        }

        /// <summary>
        ///     Returns whether this user is the env owner or delegate
        /// </summary>
        /// <param name="envName">Environment Name</param>
        /// <returns>Json string with EnvironmentApiModel object or empty model if error occurred</returns>
        [SwaggerResponse(StatusCodes.Status200OK, Type = typeof(bool))]
        [Route("IsEnvironmentOwnerOrDelegate")]
        [HttpGet]
        public IActionResult GetIsEnvironmentOwnerOrDelegate(string envName)
        {
            return Ok(_securityPrivilegesChecker.IsEnvironmentOwnerOrAdmin(User, envName));
        }

        /// <summary>
        ///     Create new environment
        /// </summary>
        /// <param name="content"></param>
        /// <returns>Json string with created EnvironmentApiModel or empty model if error</returns>
        [SwaggerResponse(StatusCodes.Status200OK, Type = typeof(EnvironmentApiModel))]
        [HttpPost]
        public IActionResult Post([FromBody] EnvironmentApiModel content)
        {
            try
            {
                if (_rolePrivilegesChecker.IsPowerUser(User) || _rolePrivilegesChecker.IsAdmin(User))
                {
                    if (content.EnvironmentIsProd && !_rolePrivilegesChecker.IsAdmin(User))
                        return StatusCode(StatusCodes.Status403Forbidden,
                            "Production Environments can only be created by Admins!");

                    var env = environmentsPersistentSource.CreateEnvironment(content, User);
                    return StatusCode(StatusCodes.Status200OK, env.EnvironmentId > 0 ? env : new EnvironmentApiModel());
                }

                return StatusCode(StatusCodes.Status403Forbidden,
                    "Production Environments can only be created by PowerUsers or Admins!");
            }
            catch (Exception exception)
            {
                return StatusCode(StatusCodes.Status400BadRequest,
                    exception.Message);
            }
        }

        /// <summary>
        /// Delete environment
        /// </summary>
        /// <param name="content"></param>
        /// <returns></returns>
        [HttpDelete]
        public bool Delete([FromBody] EnvironmentApiModel content)
        {
            return _securityPrivilegesChecker.IsEnvironmentOwnerOrAdmin(User, content.EnvironmentName) &&
                   environmentsPersistentSource.DeleteEnvironment(content, User);
        }

        /// <summary>
        /// Update environment
        /// </summary>
        /// <param name="content"></param>
        /// <returns></returns>
        [SwaggerResponse(StatusCodes.Status200OK, Type = typeof(EnvironmentApiModel))]
        [SwaggerResponse(StatusCodes.Status403Forbidden, Type = typeof(string))]
        [HttpPut]
        public IActionResult Put([FromBody] EnvironmentApiModel content)
        {
            if (!_securityPrivilegesChecker.CanModifyEnvironment(User, content.EnvironmentName))
                return StatusCode(StatusCodes.Status403Forbidden, "User does not have sufficient privileges to write environment " + content.EnvironmentName);

            var env = environmentsPersistentSource.UpdateEnvironment(content, User);
            return StatusCode(StatusCodes.Status200OK, env);
        }

        /// <summary>
        /// Clone an environment including its variables/properties
        /// </summary>
        /// <param name="request">The clone request containing source environment ID and new environment name</param>
        /// <returns>The newly created cloned environment</returns>
        [SwaggerResponse(StatusCodes.Status200OK, Type = typeof(EnvironmentApiModel))]
        [SwaggerResponse(StatusCodes.Status400BadRequest, Type = typeof(string))]
        [SwaggerResponse(StatusCodes.Status403Forbidden, Type = typeof(string))]
        [Route("Clone")]
        [HttpPost]
        public IActionResult Clone([FromBody] CloneEnvironmentRequest request)
        {
            try
            {
                var clonedEnv = environmentsPersistentSource.CloneEnvironment(request, User);
                return StatusCode(StatusCodes.Status200OK, clonedEnv);
            }
            catch (UnauthorizedAccessException ex)
            {
                return StatusCode(StatusCodes.Status403Forbidden, ex.Message);
            }
            catch (ArgumentException ex)
            {
                return StatusCode(StatusCodes.Status400BadRequest, ex.Message);
            }
            catch (Exception ex)
            {
                return StatusCode(StatusCodes.Status500InternalServerError, 
                    $"An error occurred while cloning the environment: {ex.Message}");
            }
        }
    }
}