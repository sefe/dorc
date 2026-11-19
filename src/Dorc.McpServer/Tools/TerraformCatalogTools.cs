using System.ComponentModel;
using Dorc.ApiModel;
using Dorc.Terraform.Catalog;
using ModelContextProtocol.Server;

namespace Dorc.McpServer.Tools;

[McpServerToolType]
public sealed class TerraformCatalogTools
{
    private readonly IDorcApiGateway _apiGateway;
    private readonly ProductionEnvironmentGuard _guard;

    public TerraformCatalogTools(IDorcApiGateway apiGateway, ProductionEnvironmentGuard guard)
    {
        _apiGateway = apiGateway ?? throw new ArgumentNullException(nameof(apiGateway));
        _guard = guard ?? throw new ArgumentNullException(nameof(guard));
    }

    [McpServerTool(Name = "dorc_list_terraform_templates", ReadOnly = true)]
    [Description("Lists all Terraform templates available in the DOrc catalog.")]
    public Task<McpToolOutcome<List<TerraformTemplateManifest>>> ListTerraformTemplatesAsync(
        CancellationToken cancellationToken = default)
    {
        return _apiGateway.GetAsync<List<TerraformTemplateManifest>>(
            "Terraform/templates",
            cancellationToken);
    }

    [McpServerTool(Name = "dorc_get_terraform_template", ReadOnly = true)]
    [Description("Gets details of a Terraform template by name and optional version.")]
    public Task<McpToolOutcome<TerraformTemplateManifest>> GetTerraformTemplateAsync(
        [Description("Terraform template name.")] string name,
        [Description("Optional Terraform template version.")] string? version = null,
        CancellationToken cancellationToken = default)
    {
        var path = string.IsNullOrWhiteSpace(version)
            ? $"Terraform/templates/{Uri.EscapeDataString(name)}"
            : $"Terraform/templates/{Uri.EscapeDataString(name)}/{Uri.EscapeDataString(version.Trim())}";

        return _apiGateway.GetAsync<TerraformTemplateManifest>(path, cancellationToken);
    }

    [McpServerTool(Name = "dorc_resolve_terraform_template", ReadOnly = true)]
    [Description("Resolves variables for a Terraform template version against a project, environment, and optional component ID.")]
    public Task<McpToolOutcome<TerraformTemplateResolutionApiModel>> ResolveTerraformTemplateAsync(
        [Description("Terraform template name.")] string name,
        [Description("Terraform template version.")] string version,
        [Description("Numeric project ID.")] int projectId,
        [Description("Target environment name.")] string environmentName,
        [Description("Optional numeric component ID.")] int? componentId = null,
        CancellationToken cancellationToken = default)
    {
        var path = $"Terraform/templates/{Uri.EscapeDataString(name)}/{Uri.EscapeDataString(version)}/resolution?projectId={projectId}&environmentName={Uri.EscapeDataString(environmentName)}";
        if (componentId.HasValue)
        {
            path += $"&componentId={componentId.Value}";
        }

        return _apiGateway.GetAsync<TerraformTemplateResolutionApiModel>(path, cancellationToken);
    }

    [McpServerTool(Name = "dorc_get_terraform_plan", ReadOnly = true)]
    [Description("Gets the Terraform execution plan (including PlanContentSha256) for a deployment result ID.")]
    public Task<McpToolOutcome<TerraformPlanApiModel>> GetTerraformPlanAsync(
        [Description("Component deployment result ID.")] int deploymentResultId,
        CancellationToken cancellationToken = default)
    {
        return _apiGateway.GetAsync<TerraformPlanApiModel>(
            $"Terraform/plan/{deploymentResultId}",
            cancellationToken);
    }

