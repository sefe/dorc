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
        [SwaggerResponse(StatusCodes.Status404NotFound)]
        [HttpGet]
        public IActionResult Get(string env = "")
        {
            if (string.IsNullOrEmpty(env))
                return StatusCode(StatusCodes.Status200OK, environmentsPersistentSource.GetEnvironments(User));

            var environmentApiModel = environmentsPersistentSource.GetEnvironment(env, User);

            if (environmentApiModel == null)
                return NotFound($"The environment '{env}' is not found.");

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

                    if (content.ParentId.HasValue)
                        return StatusCode(StatusCodes.Status400BadRequest,
                            "Parent environment cannot be set during creation. Use the SetParentForEnvironment endpoint instead.");

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
        [SwaggerResponse(StatusCodes.Status200OK, Type = typeof(bool))]
        [SwaggerResponse(StatusCodes.Status403Forbidden, Type = typeof(string))]
        [SwaggerResponse(StatusCodes.Status404NotFound, Type = typeof(string))]
        [HttpDelete]
        public IActionResult Delete([FromBody] EnvironmentApiModel content)
        {
            // Returning a bare 'false' for both of these told the caller only that the delete had
            // not happened, leaving the user to guess whether they lacked the rights, mistyped the
            // environment or hit a genuine failure.
            if (!_securityPrivilegesChecker.IsEnvironmentOwnerOrAdmin(User, content.EnvironmentName))
                return StatusCode(StatusCodes.Status403Forbidden,
                    $"Only the owner of environment '{content.EnvironmentName}' or an administrator can delete it.");

            if (!environmentsPersistentSource.DeleteEnvironment(content, User))
                return StatusCode(StatusCodes.Status404NotFound,
                    $"Environment '{content.EnvironmentName}' no longer exists, so there was nothing to delete.");

            return StatusCode(StatusCodes.Status200OK, true);
        }

        /// <summary>
        /// Update environment
        /// </summary>
        /// <param name="content"></param>
        /// <returns></returns>
        [SwaggerResponse(StatusCodes.Status200OK, Type = typeof(EnvironmentApiModel))]
        [SwaggerResponse(StatusCodes.Status400BadRequest, Type = typeof(string))]
        [SwaggerResponse(StatusCodes.Status403Forbidden, Type = typeof(string))]
        [SwaggerResponse(StatusCodes.Status404NotFound, Type = typeof(string))]
        [HttpPut]
        public IActionResult Put([FromBody] EnvironmentApiModel content)
        {
            if (content.EnvironmentId <= 0)
                return StatusCode(StatusCodes.Status400BadRequest, "Invalid environment Id specified!");

            var existingEnv = environmentsPersistentSource.GetEnvironment(content.EnvironmentId, User);
            if (existingEnv == null)
                return StatusCode(StatusCodes.Status404NotFound, $"Environment with ID {content.EnvironmentId} not found.");

            if (!_securityPrivilegesChecker.CanModifyEnvironment(User, existingEnv.EnvironmentName))
                return StatusCode(StatusCodes.Status403Forbidden, "User does not have sufficient privileges to write environment " + existingEnv.EnvironmentName);

            if (existingEnv.EnvironmentIsProd != content.EnvironmentIsProd && !_rolePrivilegesChecker.IsAdmin(User))
                return StatusCode(StatusCodes.Status403Forbidden, "Only Admins can change the Production status of an environment!");

            if (existingEnv.EnvironmentSecure != content.EnvironmentSecure &&
                !_securityPrivilegesChecker.IsEnvironmentOwnerOrAdmin(User, existingEnv.EnvironmentName))
                return StatusCode(StatusCodes.Status403Forbidden, "Only an Environment Owner or Admin can change the Secure status of an environment!");

            if (!string.Equals(existingEnv.EnvironmentName, content.EnvironmentName, StringComparison.OrdinalIgnoreCase) &&
                !_securityPrivilegesChecker.IsEnvironmentOwnerOrAdmin(User, existingEnv.EnvironmentName))
                return StatusCode(StatusCodes.Status403Forbidden, "Only an Environment Owner or Admin can rename an environment!");

            if (existingEnv.ParentId != content.ParentId)
            {
                if (!_securityPrivilegesChecker.IsEnvironmentOwnerOrAdmin(User, existingEnv.EnvironmentName))
                    return StatusCode(StatusCodes.Status403Forbidden,
                        $"Only an Owner of '{existingEnv.EnvironmentName}' or an Admin can change its parent environment!");

                if (existingEnv.EnvironmentIsProd && !_rolePrivilegesChecker.IsAdmin(User))
                    return StatusCode(StatusCodes.Status403Forbidden,
                        "Only Admins can attach or detach Production environments in a hierarchy!");

                if (existingEnv.ParentId.HasValue)
                {
                    var existingParent = environmentsPersistentSource.GetEnvironment(existingEnv.ParentId.Value, User);
                    if (existingParent != null)
                    {
                        if (!_securityPrivilegesChecker.IsEnvironmentOwnerOrAdmin(User, existingParent.EnvironmentName))
                            return StatusCode(StatusCodes.Status403Forbidden,
                                $"Only an Owner of parent environment '{existingParent.EnvironmentName}' or an Admin can detach a child environment from it!");

                        if (existingParent.EnvironmentIsProd && !_rolePrivilegesChecker.IsAdmin(User))
                            return StatusCode(StatusCodes.Status403Forbidden,
                                "Only Admins can attach or detach Production environments in a hierarchy!");
                    }
                }

                if (content.ParentId.HasValue)
                {
                    var parentEnvironment = environmentsPersistentSource.GetEnvironment(content.ParentId.Value, User);
                    if (parentEnvironment == null)
                        return StatusCode(StatusCodes.Status400BadRequest, "Parent environment not found.");

                    if (!_securityPrivilegesChecker.IsEnvironmentOwnerOrAdmin(User, parentEnvironment.EnvironmentName))
                        return StatusCode(StatusCodes.Status403Forbidden,
                            $"Only an Owner of parent environment '{parentEnvironment.EnvironmentName}' or an Admin can attach a child environment to it!");

                    if (parentEnvironment.EnvironmentIsProd && !_rolePrivilegesChecker.IsAdmin(User))
                        return StatusCode(StatusCodes.Status403Forbidden,
                            "Only Admins can attach or detach Production environments in a hierarchy!");

                    if ((parentEnvironment.EnvironmentIsProd || parentEnvironment.EnvironmentSecure) &&
                        !(_securityPrivilegesChecker.CanGrantReadSecrets(User, parentEnvironment.EnvironmentName) ||
                          _securityPrivilegesChecker.CanReadSecrets(User, parentEnvironment.EnvironmentName)))
                        return StatusCode(StatusCodes.Status403Forbidden,
                            $"Attaching a child environment to parent '{parentEnvironment.EnvironmentName}' requires Owner, Admin, or Read Secrets permission on the parent environment!");
                }

                try
                {
                    environmentsPersistentSource.SetParentForEnvironment(content.ParentId, existingEnv.EnvironmentId, User);
                }
                catch (Exception ex)
                {
                    return StatusCode(StatusCodes.Status400BadRequest, ex.Message);
                }
            }

            var env = environmentsPersistentSource.UpdateEnvironment(content, User);
            return StatusCode(StatusCodes.Status200OK, env);
        }

        /// <summary>
        /// Sets or clears the deployment execution identity for an environment.
        /// </summary>
        [SwaggerResponse(StatusCodes.Status200OK, Type = typeof(EnvironmentApiModel))]
        [SwaggerResponse(StatusCodes.Status400BadRequest, Type = typeof(string))]
        [SwaggerResponse(StatusCodes.Status403Forbidden, Type = typeof(string))]
        [SwaggerResponse(StatusCodes.Status404NotFound, Type = typeof(string))]
        [Route("{environmentId:int}/ExecutionIdentity")]
        [HttpPut]
        public IActionResult PutExecutionIdentity(
            int environmentId,
            [FromBody] EnvironmentExecutionIdentityApiModel content)
        {
            if (!_rolePrivilegesChecker.IsAdmin(User))
            {
                return StatusCode(StatusCodes.Status403Forbidden,
                    "Only administrators can change an environment's execution identity.");
            }

            if (environmentId <= 0)
            {
                return StatusCode(StatusCodes.Status400BadRequest, "Invalid environment Id specified!");
            }

            try
            {
                environmentsPersistentSource.SetExecutionIdentityReference(
                    environmentId,
                    content?.ExecutionIdentityReference,
                    User);

                var environment = environmentsPersistentSource.GetEnvironment(environmentId, User);
                if (environment == null)
                {
                    return StatusCode(StatusCodes.Status404NotFound,
                        $"Environment with ID {environmentId} was not found.");
                }

                return StatusCode(StatusCodes.Status200OK, environment);
            }
            catch (KeyNotFoundException exception)
            {
                return StatusCode(StatusCodes.Status404NotFound, exception.Message);
            }
            catch (ArgumentException exception)
            {
                return StatusCode(StatusCodes.Status400BadRequest, exception.Message);
            }
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
                var sourceEnv = environmentsPersistentSource.GetEnvironment(request.SourceEnvironmentId, User);
                if (sourceEnv == null)
                    return StatusCode(StatusCodes.Status400BadRequest,
                        $"Source environment with ID {request.SourceEnvironmentId} not found.");

                // Check permissions: only Admin, PowerUser, or Owner of source environment can clone
                if (!(_rolePrivilegesChecker.IsPowerUser(User) ||
                      _rolePrivilegesChecker.IsAdmin(User) ||
                      _securityPrivilegesChecker.IsEnvironmentOwnerOrAdmin(User, sourceEnv.EnvironmentName)))
                {
                    return StatusCode(StatusCodes.Status403Forbidden,
                        "Only Admin, PowerUser, or Environment Owner can clone an environment.");
                }

                // Security Covenant 1: Non-Admins cannot copy infrastructure mappings from Production environments
                var copiesInfrastructureMappings = request.CopyServerMappings ||
                                                   request.CopyDatabaseMappings ||
                                                   request.CopyContainerMappings ||
                                                   request.CopyCloudResourceMappings ||
                                                   request.CopyApiRegistrationMappings;
                if (sourceEnv.EnvironmentIsProd && copiesInfrastructureMappings && !_rolePrivilegesChecker.IsAdmin(User))
                {
                    return StatusCode(StatusCodes.Status403Forbidden,
                        "Only Admins can copy infrastructure mappings (servers, databases, containers, cloud resources, or API registrations) from a Production environment.");
                }

                // Security Covenant 2: Copying scoped variable values requires Owner, Admin, or Read Secrets permission on the source environment
                if (request.CopyPropertyValues &&
                    !(_securityPrivilegesChecker.CanGrantReadSecrets(User, sourceEnv.EnvironmentName) ||
                      _securityPrivilegesChecker.CanReadSecrets(User, sourceEnv.EnvironmentName)))
                {
                    return StatusCode(StatusCodes.Status403Forbidden,
                        $"Copying variable values from environment '{sourceEnv.EnvironmentName}' requires Owner, Admin, or Read Secrets permission on the source environment.");
                }

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