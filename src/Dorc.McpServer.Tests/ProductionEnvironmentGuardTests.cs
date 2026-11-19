using Dorc.ApiModel;
using NSubstitute;

namespace Dorc.McpServer.Tests;

[TestClass]
public sealed class ProductionEnvironmentGuardTests
{
    [TestMethod]
    public async Task ProductionEnvironmentGuard_CoversAllBranches()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() => new ProductionEnvironmentGuard(null!));

        Assert.IsFalse(ProductionEnvironmentGuard.IsProductionEnvironmentName(null));
        Assert.IsFalse(ProductionEnvironmentGuard.IsProductionEnvironmentName("   "));
        Assert.IsFalse(ProductionEnvironmentGuard.IsProductionEnvironmentName("DEV-01"));
        Assert.IsTrue(ProductionEnvironmentGuard.IsProductionEnvironmentName("EU-PROD-01"));
        Assert.IsTrue(ProductionEnvironmentGuard.IsProductionEnvironmentName("uk-prd-web"));

        var gateway = Substitute.For<IDorcApiGateway>();

        // 1. ReadOnly = true blocks everything
        var readOnlyGuard = new ProductionEnvironmentGuard(new DorcMcpOptions { ReadOnly = true, AllowProd = false });
        Assert.IsTrue(readOnlyGuard.IsReadOnly);
        Assert.IsFalse(readOnlyGuard.AllowProd);
        StringAssert.Contains(readOnlyGuard.CheckMutationAllowed("op")!, "read-only mode");
        StringAssert.Contains(await readOnlyGuard.CheckEnvironmentMutationAllowedAsync("op", "DEV", gateway, CancellationToken.None), "read-only mode");
        StringAssert.Contains(await readOnlyGuard.CheckRequestMutationAllowedAsync("op", 1, gateway, CancellationToken.None), "read-only mode");

        // 2. ReadOnly = false, AllowProd = true allows everything without API lookups
        var allowProdGuard = new ProductionEnvironmentGuard(new DorcMcpOptions { ReadOnly = false, AllowProd = true });
        Assert.IsNull(allowProdGuard.CheckMutationAllowed("op"));
        Assert.IsNull(await allowProdGuard.CheckEnvironmentMutationAllowedAsync("op", "PROD", gateway, CancellationToken.None));
        Assert.IsNull(await allowProdGuard.CheckRequestMutationAllowedAsync("op", 99, gateway, CancellationToken.None));

        // 3. ReadOnly = false, AllowProd = false enforces prod checks
        var nonProdGuard = new ProductionEnvironmentGuard(new DorcMcpOptions { ReadOnly = false, AllowProd = false });

        // Empty env name
        StringAssert.Contains(await nonProdGuard.CheckEnvironmentMutationAllowedAsync("op", "  ", gateway, CancellationToken.None), "requires a non-empty target environment name");

        // Heuristic PROD name blocked immediately
        StringAssert.Contains(await nonProdGuard.CheckEnvironmentMutationAllowedAsync("op", "PROD-LON", gateway, CancellationToken.None), "DORC_MCP_ALLOW_PROD=false");

        // API failure when looking up environment
        gateway.GetAsync<List<EnvironmentApiModel>>("RefDataEnvironments?env=UAT-FAIL", Arg.Any<CancellationToken>())
            .Returns(McpToolOutcome.Failure<List<EnvironmentApiModel>>("timeout", null));
        StringAssert.Contains(await nonProdGuard.CheckEnvironmentMutationAllowedAsync("op", "UAT-FAIL", gateway, CancellationToken.None), "could not verify production status (HTTP error: timeout)");

        // API success with null data
        gateway.GetAsync<List<EnvironmentApiModel>>("RefDataEnvironments?env=UAT-NULL", Arg.Any<CancellationToken>())
            .Returns(new McpToolOutcome<List<EnvironmentApiModel>>(true, null, "none", 502));
        StringAssert.Contains(await nonProdGuard.CheckEnvironmentMutationAllowedAsync("op", "UAT-NULL", gateway, CancellationToken.None), "HTTP 502");

        // Environment not in returned list
        gateway.GetAsync<List<EnvironmentApiModel>>("RefDataEnvironments?env=UAT-MISSING", Arg.Any<CancellationToken>())
            .Returns(McpToolOutcome.Success(new List<EnvironmentApiModel> { new() { EnvironmentName = "OTHER" } }));
        StringAssert.Contains(await nonProdGuard.CheckEnvironmentMutationAllowedAsync("op", "UAT-MISSING", gateway, CancellationToken.None), "could not find environment");

        // Environment has EnvironmentIsProd = true
        gateway.GetAsync<List<EnvironmentApiModel>>("RefDataEnvironments?env=LIVE-01", Arg.Any<CancellationToken>())
            .Returns(McpToolOutcome.Success(new List<EnvironmentApiModel> { new() { EnvironmentName = "LIVE-01", EnvironmentIsProd = true } }));
        StringAssert.Contains(await nonProdGuard.CheckEnvironmentMutationAllowedAsync("op", "LIVE-01", gateway, CancellationToken.None), "EnvironmentIsProd=True");

        // Environment has EnvironmentSecure = true
        gateway.GetAsync<List<EnvironmentApiModel>>("RefDataEnvironments?env=SEC-01", Arg.Any<CancellationToken>())
            .Returns(McpToolOutcome.Success(new List<EnvironmentApiModel> { new() { EnvironmentName = "SEC-01", EnvironmentIsProd = false, EnvironmentSecure = true } }));
        StringAssert.Contains(await nonProdGuard.CheckEnvironmentMutationAllowedAsync("op", "SEC-01", gateway, CancellationToken.None), "EnvironmentSecure=True");

        // Environment has EnvironmentIsProd = false and EnvironmentSecure = false -> allowed
        gateway.GetAsync<List<EnvironmentApiModel>>("RefDataEnvironments?env=DEV-01", Arg.Any<CancellationToken>())
            .Returns(McpToolOutcome.Success(new List<EnvironmentApiModel> { new() { EnvironmentName = "DEV-01", EnvironmentIsProd = false, EnvironmentSecure = false } }));
        Assert.IsNull(await nonProdGuard.CheckEnvironmentMutationAllowedAsync("op", "DEV-01", gateway, CancellationToken.None));

        // Request mutation: request lookup fails (with and without status code)
        gateway.GetAsync<DeploymentRequestApiModel>("RequestStatuses?requestId=404", Arg.Any<CancellationToken>())
            .Returns(McpToolOutcome.Failure<DeploymentRequestApiModel>("not found", 404));
        StringAssert.Contains(await nonProdGuard.CheckRequestMutationAllowedAsync("op", 404, gateway, CancellationToken.None), "HTTP 404");

        gateway.GetAsync<DeploymentRequestApiModel>("RequestStatuses?requestId=405", Arg.Any<CancellationToken>())
            .Returns(new McpToolOutcome<DeploymentRequestApiModel>(true, null, "empty", null));
        StringAssert.Contains(await nonProdGuard.CheckRequestMutationAllowedAsync("op", 405, gateway, CancellationToken.None), "HTTP error");

        // Request mutation: request marked IsProd=true -> blocked even if EnvironmentName looks non-prod
        gateway.GetAsync<DeploymentRequestApiModel>("RequestStatuses?requestId=99", Arg.Any<CancellationToken>())
            .Returns(McpToolOutcome.Success(new DeploymentRequestApiModel { Id = 99, EnvironmentName = "DEV-01", IsProd = true }));
        StringAssert.Contains(await nonProdGuard.CheckRequestMutationAllowedAsync("op", 99, gateway, CancellationToken.None), "IsProd=True");

        // Request mutation: request found for DEV-01 -> allowed
        gateway.GetAsync<DeploymentRequestApiModel>("RequestStatuses?requestId=100", Arg.Any<CancellationToken>())
            .Returns(McpToolOutcome.Success(new DeploymentRequestApiModel { Id = 100, EnvironmentName = "DEV-01" }));
        Assert.IsNull(await nonProdGuard.CheckRequestMutationAllowedAsync("op", 100, gateway, CancellationToken.None));

        // Terraform plan mutation checks:
        StringAssert.Contains(await readOnlyGuard.CheckTerraformPlanMutationAllowedAsync("op", 1, "DEV-01", gateway, CancellationToken.None), "read-only mode");
        Assert.IsNull(await allowProdGuard.CheckTerraformPlanMutationAllowedAsync("op", 1, "PROD", gateway, CancellationToken.None));

        gateway.GetAsync<TerraformPlanApiModel>("Terraform/plan/404", Arg.Any<CancellationToken>())
            .Returns(McpToolOutcome.Failure<TerraformPlanApiModel>("missing", 404));
        StringAssert.Contains(await nonProdGuard.CheckTerraformPlanMutationAllowedAsync("op", 404, "DEV-01", gateway, CancellationToken.None), "HTTP 404");

        gateway.GetAsync<TerraformPlanApiModel>("Terraform/plan/405", Arg.Any<CancellationToken>())
            .Returns(new McpToolOutcome<TerraformPlanApiModel>(true, null, "empty", null));
        StringAssert.Contains(await nonProdGuard.CheckTerraformPlanMutationAllowedAsync("op", 405, "DEV-01", gateway, CancellationToken.None), "HTTP error");

        gateway.GetAsync<TerraformPlanApiModel>("Terraform/plan/406", Arg.Any<CancellationToken>())
            .Returns(McpToolOutcome.Success(new TerraformPlanApiModel { Receipt = null! }));
        StringAssert.Contains(await nonProdGuard.CheckTerraformPlanMutationAllowedAsync("op", 406, "DEV-01", gateway, CancellationToken.None), "could not determine the authoritative environment");

        gateway.GetAsync<TerraformPlanApiModel>("Terraform/plan/407", Arg.Any<CancellationToken>())
            .Returns(McpToolOutcome.Success(new TerraformPlanApiModel { Receipt = new TerraformDeploymentReceiptApiModel { EnvironmentName = "  " } }));
        StringAssert.Contains(await nonProdGuard.CheckTerraformPlanMutationAllowedAsync("op", 407, "DEV-01", gateway, CancellationToken.None), "could not determine the authoritative environment");

        gateway.GetAsync<TerraformPlanApiModel>("Terraform/plan/408", Arg.Any<CancellationToken>())
            .Returns(McpToolOutcome.Success(new TerraformPlanApiModel { Receipt = new TerraformDeploymentReceiptApiModel { EnvironmentName = "PROD-01" } }));
        StringAssert.Contains(await nonProdGuard.CheckTerraformPlanMutationAllowedAsync("op", 408, "DEV-01", gateway, CancellationToken.None), "does not match the Terraform plan's authoritative environment");

        gateway.GetAsync<TerraformPlanApiModel>("Terraform/plan/409", Arg.Any<CancellationToken>())
            .Returns(McpToolOutcome.Success(new TerraformPlanApiModel { Receipt = new TerraformDeploymentReceiptApiModel { EnvironmentName = "DEV-01" } }));
        Assert.IsNull(await nonProdGuard.CheckTerraformPlanMutationAllowedAsync("op", 409, "DEV-01", gateway, CancellationToken.None));
        Assert.IsNull(await nonProdGuard.CheckTerraformPlanMutationAllowedAsync("op", 409, null, gateway, CancellationToken.None));

        // Daemon mutation checks:
        StringAssert.Contains(await readOnlyGuard.CheckDaemonMutationAllowedAsync("op", "DEV-01", "srv", "svc", gateway, CancellationToken.None), "read-only mode");
        StringAssert.Contains(await nonProdGuard.CheckDaemonMutationAllowedAsync("op", "  ", "srv", "svc", gateway, CancellationToken.None), "requires a non-empty target environment name");
        StringAssert.Contains(await nonProdGuard.CheckDaemonMutationAllowedAsync("op", "DEV-01", "  ", "svc", gateway, CancellationToken.None), "requires non-empty serverName and daemonName");
        StringAssert.Contains(await nonProdGuard.CheckDaemonMutationAllowedAsync("op", "DEV-01", "srv", "  ", gateway, CancellationToken.None), "requires non-empty serverName and daemonName");
        StringAssert.Contains(await nonProdGuard.CheckDaemonMutationAllowedAsync("op", "PROD-01", "srv", "svc", gateway, CancellationToken.None), "DORC_MCP_ALLOW_PROD=false");
        StringAssert.Contains(await nonProdGuard.CheckDaemonMutationAllowedAsync("op", "UAT-FAIL", "srv", "svc", gateway, CancellationToken.None), "HTTP error");
        StringAssert.Contains(await nonProdGuard.CheckDaemonMutationAllowedAsync("op", "UAT-NULL", "srv", "svc", gateway, CancellationToken.None), "HTTP 502");
        StringAssert.Contains(await nonProdGuard.CheckDaemonMutationAllowedAsync("op", "UAT-MISSING", "srv", "svc", gateway, CancellationToken.None), "could not find environment");
        StringAssert.Contains(await nonProdGuard.CheckDaemonMutationAllowedAsync("op", "LIVE-01", "srv", "svc", gateway, CancellationToken.None), "EnvironmentIsProd=True");
        StringAssert.Contains(await nonProdGuard.CheckDaemonMutationAllowedAsync("op", "SEC-01", "srv", "svc", gateway, CancellationToken.None), "EnvironmentSecure=True");

        // Details lookup failures (with status code, without status code, and null AppServers)
        gateway.GetAsync<List<EnvironmentApiModel>>("RefDataEnvironments?env=DEV-DET-ERR", Arg.Any<CancellationToken>())
            .Returns(McpToolOutcome.Success(new List<EnvironmentApiModel> { new() { EnvironmentId = 11, EnvironmentName = "DEV-DET-ERR" } }));
        gateway.GetAsync<EnvironmentContentApiModel>("RefDataEnvironmentsDetails/11", Arg.Any<CancellationToken>())
            .Returns(McpToolOutcome.Failure<EnvironmentContentApiModel>("err", 500));
        StringAssert.Contains(await nonProdGuard.CheckDaemonMutationAllowedAsync("op", "DEV-DET-ERR", "srv", "svc", gateway, CancellationToken.None), "HTTP 500");

        gateway.GetAsync<List<EnvironmentApiModel>>("RefDataEnvironments?env=DEV-DET-NULL", Arg.Any<CancellationToken>())
            .Returns(McpToolOutcome.Success(new List<EnvironmentApiModel> { new() { EnvironmentId = 12, EnvironmentName = "DEV-DET-NULL" } }));
        gateway.GetAsync<EnvironmentContentApiModel>("RefDataEnvironmentsDetails/12", Arg.Any<CancellationToken>())
            .Returns(new McpToolOutcome<EnvironmentContentApiModel>(true, null, "empty", null));
        StringAssert.Contains(await nonProdGuard.CheckDaemonMutationAllowedAsync("op", "DEV-DET-NULL", "srv", "svc", gateway, CancellationToken.None), "HTTP error");

        gateway.GetAsync<List<EnvironmentApiModel>>("RefDataEnvironments?env=DEV-DET-NOSRV", Arg.Any<CancellationToken>())
            .Returns(McpToolOutcome.Success(new List<EnvironmentApiModel> { new() { EnvironmentId = 13, EnvironmentName = "DEV-DET-NOSRV" } }));
        gateway.GetAsync<EnvironmentContentApiModel>("RefDataEnvironmentsDetails/13", Arg.Any<CancellationToken>())
            .Returns(McpToolOutcome.Success(new EnvironmentContentApiModel { AppServers = null! }));
        StringAssert.Contains(await nonProdGuard.CheckDaemonMutationAllowedAsync("op", "DEV-DET-NOSRV", "srv", "svc", gateway, CancellationToken.None), "could not retrieve servers");

        // Server not in environment, server with null/empty EnvironmentNames, server shared with PROD, server shared with another non-prod env
        gateway.GetAsync<List<EnvironmentApiModel>>("RefDataEnvironments?env=DEV-SRV", Arg.Any<CancellationToken>())
            .Returns(McpToolOutcome.Success(new List<EnvironmentApiModel> { new() { EnvironmentId = 14, EnvironmentName = "DEV-SRV" } }));
        gateway.GetAsync<EnvironmentContentApiModel>("RefDataEnvironmentsDetails/14", Arg.Any<CancellationToken>())
            .Returns(McpToolOutcome.Success(new EnvironmentContentApiModel
            {
                AppServers =
                [
                    new ServerApiModel { ServerId = 101, Name = "srv-null-envs", EnvironmentNames = null! },
                    new ServerApiModel { ServerId = 102, Name = "srv-empty-envs", EnvironmentNames = [] },
                    new ServerApiModel { ServerId = 103, Name = "srv-shared-prod", EnvironmentNames = ["DEV-SRV", "PROD-01"] },
                    new ServerApiModel { ServerId = 104, Name = "srv-daemons-err", EnvironmentNames = ["DEV-SRV", "DEV-01"] },
                    new ServerApiModel { ServerId = 105, Name = "srv-daemons-null", EnvironmentNames = ["DEV-SRV"] },
                    new ServerApiModel { ServerId = 106, Name = "srv-ok", EnvironmentNames = ["DEV-SRV"] }
                ]
            }));

        StringAssert.Contains(await nonProdGuard.CheckDaemonMutationAllowedAsync("op", "DEV-SRV", "missing-srv", "svc", gateway, CancellationToken.None), "does not belong to environment");
        StringAssert.Contains(await nonProdGuard.CheckDaemonMutationAllowedAsync("op", "DEV-SRV", "srv-null-envs", "svc", gateway, CancellationToken.None), "could not verify all environment associations");
        StringAssert.Contains(await nonProdGuard.CheckDaemonMutationAllowedAsync("op", "DEV-SRV", "srv-empty-envs", "svc", gateway, CancellationToken.None), "could not verify all environment associations");
        StringAssert.Contains(await nonProdGuard.CheckDaemonMutationAllowedAsync("op", "DEV-SRV", "srv-shared-prod", "svc", gateway, CancellationToken.None), "associated environment 'PROD-01' is protected");

        gateway.GetAsync<List<DaemonApiModel>>("ServerDaemons/104", Arg.Any<CancellationToken>())
            .Returns(McpToolOutcome.Failure<List<DaemonApiModel>>("err", 503));
        StringAssert.Contains(await nonProdGuard.CheckDaemonMutationAllowedAsync("op", "DEV-SRV", "srv-daemons-err", "svc", gateway, CancellationToken.None), "HTTP 503");

        gateway.GetAsync<List<DaemonApiModel>>("ServerDaemons/105", Arg.Any<CancellationToken>())
            .Returns(new McpToolOutcome<List<DaemonApiModel>>(true, null, "empty", null));
        StringAssert.Contains(await nonProdGuard.CheckDaemonMutationAllowedAsync("op", "DEV-SRV", "srv-daemons-null", "svc", gateway, CancellationToken.None), "HTTP error");

        gateway.GetAsync<List<DaemonApiModel>>("ServerDaemons/106", Arg.Any<CancellationToken>())
            .Returns(McpToolOutcome.Success(new List<DaemonApiModel> { new() { Name = "svc" } }));
        StringAssert.Contains(await nonProdGuard.CheckDaemonMutationAllowedAsync("op", "DEV-SRV", "srv-ok", "unmapped-svc", gateway, CancellationToken.None), "is not mapped to server");
        Assert.IsNull(await nonProdGuard.CheckDaemonMutationAllowedAsync("op", "DEV-SRV", "srv-ok", "svc", gateway, CancellationToken.None));

        // Also test allowProdGuard on LIVE-01 when server & daemon are mapped
        gateway.GetAsync<List<EnvironmentApiModel>>("RefDataEnvironments?env=PROD-OK", Arg.Any<CancellationToken>())
            .Returns(McpToolOutcome.Success(new List<EnvironmentApiModel> { new() { EnvironmentId = 15, EnvironmentName = "PROD-OK", EnvironmentIsProd = true, EnvironmentSecure = true } }));
        gateway.GetAsync<EnvironmentContentApiModel>("RefDataEnvironmentsDetails/15", Arg.Any<CancellationToken>())
            .Returns(McpToolOutcome.Success(new EnvironmentContentApiModel
            {
                AppServers = [new ServerApiModel { ServerId = 106, Name = "srv-ok", EnvironmentNames = null! }]
            }));
        Assert.IsNull(await allowProdGuard.CheckDaemonMutationAllowedAsync("op", "PROD-OK", "srv-ok", "svc", gateway, CancellationToken.None));
    }
}
