using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tests.Acceptance.DatabaseTests;

[TestClass]
public class DeploymentDefinitionMigrationTests : DatabaseTestBase
{
    [TestMethod]
    [TestCategory("Database")]
    public void BackendRetirement_UpgradesAndPreservesArchiveColumnAlignment()
    {
        PublishDacpac();
        ExecuteEphemeral("""
            ALTER TABLE [deploy].[DeploymentRequest] DROP COLUMN [TerraformBackendRetiredByChangeId];
            ALTER TABLE [archive].[DeploymentRequest] DROP COLUMN [TerraformBackendRetiredByChangeId];
            """);
        PublishDacpac();
        ExecuteEphemeral("""
            INSERT [deploy].[DeploymentRequest] ([RequestDetails],[UserName],[Status],[TerraformBackendRetiredByChangeId])
            VALUES (N'<root/>', N'cutover-test', N'Failed', '11111111-1111-1111-1111-111111111111');
            DELETE FROM [deploy].[DeploymentRequest]
            OUTPUT DELETED.* INTO [archive].[DeploymentRequest]
            WHERE [UserName] = N'cutover-test';
            """);
        PublishDacpac();
        Assert.AreEqual(1, ExecuteScalarInt("""
            SELECT COUNT(*) FROM [archive].[DeploymentRequest]
            WHERE [UserName] = N'cutover-test' AND [TerraformBackendRetiredByChangeId] = '11111111-1111-1111-1111-111111111111'
            """));
    }

    [TestMethod]
    [TestCategory("Database")]
    public void ProjectBackend_UpgradesLegacyRowsAndPreservesConfigurationOnRepublish()
    {
        PublishDacpac();
        ExecuteEphemeral("""
            ALTER TABLE [deploy].[Project] DROP COLUMN [TerraformStateBackendsJson];
            INSERT [deploy].[Project] ([Name], [ObjectId]) VALUES (N'backend-upgrade', NEWID());
            """);
        var originalId = ExecuteScalarInt("SELECT Id FROM [deploy].[Project] WHERE Name = N'backend-upgrade'");
        PublishDacpac();
        Assert.AreEqual(1, ExecuteScalarInt("SELECT COUNT(*) FROM [deploy].[Project] WHERE Name = N'backend-upgrade' AND TerraformStateBackendsJson IS NULL"));
        ExecuteEphemeral("""
            UPDATE [deploy].[Project]
            SET TerraformStateBackendsJson = N'[{"StorageAccount":"projectstate","ContainerName":"tfstate","SubscriptionId":"11111111-1111-1111-1111-111111111111"}]'
            WHERE Name = N'backend-upgrade';
            """);
        PublishDacpac();
        Assert.AreEqual(originalId, ExecuteScalarInt("SELECT Id FROM [deploy].[Project] WHERE Name = N'backend-upgrade'"));
        Assert.AreEqual(1, ExecuteScalarInt("SELECT COUNT(*) FROM [deploy].[Project] WHERE Name = N'backend-upgrade' AND JSON_VALUE(TerraformStateBackendsJson, '$[0].StorageAccount') = N'projectstate'"));
    }

    [TestMethod]
    [TestCategory("Database")]
    public void DefinitionEvidence_UpgradesAndRepublishesWithoutMutableForeignKeys()
    {
        PublishDacpac();
        ExecuteEphemeral("""
            DROP TABLE [deploy].[PowerShellExecutionReceipt];
            DROP TABLE [deploy].[DeploymentRequestDefinition];
            DROP TABLE [deploy].[DeploymentDefinitionRevision];
            DROP TABLE [deploy].[DeploymentDefinitionChange];
            """);
        PublishDacpac();
        ExecuteEphemeral("""
            DECLARE @change uniqueidentifier = NEWID(), @revision uniqueidentifier = NEWID();
            INSERT [deploy].[DeploymentDefinitionChange]
              ([Id],[ProjectId],[ProjectName],[OccurredAt],[Actor],[Operation],[BeforeJson],[AfterJson],[Revision])
            VALUES (@change, 7, N'retained-project', SYSDATETIMEOFFSET(), N'original-actor', N'RefData.Update', N'{}', N'{}', REPLICATE('A',64));
            INSERT [deploy].[DeploymentDefinitionRevision]
              ([Id],[ChangeId],[ProjectId],[ComponentId],[ComponentName],[DefinitionJson],[Revision],[ObservedAt],[IsBaseline])
            VALUES (@revision,@change,7,3,N'retained-component',N'{}',REPLICATE('B',64),SYSDATETIMEOFFSET(),0);
            INSERT [deploy].[DeploymentRequestDefinition]
              ([DeploymentRequestId],[Ordinal],[ProjectId],[ComponentId],[ComponentName],[DefinitionRevisionId],[DefinitionRevision],
               [DefinitionJson],[ProjectJson],[IsSkipped],[SubmittedAt],[SubmittedBy])
            VALUES (42,0,7,3,N'retained-component',@revision,REPLICATE('B',64),N'{}',N'{}',0,SYSDATETIMEOFFSET(),N'requester');
            INSERT [deploy].[PowerShellExecutionReceipt]
              ([DeploymentResultId],[ScriptOrdinal],[DeploymentRequestId],[ProjectId],[ComponentId],[DefinitionRevision],
               [TextSha256],[RuntimeVersion],[RequestedRuntime],[Phase],[PreparedAt],[ObservedAt])
            VALUES (99,0,42,7,3,REPLICATE('B',64),REPLICATE('C',64),N'7.4',N'7',N'Prepared',SYSDATETIMEOFFSET(),SYSDATETIMEOFFSET());
            """);
        PublishDacpac();
        PublishDacpac();
        Assert.AreEqual(1, ExecuteScalarInt("SELECT COUNT(*) FROM [deploy].[DeploymentDefinitionChange] WHERE Actor = N'original-actor'"));
        Assert.AreEqual(1, ExecuteScalarInt("SELECT COUNT(*) FROM [deploy].[DeploymentDefinitionRevision] WHERE ComponentId = 3"));
        Assert.AreEqual(1, ExecuteScalarInt("SELECT COUNT(*) FROM [deploy].[DeploymentRequestDefinition] WHERE DeploymentRequestId = 42"));
        Assert.AreEqual(1, ExecuteScalarInt("SELECT COUNT(*) FROM [deploy].[PowerShellExecutionReceipt] WHERE Phase = N'Prepared' AND CompletedAt IS NULL"));
        Assert.AreEqual(3, ExecuteScalarInt("SELECT COUNT(*) FROM [deploy].[DeploymentDefinitionHistoryView] WHERE ProjectId = 7"));
    }
}
