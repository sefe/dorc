using Dorc.ApiModel;
using Dorc.Core.AzureStorageAccount;
using Dorc.Core.Interfaces;
using Dorc.Core.Terraform;
using Dorc.PersistentData.Sources.Interfaces;
using Dorc.PersistentData;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace Dorc.Api.Controllers;

public partial class TerraformController
{
    [HttpPost("plan/{deploymentResultId}/recover")]
    [SwaggerResponse(StatusCodes.Status200OK, Type = typeof(TerraformDeploymentOutcomeApiModel))]
    [SwaggerResponse(StatusCodes.Status403Forbidden)]
    [SwaggerResponse(StatusCodes.Status409Conflict)]
    public IActionResult RecoverPostApply(int deploymentResultId, [FromQuery] long expectedRevision,
        [FromServices] IRolePrivilegesChecker roles,
        [FromServices] ICloudResourcesPersistentSource cloudResources,
        [FromServices] ICloudResourceAuditPersistentSource cloudAudit,
        [FromServices] IDatabasesPersistentSource databases,
        [FromQuery] bool recoverInventory = true, [FromQuery] bool recoverArchive = true)
    {
        if (!recoverInventory && !recoverArchive) return BadRequest("Choose inventory registration or audit-copy recovery.");
        var authorization = AuthorizeForEnvironment(deploymentResultId, allowHistory: true);
        if (!authorization.Succeeded) return authorization.Failure!;
        // Recovery can update shared definitions. Admin-only avoids weakening the
        // ordinary per-definition, all-environment mutation policy.
        if (!roles.IsAdmin(User)) return Forbid();
        var receipt = _requestsPersistentSource.GetTerraformDeploymentReceipt(deploymentResultId);
        var approval = _requestsPersistentSource.GetTerraformPlanApproval(deploymentResultId);
        if (receipt == null || approval == null || receipt.DeploymentRequestId != approval.DeploymentRequestId ||
            approval.DeploymentRequestId != authorization.Request!.Id ||
            !string.Equals(receipt.SourceSha256, approval.SourceSha256, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(receipt.PlanSha256, approval.PlanSha256, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(receipt.PlanContentSha256, approval.PlanContentSha256, StringComparison.OrdinalIgnoreCase))
            return Conflict("A matching retained receipt and approval are required. Legacy or incomplete records cannot be recovered automatically.");
        if (!_apiSecurityService.CanModifyEnvironment(User, receipt.EnvironmentName)) return Forbid();
        var originalEnvironment = _environmentsPersistentSource.GetEnvironment(receipt.EnvironmentName);
        if (originalEnvironment == null || originalEnvironment.EnvironmentId != receipt.EnvironmentId)
            return Conflict("The original environment identity is unavailable. Review registration manually.");
        var username = _claimsPrincipalReader.GetUserFullDomainName(User);
        if (string.IsNullOrWhiteSpace(username)) return Forbid();
        try
        {
            var recovered = _requestsPersistentSource.RecoverTerraformPostApply(deploymentResultId, expectedRevision, outcome =>
            {
                if (outcome.AppliedAt == null) throw new InvalidOperationException("The successful apply time was not retained.");
                if (recoverInventory && outcome.InventoryOutcome != "Succeeded")
                {
                    if (string.IsNullOrWhiteSpace(outcome.InventoryArtifactSha256))
                        throw new InvalidOperationException("Inventory recovery material is unavailable. Review registration manually; this action will not re-run apply or export current state.");
                    var json = _azureStorageAccountWorker.LoadVerifiedTextFromBlobs(
                        $"{deploymentResultId}.applied-resources.json", outcome.InventoryArtifactSha256, 4 * 1024 * 1024);
                    var registrar = new TerraformCloudResourceRegistrar(_log, cloudResources, cloudAudit,
                        _environmentsPersistentSource, databases);
                    var registration = registrar.RegisterSnapshot(json, receipt.EnvironmentName, username, deploymentResultId);
                    outcome.InventoryOutcome = registration.Status;
                    outcome.RegisteredCount = registration.RegisteredCount;
                    outcome.FailedCount = registration.FailedCount;
                    outcome.InventoryWarningsJson = System.Text.Json.JsonSerializer.Serialize(registration.Warnings);
                    if (registration.EnvironmentId > 0 && registration.EnvironmentId == receipt.EnvironmentId)
                        _requestsPersistentSource.RecordTerraformInventory(receipt.ComponentId, registration.EnvironmentId,
                            deploymentResultId, outcome.AppliedAt.Value, registration.Observed, registration.Status == "Succeeded");
                }
                if (recoverArchive && outcome.ArchiveOutcome != "Succeeded")
                {
                    _azureStorageAccountWorker.CopyVerifiedBlob(
                        deploymentResultId.CreateTerraformPlanBlobName() + ".source.zip",
                        $"{deploymentResultId}.terraform-source.zip", approval.SourceSha256);
                    outcome.ArchiveOutcome = "Succeeded";
                }
            }, touchesInventory: recoverInventory);
            if ((recoverInventory && recovered.InventoryOutcome != "Succeeded") ||
                (recoverArchive && recovered.ArchiveOutcome != "Succeeded"))
                return Conflict("Recovery remains incomplete. Reload the receipt for counts; review missing or ambiguous inventory manually. Terraform apply was not re-run.");
            return Ok(recovered.ToApiModel());
        }
        catch (InvalidOperationException ex) { return Conflict(ex.Message); }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Post-apply recovery was not completed for result {DeploymentResultId}.", deploymentResultId);
            return Conflict("Recovery material is missing, changed or inaccessible. Restore trusted retained artifacts or review registration manually. Terraform apply was not re-run.");
        }
    }

    [HttpGet("plan/{deploymentResultId}/inventory")]
    [SwaggerResponse(StatusCodes.Status200OK, Type = typeof(IReadOnlyList<TerraformInventoryProvenanceApiModel>))]
    public IActionResult GetInventoryProvenance(int deploymentResultId)
    {
        var authorization = AuthorizeForEnvironment(deploymentResultId, allowHistory: true);
        if (!authorization.Succeeded) return authorization.Failure!;
        var receipt = _requestsPersistentSource.GetTerraformDeploymentReceipt(deploymentResultId);
        if (receipt == null) return NotFound("Inventory provenance was not recorded for this plan.");
        if (!_apiSecurityService.CanModifyEnvironment(User, receipt.EnvironmentName)) return Forbid();
        var environment = _environmentsPersistentSource.GetEnvironment(receipt.EnvironmentName);
        if (environment == null || environment.EnvironmentId != receipt.EnvironmentId)
            return NotFound("The original environment identity is unavailable.");
        return Ok(_requestsPersistentSource.GetTerraformInventory(receipt.ComponentId, environment.EnvironmentId));
    }

    [HttpPost("plan/{deploymentResultId}/inventory/{cloudResourceId}/review")]
    [SwaggerResponse(StatusCodes.Status200OK)]
    [SwaggerResponse(StatusCodes.Status409Conflict)]
    public IActionResult ReviewInventoryProvenance(int deploymentResultId, int cloudResourceId, [FromQuery] long expectedRevision)
    {
        var authorization = AuthorizeForEnvironment(deploymentResultId, allowHistory: true);
        if (!authorization.Succeeded) return authorization.Failure!;
        var receipt = _requestsPersistentSource.GetTerraformDeploymentReceipt(deploymentResultId);
        if (receipt == null) return NotFound();
        if (!_apiSecurityService.CanModifyEnvironment(User, receipt.EnvironmentName) ||
            !_apiSecurityService.IsProjectOwnerOrAdmin(User, receipt.ProjectName)) return Forbid();
        var environment = _environmentsPersistentSource.GetEnvironment(receipt.EnvironmentName);
        if (environment == null || environment.EnvironmentId != receipt.EnvironmentId) return NotFound();
        return _requestsPersistentSource.ReviewTerraformInventory(receipt.ComponentId, environment.EnvironmentId,
            cloudResourceId, expectedRevision) ? Ok() : Conflict("Inventory changed or is no longer marked missing. Reload the inventory.");
    }
}
