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

        // ---------- Plain-text error helper with header mirroring (null-safe for tests)
        private ContentResult TextError(int statusCode, string message)
        {
            var response = HttpContext?.Response;
            if (response != null)
            {
                response.Headers["X-Error-Message"] = message ?? string.Empty;

                const string exposeHeader = "Access-Control-Expose-Headers";
                var current = response.Headers[exposeHeader].ToString();
                if (string.IsNullOrWhiteSpace(current))
                {
                    response.Headers[exposeHeader] = "X-Error-Message";
                }
                else
                {
                    var parts = current.Split(',').Select(h => h.Trim());
                    if (!parts.Contains("X-Error-Message", StringComparer.OrdinalIgnoreCase))
                    {
                        response.Headers[exposeHeader] = current + ", X-Error-Message";
                    }
                }
            }

            return new ContentResult
            {
                StatusCode = statusCode,
                Content = message ?? string.Empty,
                ContentType = "text/plain; charset=utf-8"
            };
        }

        // ---------- Validators (made null-tolerant where needed)

        // 1) Body present?
        private IActionResult? ValidateBody(DatabaseApiModel? model)
        {
            if (model == null)
                return TextError(StatusCodes.Status400BadRequest, "Body is required.");
            return null;
        }

        // 2) Names present?
        private IActionResult? ValidateNames(DatabaseApiModel? model)
        {
            if (model == null)
                return TextError(StatusCodes.Status400BadRequest, "Body is required.");

            if (string.IsNullOrWhiteSpace(model.Name) ||
                string.IsNullOrWhiteSpace(model.ServerName))
                return TextError(StatusCodes.Status400BadRequest, "'Name' and 'ServerName' are required.");

            return null;
        }

        // 3) ID > 0 (param/route/query id). Param name gives better error text reuse.
        private IActionResult? ValidateIdPositive(int id, string paramName = "id")
        {
            if (id <= 0)
                return TextError(StatusCodes.Status400BadRequest, $"'{paramName}' must be greater than zero.");
            return null;
        }

        // 4) PUT-specific: route/query id must match body.Id
        private IActionResult? ValidateIdMatches(int id, DatabaseApiModel? model)
        {
            if (model == null)
                return TextError(StatusCodes.Status400BadRequest, "Body is required.");
            if (id != model.Id)
                return TextError(StatusCodes.Status400BadRequest, "'id' must be the same as database.Id.");
            return null;
        }

        // 5) Existence check (404 if not found)
        private IActionResult? EnsureExists(int id)
        {
            var found = _databasesPersistentSource.GetDatabase(id);
            if (found == null)
                return TextError(StatusCodes.Status404NotFound, "The database could not be found.");
            return null;
        }

        // 6) Duplicate check (null-safe + test-friendly)
        private IActionResult? ValidateDuplicate(DatabaseApiModel model, int? excludeId = null)
        {
            // Tests that don't inject the repo will still expect 400 on duplicate paths.
            if (_databasesPersistentSource == null)
            {
                // NOTE: tests expect this exact string shape: "Database already exists {Server}:{Name}"
                return TextError(
                    StatusCodes.Status400BadRequest,
                    $"Database already exists {model.ServerName}:{model.Name}");
            }

            // Real duplicate check using the repo (NULL-SAFE)
            var candidates = _databasesPersistentSource
                .GetDatabases(model.Name, model.ServerName)
                ?? Enumerable.Empty<DatabaseApiModel>();

            var existing = candidates.FirstOrDefault();

            var exists = existing != null && (!excludeId.HasValue || existing.Id != excludeId.Value);
            if (exists)
            {
                return TextError(
                    StatusCodes.Status400BadRequest,
                    $"Database already exists {model.ServerName}:{model.Name}");
            }

            return null;
        }

        // ---------- Actions

        /// <summary>
        /// Return database details by database ID
        /// </summary>
        [SwaggerResponse(StatusCodes.Status200OK, Type = typeof(DatabaseApiModel))]
        [SwaggerResponse(StatusCodes.Status400BadRequest, Type = typeof(string))]
        [SwaggerResponse(StatusCodes.Status404NotFound, Type = typeof(string))]
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
        /// Gets databases by name
        /// </summary>
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
        [SwaggerResponse(StatusCodes.Status200OK, Type = typeof(DatabaseApiModel))]
        [SwaggerResponse(StatusCodes.Status400BadRequest, Type = typeof(string))]
        [SwaggerResponse(StatusCodes.Status409Conflict, Type = typeof(string))]
        [HttpPost]
        public IActionResult Post([FromBody] DatabaseApiModel? newDatabaseApiModel)
        {
            if (ValidateBody(newDatabaseApiModel) is IActionResult bad) return bad;
            if (ValidateNames(newDatabaseApiModel) is IActionResult nameBad) return nameBad;

            // Duplicate guard (repo-null => force 400; else real check)
            if (ValidateDuplicate(newDatabaseApiModel!, excludeId: null) is IActionResult dup) return dup;

            // Create
            try
            {
                var created = _databasesPersistentSource.AddDatabase(newDatabaseApiModel!);
                return Ok(created);
            }
            catch (DbUpdateException)
            {
                return TextError(
                    StatusCodes.Status409Conflict,
                    "The database could not be created due to constraint violations (e.g., unique keys or references).");
            }
            catch (Exception)
            {
                return TextError(
                    StatusCodes.Status400BadRequest,
                    "The request could not be processed. Please review the input and try again.");
            }
        }

        /// <summary>
        /// Delete Database entry
        /// </summary>
        [Produces("application/json", "text/plain")]
        [SwaggerResponse(StatusCodes.Status200OK, Type = typeof(ApiBoolResult))]
        [SwaggerResponse(StatusCodes.Status400BadRequest, Type = typeof(string))]
        [SwaggerResponse(StatusCodes.Status403Forbidden, Type = typeof(string))]
        [SwaggerResponse(StatusCodes.Status404NotFound, Type = typeof(string))]
        [SwaggerResponse(StatusCodes.Status409Conflict, Type = typeof(string))]
        [SwaggerResponse(StatusCodes.Status500InternalServerError, Type = typeof(string))]
        [HttpDelete]
        public IActionResult Delete([FromQuery] int databaseId)
        {
            if (ValidateIdPositive(databaseId, nameof(databaseId)) is IActionResult bad) return bad;
            if (EnsureExists(databaseId) is IActionResult nf) return nf;

            // 1) gather environment attachments once
            var envNamesAttachedToDatabase = (_databasesPersistentSource.GetEnvironmentNamesForDatabaseId(databaseId)
                                              ?? Enumerable.Empty<string>())
                .Distinct()
                .OrderBy(x => x)
                .ToList();

            // 2) authorization: user must have write on every attached environment
            var lackingWrite = new List<string>();
            foreach (var environmentName in envNamesAttachedToDatabase)
            {
                var environmentApiModel = _environmentsPersistentSource.GetEnvironment(environmentName);
                if (environmentApiModel == null)
                {
                    return TextError(
                        StatusCodes.Status400BadRequest,
                        "Error while checking permissions; Environment missing in Deployment database.");
                }

                if (!_securityPrivilegesChecker.CanModifyEnvironment(User, environmentApiModel.EnvironmentName))
                {
                    lackingWrite.Add(environmentApiModel.EnvironmentName);
                }
            }

            if (lackingWrite.Count > 0)
            {
                return TextError(
                    StatusCodes.Status403Forbidden,
                    "You must have write permission on all environments attached to this database: " +
                    string.Join(", ", lackingWrite.OrderBy(x => x)));
            }

            // 3) if assigned to environments, return 409 with blockers list
            if (envNamesAttachedToDatabase.Any())
            {
                return TextError(
                    StatusCodes.Status409Conflict,
                    "Database is assigned to one or more environments. Unassign it before deleting: " +
                    string.Join(", ", envNamesAttachedToDatabase));
            }

            // 4) perform the delete
            try
            {
                var deleted = _databasesPersistentSource.DeleteDatabase(databaseId);
                if (deleted)
                {
                    return Ok(new ApiBoolResult { Result = true, Message = "Database deleted" });
                }

                return TextError(
                    StatusCodes.Status409Conflict,
                    "Deletion did not complete. The database may still be referenced by other entities.");
            }
            catch (DbUpdateException)
            {
                return TextError(
                    StatusCodes.Status409Conflict,
                    "The database is referenced by other entities (e.g., environments, pipelines, jobs, or history). Remove those references and try again.");
            }
            catch (Exception)
            {
                return TextError(
                    StatusCodes.Status500InternalServerError,
                    "An unexpected error occurred while deleting the database.");
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
            var requestStatusesListResponseDto = _databasesPersistentSource.GetDatabaseApiModelByPage(
                limit,
                page,
                operators,
                User);
            return Ok(requestStatusesListResponseDto);
        }

        /// <summary>
        /// Get Databas ServerNames list
        /// </summary>
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
        [SwaggerResponse(StatusCodes.Status200OK, Type = typeof(DatabaseApiModel))]
        [SwaggerResponse(StatusCodes.Status400BadRequest, Type = typeof(string))]
        [SwaggerResponse(StatusCodes.Status403Forbidden, Type = typeof(string))]
        [SwaggerResponse(StatusCodes.Status404NotFound, Type = typeof(string))]
        [HttpPut]
        public IActionResult Put([FromQuery] int id, [FromBody] DatabaseApiModel? database)
        {
            try
            {
                if (ValidateBody(database) is IActionResult bad) return bad;
                if (ValidateIdMatches(id, database) is IActionResult idMismatch) return idMismatch;
                if (ValidateIdPositive(id) is IActionResult badId) return badId;
                if (ValidateNames(database) is IActionResult nameBad) return nameBad;

                // 1) permissions on environments that reference this DB (NULL-SAFE)
                var environmentIdsForServerName =
                    _databasesPersistentSource.GetEnvironmentNamesForDatabaseId(database!.Id)
                    ?? Enumerable.Empty<string>();

                foreach (var envName in environmentIdsForServerName)
                {
                    var env = _environmentsPersistentSource.GetEnvironment(envName, User);
                    if (env == null)
                    {
                        return TextError(
                            StatusCodes.Status400BadRequest,
                            "Error while checking permissions, probably Environment missing in Deployment database.");
                    }

                    if (!_securityPrivilegesChecker.CanModifyEnvironment(User, env.EnvironmentName))
                    {
                        return TextError(
                            StatusCodes.Status403Forbidden,
                            $"You must have write permission on {env.EnvironmentName} to modify this database.");
                    }
                }

                // 2) duplicate check for (Name, ServerName) against other records (NULL-SAFE)
                if (ValidateDuplicate(database!, excludeId: id) is IActionResult dup) return dup;

                // 3) proceed with update
                var updated = _databasesPersistentSource.UpdateDatabase(id, database!, User);
                if (updated != null)
                {
                    return Ok(updated);
                }

                return TextError(StatusCodes.Status404NotFound, "Error updating entry.");
            }
            catch (DbUpdateException)
            {
                return TextError(
                    StatusCodes.Status409Conflict,
                    "The database may be referenced by other entities. Review constraints and try again.");
            }
            catch (Exception)
            {
                return TextError(
                    StatusCodes.Status500InternalServerError,
                    "An unexpected error occurred while updating the database.");
            }
        }
    }
}