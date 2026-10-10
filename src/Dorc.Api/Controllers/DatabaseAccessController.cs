using Dorc.Api.Interfaces;
using Dorc.ApiModel;
using Dorc.Core.DatabaseAccess;
using Dorc.Core.Interfaces;
using Dorc.PersistentData.Contexts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Dorc.Api.Controllers;

[Authorize]
[ApiController]
[Route("DatabaseAccess/{envId:int}/{databaseId:int}")]
public sealed class DatabaseAccessController(
    DatabaseAccessService service, DatabaseAccessDirectory directory, DatabaseAccessProviders providers, IConfiguration configuration,
    IEnvironmentMapper mapper, ISecurityPrivilegesChecker privileges, IDeploymentContextFactory factory,
    ILogger<DatabaseAccessController> logger) : ControllerBase
{
    private bool Enabled(int id) => configuration.GetValue<bool>("DatabaseAccess:Enabled")
        && (configuration.GetSection("DatabaseAccess:DatabaseIds").Get<int[]>() ?? []).Contains(id);
    private bool ExecutionEnabled => configuration.GetValue<bool>("DatabaseAccess:ExecutionEnabled");

    [HttpGet("status")]
    public ActionResult<DatabaseAccessStatus> Status(int envId, int databaseId) =>
        Execute(envId, databaseId, false, false, () => new DatabaseAccessStatus
        {
            Enabled = Enabled(databaseId), ExecutionEnabled = Enabled(databaseId) && ExecutionEnabled,
            Providers = providers.Names,
            Limitations = ["Existing server logins only; no password/server-login provisioning.", "Protected roles and system databases cannot be managed.", "AD-backed SQL Server logins require Windows SID references."]
        }, requireEnabled: false);

    [HttpGet]
    public ActionResult<DatabaseAccessState> Get(int envId, int databaseId) =>
        Execute(envId, databaseId, false, false, () => service.Get(databaseId));

    [HttpPut]
    public ActionResult<DatabaseAccessState> Save(int envId, int databaseId, [FromBody] DatabaseAccessState state) =>
        Execute(envId, databaseId, true, false, () => service.Save(databaseId, state, Actor));

    [HttpPost("preview")]
    public ActionResult<DatabaseAccessPreview> Preview(int envId, int databaseId) =>
        Execute(envId, databaseId, true, false, () => service.Preview(databaseId));

    [HttpPost("apply")]
    public ActionResult<DatabaseAccessPreview> Apply(int envId, int databaseId, [FromBody] DatabaseAccessConfirmation confirmation) =>
        Execute(envId, databaseId, true, true, () => service.Apply(databaseId, confirmation.Token, Actor));

    [HttpGet("directory")]
    public ActionResult<List<DatabaseAccessDirectoryIdentity>> SearchDirectory(int envId, int databaseId, [FromQuery] string search) =>
        Execute(envId, databaseId, false, false, () => directory.Search(search));

    [HttpGet("directory/resolve")]
    public ActionResult<DatabaseAccessDirectoryIdentity> ResolveDirectory(int envId, int databaseId, [FromQuery] string id) =>
        Execute(envId, databaseId, false, false, () => directory.Resolve(id));

    [HttpPost("import/preview")]
    public ActionResult<DatabaseAccessImportPreview> PreviewImport(int envId, int databaseId, [FromBody] DatabaseAccessImportRequest request) =>
        Execute(envId, databaseId, true, false, () => service.Import(databaseId, request, false, Actor));

    [HttpPost("import")]
    public ActionResult<DatabaseAccessImportPreview> Import(int envId, int databaseId, [FromBody] DatabaseAccessImportRequest request) =>
        Execute(envId, databaseId, true, false, () => service.Import(databaseId, request, true, Actor));

    [HttpGet("audit")]
    public ActionResult<List<DatabaseAccessAuditApiModel>> Audit(int envId, int databaseId) =>
        Execute(envId, databaseId, false, false, () => service.Audit(databaseId));

    private string Actor => User.Identity?.Name
        ?? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
        ?? User.FindFirst("sub")?.Value
        ?? throw new ArgumentException("The authenticated identity has no audit identifier.");

    private ActionResult<T> Execute<T>(int envId, int databaseId, bool modify, bool execute, Func<T> operation, bool requireEnabled = true)
    {
        try
        {
            var environment = mapper.GetEnvironmentByDatabase(envId, databaseId, User);
            if (environment == null) return NotFound();
            if (!privileges.CanModifyEnvironment(User, environment.EnvironmentName)) return Forbid();
            if (requireEnabled && !Enabled(databaseId)) return NotFound("Database access management is not enabled for this database.");
            if (modify)
            {
                // A database can be shared: permission on one attachment must not grant changes to all others.
                using var context = factory.GetContext();
                var environments = context.Databases.Where(d => d.Id == databaseId).SelectMany(d => d.Environments).Select(e => e.Name).ToList();
                if (environments.Count == 0 || environments.Any(name => !privileges.CanModifyEnvironment(User, name)))
                    return Forbid();
            }
            if (execute && !ExecutionEnabled) return StatusCode(403, new ProblemDetails { Title = "Target execution is disabled." });
            return Ok(operation());
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
