using Dorc.ApiModel;
using Dorc.Core.Interfaces;
using Dorc.PersistentData.Sources.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
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
            IDatabasesPersistentSource databasesPersistentSource, ISecurityPrivilegesChecker securityPrivilegesChecker,
            IEnvironmentsPersistentSource environmentsPersistentSource)
        {
            _environmentsPersistentSource = environmentsPersistentSource;
            _securityPrivilegesChecker = securityPrivilegesChecker;
            _databasesPersistentSource = databasesPersistentSource;
        }

        /// <summary>
        ///     Return database details by database ID
        /// </summary>
        /// <param name="id">Database ID</param>
        /// <returns></returns>
        [SwaggerResponse(StatusCodes.Status200OK, Type = typeof(DatabaseApiModel))]
        [HttpGet]
        [Route("{id}")]
        public DatabaseApiModel? Get(int id)
        {
            return id <= 0 ? new DatabaseApiModel() : _databasesPersistentSource.GetDatabase(id);
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

            if (!string.IsNullOrEmpty(name) || !string.IsNullOrEmpty(server))
            {
                return _databasesPersistentSource.GetDatabases(name, server).ToList();
            }
            return new List<DatabaseApiModel>();
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
            catch (Exception exception)
            {
                return StatusCode(StatusCodes.Status400BadRequest,
                    exception.Message);
            }
        }

        /// <summary>
        ///     Delete Database entry
        /// </summary>
        [Produces("application/json")]
        [ProducesResponseType(typeof(ApiBoolResult), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(string), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(string), StatusCodes.Status403Forbidden)]
        [ProducesResponseType(typeof(string), StatusCodes.Status409Conflict)]
        [ProducesResponseType(typeof(string), StatusCodes.Status500InternalServerError)]
        [HttpDelete]
        public IActionResult Delete(int databaseId)
        {
            try
            {
                var environmentNamesAttachedToDatabase = (_databasesPersistentSource.GetEnvironmentNamesForDatabaseId(databaseId) ?? Enumerable.Empty<string>()).ToList();

                foreach (var environmentName in environmentNamesAttachedToDatabase)
                {
                    var environmentApiModel = _environmentsPersistentSource.GetEnvironment(environmentName);
                    if (environmentApiModel == null)
                        return BadRequest("Error while checking permissions; Environment missing in Deployment database.");

                    if (!_securityPrivilegesChecker.CanModifyEnvironment(User, environmentApiModel.EnvironmentName))
                        return StatusCode(StatusCodes.Status403Forbidden,
                            $"User doesn't have \"Write\" permission for this action on {environmentApiModel.EnvironmentName}!");
                }

                if (environmentNamesAttachedToDatabase.Any())
                    return StatusCode(StatusCodes.Status409Conflict,
                        $"Cannot delete: this database is used by environments: {string.Join(", ", environmentNamesAttachedToDatabase)}. Detach it first and retry.");

                var result = _databasesPersistentSource.DeleteDatabase(databaseId);
                if (!result)
                    return BadRequest("Delete failed.");

                return Ok(new ApiBoolResult { Result = true });
            }
            catch (DbUpdateException ex) when (ex.InnerException is SqlException sql && sql.Number == 547)
            {
                return StatusCode(StatusCodes.Status409Conflict,
                    "Cannot delete: the database is referenced by other records (foreign key constraint). Remove references and try again.");
            }
            catch (DbUpdateException ex)
            {
                return StatusCode(StatusCodes.Status500InternalServerError, string.IsNullOrWhiteSpace(ex.GetBaseException()?.Message) ?
                    "Delete failed due to a data persistence error. Please remove related references or try again later." : $"Delete failed: {ex.GetBaseException()?.Message.Trim()}");
            }
            catch (Exception ex)
            {
                return StatusCode(StatusCodes.Status500InternalServerError, string.IsNullOrWhiteSpace(ex.GetBaseException()?.Message) ?
                    "Unexpected error while deleting the database. Please try again or contact support." : $"Unexpected error: {ex.GetBaseException()?.Message.Trim()}");
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

                if (id != database.Id)
                    return BadRequest("'id' must be the same as database.Id");

                if (id <= 0)
                    return BadRequest("'id' cannot be 0");

                var databaseApiModel = _databasesPersistentSource.GetDatabase(database.Id);
                if (databaseApiModel != null && databaseApiModel.Id != id)
                    return BadRequest("Cannot set the server name to the same as one that already exists!");

                var result = _databasesPersistentSource.UpdateDatabase(id, database, User);
                return result != null
                    ? Ok(result)
                    : NotFound("Error updating entry");
            }
            catch (Exception exception)
            {
                return StatusCode(StatusCodes.Status400BadRequest,
                    exception.Message);
            }
        }
    }
}