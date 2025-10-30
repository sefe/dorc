using Dorc.ApiModel;
using Dorc.Core.Interfaces;
using Dorc.PersistentData.Sources.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore; // DbUpdateException
using Swashbuckle.AspNetCore.Annotations;
using System.Net;

namespace Dorc.Api.Controllers
{
    [Authorize]
    [ApiController]
    // Keep existing base route for backward compatibility
    [Route("[controller]")]
    // Add conventional API base as an additional alias (non-breaking)
    [Route("api/[controller]")]
    [Produces("application/json")]
    public class RefDataDatabasesController : ControllerBase
    {
        private const int DefaultPage = 1;
        private const int DefaultLimit = 50;

        private readonly IDatabasesPersistentSource _databasesPersistentSource;
        private readonly ISecurityPrivilegesChecker _securityPrivilegesChecker;
        private readonly IEnvironmentsPersistentSource _environmentsPersistentSource;
        private readonly ILogger<RefDataDatabasesController> _logger;

        public RefDataDatabasesController(
            IDatabasesPersistentSource databasesPersistentSource,
            ISecurityPrivilegesChecker securityPrivilegesChecker,
            IEnvironmentsPersistentSource environmentsPersistentSource,
            ILogger<RefDataDatabasesController> logger)
        {
            _environmentsPersistentSource = environmentsPersistentSource;
            _securityPrivilegesChecker = securityPrivilegesChecker;
            _databasesPersistentSource = databasesPersistentSource;
            _logger = logger;
        }

        // --------------------------
        // Small helpers (no behavior change)
        // --------------------------

        private ProblemDetails WithCorrelation(ProblemDetails pd)
        {
            pd.Extensions["correlationId"] = HttpContext.TraceIdentifier;
            return pd;
        }

        private ProblemDetails Problem(int status, string title, string detail) =>
            WithCorrelation(new ProblemDetails
            {
                Title = title,
                Detail = detail,
                Status = status
            });

        // --------------------------
        // GET /{id}
        // --------------------------

        /// <summary>Return database details by database ID</summary>
        /// <param name="id">Database ID</param>
        [SwaggerResponse(StatusCodes.Status200OK, Type = typeof(DatabaseApiModel))]
        [HttpGet]
        [Route("{id}")]
        public DatabaseApiModel? Get(int id)
        {
            // Preserve previous behavior: <= 0 returns empty model (not 400/404)
            return id <= 0 ? new DatabaseApiModel() : _databasesPersistentSource.GetDatabase(id);
        }

        // --------------------------
        // GET ?name=&server=
        // --------------------------

        /// <summary>Gets databases by name/server. Both params must be provided to filter; otherwise returns all or an empty list (preserved behavior).</summary>
        /// <param name="name">database name</param>
        /// <param name="server">database server name</param>
        [SwaggerResponse(StatusCodes.Status200OK, Type = typeof(List<DatabaseApiModel>))]
        [HttpGet]
        public List<DatabaseApiModel> Get(string name = "", string server = "")
        {
            if (string.IsNullOrWhiteSpace(name) && string.IsNullOrWhiteSpace(server))
            {
                return _databasesPersistentSource.GetDatabases().ToList();
            }

            if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(server))
            {
                return _databasesPersistentSource.GetDatabases(name, server).ToList();
            }

            // Preserve original logic: if only one param is provided, return empty list.
            return new List<DatabaseApiModel>();
        }

        // --------------------------
        // POST
        // --------------------------

        /// <summary>Create Database entry</summary>
        /// <param name="newDatabaseApiModel">Json object in request body</param>
        [SwaggerResponse(StatusCodes.Status200OK, Type = typeof(DatabaseApiModel))]
        [SwaggerResponse(StatusCodes.Status400BadRequest, Type = typeof(ProblemDetails))]
        [HttpPost]
        public IActionResult Post([FromBody] DatabaseApiModel newDatabaseApiModel)
        {
            try
            {
                var databaseApiModel = _databasesPersistentSource.AddDatabase(newDatabaseApiModel);
                return StatusCode(StatusCodes.Status200OK, databaseApiModel);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create database entry. TraceId: {TraceId}", HttpContext.TraceIdentifier);
                // Preserve 400, but avoid leaking exception details to clients
                return BadRequest(Problem(
                    StatusCodes.Status400BadRequest,
                    "Invalid request",
                    "The database entry could not be created due to an invalid request."));
            }
        }

        // --------------------------
        // DELETE
        // --------------------------

