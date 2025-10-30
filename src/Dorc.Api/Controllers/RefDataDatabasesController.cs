using Dorc.ApiModel;
using Dorc.Core.Interfaces;
using Dorc.PersistentData.Sources.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore; // for DbUpdateException
using Swashbuckle.AspNetCore.Annotations;
using System.Net;

namespace Dorc.Api.Controllers
{
    [Authorize]
    [ApiController]
    [Route("[controller]")]
    public class RefDataDatabasesController : ControllerBase
    {
        private readonly IDatabasesPersistentSource _databasesPersistentSource;
        private readonly ISecurityPrivilegesChecker _securityPrivilegesChecker;
        private readonly IEnvironmentsPersistentSource _environmentsPersistentSource;

        public RefDataDatabasesController(
            IDatabasesPersistentSource databasesPersistentSource,
            ISecurityPrivilegesChecker securityPrivilegesChecker,
            IEnvironmentsPersistentSource environmentsPersistentSource)
        {
            _environmentsPersistentSource = environmentsPersistentSource;
            _securityPrivilegesChecker = securityPrivilegesChecker;
            _databasesPersistentSource = databasesPersistentSource;
        }

        /// <summary>
        /// Return database details by database ID
        /// </summary>
        /// <param name="id">Database ID</param>
        [SwaggerResponse(StatusCodes.Status200OK, Type = typeof(DatabaseApiModel))]
        [HttpGet]
        [Route("{id}")]
        public DatabaseApiModel? Get(int id)
        {
            return id <= 0 ? new DatabaseApiModel() : _databasesPersistentSource.GetDatabase(id);
        }

        /// <summary>
        /// Gets databases by name
        /// </summary>
        /// <param name="name">database name</param>
        /// <param name="server">database server name</param>
        [SwaggerResponse(StatusCodes.Status200OK, Type = typeof(List<DatabaseApiModel>))]
        [HttpGet]
        public List<DatabaseApiModel> Get(string name = "", string server = "")
        {
            if (string.IsNullOrEmpty(name) && string.IsNullOrEmpty(server))
            {
                return _databasesPersistentSource.GetDatabases().ToList();
            }
            if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(server))
            {
                return _databasesPersistentSource.GetDatabases(name, server).ToList();
            }
            return new List<DatabaseApiModel>();
        }

        /// <summary>
        /// Create Database entry
        /// </summary>
        /// <param name="newDatabaseApiModel">Json string in request body</param>
        [SwaggerResponse(StatusCodes.Status200OK, Type = typeof(DatabaseApiModel))]
        [HttpPost]
        public IActionResult Post([FromBody] DatabaseApiModel newDatabaseApiModel)
        {
            try
            {
                var databaseApiModel = _databasesPersistentSource.AddDatabase(newDatabaseApiModel);
                return StatusCode(StatusCodes.Status200OK, databaseApiModel);
            }
            catch (Exception exception)
            {
                return StatusCode(StatusCodes.Status400BadRequest, exception.Message);
            }
        }