    [McpServerTool(Name = "dorc_get_terraform_plan_inventory", ReadOnly = true)]
    [Description("Gets the Terraform resource inventory provenance for a deployment result ID.")]
    public Task<McpToolOutcome<List<TerraformInventoryProvenanceApiModel>>> GetTerraformPlanInventoryAsync(
        [Description("Component deployment result ID.")] int deploymentResultId,
        CancellationToken cancellationToken = default)
    {
        return _apiGateway.GetAsync<List<TerraformInventoryProvenanceApiModel>>(
            $"Terraform/plan/{deploymentResultId}/inventory",
            cancellationToken);
    }

    [McpServerTool(Name = "dorc_confirm_terraform_plan", Destructive = true)]
    [Description("Confirms a waiting Terraform plan for execution using its optimistic concurrency SHA-256 hash. Subject to read-only and production guardrails.")]
    public async Task<McpToolOutcome<string>> ConfirmTerraformPlanAsync(
        [Description("Component deployment result ID.")] int deploymentResultId,
        [Description("Expected SHA-256 hash of the plan content (from dorc_get_terraform_plan).")] string planContentSha256,
        [Description("Target environment name for production guardrail verification.")] string environmentName,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(planContentSha256))
        {
            return McpToolOutcome.Failure<string>("planContentSha256 is required to confirm a Terraform plan.");
        }

        var guardError = await _guard.CheckTerraformPlanMutationAllowedAsync(
            "dorc_confirm_terraform_plan",
            deploymentResultId,
            environmentName,
            _apiGateway,
            cancellationToken).ConfigureAwait(false);

        if (guardError is not null)
        {
            return McpToolOutcome.Failure<string>(guardError);
        }

