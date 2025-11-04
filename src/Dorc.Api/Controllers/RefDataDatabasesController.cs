using Dorc.ApiModel;
using Dorc.Core.Interfaces;
using Dorc.PersistentData.Sources.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Swashbuckle.AspNetCore.Annotations;

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

        // --- Helper to create consistent ProblemDetails results ---
        private IActionResult ProblemResult(int statusCode, string title, string detail,
            IDictionary<string, object>? extraExtensions = null)
        {
            var pd = new ProblemDetails
            {
                Title = title,
                Detail = detail,
                Status = statusCode
            };

            // Always include correlation id for supportability
            if (!string.IsNullOrEmpty(HttpContext.TraceIdentifier))
            {
                pd.Extensions["correlationId"] = HttpContext.TraceIdentifier;
            }

            if (extraExtensions != null)
            {
                foreach (var kvp in extraExtensions)
                {
                    pd.Extensions[kvp.Key] = kvp.Value;
                }
            }
            return StatusCode(statusCode, pd);
        }


        // 1) Body present?
        private IActionResult? ValidateBody(DatabaseApiModel? model)
        {
            if (model == null)
                return ProblemResult(StatusCodes.Status400BadRequest, "Invalid request", "Body is required.");
            return null;
        }

        // 2) Names present?
        private IActionResult? ValidateNames(DatabaseApiModel model)
        {
            if (string.IsNullOrWhiteSpace(model.Name) || string.IsNullOrWhiteSpace(model.ServerName))
                return ProblemResult(StatusCodes.Status400BadRequest, "Invalid request", "'Name' and 'ServerName' are required.");
            return null;
        }

        // 3) ID > 0 (param/route/query id). Param name gives better error text reuse.
        private IActionResult? ValidateIdPositive(int id, string paramName = "id")
        {
            if (id <= 0)
                return ProblemResult(StatusCodes.Status400BadRequest, "Invalid request", $"'{paramName}' must be greater than zero.");
            return null;
        }

        // 4) PUT-specific: route/query id must match body.Id
        private IActionResult? ValidateIdMatches(int id, DatabaseApiModel model)
        {
            if (id != model.Id)
                return ProblemResult(StatusCodes.Status400BadRequest, "Invalid request", "'id' must be the same as database.Id.");
            return null;
        }

        // 5) Existence check (404 if not found)
        private IActionResult? EnsureExists(int id)
        {
            var found = _databasesPersistentSource.GetDatabase(id);
            if (found == null)
                return ProblemResult(StatusCodes.Status404NotFound, "Not found", "The database could not be found.");
            return null;
        }

        /// <summary>
        ///  Return database details by database ID
        /// </summary>
        /// <param name="id">Database ID</param>
        /// <returns></returns>
        [SwaggerResponse(StatusCodes.Status200OK, Type = typeof(DatabaseApiModel))]
        [HttpGet]
        [Route("{id}")]
        public IActionResult Get(int id)
        {
            if (ValidateIdPositive(id) is IActionResult bad) return bad;
            if (EnsureExists(id) is IActionResult nf) return nf;

            var model = _databasesPersistentSource.GetDatabase(id);
            return Ok(model);
        }

        /// <summary>
        /// Gets  databases by name
        /// </summary>
        /// <param name="name">database name</param>
        /// <param name="server">database server name</param>
        /// <returns></returns>
        [SwaggerResponse(StatusCodes.Status200OK, Type = typeof(List<DatabaseApiModel>))]
        [HttpGet]
        public List<DatabaseApiModel> Get(string name = "", string server = "")
        {
            if (string.IsNullOrEmpty(name) && string.IsNullOrEmpty(server))
            {
                return _databasesPersistentSource.GetDatabases().ToList();
            }
            return _databasesPersistentSource.GetDatabases(name, server).ToList();
        }

        /// <summary>
        /// Create Database entry
        /// </summary>
        /// <param name="newDatabaseApiModel">Json string in request body</param>
        /// <returns></returns>
        [SwaggerResponse(StatusCodes.Status200OK, Type = typeof(DatabaseApiModel))]
        [SwaggerResponse(StatusCodes.Status400BadRequest, Type = typeof(ProblemDetails))]
        [SwaggerResponse(StatusCodes.Status409Conflict, Type = typeof(ProblemDetails))]
        [HttpPost]
        public IActionResult Post([FromBody] DatabaseApiModel newDatabaseApiModel)
        {
            // 0) basic validation
            if (ValidateBody(newDatabaseApiModel) is IActionResult bad) return bad;
            if (ValidateNames(newDatabaseApiModel) is IActionResult nameBad) return nameBad;


            // 1) duplicate check BEFORE hitting storage
            var existing = _databasesPersistentSource
                .GetDatabases(newDatabaseApiModel.Name, newDatabaseApiModel.ServerName)
                .FirstOrDefault();

            if (existing != null)
            {
                return ProblemResult(StatusCodes.Status400BadRequest,
                    "Duplicate database",
                    $"Database already exists {newDatabaseApiModel.ServerName}:{newDatabaseApiModel.Name}");
            }

            // 2) create
            try
            {
                var created = _databasesPersistentSource.AddDatabase(newDatabaseApiModel);
                return Ok(created);
            }
            catch (DbUpdateException)
            {
                // true storage-level constraint collision
                return ProblemResult(StatusCodes.Status409Conflict,
                    "Create blocked by constraints",
                    "The database could not be created due to constraint violations (e.g., unique keys or references).");
            }
            catch (Exception)
            {
                return ProblemResult(StatusCodes.Status400BadRequest,
                    "Create failed",
                    "The request could not be processed. Please review the input and try again.");
            }
        }

        /// <summary>
        /// Delete Database entry
        /// </summary>
        [SwaggerResponse(StatusCodes.Status200OK, Type = typeof(ApiBoolResult))]
        [SwaggerResponse(StatusCodes.Status400BadRequest, Type = typeof(ProblemDetails))]
        [SwaggerResponse(StatusCodes.Status403Forbidden, Type = typeof(ProblemDetails))]
        [SwaggerResponse(StatusCodes.Status404NotFound, Type = typeof(ProblemDetails))]
        [SwaggerResponse(StatusCodes.Status409Conflict, Type = typeof(ProblemDetails))]
        [HttpDelete]
        public IActionResult Delete([FromQuery] int databaseId)
        {
            // 0) validate input

            if (ValidateIdPositive(databaseId, nameof(databaseId)) is IActionResult bad) return bad;
            if (EnsureExists(databaseId) is IActionResult nf) return nf;

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
                    return ProblemResult(StatusCodes.Status400BadRequest,
                        "Permissions check failed",
                        "Error while checking permissions; Environment missing in Deployment database.");
                }

                if (!_securityPrivilegesChecker.CanModifyEnvironment(User, environmentApiModel.EnvironmentName))
                {
                    lackingWrite.Add(environmentApiModel.EnvironmentName);
                }
            }

            if (lackingWrite.Count > 0)
            {
                var ext403 = new Dictionary<string, object>
                {
                    ["blockers"] = new object[]
                    {
                        new { type = "environment", items = lackingWrite.OrderBy(x => x).ToArray() }
                    }
                };
                return ProblemResult(StatusCodes.Status403Forbidden,
                    "Forbidden",
                    "You must have write permission on all environments attached to this database.",
                    ext403);
            }

            // 4) if assigned to environments, return 409 with blockers list
            if (envNamesAttachedToDatabase.Any())
            {
                var ext409 = new Dictionary<string, object>
                {
                    ["blockers"] = new object[]
                    {
                        new { type = "environment", items = envNamesAttachedToDatabase.ToArray() }
                    }
                };
                return ProblemResult(StatusCodes.Status409Conflict,
                    "Delete blocked by references",
                    "Database is assigned to one or more environments. Unassign it before deleting.",
                    ext409);
            }

            // 5) perform the delete
            try
            {
                var deleted = _databasesPersistentSource.DeleteDatabase(databaseId);
                if (deleted)
                {
                    return Ok(new ApiBoolResult { Result = true, Message = "Database deleted" });
                }

                // Delete returned false without exception -> treat as conflict
                return ProblemResult(StatusCodes.Status409Conflict,
                    "Delete failed",
                    "Deletion did not complete. The database may still be referenced by other entities.");
            }
            catch (DbUpdateException)
            {
                return ProblemResult(StatusCodes.Status409Conflict,
                    "Delete blocked by references",
                    "The database is referenced by other entities (e.g., environments, pipelines, jobs, or history). Remove those references and try again.");
            }
            catch (Exception)
            {
                return ProblemResult(StatusCodes.Status500InternalServerError,
                    "Unexpected error",
                    "An unexpected error occurred while deleting the database.");
            }
        }

        /// <summary>
        /// Get Databases by page
        /// </summary>
        /// <param name="operators"></param>
        /// <param name="page"></param>
        /// <param name="limit"></param>
        /// <returns></returns>
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
        /// <returns></returns>
        [SwaggerResponse(StatusCodes.Status200OK, Type = typeof(List<string?>))]
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
        /// <param name="id"></param>
        /// <param name="database"></param>
        /// <returns></returns>
        [SwaggerResponse(StatusCodes.Status200OK, Type = typeof(DatabaseApiModel))]
        [SwaggerResponse(StatusCodes.Status400BadRequest, Type = typeof(ProblemDetails))]
        [SwaggerResponse(StatusCodes.Status403Forbidden, Type = typeof(ProblemDetails))]
        [SwaggerResponse(StatusCodes.Status404NotFound, Type = typeof(ProblemDetails))]
        [HttpPut]
        public IActionResult Put([FromQuery] int id, [FromBody] DatabaseApiModel database)
        {
            try
            {
                // 0) basic validation
                if (ValidateBody(database) is IActionResult bad) return bad;
                if (ValidateIdMatches(id, database) is IActionResult idMismatch) return idMismatch;
                if (ValidateIdPositive(id) is IActionResult badId) return badId;
                if (ValidateNames(database) is IActionResult nameBad) return nameBad;


                // 1) permissions on environments that reference this DB
                var environmentNamesForDb = _databasesPersistentSource.GetEnvironmentNamesForDatabaseId(database.Id);
                foreach (var envName in environmentNamesForDb)
                {
                    // Ensure this overload expects a *name*; if it expects an ID, switch accordingly.
                    var env = _environmentsPersistentSource.GetEnvironment(envName, User);
                    if (env == null)
                    {
                        return ProblemResult(StatusCodes.Status400BadRequest,
                            "Permissions check failed",
                            "Error while checking permissions, probably Environment missing in Deployment database.");
                    }

                    if (!_securityPrivilegesChecker.CanModifyEnvironment(User, env.EnvironmentName))
                    {
                        return ProblemResult(StatusCodes.Status403Forbidden,
                            "Forbidden",
                            $"You must have write permission on {env.EnvironmentName} to modify this database.");
                    }
                }

                // 2) duplicate check for (Name, ServerName) against *other* records
                var dup = _databasesPersistentSource
                    .GetDatabases(database.Name, database.ServerName)
                    .FirstOrDefault();

                if (dup != null && dup.Id != id)
                {
                    return ProblemResult(StatusCodes.Status400BadRequest,
                        "Duplicate database",
                        $"Database already exists {database.ServerName}:{database.Name}");
                }

                // 3) proceed with update
                var updated = _databasesPersistentSource.UpdateDatabase(id, database, User);
                if (updated != null)
                {
                    return Ok(updated);
                }

                return ProblemResult(StatusCodes.Status404NotFound,
                    "Not found",
                    "Error updating entry.");
            }
            catch (DbUpdateException)
            {
                return ProblemResult(StatusCodes.Status409Conflict,
                    "Update blocked by constraints",
                    "The database may be referenced by other entities. Review constraints and try again.");
            }
            catch (Exception)
            {
                return ProblemResult(StatusCodes.Status500InternalServerError,
                    "Unexpected error",
                    "An unexpected error occurred while updating the database.");
            }
        }
    }
}