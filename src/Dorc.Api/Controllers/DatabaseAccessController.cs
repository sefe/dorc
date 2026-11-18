using System.ComponentModel.DataAnnotations;
using Dorc.Api.Interfaces;
using Dorc.ApiModel;
using Dorc.Core.DatabaseAccess;
using Dorc.Core.Interfaces;
using Dorc.PersistentData;
using Dorc.PersistentData.Contexts;
using Dorc.PersistentData.Sources.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Swashbuckle.AspNetCore.Annotations;

namespace Dorc.Api.Controllers;

[Authorize]
[ApiController]
[Route("DatabaseAccess/{envId:int}/{databaseId:int}")]
public sealed class DatabaseAccessController(
    DatabaseAccessReconciler reconciler, DatabaseAccessDirectory directory,
    DatabaseAccessProviders providers, IConfiguration configuration,
    IEnvironmentMapper mapper, ISecurityPrivilegesChecker privileges,
    IDeploymentContextFactory factory, ILogger<DatabaseAccessController> logger, IClaimsPrincipalReader claimsReader) : ControllerBase
{
    private bool Enabled => configuration.GetValue("DatabaseAccess:Enabled", true);

    [SwaggerOperation(OperationId = "databaseAccessStatus")]
    [HttpGet("status")]
    public ActionResult<DatabaseAccessStatus> Status(int envId, int databaseId) =>
        Execute(envId, databaseId, false, () => new DatabaseAccessStatus
        {
            Enabled = Enabled,
            ExecutionEnabled = Enabled,
            CanApproveDbOwner = Enabled && AttachedEnvironments(databaseId) is { Count: > 0 } attached
                && attached.All(name => privileges.IsEnvironmentOwnerOrAdmin(User, name)),
            Providers = providers.Names,
            Limitations =
            [
                "Existing server logins only; no password/server-login provisioning.",
                "db_owner memberships require an owner or administrator of every attached environment; protected role definitions and other protected roles remain blocked.",
                "AD-backed SQL Server logins require Windows SID references."
            ]
        }, requireEnabled: false);

    [SwaggerOperation(OperationId = "databaseAccessGet")]
    [HttpGet]
    public ActionResult<DatabaseAccessState> Get(int envId, int databaseId) =>
        Execute(envId, databaseId, false, () => reconciler.Get(databaseId));

    [SwaggerOperation(OperationId = "databaseAccessSave")]
    [HttpPut]
    public ActionResult<DatabaseAccessState> Save(int envId, int databaseId, [FromBody, Required] DatabaseAccessState state) =>
        Execute(envId, databaseId, true, () => reconciler.Save(databaseId, state, Actor, canApproveDbOwner: true));

    [SwaggerOperation(OperationId = "databaseAccessPreview")]
    [HttpPost("preview")]
    public ActionResult<DatabaseAccessPreview> Preview(int envId, int databaseId) =>
        Execute(envId, databaseId, true, () => reconciler.Preview(databaseId));

    [SwaggerOperation(OperationId = "databaseAccessApply")]
    [HttpPost("apply")]
    public ActionResult<DatabaseAccessPreview> Apply(int envId, int databaseId, [FromBody, Required] DatabaseAccessConfirmation confirmation) =>
        Execute(envId, databaseId, true, () => reconciler.Apply(databaseId, confirmation.Token, Actor));

    [SwaggerOperation(OperationId = "databaseAccessSqlLogins")]
    [HttpGet("logins")]
    public ActionResult<List<string>> SqlLogins(int envId, int databaseId) =>
        Execute(envId, databaseId, false, () => reconciler.SqlLogins(databaseId));

    [SwaggerOperation(OperationId = "databaseAccessDirectory")]
    [HttpGet("directory")]
    public ActionResult<List<DatabaseAccessDirectoryIdentity>> SearchDirectory(int envId, int databaseId, [FromQuery, Required] string search) =>
        Execute(envId, databaseId, false, () => directory.Search(search));

    [SwaggerOperation(OperationId = "databaseAccessResolve")]
    [HttpGet("directory/resolve")]
    public ActionResult<DatabaseAccessDirectoryIdentity> ResolveDirectory(int envId, int databaseId, [FromQuery, Required] string id) =>
        Execute(envId, databaseId, false, () => directory.Resolve(id));

    [SwaggerOperation(OperationId = "databaseAccessAudit")]
    [HttpGet("audit")]
    public ActionResult<List<DatabaseAccessAuditApiModel>> Audit(int envId, int databaseId) =>
        Execute(envId, databaseId, false, () => reconciler.Audit(databaseId));

    private string Actor
    {
        get
        {
            var actor = claimsReader.GetUserSafeIdentifier(User);
            return !string.IsNullOrWhiteSpace(actor) ? actor
                : throw new ArgumentException("The authenticated identity has no audit identifier.");
        }
    }

    private List<string> AttachedEnvironments(int databaseId)
    {
        using var context = factory.GetContext();
        return context.Databases.Where(d => d.Id == databaseId).SelectMany(d => d.Environments).Select(e => e.Name).ToList();
    }

    private ActionResult<T> Execute<T>(int envId, int databaseId, bool modify, Func<T> operation,
        bool requireEnabled = true) =>
        HandleErrors<T>(databaseId, () =>
        {
            var environment = mapper.GetEnvironmentByDatabase(envId, databaseId, User);
            if (environment == null) return NotFound();
            if (!privileges.IsEnvironmentOwnerOrAdmin(User, environment.EnvironmentName)) return Forbid();
            if (requireEnabled && !Enabled) return NotFound("Database access management is disabled.");
            if (modify)
            {
                // A database can be shared: permission on one attachment must not grant changes to all others.
                var attached = AttachedEnvironments(databaseId);
                if (attached.Count == 0 || attached.Any(name => !privileges.IsEnvironmentOwnerOrAdmin(User, name)))
                    return Forbid();
            }
            return Ok(operation());
        });

    private ActionResult<T> HandleErrors<T>(int databaseId, Func<ActionResult<T>> action)
    {
        try
        {
            return action();
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or DatabaseAccessConflictException or DbUpdateConcurrencyException)
        {
            logger.LogWarning(e, "Database access request rejected for database {DatabaseId}", databaseId);
            return Problem(e.Message, statusCode: e is NotSupportedException ? 422 : e is ArgumentException ? 400 : 409);
        }
        catch (Exception e) when (e is SqlException or DbUpdateException or System.DirectoryServices.DirectoryServicesCOMException)
        {
            logger.LogError(e, "Database access dependency failed for database {DatabaseId}", databaseId);
            return Problem("Database or directory operation failed. No success is implied; inspect the audit and server logs before retrying.", statusCode: 503);
        }
    }
}
