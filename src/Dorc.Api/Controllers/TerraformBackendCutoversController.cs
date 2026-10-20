using Dorc.ApiModel;
using Dorc.Core.Interfaces;
using Dorc.PersistentData;
using Dorc.PersistentData.Sources;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace Dorc.Api.Controllers;

[Authorize]
[ApiController]
[Route("Projects/{projectId:int}/terraform-state-backend-cutovers")]
public sealed class TerraformBackendCutoversController(DeploymentDefinitions definitions,
    IRolePrivilegesChecker roles, ISecurityPrivilegesChecker security, IClaimsPrincipalReader claims) : ControllerBase
{
    [HttpPost]
    [SwaggerResponse(200, Type = typeof(RefDataApiModel))]
    [SwaggerResponse(400)]
    [SwaggerResponse(403)]
    [SwaggerResponse(409)]
    [SwaggerResponse(428)]
    public IActionResult Post(int projectId, TerraformBackendCutoverApiModel input)
    {
        if (!roles.IsAdmin(User)) return Forbid();
        var actor = claims.GetUserFullDomainName(User);
        if (string.IsNullOrWhiteSpace(actor)) return Forbid();
        try
        {
            return Ok(definitions.CutoverEmptyTerraformBackend(projectId, input, actor,
                id => security.CanModifyProject(User, id),
                name => security.CanModifyEnvironment(User, name)));
        }
        catch (DefinitionPreconditionException e) { return StatusCode(e.StatusCode, e.Message); }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (ArgumentException e) { return BadRequest(e.Message); }
        catch (InvalidOperationException e) { return Conflict(e.Message); }
    }
}
