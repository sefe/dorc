using System.Text.RegularExpressions;
using Dorc.PersistentData.Contexts;
using Dorc.PersistentData.Model;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Environment = System.Environment;

namespace Dorc.Api.Tests.Sources;

[TestClass]
[TestCategory("Database")]
[DoNotParallelize]
public class ArchiveDeploymentRequestsTests
{
    private static string _masterConnectionString = null!;
    private static string _databaseName = null!;
    private static string _databaseConnectionString = null!;
    private static DeploymentContextFactory _factory = null!;
    private static string _repoRoot = null!;

    [ClassInitialize]
    public static void Initialize(TestContext _)
    {
        var connection = Environment.GetEnvironmentVariable("ConnectionStrings__DOrcConnectionString")
            ?? "Server=(localdb)\\MSSQLLocalDB;Database=master;Integrated Security=True;Encrypt=True;TrustServerCertificate=True;";

        var builder = new SqlConnectionStringBuilder(connection)
        {
            InitialCatalog = "master",
            Encrypt = SqlConnectionEncryptOption.Mandatory,
            TrustServerCertificate = true
        };
        try
        {
            using var probe = new SqlConnection(builder.ConnectionString);
            probe.Open();
        }
        catch (SqlException ex)
        {
            if (Environment.GetEnvironmentVariable("DORC_DB_TESTS_REQUIRED") == "1")
                Assert.Fail($"SQL Server is required for Database category tests: {ex.Message}");
            Assert.Inconclusive($"SQL Server is not reachable ({ex.Message}).");
        }

        _masterConnectionString = builder.ConnectionString;
        _databaseName = "dorc_archive_req_" + Guid.NewGuid().ToString("N");

        using (var sql = new SqlConnection(_masterConnectionString))
        {
            sql.Open();
            using var cmd = new SqlCommand($"CREATE DATABASE [{_databaseName}] COLLATE SQL_Latin1_General_CP1_CI_AS", sql);
            cmd.ExecuteNonQuery();
        }

        builder.InitialCatalog = _databaseName;
        _databaseConnectionString = builder.ConnectionString;
        _factory = new DeploymentContextFactory(_databaseConnectionString);

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Join(dir.FullName, "src", "Dorc.sln")))
        {
            dir = dir.Parent;
        }
        _repoRoot = dir!.FullName;

        using var context = _factory.GetContext();
        context.Database.EnsureCreated();
        context.Database.ExecuteSqlRaw("ALTER TABLE [deploy].[Project] ADD DEFAULT NEWID() FOR [ObjectId]");
        context.Database.ExecuteSqlRaw("IF COL_LENGTH('deploy.Environment', 'Owner') IS NULL ALTER TABLE [deploy].[Environment] ADD [Owner] NVARCHAR(100) NULL;");
        context.Database.ExecuteSqlRaw("CREATE SCHEMA [archive]");

        ExecuteSqlFile(Path.Join(_repoRoot, "src", "Dorc.Database", "Schema Objects", "Schemas", "archive", "Tables", "DeploymentRequest.table.sql"));
        ExecuteSqlFile(Path.Join(_repoRoot, "src", "Dorc.Database", "Schema Objects", "Schemas", "archive", "Tables", "DeploymentResult.table.sql"));
        ExecuteSqlFile(Path.Join(_repoRoot, "src", "Dorc.Database", "Schema Objects", "Schemas", "archive", "Tables", "DeploymentRequestAttempt.table.sql"));
        ExecuteSqlFile(Path.Join(_repoRoot, "src", "Dorc.Database", "Schema Objects", "Schemas", "archive", "Tables", "DeploymentResultAttempt.table.sql"));
        ExecuteSqlFile(Path.Join(_repoRoot, "src", "Dorc.Database", "Schema Objects", "Schemas", "archive", "Tables", "EnvironmentComponentStatus.table.sql"));

        var archiveProcSql = File.ReadAllText(Path.Join(_repoRoot, "src", "Dorc.Database", "deploy", "Stored Procedures", "ArchiveDeploymentRequests.sql"))
            .Replace("WAITFOR DELAY '00:00:02'", "-- WAITFOR DELAY omitted in test");
        ExecuteSqlScriptBatches(archiveProcSql);
    }

    [ClassCleanup]
    public static void Cleanup()
    {
        if (string.IsNullOrEmpty(_databaseName) || string.IsNullOrEmpty(_masterConnectionString))
            return;

        using var sql = new SqlConnection(_masterConnectionString);
        sql.Open();
        using var cmd = new SqlCommand($"ALTER DATABASE [{_databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{_databaseName}]", sql);
        cmd.ExecuteNonQuery();
    }

    private static void ExecuteSqlFile(string filePath)
    {
        ExecuteSqlScriptBatches(File.ReadAllText(filePath));
    }

    private static void ExecuteSqlScriptBatches(string script)
    {
        var batches = Regex.Split(script, @"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase);
        using var sql = new SqlConnection(_databaseConnectionString);
        sql.Open();
        foreach (var command in batches.Where(b => !string.IsNullOrWhiteSpace(b)).Select(batch => new SqlCommand(batch, sql)))
        {
            using (command)
            {
                command.ExecuteNonQuery();
            }
        }
    }

    [TestMethod]
    public void ArchiveDeploymentRequests_ArchivesTerminalRequestsAttemptsResults_AndPreservesActiveAndBackendEvidence()
    {
        int oldCompletedRequestId;
        int olderUnarchivedCompletedRequestId;
        int oldActiveWaitingRequestId;
        int boundaryNextYearRequestId;
        int catalogBackendOwnerRequestId;
        int projectId;
        string envName;

        using (var context = _factory.GetContext())
        {
            var project = new Project
            {
                Name = "ArchivalProj_" + Guid.NewGuid().ToString("N")[..8],
                ArtefactsUrl = "https://example.org",
                TerraformStateBackendsJson = "[{\"StorageAccount\":\"tfstateacct\",\"ContainerName\":\"tfstate\",\"SubscriptionId\":\"11111111-1111-1111-1111-111111111111\"}]"
            };
            var env = new Dorc.PersistentData.Model.Environment { Name = "ArchivalEnv_" + Guid.NewGuid().ToString("N")[..8], ObjectId = Guid.NewGuid(), Secure = false, IsProd = false };
            var script = new Script { Name = "deploy.ps1", Path = "deploy.ps1", IsPathJSON = false };
            context.Projects.Add(project);
            context.Environments.Add(env);
            context.Scripts.Add(script);
            context.SaveChanges();

            projectId = project.Id;
            envName = env.Name;

            var component = new Component { Name = "ArchivalComp_" + Guid.NewGuid().ToString("N")[..8], ScriptId = script.Id, IsEnabled = true };
            context.Components.Add(component);
            context.SaveChanges();

            var req2024 = new DeploymentRequest
            {
                Project = project.Name,
                Environment = env.Name,
                BuildNumber = "1.0.2024",
                Components = component.Name,
                RequestDetails = "<DeploymentRequestDetail />",
                UserName = "tester",
                RequestedTime = new DateTimeOffset(2024, 6, 15, 12, 0, 0, TimeSpan.Zero),
                CompletedTime = new DateTimeOffset(2024, 6, 15, 12, 5, 0, TimeSpan.Zero),
                Status = "Completed"
            };
            var req2022 = new DeploymentRequest
            {
                Project = project.Name,
                Environment = env.Name,
                BuildNumber = "1.0.2022",
                Components = component.Name,
                RequestDetails = "<DeploymentRequestDetail />",
                UserName = "tester",
                RequestedTime = new DateTimeOffset(2022, 3, 10, 12, 0, 0, TimeSpan.Zero),
                CompletedTime = new DateTimeOffset(2022, 3, 10, 12, 5, 0, TimeSpan.Zero),
                Status = "Failed"
            };
            var reqWaiting = new DeploymentRequest
            {
                Project = project.Name,
                Environment = env.Name,
                BuildNumber = "1.0.waiting",
                Components = component.Name,
                RequestDetails = "<DeploymentRequestDetail />",
                UserName = "tester",
                RequestedTime = new DateTimeOffset(2024, 7, 1, 12, 0, 0, TimeSpan.Zero),
                Status = "WaitingConfirmation"
            };
            var req2025Boundary = new DeploymentRequest
            {
                Project = project.Name,
                Environment = env.Name,
                BuildNumber = "1.0.2025",
                Components = component.Name,
                RequestDetails = "<DeploymentRequestDetail />",
                UserName = "tester",
                RequestedTime = new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero),
                CompletedTime = new DateTimeOffset(2025, 1, 1, 0, 5, 0, TimeSpan.Zero),
                Status = "Completed"
            };
            var reqCatalogOwner = new DeploymentRequest
            {
                Project = project.Name,
                Environment = env.Name,
                BuildNumber = "1.0.catalog",
                Components = component.Name,
                RequestDetails = "<DeploymentRequestDetail />",
                UserName = "tester",
                RequestedTime = new DateTimeOffset(2023, 9, 1, 12, 0, 0, TimeSpan.Zero),
                CompletedTime = new DateTimeOffset(2023, 9, 1, 12, 5, 0, TimeSpan.Zero),
                Status = "Completed"
            };

            context.DeploymentRequests.AddRange(req2024, req2022, reqWaiting, req2025Boundary, reqCatalogOwner);
            context.SaveChanges();

            oldCompletedRequestId = req2024.Id;
            olderUnarchivedCompletedRequestId = req2022.Id;
            oldActiveWaitingRequestId = reqWaiting.Id;
            boundaryNextYearRequestId = req2025Boundary.Id;
            catalogBackendOwnerRequestId = reqCatalogOwner.Id;

            var res2024 = new DeploymentResult { DeploymentRequest = req2024, Component = component, Status = "Complete", Log = "ok" };
            context.DeploymentResults.Add(res2024);
            context.EnvironmentComponentStatuses.Add(new EnvironmentComponentStatus
            {
                Environment = env,
                Component = component,
                Status = "Complete",
                UpdateDate = DateTimeOffset.UtcNow,
                DeploymentRequest = req2024
            });
            context.SaveChanges();

            context.Database.ExecuteSqlInterpolated($@"
                INSERT INTO [deploy].[DeploymentRequestAttempt] ([DeploymentRequestId], [AttemptNumber], [StartedTime], [Status], [UserName])
                VALUES ({oldCompletedRequestId}, 1, SYSDATETIMEOFFSET(), N'Completed', N'tester');
                DECLARE @attemptId INT = SCOPE_IDENTITY();
                INSERT INTO [deploy].[DeploymentResultAttempt] ([DeploymentRequestAttemptId], [ComponentId], [ComponentName], [StartedTime], [Status], [DeploymentResultId], [Log])
                VALUES (@attemptId, {component.Id}, {component.Name}, SYSDATETIMEOFFSET(), N'Complete', {res2024.Id}, N'attempt-log');
                INSERT INTO [deploy].[PowerShellExecutionReceipt] ([DeploymentResultId], [ScriptOrdinal], [DeploymentRequestId], [ProjectId], [ComponentId], [DefinitionRevision], [TextSha256], [RuntimeVersion], [RequestedRuntime], [Phase], [ObservedAt])
                VALUES ({res2024.Id}, 0, {oldCompletedRequestId}, {project.Id}, {component.Id}, N'1', N'sha256', N'7.4', N'7', N'Execute', SYSDATETIMEOFFSET());
                INSERT INTO [deploy].[TerraformDeploymentOutcome] ([DeploymentResultId], [Revision], [ApplyOutcome], [InventoryOutcome], [ArchiveOutcome], [RegisteredCount], [FailedCount], [InventoryWarningsJson])
                VALUES ({res2024.Id}, 1, N'Succeeded', N'Registered', N'Archived', 1, 0, N'[]');
                INSERT INTO [deploy].[TerraformDeploymentReceipt] ([DeploymentResultId], [DeploymentRequestId], [ComponentId], [ProjectName], [EnvironmentName], [ComponentName], [PlannedAt], [PlanContentSha256], [SourceSha256], [PlanSha256])
                VALUES ({res2024.Id}, {oldCompletedRequestId}, {component.Id}, {project.Name}, {env.Name}, {component.Name}, SYSDATETIMEOFFSET(), 'sha', 'sha', 'sha');
                INSERT INTO [deploy].[TerraformPlanApproval] ([DeploymentResultId], [DeploymentRequestId], [ApprovedBy], [ApprovedAt], [PlanSha256], [SourceSha256], [PlanContentSha256])
                VALUES ({res2024.Id}, {oldCompletedRequestId}, N'approver', SYSDATETIMEOFFSET(), 'sha', 'sha', 'sha');
                INSERT INTO [deploy].[CatalogRequestInputs] ([DeploymentRequestId], [ComponentId], [TemplateName], [TemplateVersion], [ConfigurationRevision], [InputsJson])
                VALUES ({oldCompletedRequestId}, {component.Id}, N'mod', N'1.0.0', 1, N'{{}}');
                INSERT INTO [deploy].[DeploymentRequestDefinition] ([DeploymentRequestId], [Ordinal], [ProjectId], [ComponentId], [ComponentName], [DefinitionRevisionId], [DefinitionRevision], [DefinitionJson], [ProjectJson], [IsSkipped], [SubmittedAt], [SubmittedBy])
                VALUES ({oldCompletedRequestId}, 0, {project.Id}, {component.Id}, {component.Name}, NEWID(), N'1', N'{{}}', N'{{}}', 0, SYSDATETIMEOFFSET(), N'tester');
                INSERT INTO [deploy].[DeploymentRequestDefinition] ([DeploymentRequestId], [Ordinal], [ProjectId], [ComponentId], [ComponentName], [DefinitionRevisionId], [DefinitionRevision], [DefinitionJson], [ProjectJson], [IsSkipped], [SubmittedAt], [SubmittedBy])
                VALUES ({catalogBackendOwnerRequestId}, 0, {project.Id}, {component.Id}, {component.Name}, NEWID(), N'1', N'{{""ComponentType"":1,""TerraformSourceType"":4}}', N'{{""TerraformStateBackends"":[{{""StorageAccount"":""tfstateacct"",""ContainerName"":""tfstate"",""SubscriptionId"":""11111111-1111-1111-1111-111111111111""}}]}}', 0, SYSDATETIMEOFFSET(), N'tester');
            ");
        }

        using var sql = new SqlConnection(_databaseConnectionString);
        sql.Open();

        using (var cmd = new SqlCommand("EXEC [deploy].[ArchiveDeploymentRequests] @LastYear = 2024, @BatchSize = 1", sql))
        {
            var totalArchived = (int)cmd.ExecuteScalar()!;
            Assert.IsTrue(totalArchived >= 3, $"Expected at least 3 terminal requests archived, got {totalArchived}");
        }

        using (var checkCmd = new SqlCommand("SELECT COUNT(*) FROM [archive].[DeploymentRequest] WHERE Id IN (@id1, @id2, @id3)", sql))
        {
            checkCmd.Parameters.AddWithValue("@id1", oldCompletedRequestId);
            checkCmd.Parameters.AddWithValue("@id2", olderUnarchivedCompletedRequestId);
            checkCmd.Parameters.AddWithValue("@id3", catalogBackendOwnerRequestId);
            Assert.AreEqual(3, (int)checkCmd.ExecuteScalar()!);
        }

        using (var checkAttemptsCmd = new SqlCommand("SELECT COUNT(*) FROM [archive].[DeploymentRequestAttempt] WHERE DeploymentRequestId = @id1", sql))
        {
            checkAttemptsCmd.Parameters.AddWithValue("@id1", oldCompletedRequestId);
            Assert.AreEqual(1, (int)checkAttemptsCmd.ExecuteScalar()!, "DeploymentRequestAttempt must be archived into archive.DeploymentRequestAttempt.");
        }

        using (var checkResultAttemptsCmd = new SqlCommand(
            "SELECT COUNT(*) FROM [archive].[DeploymentResultAttempt] dra INNER JOIN [archive].[DeploymentRequestAttempt] a ON a.Id = dra.DeploymentRequestAttemptId WHERE a.DeploymentRequestId = @id1",
            sql))
        {
            checkResultAttemptsCmd.Parameters.AddWithValue("@id1", oldCompletedRequestId);
            Assert.AreEqual(1, (int)checkResultAttemptsCmd.ExecuteScalar()!, "DeploymentResultAttempt must be archived into archive.DeploymentResultAttempt.");
        }

        using (var checkResultCmd = new SqlCommand("SELECT COUNT(*) FROM [archive].[DeploymentResult] WHERE DeploymentRequestId = @id1", sql))
        {
            checkResultCmd.Parameters.AddWithValue("@id1", oldCompletedRequestId);
            Assert.AreEqual(1, (int)checkResultCmd.ExecuteScalar()!);
        }

        using (var checkStatusCmd = new SqlCommand("SELECT COUNT(*) FROM [archive].[EnvironmentComponentStatus] WHERE DeploymentRequestId = @id1", sql))
        {
            checkStatusCmd.Parameters.AddWithValue("@id1", oldCompletedRequestId);
            Assert.AreEqual(1, (int)checkStatusCmd.ExecuteScalar()!);
        }

        using (var checkCatalogEvidenceCmd = new SqlCommand("SELECT COUNT(*) FROM [deploy].[DeploymentRequestDefinition] WHERE DeploymentRequestId = @id3", sql))
        {
            checkCatalogEvidenceCmd.Parameters.AddWithValue("@id3", catalogBackendOwnerRequestId);
            Assert.AreEqual(1, (int)checkCatalogEvidenceCmd.ExecuteScalar()!, "Latest active Catalog definition evidence must survive archival.");
        }

        using (var context = _factory.GetContext())
        {
            Assert.IsFalse(context.DeploymentRequests.Any(r => r.Id == oldCompletedRequestId || r.Id == olderUnarchivedCompletedRequestId));
            Assert.IsTrue(context.DeploymentRequests.Any(r => r.Id == oldActiveWaitingRequestId), "Non-terminal WaitingConfirmation request must NOT be archived.");
            Assert.IsTrue(context.DeploymentRequests.Any(r => r.Id == boundaryNextYearRequestId), "@LastYear + 1 request must NOT be archived.");

            var project = context.Projects.Single(p => p.Id == projectId);
            var conflictEx = Assert.Throws<ArgumentException>(() =>
                Dorc.PersistentData.Sources.ProjectTerraformBackends.ValidateChange(
                    context,
                    project,
                    new[]
                    {
                        new Dorc.ApiModel.TerraformStateBackendApiModel
                        {
                            StorageAccount = "otheracct",
                            ContainerName = "tfstate",
                            SubscriptionId = "11111111-1111-1111-1111-111111111111"
                        }
                    }));
            StringAssert.Contains(conflictEx.Message, "This Terraform backend is bound to submitted requests");
        }
    }
}
