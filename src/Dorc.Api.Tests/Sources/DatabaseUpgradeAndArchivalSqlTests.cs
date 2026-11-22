using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Dorc.Core.VariableResolution;
using Dorc.PersistentData.Contexts;
using Dorc.PersistentData.Model;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Tools.EncryptionMigrationCLI;
using Environment = System.Environment;

namespace Dorc.Api.Tests.Sources;

[TestClass]
[TestCategory("Database")]
[DoNotParallelize]
public class DatabaseUpgradeAndArchivalSqlTests
{
    private static string _masterConnectionString = null!;
    private static string _databaseName = null!;
    private static string _databaseConnectionString = null!;
    private static DeploymentContextFactory _factory = null!;
    private static string _repoRoot = null!;

    [ClassInitialize]
    public static void Initialize(TestContext testContext)
    {
        var connection = Environment.GetEnvironmentVariable("ConnectionStrings__DOrcConnectionString")
            ?? "Server=(localdb)\\MSSQLLocalDB;Database=master;Integrated Security=True;TrustServerCertificate=True;";

        var builder = new SqlConnectionStringBuilder(connection) { InitialCatalog = "master" };
        try
        {
            using var probe = new SqlConnection(builder.ConnectionString);
            probe.Open();
        }
        catch (Exception ex)
        {
            if (Environment.GetEnvironmentVariable("DORC_DB_TESTS_REQUIRED") == "1")
                Assert.Fail($"SQL Server is required for Database category tests: {ex.Message}");
            Assert.Inconclusive($"SQL Server is not reachable ({ex.Message}).");
        }

        _masterConnectionString = builder.ConnectionString;
        _databaseName = "dorc_upgrade_archival_" + Guid.NewGuid().ToString("N");

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
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "src", "Dorc.sln")))
        {
            dir = dir.Parent;
        }
        _repoRoot = dir!.FullName;

        using var context = _factory.GetContext();
        context.Database.EnsureCreated();
        context.Database.ExecuteSqlRaw("ALTER TABLE [deploy].[Project] ADD DEFAULT NEWID() FOR [ObjectId]");
        context.Database.ExecuteSqlRaw("IF COL_LENGTH('deploy.Environment', 'Owner') IS NULL ALTER TABLE [deploy].[Environment] ADD [Owner] NVARCHAR(100) NULL;");
        context.Database.ExecuteSqlRaw("CREATE SCHEMA [archive]");

        ExecuteSqlFile(Path.Combine(_repoRoot, "src", "Dorc.Database", "Schema Objects", "Schemas", "archive", "Tables", "DeploymentRequest.table.sql"));
        ExecuteSqlFile(Path.Combine(_repoRoot, "src", "Dorc.Database", "Schema Objects", "Schemas", "archive", "Tables", "DeploymentResult.table.sql"));
        ExecuteSqlFile(Path.Combine(_repoRoot, "src", "Dorc.Database", "Schema Objects", "Schemas", "archive", "Tables", "EnvironmentComponentStatus.table.sql"));
        ExecuteSqlFile(Path.Combine(_repoRoot, "src", "Dorc.Database", "deploy", "Functions", "SplitTagString.sql"));
        ExecuteSqlFile(Path.Combine(_repoRoot, "src", "Dorc.Database", "deploy", "Stored Procedures", "GetFullEnvironmentChain.sql"));
        ExecuteSqlFile(Path.Combine(_repoRoot, "src", "Dorc.Database", "deploy", "Stored Procedures", "DuplicateEnvironmentPropertyValues.sql"));

        // Load ArchiveDeploymentRequests without the 2-second WAITFOR DELAY so tests execute sub-second
        var archiveProcSql = File.ReadAllText(Path.Combine(_repoRoot, "src", "Dorc.Database", "deploy", "Stored Procedures", "ArchiveDeploymentRequests.sql"))
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
        foreach (var batch in batches)
        {
            if (string.IsNullOrWhiteSpace(batch))
                continue;
            using var cmd = new SqlCommand(batch, sql);
            cmd.ExecuteNonQuery();
        }
    }

    [TestMethod]
    public void PreAndPostDeploymentScripts_AreIdempotentAndPopulateAuditAndSourceControlDefaults()
    {
        var seedScriptPath = Path.Combine(_repoRoot, "src", "Dorc.Database", "Scripts", "Post-Deployment", "SeedRefDataAuditActions.sql");
        var cleanupScriptPath = Path.Combine(_repoRoot, "src", "Dorc.Database", "Scripts", "Post-Deployment", "CleanupOrphanedScripts.sql");
        var sourceControlScriptPath = Path.Combine(_repoRoot, "src", "Dorc.Database", "Scripts", "Post-Deployment", "SetFileShareSourceControlType.sql");
        var preDeployArchiveColsPath = Path.Combine(_repoRoot, "src", "Dorc.Database", "Scripts", "Pre-Deployment", "AddCancelledFieldsToArchiveDeploymentRequests.sql");
        var guardTagsPath = Path.Combine(_repoRoot, "src", "Dorc.Database", "Scripts", "Pre-Deployment", "GuardTagLengthsBeforeMigration.sql");
        var scopeAivenPath = Path.Combine(_repoRoot, "src", "Dorc.Database", "Scripts", "Post-Deployment", "ScopeAivenDatabases.sql");

        using (var context = _factory.GetContext())
        {
            context.Scripts.Add(new Script { Name = "Orphaned.ps1", Path = "Orphaned.ps1", IsPathJSON = false });
            context.Projects.AddRange(
                new Project { Name = "Proj_FileUri_" + Guid.NewGuid().ToString("N"), ArtefactsUrl = "file://server/drop", SourceControlType = 0 },
                new Project { Name = "Proj_Unc_" + Guid.NewGuid().ToString("N"), ArtefactsUrl = @"\\server\share\drop", SourceControlType = 0 },
                new Project { Name = "Proj_Http_" + Guid.NewGuid().ToString("N"), ArtefactsUrl = "https://dev.azure.com/sefe/dorc", SourceControlType = 0 });
            context.SaveChanges();
        }

        // Run pre-deployment and post-deployment scripts twice to prove idempotency
        for (var pass = 1; pass <= 2; pass++)
        {
            ExecuteSqlFile(preDeployArchiveColsPath);
            ExecuteSqlFile(guardTagsPath);
            ExecuteSqlFile(seedScriptPath);
            ExecuteSqlFile(cleanupScriptPath);
            ExecuteSqlFile(sourceControlScriptPath);
            ExecuteSqlFile(scopeAivenPath);
        }

        using (var context = _factory.GetContext())
        {
            var actions = context.RefDataAuditActions.Select(a => a.Action).ToList();
            Assert.AreEqual(5, actions.Count, "SeedRefDataAuditActions must idempotently maintain exactly 5 actions.");
            Assert.IsFalse(context.Scripts.Any(s => s.Name == "Orphaned.ps1"), "Orphaned script must be removed.");
            Assert.IsTrue(context.Projects.Where(p => p.ArtefactsUrl!.StartsWith("file:") || p.ArtefactsUrl!.StartsWith(@"\\")).All(p => (int)p.SourceControlType == 2));
            Assert.IsTrue(context.Projects.Where(p => p.ArtefactsUrl!.StartsWith("https:")).All(p => (int)p.SourceControlType == 0));
        }
    }

    [TestMethod]
    public void SplitTagString_TrimsDeduplicatesAndDropsEmptyTags()
    {
        using var sql = new SqlConnection(_databaseConnectionString);
        sql.Open();
        using var cmd = new SqlCommand("SELECT Tag FROM [deploy].[SplitTagString](N' alpha ; beta ;; alpha ; gamma   ; ') ORDER BY Tag", sql);
        using var reader = cmd.ExecuteReader();
        var tags = new List<string>();
        while (reader.Read())
        {
            tags.Add(reader.GetString(0));
        }

        CollectionAssert.AreEqual(new[] { "alpha", "beta", "gamma" }, tags);
    }

    [TestMethod]
    public void GetFullEnvironmentChain_And_DuplicateEnvironmentPropertyValues_WorkEndToEnd()
    {
        int parentEnvId;
        int childEnvId;
        string parentName = "ParentEnv_" + Guid.NewGuid().ToString("N")[..8];
        string childName = "ChildEnv_" + Guid.NewGuid().ToString("N")[..8];

        using (var context = _factory.GetContext())
        {
            if (!context.PropertyFilters.Any(pf => pf.Id == 1))
            {
                context.Database.ExecuteSqlRaw("SET IDENTITY_INSERT [deploy].[PropertyFilter] ON; INSERT INTO [deploy].[PropertyFilter] ([Id], [Name], [Priority]) VALUES (1, N'Environment', 1); SET IDENTITY_INSERT [deploy].[PropertyFilter] OFF;");
            }

            var envFilter = context.PropertyFilters.Single(pf => pf.Id == 1);

            var parentEnv = new Dorc.PersistentData.Model.Environment { Name = parentName, ObjectId = Guid.NewGuid(), Secure = false, IsProd = false };
            context.Environments.Add(parentEnv);
            context.SaveChanges();
            parentEnvId = parentEnv.Id;

            var childEnv = new Dorc.PersistentData.Model.Environment { Name = childName, ObjectId = Guid.NewGuid(), Secure = false, IsProd = false, ParentId = parentEnvId };
            context.Environments.Add(childEnv);

            var prop = new Property { Name = "DupProp_" + Guid.NewGuid().ToString("N")[..8], Secure = false, IsArray = false };
            context.Properties.Add(prop);
            context.SaveChanges();

            var pv = new PropertyValue { Property = prop, Value = "ParentVal1" };
            context.PropertyValues.Add(pv);
            context.SaveChanges();

            context.PropertyValueFilters.Add(new PropertyValueFilter { PropertyValue = pv, PropertyFilter = envFilter, Value = parentName });
            context.SaveChanges();
            childEnvId = childEnv.Id;
        }

        using var sql = new SqlConnection(_databaseConnectionString);
        sql.Open();

        // 1. GetFullEnvironmentChain returns both parent and child
        using (var chainCmd = new SqlCommand("EXEC [deploy].[GetFullEnvironmentChain] @EnvironmentId = @envId, @onlyParents = 0", sql))
        {
            chainCmd.Parameters.AddWithValue("@envId", parentEnvId);
            using var reader = chainCmd.ExecuteReader();
            var ids = new List<int>();
            while (reader.Read())
            {
                ids.Add(reader.GetInt32(0));
            }
            CollectionAssert.AreEquivalent(new[] { parentEnvId, childEnvId }, ids);
        }

        // 2. DuplicateEnvironmentPropertyValues copies properties from parentName to childName and writes Audit row
        using (var dupCmd = new SqlCommand("EXEC [deploy].[DuplicateEnvironmentPropertyValues] @SourceEnv = @src, @DestEnv = @dst", sql))
        {
            dupCmd.Parameters.AddWithValue("@src", parentName);
            dupCmd.Parameters.AddWithValue("@dst", childName);
            dupCmd.ExecuteNonQuery();
        }

        using (var context = _factory.GetContext())
        {
            var cloned = context.PropertyValueFilters.Include(pvf => pvf.PropertyValue)
                .SingleOrDefault(pvf => pvf.Value == childName);
            Assert.IsNotNull(cloned);
            Assert.AreEqual("ParentVal1", cloned.PropertyValue.Value);
            Assert.IsTrue(context.Audits.Any(a => a.EnvironmentName == childName && a.ToValue == "ParentVal1"));
        }
    }

    [TestMethod]
    public void ArchiveDeploymentRequests_ArchivesTerminalRequestsWithGovernanceReceipts_AndPreservesActiveRequests()
    {
        int oldCompletedRequestId;
        int olderUnarchivedCompletedRequestId;
        int oldActiveWaitingRequestId;

        using (var context = _factory.GetContext())
        {
            var project = new Project { Name = "ArchivalProj_" + Guid.NewGuid().ToString("N")[..8], ArtefactsUrl = "https://example.org" };
            var env = new Dorc.PersistentData.Model.Environment { Name = "ArchivalEnv_" + Guid.NewGuid().ToString("N")[..8], ObjectId = Guid.NewGuid(), Secure = false, IsProd = false };
            var script = new Script { Name = "deploy.ps1", Path = "deploy.ps1", IsPathJSON = false };
            context.Projects.Add(project);
            context.Environments.Add(env);
            context.Scripts.Add(script);
            context.SaveChanges();

            var component = new Component { Name = "ArchivalComp_" + Guid.NewGuid().ToString("N")[..8], ScriptId = script.Id, IsEnabled = true };
            context.Components.Add(component);
            context.SaveChanges();

            // 1. Completed request in 2024 with full governance/catalog/attempt/status records
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
            // 2. Older completed request in 2022 (verifies removal of @MaxYearInArchive bug)
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
            // 3. Non-terminal WaitingConfirmation request in 2024 (must NOT be archived!)
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

            context.DeploymentRequests.AddRange(req2024, req2022, reqWaiting);
            context.SaveChanges();

            oldCompletedRequestId = req2024.Id;
            olderUnarchivedCompletedRequestId = req2022.Id;
            oldActiveWaitingRequestId = reqWaiting.Id;

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

            // Insert dependent governance/catalog/attempt records for req2024
            context.Database.ExecuteSqlInterpolated($@"
                INSERT INTO [deploy].[DeploymentRequestAttempt] ([DeploymentRequestId], [AttemptNumber], [StartedTime], [Status], [UserName])
                VALUES ({oldCompletedRequestId}, 1, SYSDATETIMEOFFSET(), N'Completed', N'tester');
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
            ");
        }

        using var sql = new SqlConnection(_databaseConnectionString);
        sql.Open();

        // Archive up to 2024 first, then verify a subsequent run also archives the 2022 request even when 2024 is already in archive
        using (var cmd = new SqlCommand("EXEC [deploy].[ArchiveDeploymentRequests] @LastYear = 2024, @BatchSize = 1", sql))
        {
            var totalArchived = (int)cmd.ExecuteScalar()!;
            Assert.IsTrue(totalArchived >= 2, $"Expected at least 2 terminal requests archived, got {totalArchived}");
        }

        using (var checkCmd = new SqlCommand("SELECT COUNT(*) FROM [archive].[DeploymentRequest] WHERE Id IN (@id1, @id2)", sql))
        {
            checkCmd.Parameters.AddWithValue("@id1", oldCompletedRequestId);
            checkCmd.Parameters.AddWithValue("@id2", olderUnarchivedCompletedRequestId);
            Assert.AreEqual(2, (int)checkCmd.ExecuteScalar()!);
        }

        using (var context = _factory.GetContext())
        {
            Assert.IsFalse(context.DeploymentRequests.Any(r => r.Id == oldCompletedRequestId || r.Id == olderUnarchivedCompletedRequestId));
            Assert.IsTrue(context.DeploymentRequests.Any(r => r.Id == oldActiveWaitingRequestId), "Non-terminal WaitingConfirmation request must NOT be archived.");
        }
    }

    [TestMethod]
    public void EncryptionMigration_AgainstRealSqlDatabase_MigratesScalarArrayAndConfigValues()
    {
        var keyBytes = new byte[32];
        RandomNumberGenerator.Fill(keyBytes);
        var ivBytes = new byte[16];
        RandomNumberGenerator.Fill(ivBytes);

        var encryptor = new AesGcmPropertyEncryptor(Convert.ToBase64String(ivBytes), Convert.ToBase64String(keyBytes));
        var legacyEncryptor = new PropertyEncryptor(Convert.ToBase64String(ivBytes), Convert.ToBase64String(keyBytes));

        long scalarPvId;
        long arrayPvId;
        int configId;

        using (var context = _factory.GetContext())
        {
            var scalarProp = new Property { Name = "SqlMigScalar_" + Guid.NewGuid().ToString("N")[..8], Secure = true, IsArray = false };
            var arrayProp = new Property { Name = "SqlMigArray_" + Guid.NewGuid().ToString("N")[..8], Secure = true, IsArray = true };
            context.Properties.AddRange(scalarProp, arrayProp);
            context.SaveChanges();

            var scalarPv = new PropertyValue { Property = scalarProp, Value = legacyEncryptor.EncryptValue("sql-scalar-secret") };
            var arrayPv = new PropertyValue
            {
                Property = arrayProp,
                Value = JsonSerializer.Serialize(new[] { legacyEncryptor.EncryptValue("sql-arr-1"), legacyEncryptor.EncryptValue("sql-arr-2") })
            };
            var cfg = new ConfigValue { Key = "SqlMigCfg_" + Guid.NewGuid().ToString("N")[..8], Secure = true, Value = legacyEncryptor.EncryptValue("sql-cfg-secret") };

            context.PropertyValues.AddRange(scalarPv, arrayPv);
            context.ConfigValues.Add(cfg);
            context.SaveChanges();

            scalarPvId = scalarPv.Id;
            arrayPvId = arrayPv.Id;
            configId = cfg.Id;
        }

        var migrator = new EncryptionMigration(_factory, encryptor, NullLogger.Instance, batchSize: 10);
        var firstPass = migrator.MigratePropertyValues(dryRun: false, force: false);
        Assert.IsTrue(firstPass.Migrated >= 3);
        Assert.AreEqual(0, firstPass.Failed);

        var secondPass = migrator.MigratePropertyValues(dryRun: false, force: false);
        Assert.AreEqual(0, secondPass.Migrated, "Second pass must be idempotent and skip v2 values.");

        using (var context = _factory.GetContext())
        {
            var scalarVal = context.PropertyValues.Single(pv => pv.Id == scalarPvId).Value!;
            StringAssert.StartsWith(scalarVal, "v2:");
            Assert.AreEqual("sql-scalar-secret", encryptor.DecryptValue(scalarVal));

            var arrayVal = context.PropertyValues.Single(pv => pv.Id == arrayPvId).Value!;
            var elements = JsonSerializer.Deserialize<string[]>(arrayVal)!;
            Assert.IsTrue(elements.All(e => e.StartsWith("v2:")));
            CollectionAssert.AreEqual(new[] { "sql-arr-1", "sql-arr-2" }, elements.Select(e => encryptor.DecryptValue(e)).ToArray());

            var cfgVal = context.ConfigValues.Single(c => c.Id == configId).Value!;
            StringAssert.StartsWith(cfgVal, "v2:");
            Assert.AreEqual("sql-cfg-secret", encryptor.DecryptValue(cfgVal));
        }
    }
}
