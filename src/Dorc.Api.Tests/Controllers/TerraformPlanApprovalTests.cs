using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using Dorc.ApiModel;
using Dorc.Core.AzureStorageAccount;
using Dorc.Core.Terraform;
using Dorc.PersistentData.Model;
using NSubstitute;

namespace Dorc.Api.Tests.Controllers;

[TestClass]
[TestCategory("Database")]
[DoNotParallelize]
public class TerraformPlanApprovalTests
{
    private static DorcApiFixture _fixture = null!;

    [ClassInitialize]
    public static void Initialize(TestContext _) => _fixture = new DorcApiFixture("dorc_api_tf_app");

    [ClassCleanup]
    public static void Cleanup() => _fixture?.Dispose();

    [TestMethod]
    public async Task TerraformPlanApproval_EnforcesSeparateApproverOnProd_VerifiesArtifactDigest_AndAllowsDecline()
    {
        using var submitterClient = _fixture.CreateClientForUser("sub-oid", "submitter.user@sefe.eu", "Admin");
        using var approverClient = _fixture.CreateClientForUser("app-oid", "approver.user@sefe.eu", "Admin");

        var projectName = "TfGateProj_" + Guid.NewGuid().ToString("N")[..8];
        var prodEnvName = "TfProdEnv_" + Guid.NewGuid().ToString("N")[..8];
        var compName = "TfGateComp_" + Guid.NewGuid().ToString("N")[..8];

        int requestId;
        int confirmResultId;
        int declineResultId;
        var planText = "Terraform will perform the following actions:\n  + azurerm_storage_account.this";
        var planSha = Convert.ToHexString(SHA256.HashData("binary-plan-v1"u8));
        var sourceSha = Convert.ToHexString(SHA256.HashData("source-zip-v1"u8));
        var planContentSha = TerraformPlanArtifactIntegrity.HashPlanContent(planText);

        using (var context = _fixture.ContextFactory.GetContext())
        {
            var env = new Dorc.PersistentData.Model.Environment
            {
                Name = prodEnvName,
                ObjectId = Guid.NewGuid(),
                IsProd = true,
                Secure = false
            };
            var project = new Project
            {
                Name = projectName,
                Description = "Terraform gate project",
                ArtefactsUrl = "https://github.com/sefe/dorc-terraform-modules",
                SourceControlType = SourceControlType.GitHub
            };
            var comp1 = new Component
            {
                Name = compName + "_1",
                ComponentType = ComponentType.Terraform,
                TerraformSourceType = TerraformSourceType.Catalog,
                TerraformTemplateName = "azure-storage-account",
                TerraformTemplateVersion = "1.0.0",
                IsEnabled = true
            };
            var comp2 = new Component
            {
                Name = compName + "_2",
                ComponentType = ComponentType.Terraform,
                TerraformSourceType = TerraformSourceType.Catalog,
                TerraformTemplateName = "azure-storage-account",
                TerraformTemplateVersion = "1.0.0",
                IsEnabled = true
            };
            context.Environments.Add(env);
            context.Projects.Add(project);
            context.Components.AddRange(comp1, comp2);
            context.SaveChanges();

            var reqConfirm = new DeploymentRequest
            {
                Project = projectName,
                Environment = prodEnvName,
                BuildNumber = "1.0.0",
                Components = comp1.Name,
                RequestDetails = "<DeploymentRequestDetail />",
                UserName = "submitter.user@sefe.eu",
                RequestedTime = DateTimeOffset.UtcNow,
                Status = DeploymentRequestStatus.WaitingConfirmation.ToString(),
                IsProd = true
            };
            var reqDecline = new DeploymentRequest
            {
                Project = projectName,
                Environment = prodEnvName,
                BuildNumber = "1.0.0",
                Components = comp2.Name,
                RequestDetails = "<DeploymentRequestDetail />",
                UserName = "submitter.user@sefe.eu",
                RequestedTime = DateTimeOffset.UtcNow,
                Status = DeploymentRequestStatus.WaitingConfirmation.ToString(),
                IsProd = true
            };
            context.DeploymentRequests.AddRange(reqConfirm, reqDecline);
            context.SaveChanges();
            requestId = reqConfirm.Id;

            var resConfirm = new DeploymentResult
            {
                DeploymentRequest = reqConfirm,
                Component = comp1,
                Status = DeploymentResultStatus.WaitingConfirmation.ToString()
            };
            var resDecline = new DeploymentResult
            {
                DeploymentRequest = reqDecline,
                Component = comp2,
                Status = DeploymentResultStatus.WaitingConfirmation.ToString()
            };
            context.DeploymentResults.AddRange(resConfirm, resDecline);
            context.SaveChanges();
            confirmResultId = resConfirm.Id;
            declineResultId = resDecline.Id;

            context.TerraformDeploymentReceipts.Add(new TerraformDeploymentReceipt
            {
                DeploymentResultId = confirmResultId,
                DeploymentRequestId = reqConfirm.Id,
                ProjectId = project.Id,
                DefinitionRevision = "1",
                SourceType = TerraformSourceType.Catalog.ToString(),
                RequestedSource = "azure-storage-account",
                ComponentId = comp1.Id,
                EnvironmentId = env.Id,
                ProjectName = projectName,
                EnvironmentName = prodEnvName,
                ComponentName = comp1.Name,
                TemplateName = "azure-storage-account",
                TemplateVersion = "1.0.0",
                GitCommitSha = new string('a', 40),
                StateStorageAccount = "sttfstate01",
                StateContainerName = "tfstate",
                StateKey = $"{projectName}/{comp1.Name}/{prodEnvName}.tfstate",
                PlanSha256 = planSha,
                SourceSha256 = sourceSha,
                PlanContentSha256 = planContentSha,
                PlannedAt = DateTimeOffset.UtcNow
            });
            context.SaveChanges();
        }

        _fixture.AzureStorageWorker.LoadFileFromBlobs(confirmResultId.CreateTerraformPlanContentBlobName()).Returns(planText);
        _fixture.AzureStorageWorker.GetFileSha256FromBlobs(confirmResultId.CreateTerraformPlanBlobName()).Returns(planSha);
        _fixture.AzureStorageWorker.GetFileSha256FromBlobs(confirmResultId.CreateTerraformPlanBlobName() + ".source.zip").Returns(sourceSha);

        var getPlanResp = await approverClient.GetFromJsonAsync<TerraformPlanApiModel>(
            $"/Terraform/plan/{confirmResultId}", _fixture.JsonOptions);
        Assert.IsNotNull(getPlanResp);
        Assert.AreEqual(planContentSha, getPlanResp.PlanContentSha256);
        Assert.AreEqual("azure-storage-account", getPlanResp.Receipt?.TemplateName);

        // Submitter cannot self-approve a production-tier Terraform plan (Segregation of Duties)
        var selfApproveResp = await submitterClient.PostAsync(
            $"/Terraform/plan/{confirmResultId}/confirm?planContentSha256={planContentSha}", null);
        Assert.AreEqual(HttpStatusCode.Forbidden, selfApproveResp.StatusCode);
        var selfApproveBody = await selfApproveResp.Content.ReadAsStringAsync();
        StringAssert.Contains(selfApproveBody, "A different approver is required for this environment");

        // Approver with stale/mismatched planContentSha256 receives 409 Conflict
        var staleApproveResp = await approverClient.PostAsync(
            $"/Terraform/plan/{confirmResultId}/confirm?planContentSha256={new string('0', 64)}", null);
        Assert.AreEqual(HttpStatusCode.Conflict, staleApproveResp.StatusCode);

        // Separate approver with matching digest confirms the plan -> status transitions to Confirmed
        var approveResp = await approverClient.PostAsync(
            $"/Terraform/plan/{confirmResultId}/confirm?planContentSha256={planContentSha}", null);
        Assert.AreEqual(HttpStatusCode.OK, approveResp.StatusCode);

        using (var context = _fixture.ContextFactory.GetContext())
        {
            Assert.AreEqual(DeploymentResultStatus.Confirmed.ToString(), context.DeploymentResults.Single(r => r.Id == confirmResultId).Status);
            Assert.AreEqual(DeploymentRequestStatus.Confirmed.ToString(), context.DeploymentRequests.Single(r => r.Id == requestId).Status);
            var approval = context.TerraformPlanApprovals.Single(a => a.DeploymentResultId == confirmResultId);
            Assert.AreEqual("approver.user@sefe.eu", approval.ApprovedBy);
        }

        // Submitter CAN decline their own pending plan -> cancels result and request
        var declineResp = await submitterClient.PostAsync($"/Terraform/plan/{declineResultId}/decline", null);
        Assert.AreEqual(HttpStatusCode.OK, declineResp.StatusCode);

        using (var context = _fixture.ContextFactory.GetContext())
        {
            Assert.AreEqual(DeploymentResultStatus.Cancelled.ToString(), context.DeploymentResults.Single(r => r.Id == declineResultId).Status);
        }
    }
}
