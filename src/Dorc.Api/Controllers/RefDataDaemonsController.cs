using System.Text.Json;
using Dorc.ApiModel;
using Dorc.PersistentData;
using Dorc.PersistentData.Exceptions;
using Dorc.PersistentData.Model;
using Dorc.PersistentData.Sources.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Swashbuckle.AspNetCore.Annotations;

namespace Dorc.Api.Controllers
{
    [Authorize]
    [ApiController]
    [Route("[controller]")]
    public sealed class RefDataDaemonsController : ControllerBase
    {
        private static readonly JsonSerializerOptions _auditJsonOptions = new() { WriteIndented = true };

        private readonly IDaemonsPersistentSource _daemonsPersistentSource;
        private readonly IDaemonAuditPersistentSource _daemonAuditPersistentSource;
        private readonly IRolePrivilegesChecker _rolePrivilegesChecker;
        private readonly IClaimsPrincipalReader _claimsPrincipalReader;

        public RefDataDaemonsController(
            IDaemonsPersistentSource daemonsPersistentSource,
            IDaemonAuditPersistentSource daemonAuditPersistentSource,
            IRolePrivilegesChecker rolePrivilegesChecker,
            IClaimsPrincipalReader claimsPrincipalReader)
        {
            _daemonsPersistentSource = daemonsPersistentSource;
            _daemonAuditPersistentSource = daemonAuditPersistentSource;
            _rolePrivilegesChecker = rolePrivilegesChecker;
            _claimsPrincipalReader = claimsPrincipalReader;
        }

        /// <summary>
        /// Get all daemons definitions
        /// </summary>
        [SwaggerResponse(StatusCodes.Status200OK, Type = typeof(List<DaemonApiModel>))]
        [HttpGet]
        public IActionResult Get()
        {
            var output = _daemonsPersistentSource
                .GetDaemons()
                .Select(service =>
                    new DaemonApiModel
                    {
                        Id = service.Id,
                        AccountName = service.AccountName,
                        DisplayName = service.DisplayName,
                        Name = service.Name,
                        ServiceType = service.ServiceType
                    }
                )
                .ToList();

            return Ok(output);
        }

        /// <summary>
        /// Create new daemon definition
        /// </summary>
        [HttpPost]
        [SwaggerResponse(StatusCodes.Status200OK, Type = typeof(DaemonApiModel))]
        [SwaggerResponse(StatusCodes.Status403Forbidden, Type = typeof(string))]
        [SwaggerResponse(StatusCodes.Status409Conflict, Type = typeof(string))]
        public IActionResult Post([FromBody] DaemonApiModel model)
        {
            if (!_rolePrivilegesChecker.IsPowerUser(User) && !_rolePrivilegesChecker.IsAdmin(User))
            {
                return StatusCode(StatusCodes.Status403Forbidden,
                    "Daemons can only be created by PowerUsers or Admins!");
            }

            try
            {
                var created = _daemonsPersistentSource.Add(model);

                _daemonAuditPersistentSource.InsertDaemonAudit(
                    _claimsPrincipalReader.GetUserFullDomainName(User),
                    ActionType.Create,
                    created.Id,
                    fromValue: null,
                    toValue: JsonSerializer.Serialize(created, _auditJsonOptions));

                return Ok(created);
            }
            catch (DaemonDuplicateException ex)
            {
                return Conflict(ex.Message);
            }
            catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
            {
                return Conflict("A daemon with the same Name or DisplayName already exists");
            }
        }

        private static bool IsUniqueConstraintViolation(DbUpdateException ex)
        {
            return ex.InnerException is SqlException sql
                   && (sql.Number == 2601 || sql.Number == 2627);
        }

        /// <summary>
        /// Edit daemon definition
        /// </summary>
        [HttpPut]
        [SwaggerResponse(StatusCodes.Status200OK, Type = typeof(DaemonApiModel))]
        [SwaggerResponse(StatusCodes.Status400BadRequest, Type = typeof(string))]
        [SwaggerResponse(StatusCodes.Status403Forbidden, Type = typeof(string))]
        [SwaggerResponse(StatusCodes.Status404NotFound, Type = typeof(string))]
        [SwaggerResponse(StatusCodes.Status409Conflict, Type = typeof(string))]
        public IActionResult Put(int id, [FromBody] DaemonApiModel model)
        {
            if (!_rolePrivilegesChecker.IsPowerUser(User) && !_rolePrivilegesChecker.IsAdmin(User))
            {
                return StatusCode(StatusCodes.Status403Forbidden,
                    "Daemons can only be edited by PowerUsers or Admins!");
            }

            if (id != model.Id)
            {
                return BadRequest($"Route id ({id}) does not match body Id ({model.Id}).");
            }

            // Resolve existence up-front so we can 404 before calling Update (Update's
            // interface contract returns non-nullable DaemonApiModel — we shouldn't rely on
            // a null return to signal "not found").
            var before = _daemonsPersistentSource.GetDaemonById(model.Id);
            if (before == null)
            {
                return NotFound($"Unable to find daemon {model.Id}");
            }

            var fromJson = JsonSerializer.Serialize(before, _auditJsonOptions);

            DaemonApiModel updated;
            try
            {
                updated = _daemonsPersistentSource.Update(model);
            }
            catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
            {
                // Rename collides with UQ_Daemon_Name / UQ_Daemon_DisplayName — surface
                // the same 409 the Post path does so the UI sees a readable error, not a 500.
                return Conflict("A daemon with the same Name or DisplayName already exists");
            }

            var toJson = JsonSerializer.Serialize(updated, _auditJsonOptions);
            _daemonAuditPersistentSource.InsertDaemonAudit(
                _claimsPrincipalReader.GetUserFullDomainName(User),
                ActionType.Update,
                updated.Id,
                fromValue: fromJson,
                toValue: toJson);

            return Ok(updated);
        }

        /// <summary>
        /// Delete daemon definition
        /// </summary>
        [HttpDelete]
        [SwaggerResponse(StatusCodes.Status200OK)]
        [SwaggerResponse(StatusCodes.Status403Forbidden, Type = typeof(string))]
        [SwaggerResponse(StatusCodes.Status404NotFound, Type = typeof(string))]
        public IActionResult Delete(int id)
        {
            if (!_rolePrivilegesChecker.IsAdmin(User))
            {
                return StatusCode(StatusCodes.Status403Forbidden,
                    "Daemons can only be deleted by Admins!");
            }

            var before = _daemonsPersistentSource.GetDaemonById(id);

            if (!_daemonsPersistentSource.Delete(id))
            {
                return NotFound($"Unable to find {id}");
            }

            _daemonAuditPersistentSource.InsertDaemonAudit(
                _claimsPrincipalReader.GetUserFullDomainName(User),
                ActionType.Delete,
                id,
                fromValue: before != null ? JsonSerializer.Serialize(before, _auditJsonOptions) : null,
                toValue: null);

            return Ok();
        }
    }
}
