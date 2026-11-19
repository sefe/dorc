using Dorc.ApiModel;
using Dorc.McpServer.Tools;
using Dorc.Terraform.Catalog;
using NSubstitute;

namespace Dorc.McpServer.Tests;

[TestClass]
public sealed class TerraformCatalogToolsTests
{
    [TestMethod]
    public async Task TerraformCatalogTools_CoversAllMethodsAndBranches()
    {
        var gateway = Substitute.For<IDorcApiGateway>();
        var allowGuard = new ProductionEnvironmentGuard(new DorcMcpOptions { ReadOnly = false, AllowProd = true });
        var roGuard = new ProductionEnvironmentGuard(new DorcMcpOptions { ReadOnly = true, AllowProd = false });

        Assert.ThrowsExactly<ArgumentNullException>(() => new TerraformCatalogTools(null!, allowGuard));
        Assert.ThrowsExactly<ArgumentNullException>(() => new TerraformCatalogTools(gateway, null!));

        var tools = new TerraformCatalogTools(gateway, allowGuard);
        var roTools = new TerraformCatalogTools(gateway, roGuard);

        gateway.GetAsync<List<TerraformTemplateManifest>>("Terraform/templates", Arg.Any<CancellationToken>())
            .Returns(McpToolOutcome.Success(new List<TerraformTemplateManifest>()));
        Assert.IsTrue((await tools.ListTerraformTemplatesAsync()).Succeeded);

        gateway.GetAsync<TerraformTemplateManifest>("Terraform/templates/redis", Arg.Any<CancellationToken>())
            .Returns(McpToolOutcome.Success<TerraformTemplateManifest>(null!));
        Assert.IsTrue((await tools.GetTerraformTemplateAsync("redis", null)).Succeeded);

        gateway.GetAsync<TerraformTemplateManifest>("Terraform/templates/redis/1.0.0", Arg.Any<CancellationToken>())
            .Returns(McpToolOutcome.Success<TerraformTemplateManifest>(null!));
        Assert.IsTrue((await tools.GetTerraformTemplateAsync("redis", " 1.0.0 ")).Succeeded);

        gateway.GetAsync<TerraformTemplateResolutionApiModel>("Terraform/templates/redis/1.0.0/resolution?projectId=1&environmentName=DEV", Arg.Any<CancellationToken>())
            .Returns(McpToolOutcome.Success(new TerraformTemplateResolutionApiModel()));
        Assert.IsTrue((await tools.ResolveTerraformTemplateAsync("redis", "1.0.0", 1, "DEV", null)).Succeeded);

        gateway.GetAsync<TerraformTemplateResolutionApiModel>("Terraform/templates/redis/1.0.0/resolution?projectId=1&environmentName=DEV&componentId=9", Arg.Any<CancellationToken>())
            .Returns(McpToolOutcome.Success(new TerraformTemplateResolutionApiModel()));
        Assert.IsTrue((await tools.ResolveTerraformTemplateAsync("redis", "1.0.0", 1, "DEV", 9)).Succeeded);

        gateway.GetAsync<TerraformPlanApiModel>("Terraform/plan/77", Arg.Any<CancellationToken>())
            .Returns(McpToolOutcome.Success(new TerraformPlanApiModel()));
        Assert.IsTrue((await tools.GetTerraformPlanAsync(77)).Succeeded);

        gateway.GetAsync<List<TerraformInventoryProvenanceApiModel>>("Terraform/plan/77/inventory", Arg.Any<CancellationToken>())
            .Returns(McpToolOutcome.Success(new List<TerraformInventoryProvenanceApiModel>()));
        Assert.IsTrue((await tools.GetTerraformPlanInventoryAsync(77)).Succeeded);

        // ConfirmTerraformPlanAsync: empty hash, blocked by guard, allowed
        Assert.IsFalse((await tools.ConfirmTerraformPlanAsync(77, "  ", "DEV")).Succeeded);
        Assert.IsFalse((await roTools.ConfirmTerraformPlanAsync(77, "sha256", "DEV")).Succeeded);

        gateway.PostWithoutResponseBodyAsync<object?>("Terraform/plan/77/confirm?planContentSha256=sha256", null, Arg.Any<CancellationToken>())
            .Returns(McpToolOutcome.Success("confirmed"));
        Assert.IsTrue((await tools.ConfirmTerraformPlanAsync(77, " sha256 ", "DEV")).Succeeded);

        // DeclineTerraformPlanAsync: blocked by guard, allowed
        Assert.IsFalse((await roTools.DeclineTerraformPlanAsync(77, "DEV")).Succeeded);
        gateway.PostWithoutResponseBodyAsync<object?>("Terraform/plan/77/decline", null, Arg.Any<CancellationToken>())
            .Returns(McpToolOutcome.Success("declined"));
        Assert.IsTrue((await tools.DeclineTerraformPlanAsync(77, "DEV")).Succeeded);

        // RecoverTerraformPostApplyAsync: both flags false, blocked by guard, allowed (both flag combinations)
        Assert.IsFalse((await tools.RecoverTerraformPostApplyAsync(77, 1, "DEV", false, false)).Succeeded);
        Assert.IsFalse((await roTools.RecoverTerraformPostApplyAsync(77, 1, "DEV", true, true)).Succeeded);
        gateway.PostAsync<object?, TerraformDeploymentOutcomeApiModel>(
                "Terraform/plan/77/recover?expectedRevision=1&recoverInventory=true&recoverArchive=false",
                null,
                Arg.Any<CancellationToken>())
            .Returns(McpToolOutcome.Success(new TerraformDeploymentOutcomeApiModel()));
        Assert.IsTrue((await tools.RecoverTerraformPostApplyAsync(77, 1, "DEV", true, false)).Succeeded);

        gateway.PostAsync<object?, TerraformDeploymentOutcomeApiModel>(
                "Terraform/plan/77/recover?expectedRevision=2&recoverInventory=false&recoverArchive=true",
                null,
                Arg.Any<CancellationToken>())
            .Returns(McpToolOutcome.Success(new TerraformDeploymentOutcomeApiModel()));
        Assert.IsTrue((await tools.RecoverTerraformPostApplyAsync(77, 2, "DEV", false, true)).Succeeded);

        // ReviewTerraformInventoryAsync: blocked by guard, allowed
        Assert.IsFalse((await roTools.ReviewTerraformInventoryAsync(77, 12, 3, "DEV")).Succeeded);
        gateway.PostWithoutResponseBodyAsync<object?>(
                "Terraform/plan/77/inventory/12/review?expectedRevision=3",
                null,
                Arg.Any<CancellationToken>())
            .Returns(McpToolOutcome.Success(string.Empty));
        Assert.IsTrue((await tools.ReviewTerraformInventoryAsync(77, 12, 3, "DEV")).Succeeded);

        // PreviewTerraformBackendCutoverAsync
        gateway.GetAsync<TerraformBackendCutoverPreviewApiModel>(
                "Projects/5/terraform-state-backend-cutovers/preview?environmentId=10",
                Arg.Any<CancellationToken>())
            .Returns(McpToolOutcome.Success(new TerraformBackendCutoverPreviewApiModel()));
        Assert.IsTrue((await tools.PreviewTerraformBackendCutoverAsync(5, 10)).Succeeded);

        // ExecuteTerraformBackendCutoverAsync: null check, guard blocked, allowed
        await Assert.ThrowsExactlyAsync<ArgumentNullException>(() =>
            tools.ExecuteTerraformBackendCutoverAsync(5, "DEV", null!));
        Assert.IsFalse((await roTools.ExecuteTerraformBackendCutoverAsync(
            5,
            "DEV",
            new TerraformBackendCutoverApiModel())).Succeeded);

        gateway.PostAsync<TerraformBackendCutoverApiModel, RefDataApiModel>(
                "Projects/5/terraform-state-backend-cutovers",
                Arg.Any<TerraformBackendCutoverApiModel>(),
                Arg.Any<CancellationToken>())
            .Returns(McpToolOutcome.Success(new RefDataApiModel()));
        Assert.IsTrue((await tools.ExecuteTerraformBackendCutoverAsync(
            5,
            "DEV",
            new TerraformBackendCutoverApiModel())).Succeeded);
    }
}
