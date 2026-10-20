using System.Text.Json;
using Dorc.ApiModel;
using Dorc.PersistentData;
using Dorc.PersistentData.Contexts;
using Dorc.PersistentData.Model;
using Dorc.PersistentData.Sources;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Environment = System.Environment;
using Dorc.Api.Controllers;
using Dorc.Core.Interfaces;
using Dorc.PersistentData.Sources.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace Dorc.Api.Tests.Sources;

[TestClass]
[TestCategory("Database")]
[DoNotParallelize]
public class DeploymentDefinitionsSqlTests
{
    private static string master = null!;
    private static string database = null!;
    private static DeploymentContextFactory factory = null!;
    private static DeploymentDefinitions definitions = null!;

    [ClassInitialize]
    public static void Initialize(TestContext testContext)
    {
        var connection = Environment.GetEnvironmentVariable("ConnectionStrings__DOrcConnectionString");
        if (string.IsNullOrWhiteSpace(connection))
        {
            if (Environment.GetEnvironmentVariable("DORC_DB_TESTS_REQUIRED") == "1") Assert.Fail("SQL is required.");
            Assert.Inconclusive("Configure ConnectionStrings__DOrcConnectionString.");
        }
        var builder = new SqlConnectionStringBuilder(connection) { InitialCatalog = "master" };
        master = builder.ConnectionString;
        database = "dorc_definitions_test_" + Guid.NewGuid().ToString("N");
        using (var sql = new SqlConnection(master))
        {
            sql.Open();
            new SqlCommand($"CREATE DATABASE [{database}] COLLATE SQL_Latin1_General_CP1_CI_AS", sql).ExecuteNonQuery();
        }
        builder.InitialCatalog = database;
        factory = new DeploymentContextFactory(builder.ConnectionString);
        using var context = factory.GetContext();
        context.Database.EnsureCreated();
        context.Database.ExecuteSqlRaw("ALTER TABLE [deploy].[Project] ADD DEFAULT NEWID() FOR [ObjectId]");
        context.RefDataAuditActions.AddRange(new Dorc.PersistentData.Model.RefDataAuditAction { Action = ActionType.Create },
            new Dorc.PersistentData.Model.RefDataAuditAction { Action = ActionType.Update });
        context.SaveChanges();
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root != null && !File.Exists(Path.Combine(root.FullName, "src", "Dorc.sln"))) root = root.Parent;
        context.Database.ExecuteSqlRaw(File.ReadAllText(Path.Combine(root!.FullName, "src", "Dorc.Database",
            "Schema Objects", "Schemas", "deploy", "Views", "DeploymentDefinitionHistoryView.view.sql")));
        definitions = new DeploymentDefinitions(factory);
    }

    [ClassCleanup]
    public static void Cleanup()
    {
        if (database == null) return;
        using var sql = new SqlConnection(master); sql.Open();
        new SqlCommand($"ALTER DATABASE [{database}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{database}]", sql).ExecuteNonQuery();
    }

    private static RefDataApiModel Create() => definitions.Save(new RefDataApiModel
    {
        Project = new ProjectApiModel { ProjectName = Guid.NewGuid().ToString("N"), ArtefactsUrl = "https://example.org/build" },
        Components = new List<ComponentApiModel>
        {
            new() { ComponentId = 0, ComponentName = Guid.NewGuid().ToString("N"), ScriptPath = "original.ps1",
                PSVersion = "7", IsEnabled = true, Children = new List<ComponentApiModel>() }
        }
    }, true, "creator", _ => true, () => { });

    [TestMethod]
    public void MissingAndStaleRevision_DoNotMutateOrAudit()
    {
        var document = Create();
        var stale = definitions.Get(document.Project.ProjectId);
        document.Components[0].ScriptPath = "updated.ps1";
        var saved = definitions.Save(document, false, "editor", _ => true, () => { });
        Assert.AreNotEqual(document.Revision, saved.Revision);
        var conflict = Assert.Throws<DefinitionPreconditionException>(() =>
            definitions.Save(stale, false, "stale", _ => true, () => { }));
        Assert.AreEqual(409, conflict.StatusCode);
        stale.Revision = null!;
        Assert.AreEqual(428, Assert.Throws<DefinitionPreconditionException>(() =>
            definitions.Save(stale, false, "missing", _ => true, () => { })).StatusCode);
        using var context = factory.GetContext();
        Assert.AreEqual(2, context.DeploymentDefinitionChanges.Count(c => c.ProjectId == document.Project.ProjectId));
        Assert.AreEqual(2, context.RefDataAudits.Count(c => c.ProjectId == document.Project.ProjectId && c.ChangeId != null));
        Assert.AreEqual("updated.ps1", definitions.Get(document.Project.ProjectId).Components[0].ScriptPath);
    }

    [TestMethod]
    public void DefinitionsAndHistory_FilterPermissionsBeforeCountAndPaging()
    {
        var first = Create(); var hidden = Create();
        var projects = Substitute.For<IProjectsPersistentSource>();
        projects.GetProjects(Arg.Any<System.Security.Principal.IPrincipal>()).Returns(new[] { first.Project, hidden.Project });
        var security = Substitute.For<ISecurityPrivilegesChecker>();
        security.CanModifyProject(Arg.Any<ClaimsPrincipal>(), first.Project.ProjectId).Returns(true);
        var controller = new DeploymentDefinitionsController(factory, definitions, security, projects)
        { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() } };
        var listed = (GetDeploymentDefinitionsResponse)((ObjectResult)controller.Get(limit: 1)).Value!;
        Assert.AreEqual(1, listed.TotalItems);
        Assert.AreEqual(first.Project.ProjectId, listed.Items.Single().ProjectId);
        var history = (GetDeploymentDefinitionHistoryResponse)((ObjectResult)controller.History(limit: 1)).Value!;
        Assert.AreEqual(1, history.TotalItems);
        Assert.AreEqual(first.Project.ProjectId, history.Items.Single().ProjectId);
        var empty = (GetDeploymentDefinitionHistoryResponse)((ObjectResult)controller.History(hidden.Project.ProjectId)).Value!;
        Assert.AreEqual(0, empty.TotalItems);
    }

    [TestMethod]
    [DataRow("{\"ScriptPath\":\"deploy.ps1\",\"GenericArguments\":[{\"password\":\"literal\"}]}")]
    [DataRow("https://user:password@example.org/module")]
    [DataRow("https://example.org/module?sig=credential")]
    public void SensitiveDefinitions_FailBeforeMutation(string value)
    {
        var document = Create(); document.Components[0].ScriptPath = value;
        Assert.Throws<ArgumentException>(() => definitions.Save(document, false, "editor", _ => true, () => { }));
        Assert.AreEqual(document.Revision, definitions.Get(document.Project.ProjectId).Revision);
    }

    [TestMethod]
    public void LateCollision_RollsBackProjectAndEarlierComponentWrites()
    {
        var document = Create();
        var original = document.Revision;
        document.Project.ProjectDescription = "must rollback";
        document.Components[0].ScriptPath = "must-rollback.ps1";
        document.Components.Add(new ComponentApiModel
        { ComponentId = 0, ComponentName = document.Components[0].ComponentName, ScriptPath = "other.ps1" });
        Assert.Throws<ArgumentException>(() => definitions.Save(document, false, "editor", _ => true, () => { }));
        Assert.AreEqual(original, definitions.Get(document.Project.ProjectId).Revision);
    }

    [TestMethod]
    public async Task ConcurrentProjectSaves_AllowOnlyOneExpectedRevision()
    {
        var created = Create();
        var left = definitions.Get(created.Project.ProjectId);
        var right = definitions.Get(created.Project.ProjectId);
        left.Project.ProjectDescription = "left";
        right.Project.ProjectDescription = "right";
        int Save(RefDataApiModel input)
        {
            try { definitions.Save(input, false, "editor", _ => true, () => { }); return 200; }
            catch (DefinitionPreconditionException error) { return error.StatusCode; }
        }
        var outcomes = await Task.WhenAll(Task.Run(() => Save(left)), Task.Run(() => Save(right)));
        CollectionAssert.AreEqual(new[] { 200, 409 }, outcomes.Order().ToArray());
        using var context = factory.GetContext();
        Assert.AreEqual(2, context.DeploymentDefinitionChanges.Count(c => c.ProjectId == created.Project.ProjectId));
    }

    [TestMethod]
    public void AuditStoreFailure_RollsBackEntireMutation()
    {
        var document = Create();
        using (var context = factory.GetContext())
            context.Database.ExecuteSqlRaw("CREATE TRIGGER [deploy].[RejectDefinitionAudit] ON [deploy].[DeploymentDefinitionChange] AFTER INSERT AS THROW 51000, 'Injected audit failure', 1;");
        try
        {
            document.Components[0].ScriptPath = "rollback.ps1";
            Assert.Throws<DbUpdateException>(() => definitions.Save(document, false, "editor", _ => true, () => { }));
            Assert.AreEqual(document.Revision, definitions.Get(document.Project.ProjectId).Revision);
        }
        finally
        {
            using var context = factory.GetContext();
            context.Database.ExecuteSqlRaw("DROP TRIGGER [deploy].[RejectDefinitionAudit]");
        }
    }

    [TestMethod]
    public void Detach_PreservesOtherMembershipAndHistory()
    {
        var document = Create(); var other = Create();
        using (var context = factory.GetContext())
        {
            var component = context.Components.Include(c => c.Projects).Single(c => c.Id == document.Components[0].ComponentId);
            component.Projects.Add(context.Projects.Single(p => p.Id == other.Project.ProjectId));
            context.SaveChanges();
        }
        document = definitions.Get(document.Project.ProjectId);
        var id = document.Components[0].ComponentId;
        document.Components.Clear();
        definitions.Save(document, false, "editor", _ => true, () => { });
        using var check = factory.GetContext();
        var stored = check.Components.Include(c => c.Projects).Single(c => c.Id == id);
        CollectionAssert.AreEqual(new[] { other.Project.ProjectId }, stored.Projects.Select(p => p.Id).ToArray());
        Assert.IsTrue(check.DeploymentDefinitionRevisions.Any(r => r.ComponentId == id));
    }

    [TestMethod]
    public void SharedScript_LocalEditCopiesButGlobalEditRequiresAllConsumers()
    {
        var first = Create(); var second = Create();
        int scriptId;
        using (var context = factory.GetContext())
        {
            var a = context.Components.Include(c => c.Script).Single(c => c.Id == first.Components[0].ComponentId);
            var b = context.Components.Single(c => c.Id == second.Components[0].ComponentId);
            b.Script = a.Script; scriptId = a.Script!.Id; context.SaveChanges();
        }
        var scripts = new ScriptsPersistentSource(factory, Substitute.For<IClaimsPrincipalReader>(),
            Substitute.For<PersistentData.Sources.Interfaces.IScriptsAuditPersistentSource>());
        var global = scripts.GetScript(scriptId);
        global.ConfirmImpact = true; global.Path = "global.ps1";
        Assert.Throws<UnauthorizedAccessException>(() => definitions.EditScript(global, "editor", id => id == first.Project.ProjectId));
        first = definitions.Get(first.Project.ProjectId); first.Components[0].ScriptPath = "local.ps1";
        definitions.Save(first, false, "editor", _ => true, () => { });
        Assert.AreEqual("original.ps1", definitions.Get(second.Project.ProjectId).Components[0].ScriptPath);
        Assert.Throws<DefinitionPreconditionException>(() => definitions.EditScript(global, "editor", _ => true));
    }

    [TestMethod]
    public void SubmittedGraphSurvivesSourceRuntimeAndNameEdits()
    {
        var document = Create();
        var requests = new RequestsPersistentSource(factory, Substitute.For<IClaimsPrincipalReader>());
        var requestId = requests.SubmitRequest(new DeploymentRequest
        {
            Project = document.Project.ProjectName, Components = document.Components[0].ComponentName,
            RequestDetails = "<root/>", UserName = "requester", RequestedTime = DateTimeOffset.UtcNow
        });
        document.Components[0].ScriptPath = "changed.ps1";
        document.Components[0].PSVersion = "5";
        document.Components[0].ComponentName = Guid.NewGuid().ToString("N");
        document.Project.TerraformGitRepoUrl = "https://example.org/changed";
        definitions.Save(document, false, "editor", _ => true, () => { });
        Assert.AreEqual("original.ps1", requests.GetSubmittedScript(requestId, document.Components[0].ComponentId!.Value).Path);
        Assert.IsNull(requests.GetSubmittedProject(requestId, document.Components[0].ComponentId!.Value).TerraformGitRepoUrl);
        Assert.Throws<InvalidOperationException>(() => requests.GetRequestDefinitions(int.MaxValue));
    }

    [TestMethod]
    [DataRow(TerraformSourceType.SharedFolder)]
    [DataRow(TerraformSourceType.Git)]
    [DataRow(TerraformSourceType.AzureArtifact)]
    [DataRow(TerraformSourceType.GitHubArtifact)]
    [DataRow(TerraformSourceType.Catalog)]
    public void AllTerraformSources_RoundTripWithoutFabricatedScripts(TerraformSourceType source)
    {
        var document = Create();
        var component = document.Components[0];
        component.ComponentType = ComponentType.Terraform; component.TerraformSourceType = source;
        component.ScriptPath = source == TerraformSourceType.SharedFolder ? @"\\allowed\module" : "";
        component.TerraformTemplateName = source == TerraformSourceType.Catalog ? "module" : null;
        component.TerraformTemplateVersion = source == TerraformSourceType.Catalog ? "1.0.0" : null;
        component.TerraformSubPath = "module";
        var saved = definitions.Save(document, false, "editor", _ => true, () => { });
        var roundtrip = JsonSerializer.Deserialize<RefDataApiModel>(JsonSerializer.Serialize(saved))!;
        Assert.AreEqual(source, roundtrip.Components[0].TerraformSourceType);
        Assert.AreEqual("module", roundtrip.Components[0].TerraformSubPath);
        using var context = factory.GetContext();
        Assert.AreEqual(source == TerraformSourceType.SharedFolder,
            context.Components.Single(c => c.Id == component.ComponentId).ScriptId.HasValue);
    }
}