        /// <summary>
        /// Delete Database entry
        /// </summary>
        [SwaggerResponse(StatusCodes.Status200OK, Type = typeof(ApiBoolResult))]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
        [HttpDelete]
        public IActionResult Delete([FromQuery] int databaseId)
        {
            // 0) validate input
            if (databaseId <= 0)
            {
                return BadRequest(new ProblemDetails
                {
                    Title = "Invalid request",
                    Detail = "'databaseId' must be greater than zero.",
                    Status = StatusCodes.Status400BadRequest
                });
            }

            // 1) existence check
            var dbModel = _databasesPersistentSource.GetDatabase(databaseId);
            if (dbModel == null)
            {
                return NotFound(new ProblemDetails
                {
                    Title = "Not found",
                    Detail = "The database could not be found.",
                    Status = StatusCodes.Status404NotFound
                });
            }

            // 2) gather environment attachments once
            var envNamesAttachedToDatabase = (_databasesPersistentSource.GetEnvironmentNamesForDatabaseId(databaseId)
                                             ?? Enumerable.Empty<string>())
                                             .Distinct()
                                             .OrderBy(x => x)
                                             .ToList();

            // 3) authorization: user must have write on every attached environment
            var lackingWrite = new List<string>();
            foreach (var environmentName in envNamesAttachedToDatabase)
            {
                var environmentApiModel = _environmentsPersistentSource.GetEnvironment(environmentName);
                if (environmentApiModel == null)
                {
                    return BadRequest(new ProblemDetails
                    {
                        Title = "Permission check error",
                        Detail = "Error while checking permissions; Environment missing in Deployment database.",
                        Status = StatusCodes.Status400BadRequest
                    });
                }

                if (!_securityPrivilegesChecker.CanModifyEnvironment(User, environmentApiModel.EnvironmentName))
                {
                    lackingWrite.Add(environmentApiModel.EnvironmentName);
                }
            }

            if (lackingWrite.Count > 0)
            {
                var pd403 = new ProblemDetails
                {
                    Title = "Forbidden",
                    Detail = "You must have write permission on all environments attached to this database.",
                    Status = StatusCodes.Status403Forbidden
                };
                pd403.Extensions["blockers"] = new object[]
                {
                    new { type = "environment", items = lackingWrite.OrderBy(x => x).ToArray() }
                };
                pd403.Extensions["correlationId"] = HttpContext.TraceIdentifier;
                return StatusCode(StatusCodes.Status403Forbidden, pd403);
            }

            // 4) if assigned to environments, return 409 with blockers list
            if (envNamesAttachedToDatabase.Any())
            {
                var pd409Assigned = new ProblemDetails
                {
                    Title = "Delete blocked by references",
                    Detail = "Database is assigned to one or more environments. Unassign it before deleting.",
                    Status = StatusCodes.Status409Conflict
                };
                pd409Assigned.Extensions["blockers"] = new object[]
                {
                    new { type = "environment", items = envNamesAttachedToDatabase.ToArray() }
                };
                if (!string.IsNullOrEmpty(HttpContext.TraceIdentifier))
                {
                    pd409Assigned.Extensions["correlationId"] = HttpContext.TraceIdentifier;
                }
                return Conflict(pd409Assigned);
            }

            // 5) perform the delete; convert low-level issues into user-friendly conflicts
            try
            {
                var deleted = _databasesPersistentSource.DeleteDatabase(databaseId);
                if (deleted)
                {
                    return Ok(new ApiBoolResult { Result = true, Message = "Database deleted" });
                }

                // Delete returned false without exception -> treat as conflict
                var pd409 = new ProblemDetails
                {
                    Title = "Delete failed",
                    Detail = "Deletion did not complete. The database may still be referenced by other entities.",
                    Status = StatusCodes.Status409Conflict
                };
                pd409.Extensions["correlationId"] = HttpContext.TraceIdentifier;
                return Conflict(pd409);
            }
            catch (DbUpdateException)
            {
                // Translate EF/SQL constraint into actionable message for users
                var pd = new ProblemDetails
                {
                    Title = "Delete blocked by references",
                    Detail = "The database is referenced by other entities (e.g., environments, pipelines, jobs, or history). Remove those references and try again.",
                    Status = StatusCodes.Status409Conflict
                };

                pd.Extensions["correlationId"] = HttpContext.TraceIdentifier;
                return Conflict(pd);
            }
            catch (Exception)
            {
                var pd = new ProblemDetails
                {
                    Title = "Unexpected error",
                    Detail = "An unexpected error occurred while deleting the database.",
                    Status = StatusCodes.Status500InternalServerError
                };
                pd.Extensions["correlationId"] = HttpContext.TraceIdentifier;
                return StatusCode(StatusCodes.Status500InternalServerError, pd);
            }
        }

        /// <summary>
        /// Get Databases by page
        /// </summary>
        [SwaggerResponse(StatusCodes.Status200OK, Type = typeof(GetDatabaseApiModelListResponseDto))]
        [HttpPut]
        [Route("ByPage")]
        public IActionResult Put([FromBody] PagedDataOperators operators, int page = 1, int limit = 50)
        {
            var requestStatusesListResponseDto = _databasesPersistentSource.GetDatabaseApiModelByPage(limit,
                page, operators, User);
            return Ok(requestStatusesListResponseDto);
        }

        /// <summary>
        /// Get Databas ServerNames list
        /// </summary>
        [SwaggerResponse(StatusCodes.Status200OK, Type = typeof(List<String?>))]
        [HttpGet]
        [Route("GetDatabasServerNameslist")]
        public IActionResult GetDatabasServerNameslist()
        {
            var result = _databasesPersistentSource.GetDatabasServerNameslist();
            return Ok(result);
        }

        /// <summary>
        /// Edit database entry
        /// </summary>
        [SwaggerResponse(StatusCodes.Status404NotFound, Type = typeof(string))]
        [SwaggerResponse(StatusCodes.Status400BadRequest, Type = typeof(string))]
        [SwaggerResponse(StatusCodes.Status403Forbidden, Type = typeof(string))]
        [SwaggerResponse(StatusCodes.Status200OK, Type = typeof(DatabaseApiModel))]
        [HttpPut]
        public IActionResult Put([FromQuery] int id, [FromBody] DatabaseApiModel database)
        {
            try
            {
                var environmentIdsForServerName = _databasesPersistentSource.GetEnvironmentNamesForDatabaseId(database.Id);
                foreach (var envIds in environmentIdsForServerName)
                {
                    var env = _environmentsPersistentSource.GetEnvironment(envIds, User);
                    if (env == null)
                    {
                        return BadRequest(
                            "Error while checking permissions, probably Environment missing in Deployment database");
                    }
                    if (!_securityPrivilegesChecker.CanModifyEnvironment(User, env.EnvironmentName))
                    {
                        return StatusCode((int)HttpStatusCode.Forbidden, $"You should have write permission on " + env.EnvironmentName + " to modify this database");
                    }
                }

                if (id != database.Id) return BadRequest("'id' must be the same as database.Id");
                if (id <= 0) return BadRequest("'id' cannot be 0");

                var databaseApiModel = _databasesPersistentSource.GetDatabase(database.Id);
                if (databaseApiModel != null && databaseApiModel.Id != id)
                    return BadRequest("Cannot set the server name to the same as one that already exists!");

                var result = _databasesPersistentSource.UpdateDatabase(id, database, User);
                return result != null ? Ok(result) : NotFound("Error updating entry");
            }
            catch (Exception exception)
            {
                return StatusCode(StatusCodes.Status400BadRequest, exception.Message);
            }
        }
    }
}