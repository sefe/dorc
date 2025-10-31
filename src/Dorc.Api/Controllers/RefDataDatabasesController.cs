﻿using Dorc.ApiModel;
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

        /// <summary>
        ///     Return database details by database ID
        /// </summary>
        /// <param name="id">Database ID</param>
        /// <returns></returns>
        [SwaggerResponse(StatusCodes.Status200OK, Type = typeof(DatabaseApiModel))]
        [HttpGet]
        [Route("{id}")]
        public IActionResult Get(int id)
        {
            if (id <= 0)
            {
                return ProblemResult(StatusCodes.Status400BadRequest,
                    "Invalid request",
                    "'id' must be greater than zero.");
            }

            var model = _databasesPersistentSource.GetDatabase(id);
            if (model == null)
            {
                return ProblemResult(StatusCodes.Status404NotFound,
                    "Not found",
                    "The database could not be found.");
            }

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
            // When neither filter is provided, return all
            if (string.IsNullOrEmpty(name) && string.IsNullOrEmpty(server))
            {
                return _databasesPersistentSource.GetDatabases().ToList();
            }
            // When either (or both) filter(s) is provided, return filtered
            return _databasesPersistentSource.GetDatabases(name, server).ToList();
        }

        /// <summary>
        ///     Create Database entry
        /// </summary>
        /// <param name="newDatabaseApiModel">Json string in request body</param>
        /// <returns></returns>
        [SwaggerResponse(StatusCodes.Status200OK, Type = typeof(DatabaseApiModel))]
        [HttpPost]
        public IActionResult Post([FromBody] DatabaseApiModel newDatabaseApiModel)
        {
            try
            {
                var databaseApiModel = _databasesPersistentSource.AddDatabase(newDatabaseApiModel);

                return StatusCode(StatusCodes.Status200OK, databaseApiModel);
            }
            catch (DbUpdateException ex)
            {
                _logger.LogWarning(ex,
                    "Create blocked by constraint; CorrelationId={CorrelationId}",
                    HttpContext.TraceIdentifier);

                return ProblemResult(StatusCodes.Status409Conflict,
                    "Create blocked by constraints",
                    "The database could not be created due to constraint violations (e.g., unique keys or references).");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Unexpected error creating database; CorrelationId={CorrelationId}",
                    HttpContext.TraceIdentifier);

                return ProblemResult(StatusCodes.Status400BadRequest,
                    "Create failed",
                    "The request could not be processed. Please review the input and try again.");
            }
        }

        /// <summary>
        /// Delete Database entry
        /// </summary>
        [SwaggerResponse(StatusCodes.Status200OK, Type = typeof(ApiBoolResult))]
        [HttpDelete]
        public IActionResult Delete([FromQuery] int databaseId)
        {
            // 0) validate input
            if (databaseId <= 0)
            {
                return ProblemResult(StatusCodes.Status400BadRequest,
                    "Invalid request",
                    "'databaseId' must be greater than zero.");
            }

            // 1) existence check
            var dbModel = _databasesPersistentSource.GetDatabase(databaseId);
            if (dbModel == null)
            {
                return ProblemResult(StatusCodes.Status404NotFound,
                    "Not found",
                    "The database could not be found.");
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
                var ext = new Dictionary<string, object>
                {
                    ["blockers"] = new object[]
                    {
                        new { type = "environment", items = lackingWrite.OrderBy(x => x).ToArray() }
                    }
                };
                return ProblemResult(StatusCodes.Status403Forbidden,
                    "Forbidden",
                    "You must have write permission on all environments attached to this database.",
                    ext);
            }

            // 4) if assigned to environments, return 409 with blockers list
            if (envNamesAttachedToDatabase.Any())
            {
                var ext = new Dictionary<string, object>
                {
                    ["blockers"] = new object[]
                    {
                        new { type = "environment", items = envNamesAttachedToDatabase.ToArray() }
                    }
                };
                return ProblemResult(StatusCodes.Status409Conflict,
                    "Delete blocked by references",
                    "Database is assigned to one or more environments. Unassign it before deleting.",
                    ext);
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
                return ProblemResult(StatusCodes.Status409Conflict,
                    "Delete failed",
                    "Deletion did not complete. The database may still be referenced by other entities.");
            }
            catch (DbUpdateException ex)
            {
                _logger.LogWarning(ex,
                    "Delete blocked by FK/constraint for DatabaseId={DatabaseId}; CorrelationId={CorrelationId}",
                    databaseId, HttpContext.TraceIdentifier);

                return ProblemResult(StatusCodes.Status409Conflict,
                    "Delete blocked by references",
                    "The database is referenced by other entities (e.g., environments, pipelines, jobs, or history). Remove those references and try again.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Unexpected error deleting DatabaseId={DatabaseId}; CorrelationId={CorrelationId}",
                    databaseId, HttpContext.TraceIdentifier);

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
                var environmentIdsForServerName =
                    _databasesPersistentSource.GetEnvironmentNamesForDatabaseId(database.Id);

                foreach (var envIds in environmentIdsForServerName)
                {
                    var env = _environmentsPersistentSource.GetEnvironment(envIds, User);
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

                if (id != database.Id)
                {
                    return ProblemResult(StatusCodes.Status400BadRequest,
                        "Invalid request",
                        "'id' must be the same as database.Id.");
                }

                if (id <= 0)
                {
                    return ProblemResult(StatusCodes.Status400BadRequest,
                        "Invalid request",
                        "'id' cannot be 0.");
                }

                var databaseApiModel = _databasesPersistentSource.GetDatabase(database.Id);
                if (databaseApiModel != null && databaseApiModel.Id != id)
                {
                    return ProblemResult(StatusCodes.Status400BadRequest,
                        "Invalid request",
                        "Cannot set the server name to the same as one that already exists!");
                }

                var result = _databasesPersistentSource.UpdateDatabase(id, database, User);
                if (result != null)
                {
                    return Ok(result);
                }

                return ProblemResult(StatusCodes.Status404NotFound,
                    "Not found",
                    "Error updating entry.");
            }
            catch (DbUpdateException ex)
            {
                _logger.LogWarning(ex,
                    "Update blocked by constraint for DatabaseId={DatabaseId}; CorrelationId={CorrelationId}",
                    id, HttpContext.TraceIdentifier);

                return ProblemResult(StatusCodes.Status409Conflict,
                    "Update blocked by constraints",
                    "The database may be referenced by other entities. Review constraints and try again.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Unexpected error updating DatabaseId={DatabaseId}; CorrelationId={CorrelationId}",
                    id, HttpContext.TraceIdentifier);

                return ProblemResult(StatusCodes.Status500InternalServerError,
                    "Unexpected error",
                    "An unexpected error occurred while updating the database.");
            }
        }
    }
}