        /// <summary>Delete Database entry</summary>
        [SwaggerResponse(StatusCodes.Status200OK, Type = typeof(ApiBoolResult))]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
        [HttpDelete]
        public IActionResult Delete([FromQuery] int databaseId)
        {
            // 0) Validate input
            if (databaseId <= 0)
            {
                return BadRequest(Problem(
                    StatusCodes.Status400BadRequest,
                    "Invalid request",
                    "'databaseId' must be greater than zero."));
            }

            // 1) Existence check
            var dbModel = _databasesPersistentSource.GetDatabase(databaseId);
            if (dbModel == null)
            {
                return NotFound(Problem(
                    StatusCodes.Status404NotFound,
                    "Not found",
                    "The database could not be found."));
            }

            // 2) Gather environment attachments once
            var envNamesAttachedToDatabase = (_databasesPersistentSource.GetEnvironmentNamesForDatabaseId(databaseId)
                                              ?? Enumerable.Empty<string>())
                                             .Distinct()
                                             .OrderBy(x => x)
                                             .ToList();

            // 3) Authorization: user must have write on every attached environment
            var lackingWrite = new List<string>();
            foreach (var environmentName in envNamesAttachedToDatabase)
            {
                var environmentApiModel = _environmentsPersistentSource.GetEnvironment(environmentName);
                if (environmentApiModel == null)
                {
                    return BadRequest(Problem(
                        StatusCodes.Status400BadRequest,
                        "Permission check error",
                        "Error while checking permissions; Environment missing in Deployment database."));
                }

                if (!_securityPrivilegesChecker.CanModifyEnvironment(User, environmentApiModel.EnvironmentName))
                {
                    lackingWrite.Add(environmentApiModel.EnvironmentName);
                }
            }

            if (lackingWrite.Count > 0)
            {
                var pd403 = Problem(
                    StatusCodes.Status403Forbidden,
                    "Forbidden",
                    "You must have write permission on all environments attached to this database.");

                pd403.Extensions["blockers"] = new object[]
                {
                    new { type = "environment", items = lackingWrite.OrderBy(x => x).ToArray() }
                };

                return StatusCode(StatusCodes.Status403Forbidden, pd403);
            }

            // 4) If assigned to environments, return 409 with blockers list
            if (envNamesAttachedToDatabase.Any())
            {
                var pd409Assigned = Problem(
                    StatusCodes.Status409Conflict,
                    "Delete blocked by references",
                    "Database is assigned to one or more environments. Unassign it before deleting.");

                pd409Assigned.Extensions["blockers"] = new object[]
                {
                    new { type = "environment", items = envNamesAttachedToDatabase.ToArray() }
                };

                return Conflict(pd409Assigned);
            }

            // 5) Perform the delete; convert low-level issues into user-friendly conflicts
            try
            {
                var deleted = _databasesPersistentSource.DeleteDatabase(databaseId);
                if (deleted)
                {
                    _logger.LogInformation("Database {DatabaseId} deleted. TraceId: {TraceId}", databaseId, HttpContext.TraceIdentifier);
                    return Ok(new ApiBoolResult { Result = true, Message = "Database deleted" });
                }

                // Delete returned false without exception -> treat as conflict
                return Conflict(Problem(
                    StatusCodes.Status409Conflict,
                    "Delete failed",
                    "Deletion did not complete. The database may still be referenced by other entities."));
            }
            catch (DbUpdateException dbex)
            {
                // Translate EF/SQL constraint into actionable message for users
                _logger.LogWarning(dbex, "Delete blocked by references for database {DatabaseId}. TraceId: {TraceId}", databaseId, HttpContext.TraceIdentifier);

                return Conflict(Problem(
                    StatusCodes.Status409Conflict,
                    "Delete blocked by references",
                    "The database is referenced by other entities (e.g., environments, pipelines, jobs, or history). Remove those references and try again."));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error deleting database {DatabaseId}. TraceId: {TraceId}", databaseId, HttpContext.TraceIdentifier);

                return StatusCode(StatusCodes.Status500InternalServerError, Problem(
                    StatusCodes.Status500InternalServerError,
                    "Unexpected error",
                    "An unexpected error occurred while deleting the database."));
            }
        }

        // --------------------------
        // PUT /ByPage (paging)
        // --------------------------

        /// <summary>Get Databases by page</summary>
        [SwaggerResponse(StatusCodes.Status200OK, Type = typeof(GetDatabaseApiModelListResponseDto))]
        [HttpPut]
        [Route("ByPage")]
        public IActionResult Put([FromBody] PagedDataOperators operators, int page = DefaultPage, int limit = DefaultLimit)
        {
            var response = _databasesPersistentSource.GetDatabaseApiModelByPage(limit, page, operators, User);
            return Ok(response);
        }

        // --------------------------
        // GET Server Names list
        // --------------------------

        /// <summary>Get Database ServerNames list</summary>
        [SwaggerResponse(StatusCodes.Status200OK, Type = typeof(List<string?>))]
        [HttpGet]
        // Preserve existing route (typo kept for backward compatibility)
        [Route("GetDatabasServerNameslist")]
        // Add corrected alias (non-breaking)
        [Route("GetDatabaseServerNamesList")]
        public IActionResult GetDatabaseServerNamesList()
        {
            var result = _databasesPersistentSource.GetDatabasServerNameslist();
            return Ok(result);
        }

        // --------------------------
        // PUT (edit)
        // --------------------------

        /// <summary>Edit database entry</summary>
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
                    // Preserve existing call shape and response messages
                    var env = _environmentsPersistentSource.GetEnvironment(envIds, User);
                    if (env == null)
                    {
                        return BadRequest("Error while checking permissions, probably Environment missing in Deployment database");
                    }

                    if (!_securityPrivilegesChecker.CanModifyEnvironment(User, env.EnvironmentName))
                    {
                        return StatusCode((int)HttpStatusCode.Forbidden,
                            "You should have write permission on " + env.EnvironmentName + " to modify this database");
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
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating database {DatabaseId}. TraceId: {TraceId}", id, HttpContext.TraceIdentifier);
                // Preserve 400 with string per original contract
                return StatusCode(StatusCodes.Status400BadRequest, "An error occurred while updating the entry.");
            }
        }
    }
}