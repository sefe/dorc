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
        ExecuteSqlFile(Path.Join(_repoRoot, "src", "Dorc.Database", "deploy", "Stored Procedures", "get_environment_properties.sql"));
        ExecuteSqlFile(Path.Join(_repoRoot, "src", "Dorc.Database", "deploy", "Stored Procedures", "get_global_properties.sql"));
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

    [TestMethod]
    public void PersistentSources_GuardDuplicateNullAndOrphanedAccessControlCases()
    {
        var claimsReader = NSubstitute.Substitute.For<Dorc.PersistentData.IClaimsPrincipalReader>();
        var roleChecker = NSubstitute.Substitute.For<Dorc.PersistentData.IRolePrivilegesChecker>();
        var envSource = NSubstitute.Substitute.For<Dorc.PersistentData.Sources.Interfaces.IEnvironmentsPersistentSource>();
        var encryptor = NSubstitute.Substitute.For<Dorc.PersistentData.IPropertyEncryptor>();
        var nonAdminUser = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity("TestAuth"));
        var adminUser = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity("TestAuth"));
        NSubstitute.SubstituteExtensions.Returns(
            claimsReader.GetUserFullDomainName(NSubstitute.Arg.Any<System.Security.Principal.IPrincipal>()),
            "DOMAIN\\tester");
        NSubstitute.SubstituteExtensions.Returns(
            claimsReader.GetSidsForUser(NSubstitute.Arg.Any<System.Security.Principal.IPrincipal>()),
            new List<string> { "S-1-5-21-999" });
        NSubstitute.SubstituteExtensions.Returns(roleChecker.IsAdmin(nonAdminUser), false);
        NSubstitute.SubstituteExtensions.Returns(roleChecker.IsAdmin(adminUser), true);

        // 1. ProjectsPersistentSource.DeleteProject removes orphaned AccessControl rows matching project.ObjectId
        var allowList = new Dorc.PersistentData.Security.SourceHostAllowList(
            new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build());
        var projectsSource = new Dorc.PersistentData.Sources.ProjectsPersistentSource(
            _factory,
            envSource,
            NSubstitute.Substitute.For<Microsoft.Extensions.Logging.ILogger<Dorc.PersistentData.Sources.ProjectsPersistentSource>>(),
            claimsReader,
            allowList);

        int projId;
        Guid projObjId;
        using (var ctx = _factory.GetContext())
        {
            ctx.Database.ExecuteSqlRaw("IF OBJECT_ID('deploy.FK_AccessControl_Environment_ObjectId', 'F') IS NOT NULL ALTER TABLE [deploy].[AccessControl] DROP CONSTRAINT [FK_AccessControl_Environment_ObjectId];");
            var proj = new Project { Name = "DelProj_" + Guid.NewGuid().ToString("N")[..8] };
            ctx.Projects.Add(proj);
            ctx.SaveChanges();
            projId = proj.Id;
            projObjId = proj.ObjectId;
            ctx.AccessControls.Add(new AccessControl { ObjectId = projObjId, Name = "ACL1", Sid = "S-1-5-21-999", Allow = 1 });
            ctx.SaveChanges();
        }
        projectsSource.DeleteProject(projId);
        using (var ctx = _factory.GetContext())
        {
            Assert.IsFalse(ctx.Projects.Any(p => p.Id == projId));
            Assert.IsFalse(ctx.AccessControls.Any(ac => ac.ObjectId == projObjId));
        }

        // 2. ServersPersistentSource.GetServer(string) and GetServer(int) include Environments and set UserEditable=false on unattached server for non-admin
        var serversSource = new Dorc.PersistentData.Sources.ServersPersistentSource(_factory, roleChecker, claimsReader);
        int unattachedSrvId;
        int attachedSrvId;
        string unattachedSrvName = "SRV_UNATTACHED_" + Guid.NewGuid().ToString("N")[..6];
        string attachedSrvName = "SRV_ATTACHED_" + Guid.NewGuid().ToString("N")[..6];
        string attachedEnvName = "ENV_ATTACHED_" + Guid.NewGuid().ToString("N")[..6];
        using (var ctx = _factory.GetContext())
        {
            var envObjId = Guid.NewGuid();
            var env = new Dorc.PersistentData.Model.Environment { Name = attachedEnvName, ObjectId = envObjId };
            ctx.AccessControls.Add(new AccessControl { ObjectId = envObjId, Name = "EnvACL", Sid = "S-1-5-21-999", Allow = 1 });
            var s1 = new Server { Name = unattachedSrvName, OsName = "Win2022" };
            var s2 = new Server { Name = attachedSrvName, OsName = "Win2022" };
            s2.Environments.Add(env);
            ctx.Environments.Add(env);
            ctx.Servers.AddRange(s1, s2);
            ctx.SaveChanges();
            unattachedSrvId = s1.Id;
            attachedSrvId = s2.Id;
        }
        var unattachedByName = serversSource.GetServer(unattachedSrvName, nonAdminUser);
        var unattachedById = serversSource.GetServer(unattachedSrvId, nonAdminUser);
        Assert.IsNotNull(unattachedByName);
        Assert.IsNotNull(unattachedById);
        Assert.IsFalse(unattachedByName.UserEditable);
        Assert.IsFalse(unattachedById.UserEditable);
        Assert.IsTrue(serversSource.GetServer(unattachedSrvName, adminUser)!.UserEditable);

        var attachedByName = serversSource.GetServer(attachedSrvName, nonAdminUser);
        var attachedById = serversSource.GetServer(attachedSrvId, nonAdminUser);
        Assert.IsTrue(attachedByName!.UserEditable);
        Assert.IsTrue(attachedById!.UserEditable);
        CollectionAssert.Contains(attachedByName.EnvironmentNames.ToList(), attachedEnvName);

        // 3. PermissionsPersistentSource
        var permSource = new Dorc.PersistentData.Sources.PermissionsPersistentSource(_factory);
        Assert.IsNull(permSource.GetPermissions(999999));
        var permKey = "Perm_" + Guid.NewGuid().ToString("N")[..6];
        permSource.CreatePermission(new Dorc.ApiModel.PermissionDto { DisplayName = permKey + "_Disp", PermissionName = permKey });
        var createdPerm = permSource.GetAllPermissions().Single(p => p.PermissionName == permKey);
        Assert.IsNotNull(permSource.GetPermissions(createdPerm.Id));

        Assert.Throws<ArgumentException>(() => permSource.CreatePermission(
            new Dorc.ApiModel.PermissionDto { DisplayName = "OtherDisp", PermissionName = permKey }));
        Assert.Throws<ArgumentException>(() => permSource.UpdatePermission(
            999999, new Dorc.ApiModel.PermissionDto { DisplayName = "Missing", PermissionName = "Missing" }));

        var permKey2 = "Perm2_" + Guid.NewGuid().ToString("N")[..6];
        permSource.CreatePermission(new Dorc.ApiModel.PermissionDto { DisplayName = permKey2 + "_Disp", PermissionName = permKey2 });
        var createdPerm2 = permSource.GetAllPermissions().Single(p => p.PermissionName == permKey2);
        Assert.Throws<ArgumentException>(() => permSource.UpdatePermission(
            createdPerm2.Id, new Dorc.ApiModel.PermissionDto { DisplayName = "UniqueDisp", PermissionName = permKey }));

        permSource.UpdatePermission(createdPerm2.Id, new Dorc.ApiModel.PermissionDto { DisplayName = permKey2 + "_Upd", PermissionName = permKey2 + "_Upd" });
        permSource.DeletePermission(999999);
        permSource.DeletePermission(createdPerm2.Id);
        Assert.IsNull(permSource.GetPermissions(createdPerm2.Id));

        // 4. PropertyValuesPersistentSource
        var propValSource = new Dorc.PersistentData.Sources.PropertyValuesPersistentSource(_factory, encryptor, claimsReader);
        Assert.Throws<ArgumentException>(() => propValSource.UpdatePropertyValue(999999999, "val"));
        var propName = "CaseProp_" + Guid.NewGuid().ToString("N")[..6];
        using (var ctx = _factory.GetContext())
        {
            var p = new Property { Name = propName, Secure = false, IsArray = false };
            ctx.Properties.Add(p);
            ctx.SaveChanges();
            ctx.PropertyValues.Add(new PropertyValue { Property = p, Value = "DefaultVal" });
            ctx.SaveChanges();
        }
        var caseInsensitiveVals = propValSource.GetPropertyValues(propName.ToUpperInvariant(), attachedEnvName, false);
        Assert.AreEqual(1, caseInsensitiveVals.Count);
        Assert.AreEqual("DefaultVal", caseInsensitiveVals[0].Value);

        // 5. UsersPersistentSource
        var usersSource = new Dorc.PersistentData.Sources.UsersPersistentSource(_factory, claimsReader);
        Assert.IsNull(usersSource.GetUser("missing_lan_id_" + Guid.NewGuid().ToString("N")));
        var lanId = "lan_" + Guid.NewGuid().ToString("N")[..8];
        var addedUser = usersSource.AddUser(new Dorc.ApiModel.UserApiModel
        {
            LanId = lanId,
            DisplayName = "Test User",
            LoginType = "Windows",
            LanIdType = "USER",
            Team = "Core"
        });
        Assert.IsNotNull(usersSource.GetUser(lanId));
        Assert.Throws<ArgumentException>(() => usersSource.AddUser(new Dorc.ApiModel.UserApiModel
        {
            LanId = lanId,
            DisplayName = "Test User Dup",
            LoginType = "Windows",
            LanIdType = "USER",
            Team = "Core"
        }));
        Assert.AreEqual(0, usersSource.GetDatabaseUsers(999999).Count());

        // 6. EnvironmentHistoryPersistentSource.GetEnvironmentNameForHistoryId
        var envHistorySource = new Dorc.PersistentData.Sources.EnvironmentHistoryPersistentSource(
            NSubstitute.Substitute.For<Microsoft.Extensions.Logging.ILogger<Dorc.PersistentData.Sources.EnvironmentHistoryPersistentSource>>(),
            _factory);
        Assert.IsNull(envHistorySource.GetEnvironmentNameForHistoryId(999999999));
        Assert.IsTrue(envHistorySource.UpdateHistory(attachedEnvName, "bkp.bak", "History comment", "tester", "Deploy"));
        var envHistories = envHistorySource.GetEnvironmentDetailHistory(attachedEnv.EnvironmentId);
        Assert.IsTrue(envHistories.Count > 0);
        Assert.AreEqual(attachedEnvName, envHistorySource.GetEnvironmentNameForHistoryId(envHistories[0].Id));

        int orphanHistoryId;
        using (var ctx = _factory.GetContext())
        {
            var orphanHistory = new EnvironmentHistory
            {
                EnvId = null,
                Environment = null,
                UpdateDate = DateTime.UtcNow,
                UpdateType = "DeletedEnv",
                UpdatedBy = "tester",
                ToValue = "v1"
            };
            ctx.EnvironmentHistories.Add(orphanHistory);
            ctx.SaveChanges();
            orphanHistoryId = orphanHistory.Id;
        }
        Assert.IsNull(envHistorySource.GetEnvironmentNameForHistoryId(orphanHistoryId));
    }
}
