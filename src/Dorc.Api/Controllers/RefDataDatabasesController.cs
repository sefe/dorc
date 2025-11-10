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

        private static class Err
        {
            public const string BodyRequired = "Body is required.";
            public const string NameServerRequired = "'Name' and 'ServerName' are required.";
            public const string IdPositive = "'{0}' must be greater than zero.";
            public const string IdMismatch = "'id' must be the same as database.Id.";
            public const string NotFound = "The database could not be found.";
            public const string EnvMissing = "Error while checking permissions; Environment missing in Deployment database.";
            public const string PermissionAll = "You must have write permission on all environments attached to this database: {0}";
            public const string PermissionSingle = "You must have write permission on {0} to modify this database.";
            public const string DeleteBlocked = "Database is assigned to one or more environments. Unassign it before deleting: {0}";
            public const string DeleteIncomplete = "Deletion did not complete. The database may still be referenced by other entities.";
            public const string DeleteConstraint = "The database is referenced by other entities (e.g., environments, pipelines, jobs, or history). Remove those references and try again.";
            public const string DeleteUnexpected = "An unexpected error occurred while deleting the database.";
            public const string UpdateConstraint = "The database may be referenced by other entities. Review constraints and try again.";
            public const string UpdateUnexpected = "An unexpected error occurred while updating the database.";
            public const string CreateConstraint = "The database could not be created due to constraint violations (e.g., unique keys or references).";
            public const string CreateGenericBad = "The request could not be processed. Please review the input and try again.";

            public static string Duplicate(string server, string name) => $"Database already exists {server}:{name}";
        }

        public RefDataDatabasesController(
            IDatabasesPersistentSource databasesPersistentSource,
            ISecurityPrivilegesChecker securityPrivilegesChecker,
            IEnvironmentsPersistentSource environmentsPersistentSource)
        {
            _environmentsPersistentSource = environmentsPersistentSource;
            _securityPrivilegesChecker = securityPrivilegesChecker;
            _databasesPersistentSource = databasesPersistentSource;
        }

        // ---------- Helpers
        private IActionResult BadRequestText(string message) => new BadRequestObjectResult(message);

        private ContentResult TextError(int statusCode, string message)
        {
            var response = HttpContext?.Response;
            if (response != null)
            {
                response.Headers["X-Error-Message"] = message ?? string.Empty;

                const string expose = "Access-Control-Expose-Headers";
                var current = response.Headers[expose].ToString();
                if (string.IsNullOrWhiteSpace(current))
                {
                    response.Headers[expose] = "X-Error-Message";
                }
                else
                {
                    var parts = current.Split(',').Select(h => h.Trim());
                    if (!parts.Contains("X-Error-Message", StringComparer.OrdinalIgnoreCase))
                    {
                        response.Headers[expose] = current + ", X-Error-Message";
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

        // ---------- Validators 
        private IActionResult? ValidateBody(DatabaseApiModel? model)
        {
            if (model == null) return BadRequestText(Err.BodyRequired);
            return null;
        }

        private IActionResult? ValidateNames(DatabaseApiModel? model)
        {
            if (model == null) return BadRequestText(Err.BodyRequired);
            if (string.IsNullOrWhiteSpace(model.Name) || string.IsNullOrWhiteSpace(model.ServerName))
                return BadRequestText(Err.NameServerRequired);
            return null;
        }

        private IActionResult? ValidateIdPositive(int id, string paramName = "id")
        {
            if (id <= 0) return BadRequestText(string.Format(Err.IdPositive, paramName));
            return null;
        }

        private IActionResult? ValidateIdMatches(int id, DatabaseApiModel? model)
        {
            if (model == null) return BadRequestText(Err.BodyRequired);
            if (id != model.Id) return BadRequestText(Err.IdMismatch);
            return null;
        }

        private IActionResult? EnsureExists(int id)
        {
            var found = _databasesPersistentSource.GetDatabase(id);
            if (found == null) return TextError(StatusCodes.Status404NotFound, Err.NotFound);
            return null;
        }

        private IActionResult? ValidateDuplicate(DatabaseApiModel model, int? excludeId = null)
        {
            if (_databasesPersistentSource == null)
                return BadRequestText(Err.Duplicate(model.ServerName, model.Name));

            var candidates = _databasesPersistentSource.GetDatabases(model.Name, model.ServerName)
                            ?? Enumerable.Empty<DatabaseApiModel>();
            var existing = candidates.FirstOrDefault();
            var exists = existing != null && (!excludeId.HasValue || existing.Id != excludeId.Value);

            if (exists) return BadRequestText(Err.Duplicate(model.ServerName, model.Name));
            return null;
        }

        /// <summary>
        ///     Return database details by database ID
        /// </summary>
        /// <param name="id">Database ID</param>
        /// <returns></returns>
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
                return (_databasesPersistentSource.GetDatabases() ?? Enumerable.Empty<DatabaseApiModel>()).ToList();

            return (_databasesPersistentSource.GetDatabases(name, server) ?? Enumerable.Empty<DatabaseApiModel>()).ToList();
        }

        /// <summary>
        ///     Create Database entry
        /// </summary>
        /// <param name="newDatabaseApiModel">Json string in request body</param>
        /// <returns></returns>
        [SwaggerResponse(StatusCodes.Status200OK, Type = typeof(DatabaseApiModel))]
        [SwaggerResponse(StatusCodes.Status400BadRequest, Type = typeof(string))]
        [SwaggerResponse(StatusCodes.Status409Conflict, Type = typeof(string))]
        [HttpPost]
        public IActionResult Post([FromBody] DatabaseApiModel? newDatabaseApiModel)
        {
            if (ValidateBody(newDatabaseApiModel) is IActionResult bad) return bad;
            if (ValidateNames(newDatabaseApiModel) is IActionResult nameBad) return nameBad;

            // Duplicate guard
            if (ValidateDuplicate(newDatabaseApiModel!, excludeId: null) is IActionResult dup) return dup;

            try
            {
                var created = _databasesPersistentSource.AddDatabase(newDatabaseApiModel!);
                return Ok(created);
            }
            catch (ArgumentException ex)
            {
                // 400 with exact message (tests)
                return BadRequestText(ex.Message);
            }
            catch (DbUpdateException)
            {
                return TextError(StatusCodes.Status409Conflict, Err.CreateConstraint);
            }
            catch (Exception)
            {
                return BadRequestText(Err.CreateGenericBad);
            }
        }

        /// <summary>
        ///     Delete Database entry
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

            var envNamesAttachedToDatabase =
                (_databasesPersistentSource.GetEnvironmentNamesForDatabaseId(databaseId) ?? Enumerable.Empty<string>())
                .Distinct().OrderBy(x => x).ToList();

            var lackingWrite = new List<string>();
            foreach (var environmentName in envNamesAttachedToDatabase)
            {
                var environmentApiModel = _environmentsPersistentSource.GetEnvironment(environmentName);
                if (environmentApiModel == null)
                    return TextError(StatusCodes.Status400BadRequest, Err.EnvMissing);

                if (!_securityPrivilegesChecker.CanModifyEnvironment(User, environmentApiModel.EnvironmentName))
                    lackingWrite.Add(environmentApiModel.EnvironmentName);
            }

            if (lackingWrite.Count > 0)
                return TextError(StatusCodes.Status403Forbidden,
                                 string.Format(Err.PermissionAll, string.Join(", ", lackingWrite.OrderBy(x => x))));

            if (envNamesAttachedToDatabase.Any())
                return TextError(StatusCodes.Status409Conflict,
                                 string.Format(Err.DeleteBlocked, string.Join(", ", envNamesAttachedToDatabase)));

            try
            {
                var deleted = _databasesPersistentSource.DeleteDatabase(databaseId);
                if (deleted) return Ok(new ApiBoolResult { Result = true, Message = "Database deleted" });

                return TextError(StatusCodes.Status409Conflict, Err.DeleteIncomplete);
            }
            catch (DbUpdateException)
            {
                return TextError(StatusCodes.Status409Conflict, Err.DeleteConstraint);
            }
            catch (Exception)
            {
                return TextError(StatusCodes.Status500InternalServerError, Err.DeleteUnexpected);
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

                var environmentIdsForServerName = _databasesPersistentSource.GetEnvironmentNamesForDatabaseId(database!.Id)
                    ?? Enumerable.Empty<string>();

                foreach (var envName in environmentIdsForServerName)
                {
                    var env = _environmentsPersistentSource.GetEnvironment(envName, User);
                    if (env == null)
                    {
                        return TextError(StatusCodes.Status400BadRequest, Err.EnvMissing);
                    }
                    if (!_securityPrivilegesChecker.CanModifyEnvironment(User, env.EnvironmentName))
                    {
                        return TextError(StatusCodes.Status403Forbidden, string.Format(Err.PermissionSingle, env.EnvironmentName));
                    }
                }

                if (ValidateDuplicate(database!, excludeId: id) is IActionResult dup) return dup;

                var updated = _databasesPersistentSource.UpdateDatabase(id, database!, User);
                if (updated != null) return Ok(updated);

                return TextError(StatusCodes.Status404NotFound, "Error updating entry.");
            }
            catch (ArgumentException ex)
            {
                return BadRequestText(ex.Message);
            }
            catch (DbUpdateException)
            {
                return TextError(StatusCodes.Status409Conflict, Err.UpdateConstraint);
            }
            catch (Exception)
            {
                return TextError(StatusCodes.Status500InternalServerError, Err.UpdateUnexpected);
            }
        }
    }
}