        return await _apiGateway.PostWithoutResponseBodyAsync<object?>(
            $"Terraform/plan/{deploymentResultId}/confirm?planContentSha256={Uri.EscapeDataString(planContentSha256.Trim())}",
            null,
            cancellationToken).ConfigureAwait(false);
    }

    [McpServerTool(Name = "dorc_decline_terraform_plan", Destructive = true)]
    [Description("Declines a waiting Terraform plan for a deployment result ID. Subject to read-only and production guardrails.")]
    public async Task<McpToolOutcome<string>> DeclineTerraformPlanAsync(
        [Description("Component deployment result ID.")] int deploymentResultId,
        [Description("Target environment name for production guardrail verification.")] string environmentName,
        CancellationToken cancellationToken = default)
    {
        var guardError = await _guard.CheckTerraformPlanMutationAllowedAsync(
            "dorc_decline_terraform_plan",
            deploymentResultId,
            environmentName,
            _apiGateway,
            cancellationToken).ConfigureAwait(false);

        if (guardError is not null)
        {
            return McpToolOutcome.Failure<string>(guardError);
        }

        return await _apiGateway.PostWithoutResponseBodyAsync<object?>(
            $"Terraform/plan/{deploymentResultId}/decline",
            null,
            cancellationToken).ConfigureAwait(false);
    }

    [McpServerTool(Name = "dorc_recover_terraform_post_apply", Destructive = true)]
    [Description("Triggers post-apply state and inventory recovery for a Terraform deployment result ID. Subject to read-only and production guardrails.")]
    public async Task<McpToolOutcome<TerraformDeploymentOutcomeApiModel>> RecoverTerraformPostApplyAsync(
        [Description("Component deployment result ID.")] int deploymentResultId,
        [Description("Expected revision of the Terraform deployment outcome for optimistic concurrency.")] long expectedRevision,
        [Description("Target environment name for production guardrail verification.")] string environmentName,
        [Description("Whether to recover cloud resource inventory registration (default true).")] bool recoverInventory = true,
        [Description("Whether to recover the Terraform source archive copy (default true).")] bool recoverArchive = true,
        CancellationToken cancellationToken = default)
    {
        if (!recoverInventory && !recoverArchive)
        {
            return McpToolOutcome.Failure<TerraformDeploymentOutcomeApiModel>(
                "At least one of 'recoverInventory' or 'recoverArchive' must be true.");
        }

        var guardError = await _guard.CheckTerraformPlanMutationAllowedAsync(
            "dorc_recover_terraform_post_apply",
            deploymentResultId,
            environmentName,
            _apiGateway,
            cancellationToken).ConfigureAwait(false);

        if (guardError is not null)
        {
            return McpToolOutcome.Failure<TerraformDeploymentOutcomeApiModel>(guardError);
        }

        var invFlag = recoverInventory ? "true" : "false";
        var archFlag = recoverArchive ? "true" : "false";

        return await _apiGateway.PostAsync<object?, TerraformDeploymentOutcomeApiModel>(
            $"Terraform/plan/{deploymentResultId}/recover?expectedRevision={expectedRevision}&recoverInventory={invFlag}&recoverArchive={archFlag}",
            null,
            cancellationToken).ConfigureAwait(false);
    }

    [McpServerTool(Name = "dorc_review_terraform_inventory", Destructive = true)]
    [Description("Reviews a missing Terraform inventory provenance item for a deployment result ID and cloud resource ID. Subject to read-only and production guardrails.")]
    public async Task<McpToolOutcome<string>> ReviewTerraformInventoryAsync(
        [Description("Component deployment result ID.")] int deploymentResultId,
        [Description("Numeric cloud resource ID.")] int cloudResourceId,
        [Description("Expected revision of the inventory provenance entry.")] long expectedRevision,
        [Description("Target environment name for production guardrail verification.")] string environmentName,
        CancellationToken cancellationToken = default)
    {
        var guardError = await _guard.CheckTerraformPlanMutationAllowedAsync(
            "dorc_review_terraform_inventory",
            deploymentResultId,
            environmentName,
            _apiGateway,
            cancellationToken).ConfigureAwait(false);

        if (guardError is not null)
        {
            return McpToolOutcome.Failure<string>(guardError);
        }

        return await _apiGateway.PostWithoutResponseBodyAsync<object?>(
            $"Terraform/plan/{deploymentResultId}/inventory/{cloudResourceId}/review?expectedRevision={expectedRevision}",
            null,
            cancellationToken).ConfigureAwait(false);
    }

    [McpServerTool(Name = "dorc_preview_terraform_backend_cutover", ReadOnly = true)]
    [Description("Previews Terraform state backend cutover eligibility and blockers for a project ID and environment ID.")]
    public Task<McpToolOutcome<TerraformBackendCutoverPreviewApiModel>> PreviewTerraformBackendCutoverAsync(
        [Description("Numeric project ID.")] int projectId,
        [Description("Numeric environment ID.")] int environmentId,
        CancellationToken cancellationToken = default)
    {
        return _apiGateway.GetAsync<TerraformBackendCutoverPreviewApiModel>(
            $"Projects/{projectId}/terraform-state-backend-cutovers/preview?environmentId={environmentId}",
            cancellationToken);
    }

    [McpServerTool(Name = "dorc_execute_terraform_backend_cutover", Destructive = true)]
    [Description("Executes an empty-container Terraform state backend cutover for a project and environment. Subject to read-only and production guardrails.")]
    public async Task<McpToolOutcome<RefDataApiModel>> ExecuteTerraformBackendCutoverAsync(
        [Description("Numeric project ID.")] int projectId,
        [Description("Target environment name for production guardrail verification.")] string environmentName,
        [Description("Backend cutover request payload including Revision, EnvironmentId, Backend, and VerificationSha256.")] TerraformBackendCutoverApiModel cutover,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(cutover);

        var guardError = await _guard.CheckEnvironmentMutationAllowedAsync(
            "dorc_execute_terraform_backend_cutover",
            environmentName,
            _apiGateway,
            cancellationToken).ConfigureAwait(false);

        if (guardError is not null)
        {
            return McpToolOutcome.Failure<RefDataApiModel>(guardError);
        }

        return await _apiGateway.PostAsync<TerraformBackendCutoverApiModel, RefDataApiModel>(
            $"Projects/{projectId}/terraform-state-backend-cutovers",
            cutover,
            cancellationToken).ConfigureAwait(false);
    }
}
