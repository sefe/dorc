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
public class DuplicateEnvironmentPropertyValuesTests
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
        _databaseName = "dorc_env_dup_" + Guid.NewGuid().ToString("N");

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
        ExecuteSqlFile(Path.Join(_repoRoot, "src", "Dorc.Database", "Schema Objects", "Schemas", "archive", "Tables", "EnvironmentComponentStatus.table.sql"));
        ExecuteSqlFile(Path.Join(_repoRoot, "src", "Dorc.Database", "deploy", "Functions", "SplitTagString.sql"));
        ExecuteSqlFile(Path.Join(_repoRoot, "src", "Dorc.Database", "deploy", "Stored Procedures", "GetFullEnvironmentChain.sql"));
        ExecuteSqlFile(Path.Join(_repoRoot, "src", "Dorc.Database", "deploy", "Stored Procedures", "DuplicateEnvironmentPropertyValues.sql"));
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
        var batches = Regex.Split(File.ReadAllText(filePath), @"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase);
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
    public void PreAndPostDeploymentScripts_AreIdempotentAndPopulateAuditAndSourceControlDefaults()
    {
        var seedScriptPath = Path.Join(_repoRoot, "src", "Dorc.Database", "Scripts", "Post-Deployment", "SeedRefDataAuditActions.sql");
        var cleanupScriptPath = Path.Join(_repoRoot, "src", "Dorc.Database", "Scripts", "Post-Deployment", "CleanupOrphanedScripts.sql");
        var sourceControlScriptPath = Path.Join(_repoRoot, "src", "Dorc.Database", "Scripts", "Post-Deployment", "SetFileShareSourceControlType.sql");
        var preDeployArchiveColsPath = Path.Join(_repoRoot, "src", "Dorc.Database", "Scripts", "Pre-Deployment", "AddCancelledFieldsToArchiveDeploymentRequests.sql");
        var guardTagsPath = Path.Join(_repoRoot, "src", "Dorc.Database", "Scripts", "Pre-Deployment", "GuardTagLengthsBeforeMigration.sql");
        var scopeAivenPath = Path.Join(_repoRoot, "src", "Dorc.Database", "Scripts", "Post-Deployment", "ScopeAivenDatabases.sql");

        using (var context = _factory.GetContext())
        {
            context.Scripts.Add(new Script { Name = "Orphaned.ps1", Path = "Orphaned.ps1", IsPathJSON = false });
            context.Projects.AddRange(
                new Project { Name = "Proj_FileUri_" + Guid.NewGuid().ToString("N"), ArtefactsUrl = "file://server/drop", SourceControlType = 0 },
                new Project { Name = "Proj_Unc_" + Guid.NewGuid().ToString("N"), ArtefactsUrl = @"\\server\share\drop", SourceControlType = 0 },
                new Project { Name = "Proj_Http_" + Guid.NewGuid().ToString("N"), ArtefactsUrl = "https://dev.azure.com/sefe/dorc", SourceControlType = 0 });
            context.SaveChanges();
        }

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
}
