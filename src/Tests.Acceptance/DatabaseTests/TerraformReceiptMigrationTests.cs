using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tests.Acceptance.DatabaseTests;

[TestClass]
public class TerraformReceiptMigrationTests : DatabaseTestBase
{
    [TestMethod]
    [TestCategory("Database")]
    public void AdditiveReceiptsAndSubmissionKeys_PreserveApprovalHistoryAndSurviveRepublish()
    {
        PublishDacpac();
        ExecuteEphemeral("""
            DROP TABLE [deploy].[TerraformDeploymentReceipt];
            DROP TABLE [deploy].[CatalogSubmission];
            DROP TABLE [deploy].[CatalogEnvironmentInputs];
            DROP TABLE [deploy].[CatalogRequestInputs];
            DROP TABLE [deploy].[CatalogModuleUpgrade];
            DROP TABLE [deploy].[TerraformDeploymentOutcome];
            DROP TABLE [deploy].[TerraformInventoryProvenance];
            INSERT [deploy].[TerraformPlanApproval]
                ([DeploymentResultId], [DeploymentRequestId], [ApprovedBy], [ApprovedAt],
                 [PlanSha256], [SourceSha256], [PlanContentSha256])
            VALUES (42, 7, N'original-approver', SYSDATETIMEOFFSET(), REPLICATE('A',64), REPLICATE('B',64), REPLICATE('C',64));
            """);
        PublishDacpac();
        Assert.AreEqual(0, ExecuteScalarInt("SELECT COUNT(*) FROM [deploy].[TerraformDeploymentReceipt]"));
        Assert.AreEqual(1, ExecuteScalarInt("SELECT COUNT(*) FROM [deploy].[TerraformPlanApproval] WHERE [ApprovedBy] = N'original-approver'"));
        ExecuteEphemeral("""
            INSERT [deploy].[TerraformDeploymentReceipt]
                ([DeploymentResultId], [DeploymentRequestId], [ComponentId], [ProjectName], [EnvironmentName],
                 [ComponentName], [PlannedAt], [TemplateVersion], [PlanSha256], [SourceSha256], [PlanContentSha256])
            VALUES (42, 7, 3, N'project', N'environment', N'component', SYSDATETIMEOFFSET(), N'1.0.0',
                    REPLICATE('A',64), REPLICATE('B',64), REPLICATE('C',64));
            INSERT [deploy].[CatalogSubmission]
                ([CallerHash], [SubmissionKey], [PayloadSalt], [PayloadFingerprint], [DeploymentRequestId], [ComponentJson])
            VALUES (REPLICATE('D',64), NEWID(), CRYPT_GEN_RANDOM(32), CRYPT_GEN_RANDOM(32), 7, N'{}');
            INSERT [deploy].[CatalogEnvironmentInputs] VALUES (3,9,N'2.0.0',1,N'{"create_service":"false"}');
            INSERT [deploy].[CatalogRequestInputs] VALUES (7,3,N'module',N'1.0.0',1,N'{"create_service":"false"}');
            INSERT [deploy].[CatalogModuleUpgrade] VALUES (3,1,N'1.0.0',N'2.0.0',N'upgrade-actor',SYSDATETIMEOFFSET());
            INSERT [deploy].[TerraformDeploymentOutcome] VALUES (42,1,N'Succeeded',N'PartialFailure',N'Succeeded',1,1,SYSDATETIMEOFFSET(),NULL,N'[]');
            INSERT [deploy].[TerraformInventoryProvenance] VALUES (3,9,17,NULL,42,42,SYSDATETIMEOFFSET(),N'project/service',N'Observed',1);
            """);
        PublishDacpac();
        PublishDacpac();
        Assert.AreEqual(1, ExecuteScalarInt("SELECT COUNT(*) FROM [deploy].[TerraformDeploymentReceipt] WHERE [TemplateVersion] = N'1.0.0'"));
        Assert.AreEqual(1, ExecuteScalarInt("SELECT COUNT(*) FROM [deploy].[CatalogSubmission] WHERE [DeploymentRequestId] = 7"));
        Assert.AreEqual(1, ExecuteScalarInt("SELECT COUNT(*) FROM [deploy].[TerraformPlanApproval] WHERE [ApprovedBy] = N'original-approver'"));
        Assert.AreEqual(1, ExecuteScalarInt("SELECT COUNT(*) FROM [deploy].[CatalogEnvironmentInputs] WHERE [InputsJson] LIKE '%false%'"));
        Assert.AreEqual(1, ExecuteScalarInt("SELECT COUNT(*) FROM [deploy].[CatalogRequestInputs] WHERE [TemplateVersion] = N'1.0.0'"));
        Assert.AreEqual(1, ExecuteScalarInt("SELECT COUNT(*) FROM [deploy].[CatalogModuleUpgrade] WHERE [UpdatedBy] = N'upgrade-actor'"));
        Assert.AreEqual(1, ExecuteScalarInt("SELECT COUNT(*) FROM [deploy].[TerraformDeploymentOutcome] WHERE [ApplyOutcome] = N'Succeeded'"));
        Assert.AreEqual(1, ExecuteScalarInt("SELECT COUNT(*) FROM [deploy].[TerraformInventoryProvenance] WHERE [LastSeenResultId] = 42"));
    }
}
