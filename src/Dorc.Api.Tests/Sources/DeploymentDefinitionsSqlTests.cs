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
        context.Database.ExecuteSqlRaw("CREATE SCHEMA [archive]");
        context.Database.ExecuteSqlRaw(File.ReadAllText(Path.Combine(root!.FullName, "src", "Dorc.Database",
            "Schema Objects", "Schemas", "archive", "Tables", "DeploymentRequest.table.sql")));
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

    private static (RefDataApiModel Document, TerraformBackendCutoverApiModel Cutover, int RequestId, string EnvironmentName)
        CreateCutover(string status = "Failed")
    {
        var document = Create();
        var component = document.Components.Single();
        component.ComponentType = ComponentType.Terraform;
        component.TerraformSourceType = TerraformSourceType.Catalog;
        component.TerraformTemplateName = "module";
        component.TerraformTemplateVersion = "1.0.0";
        component.ScriptPath = "";
        var name = Guid.NewGuid().ToString("N");
        int environmentId;
        using (var context = factory.GetContext())
        {
            context.Database.ExecuteSqlInterpolated(
                $"INSERT [deploy].[Environment] ([Name], [ObjectId], [Secure], [IsProd]) VALUES ({name}, NEWID(), 0, 0)");
            var environment = context.Environments.Single(e => e.Name == name);
            environment.Projects.Add(context.Projects.Single(p => p.Id == document.Project.ProjectId));
            context.SaveChanges();
            environmentId = environment.Id;
        }
        var oldBackend = new TerraformStateBackendApiModel
        {
            StorageAccount = "oldstate", ContainerName = "tfstate",
            SubscriptionId = "11111111-1111-1111-1111-111111111111"
        };
        document.Project.TerraformStateBackends = [oldBackend];
        document = definitions.Save(document, false, "editor", _ => true, () => { });
        var requests = new RequestsPersistentSource(factory, Substitute.For<IClaimsPrincipalReader>());
        var requestId = requests.SubmitRequest(new DeploymentRequest
        {
            Project = document.Project.ProjectName, Environment = name, Components = component.ComponentName,
            RequestDetails = "<root/>", UserName = "requester", RequestedTime = DateTimeOffset.UtcNow,
            Status = status
        });
        return (document, new TerraformBackendCutoverApiModel
        {
            Revision = document.Revision, EnvironmentId = environmentId, ExpectedBackend = oldBackend,
            Backend = new TerraformStateBackendApiModel
            {
                EnvironmentId = environmentId, StorageAccount = "newstate", ContainerName = "tfstate",
                SubscriptionId = oldBackend.SubscriptionId
            },
            ConfirmBothContainersEmpty = true, VerificationSha256 = new string('a', 64)
        }, requestId, name);
    }

    [TestMethod]
    public void EmptyBackendCutover_AuditsAndRetiresRequestsWithoutRewritingSnapshots()
    {
        var fixture = CreateCutover();
        var requests = new RequestsPersistentSource(factory, Substitute.For<IClaimsPrincipalReader>());
        var snapshot = requests.GetRequestDefinitions(fixture.RequestId).Single().ProjectJson;
        var saved = definitions.CutoverEmptyTerraformBackend(fixture.Document.Project.ProjectId,
            fixture.Cutover, "operator", _ => true, _ => true);
        Assert.AreEqual("newstate", saved.Project.TerraformStateBackends.Single(b => b.EnvironmentId.HasValue).StorageAccount);
        Assert.AreEqual("oldstate", saved.Project.TerraformStateBackends.Single(b => !b.EnvironmentId.HasValue).StorageAccount);
        Assert.AreNotEqual(fixture.Document.Revision, saved.Revision);
        Assert.AreEqual(snapshot, requests.GetRequestDefinitions(fixture.RequestId).Single().ProjectJson);
        Assert.Throws<TerraformBackendRetiredException>(() => requests.GetSubmittedComponents(fixture.RequestId));
        Assert.Throws<TerraformBackendRetiredException>(() => requests.GetSubmittedProject(fixture.RequestId,
            fixture.Document.Components.Single().ComponentId!.Value));
        Assert.Throws<TerraformBackendRetiredException>(() =>
            requests.UpdateRequestStatus(fixture.RequestId, DeploymentRequestStatus.Restarting, "restarter"));
        using var context = factory.GetContext();
        var request = context.DeploymentRequests.Single(r => r.Id == fixture.RequestId);
        Assert.AreEqual("Failed", request.Status);
        Assert.AreEqual("requester", request.UserName);
        var change = context.DeploymentDefinitionChanges.Single(c => c.Id == request.TerraformBackendRetiredByChangeId);
        Assert.AreEqual("operator", change.Actor);
        Assert.AreEqual("TerraformBackend.EmptyCutover", change.Operation);
        Assert.AreEqual("sha256:" + fixture.Cutover.VerificationSha256, change.SourceRecordId);
        Assert.IsTrue(change.BeforeJson.Contains("oldstate"));
        Assert.IsTrue(change.AfterJson.Contains("newstate"));
        var newRequestId = requests.SubmitRequest(new DeploymentRequest
        {
            Project = saved.Project.ProjectName, Environment = fixture.EnvironmentName,
            Components = saved.Components.Single().ComponentName, RequestDetails = "<root/>", UserName = "new-requester"
        });
        Assert.AreEqual("newstate", requests.GetSubmittedProject(newRequestId,
            saved.Components.Single().ComponentId!.Value).TerraformStateBackends.Single().StorageAccount);
        saved.Project.TerraformStateBackends.Single(b => b.EnvironmentId.HasValue).StorageAccount = "thirdstate";
        Assert.Throws<ArgumentException>(() => definitions.Save(saved, false, "editor", _ => true, () => { }));
    }

    [TestMethod]
    [DataRow("Pending")]
    [DataRow("Running")]
    [DataRow("WaitingConfirmation")]
    [DataRow("Paused")]
    public void EmptyBackendCutover_RejectsActiveRequests(string status)
    {
        var fixture = CreateCutover(status);
        Assert.Throws<InvalidOperationException>(() => definitions.CutoverEmptyTerraformBackend(
            fixture.Document.Project.ProjectId, fixture.Cutover, "operator", _ => true, _ => true));
        Assert.AreEqual(fixture.Document.Revision, definitions.Get(fixture.Document.Project.ProjectId).Revision);
    }

    [TestMethod]
    public void EmptyBackendCutover_RequiresRevisionAuthorizationAndVerification()
    {
        var fixture = CreateCutover();
        var projectId = fixture.Document.Project.ProjectId;
        Assert.Throws<UnauthorizedAccessException>(() =>
            definitions.CutoverEmptyTerraformBackend(projectId, fixture.Cutover, "operator", _ => false, _ => true));
        Assert.Throws<UnauthorizedAccessException>(() =>
            definitions.CutoverEmptyTerraformBackend(projectId, fixture.Cutover, "operator", _ => true, _ => false));
        fixture.Cutover.Revision = "stale";
        Assert.Throws<DefinitionPreconditionException>(() =>
            definitions.CutoverEmptyTerraformBackend(projectId, fixture.Cutover, "operator", _ => true, _ => true));
        fixture.Cutover.Revision = fixture.Document.Revision;
        fixture.Cutover.ConfirmBothContainersEmpty = false;
        Assert.Throws<ArgumentException>(() =>
            definitions.CutoverEmptyTerraformBackend(projectId, fixture.Cutover, "operator", _ => true, _ => true));
        fixture.Cutover.ConfirmBothContainersEmpty = true;
        fixture.Cutover.VerificationSha256 = "unverified";
        Assert.Throws<ArgumentException>(() =>
            definitions.CutoverEmptyTerraformBackend(projectId, fixture.Cutover, "operator", _ => true, _ => true));
        Assert.AreEqual(fixture.Document.Revision, definitions.Get(projectId).Revision);
        using var context = factory.GetContext();
        Assert.IsNull(context.DeploymentRequests.Single(r => r.Id == fixture.RequestId).TerraformBackendRetiredByChangeId);
        Assert.IsFalse(context.DeploymentDefinitionChanges.Any(c => c.ProjectId == projectId && c.Operation == "TerraformBackend.EmptyCutover"));
    }

    [TestMethod]
    public void EmptyBackendCutover_RejectsRetainedPlanEvidence()
    {
        var fixture = CreateCutover();
        using (var context = factory.GetContext())
        {
            context.TerraformDeploymentReceipts.Add(new TerraformDeploymentReceipt
            {
                DeploymentResultId = 200000 + fixture.RequestId, DeploymentRequestId = fixture.RequestId,
                ProjectId = fixture.Document.Project.ProjectId, ProjectName = fixture.Document.Project.ProjectName,
                EnvironmentId = fixture.Cutover.EnvironmentId, EnvironmentName = fixture.EnvironmentName,
                ComponentName = fixture.Document.Components.Single().ComponentName, TemplateName = "module",
                StateStorageAccount = "oldstate", StateContainerName = "tfstate", PlannedAt = DateTimeOffset.UtcNow
            });
            context.SaveChanges();
        }
        Assert.Throws<InvalidOperationException>(() => definitions.CutoverEmptyTerraformBackend(
            fixture.Document.Project.ProjectId, fixture.Cutover, "operator", _ => true, _ => true));
        Assert.AreEqual(fixture.Document.Revision, definitions.Get(fixture.Document.Project.ProjectId).Revision);
    }

    [TestMethod]
    [DataRow("Completed")]
    [DataRow("Failed")]
    public void EmptyBackendCutover_RejectsLegacyRequestsWithoutStateEvidence(string status)
    {
        var fixture = CreateCutover();
        using (var context = factory.GetContext())
        {
            context.DeploymentRequests.Add(new DeploymentRequest
            {
                Project = fixture.Document.Project.ProjectName, Environment = fixture.EnvironmentName,
                Components = fixture.Document.Components.Single().ComponentName,
                RequestDetails = "<root/>", UserName = "legacy-requester", Status = status
            });
            context.SaveChanges();
        }
        var error = Assert.Throws<InvalidOperationException>(() => definitions.CutoverEmptyTerraformBackend(
            fixture.Document.Project.ProjectId, fixture.Cutover, "operator", _ => true, _ => true));
        StringAssert.Contains(error.Message, "Legacy requests");
        Assert.AreEqual(fixture.Document.Revision, definitions.Get(fixture.Document.Project.ProjectId).Revision);
    }

    [TestMethod]
    public void EmptyBackendCutover_RejectsArchivedHistory()
    {
        var fixture = CreateCutover();
        using (var context = factory.GetContext())
        {
            var id = 1000000 + fixture.RequestId;
            context.Database.ExecuteSqlInterpolated($"""
                INSERT [archive].[DeploymentRequest] ([Id],[RequestDetails],[UserName],[Status],[IsProd],[Project],[Environment])
                VALUES ({id}, N'<root/>', N'legacy-requester', N'Completed', 0,
                    {fixture.Document.Project.ProjectName}, {fixture.EnvironmentName})
                """);
        }
        var error = Assert.Throws<InvalidOperationException>(() => definitions.CutoverEmptyTerraformBackend(
            fixture.Document.Project.ProjectId, fixture.Cutover, "operator", _ => true, _ => true));
        StringAssert.Contains(error.Message, "Archived deployment history");
        Assert.AreEqual(fixture.Document.Revision, definitions.Get(fixture.Document.Project.ProjectId).Revision);
    }

    [TestMethod]
    public void EmptyBackendCutover_EndpointRequiresAdminBeforeDatabaseAccess()
    {
        var roles = Substitute.For<IRolePrivilegesChecker>();
        var security = Substitute.For<ISecurityPrivilegesChecker>();
        var claims = Substitute.For<IClaimsPrincipalReader>();
        var controller = new TerraformBackendCutoversController(definitions, roles, security, claims);
        Assert.IsInstanceOfType<ForbidResult>(controller.Post(0, new TerraformBackendCutoverApiModel()));
    }

    [TestMethod]
    public void EmptyBackendCutover_AuditFailureRollsBackConfigurationAndRetirement()
    {
        var fixture = CreateCutover();
        using var context = factory.GetContext();
        context.Database.ExecuteSqlRaw("CREATE TRIGGER [deploy].[RejectCutoverAudit] ON [deploy].[DeploymentDefinitionChange] AFTER INSERT AS THROW 51000, 'Injected audit failure', 1;");
        try
        {
            Assert.Throws<DbUpdateException>(() => definitions.CutoverEmptyTerraformBackend(
                fixture.Document.Project.ProjectId, fixture.Cutover, "operator", _ => true, _ => true));
        }
        finally
        {
            context.Database.ExecuteSqlRaw("DROP TRIGGER [deploy].[RejectCutoverAudit]");
        }
        Assert.AreEqual(fixture.Document.Revision, definitions.Get(fixture.Document.Project.ProjectId).Revision);
        Assert.IsNull(context.DeploymentRequests.Single(r => r.Id == fixture.RequestId).TerraformBackendRetiredByChangeId);
    }

    [TestMethod]
    public async Task EmptyBackendCutover_ConcurrentAttemptsCommitOnlyOnce()
    {
        var fixture = CreateCutover();
        using var ready = new CountdownEvent(2);
        using var start = new ManualResetEventSlim();
        Task<bool> Attempt() => Task.Run(() =>
        {
            ready.Signal();
            start.Wait();
            try
            {
                definitions.CutoverEmptyTerraformBackend(fixture.Document.Project.ProjectId,
                    fixture.Cutover, "operator", _ => true, _ => true);
                return true;
            }
            catch (DefinitionPreconditionException) { return false; }
        });
        var first = Attempt();
        var second = Attempt();
        Assert.IsTrue(ready.Wait(TimeSpan.FromSeconds(10)));
        start.Set();
        var results = await Task.WhenAll(first, second);
        Assert.AreEqual(1, results.Count(succeeded => succeeded));
        using var context = factory.GetContext();
        Assert.AreEqual(1, context.DeploymentDefinitionChanges.Count(c =>
            c.ProjectId == fixture.Document.Project.ProjectId && c.Operation == "TerraformBackend.EmptyCutover"));
    }

    [TestMethod]
    public void BackendOverride_IsPinnedAndCannotMoveAfterSubmission()
    {
        var document = Create();
        var component = document.Components.Single();
        component.ComponentType = ComponentType.Terraform;
        component.TerraformSourceType = TerraformSourceType.Catalog;
        component.TerraformTemplateName = "module";
        component.TerraformTemplateVersion = "1.0.0";
        component.ScriptPath = "";
        var environmentName = Guid.NewGuid().ToString("N");
        int environmentId;
        using (var context = factory.GetContext())
        {
            context.Database.ExecuteSqlInterpolated(
                $"INSERT [deploy].[Environment] ([Name], [ObjectId], [Secure], [IsProd]) VALUES ({environmentName}, NEWID(), 0, 0)");
            var environment = context.Environments.Single(e => e.Name == environmentName);
            environment.Projects.Add(context.Projects.Single(p => p.Id == document.Project.ProjectId));
            context.SaveChanges();
            environmentId = environment.Id;
        }
        TerraformStateBackendApiModel Backend(string account, int? scope = null) => new()
        {
            EnvironmentId = scope, StorageAccount = account, ContainerName = "tfstate",
            SubscriptionId = "11111111-1111-1111-1111-111111111111"
        };
        document.Project.TerraformStateBackends = [Backend("projectstate"), Backend("envstate", environmentId)];
        document = definitions.Save(document, false, "editor", _ => true, () => { });
        var requests = new RequestsPersistentSource(factory, Substitute.For<IClaimsPrincipalReader>());
        var requestId = requests.SubmitRequest(new DeploymentRequest
        {
            Project = document.Project.ProjectName, Environment = environmentName, Components = component.ComponentName,
            RequestDetails = "<root/>", UserName = "requester", RequestedTime = DateTimeOffset.UtcNow
        });
        document.Project.TerraformStateBackends[0].StorageAccount = "futurestate";
        document = definitions.Save(document, false, "editor", _ => true, () => { });
        var pinned = requests.GetSubmittedProject(requestId, component.ComponentId!.Value).TerraformStateBackends.Single();
        Assert.AreEqual("envstate", pinned.StorageAccount);
        Assert.AreEqual(environmentId, pinned.EnvironmentId, "The submitted backend retains its stable environment identity.");
        using (var context = factory.GetContext())
        {
            context.Components.Single(c => c.Id == component.ComponentId).TerraformSourceType = TerraformSourceType.Git;
            context.Environments.Single(e => e.Id == environmentId).Name = environmentName + "-renamed";
            context.SaveChanges();
        }
        document = definitions.Get(document.Project.ProjectId);
        document.Project.TerraformStateBackends.RemoveAt(1);
        Assert.Throws<ArgumentException>(() => definitions.Save(document, false, "editor", _ => true, () => { }));
        Assert.AreEqual(document.Revision, definitions.Get(document.Project.ProjectId).Revision);
    }

    [TestMethod]
    public void LegacyReceipt_RequiresAdoptingObservedBackendEvenWithoutCurrentMapping()
    {
        var document = Create();
        using (var context = factory.GetContext())
        {
            context.TerraformDeploymentReceipts.Add(new TerraformDeploymentReceipt
            {
                DeploymentResultId = 100000 + document.Project.ProjectId,
                ProjectName = document.Project.ProjectName, EnvironmentName = "detached-environment",
                ComponentName = document.Components[0].ComponentName, TemplateName = "legacy-module",
                StateStorageAccount = "legacyaccount", StateContainerName = "tfstate",
                PlannedAt = DateTimeOffset.UtcNow
            });
            context.SaveChanges();
        }
        document.Project.TerraformStateBackends =
        [
            new() { StorageAccount = "differentaccount", ContainerName = "tfstate",
                SubscriptionId = "11111111-1111-1111-1111-111111111111" }
        ];
        Assert.Throws<ArgumentException>(() => definitions.Save(document, false, "editor", _ => true, () => { }));
        Assert.AreEqual(document.Revision, definitions.Get(document.Project.ProjectId).Revision);
        document.Project.TerraformStateBackends[0].StorageAccount = "legacyaccount";
        var saved = definitions.Save(document, false, "editor", _ => true, () => { });
        Assert.AreEqual("legacyaccount", saved.Project.TerraformStateBackends.Single().StorageAccount);
    }

    [TestMethod]
    public void MissingBackend_RejectsCatalogRequestWithoutPersistingSubmission()
    {
        var document = Create();
        var component = document.Components.Single();
        component.ComponentType = ComponentType.Terraform;
        component.TerraformSourceType = TerraformSourceType.Catalog;
        component.TerraformTemplateName = "module";
        component.TerraformTemplateVersion = "1.0.0";
        component.ScriptPath = "";
        document = definitions.Save(document, false, "editor", _ => true, () => { });
        var environmentName = Guid.NewGuid().ToString("N");
        using (var context = factory.GetContext())
        {
            context.Database.ExecuteSqlInterpolated(
                $"INSERT [deploy].[Environment] ([Name], [ObjectId], [Secure], [IsProd]) VALUES ({environmentName}, NEWID(), 0, 0)");
            context.Environments.Single(e => e.Name == environmentName).Projects.Add(context.Projects.Single(p => p.Id == document.Project.ProjectId));
            context.SaveChanges();
        }
        var requests = new RequestsPersistentSource(factory, Substitute.For<IClaimsPrincipalReader>());
        var error = Assert.Throws<InvalidOperationException>(() => requests.SubmitRequest(new DeploymentRequest
        {
            Project = document.Project.ProjectName, Environment = environmentName, Components = component.ComponentName,
            RequestDetails = "<root/>", UserName = "requester"
        }));
        StringAssert.Contains(error.Message, "Project Ref Data");
        using var check = factory.GetContext();
        Assert.IsFalse(check.DeploymentRequests.Any(r => r.Project == document.Project.ProjectName));
    }

    [TestMethod]
    [DataRow("invalid-account", false, null)]
    [DataRow("validaccount", true, null)]
    [DataRow("validaccount", false, 2147483647)]
    public void InvalidBackendScopeOrCoordinates_RollBackDefinition(string account, bool duplicate, int? environmentId)
    {
        var document = Create();
        var backend = new TerraformStateBackendApiModel
        {
            EnvironmentId = environmentId, StorageAccount = account, ContainerName = "tfstate",
            SubscriptionId = "11111111-1111-1111-1111-111111111111"
        };
        document.Project.TerraformStateBackends = duplicate ? [backend, backend] : [backend];
        Assert.Throws<ArgumentException>(() => definitions.Save(document, false, "editor", _ => true, () => { }));
        Assert.AreEqual(document.Revision, definitions.Get(document.Project.ProjectId).Revision);
    }

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

    [TestMethod]
    public void SkippedCatalogComponents_DoNotPinBackendsOrBlockBackendEdits()
    {
        var document = Create();
        var active = document.Components[0];
        active.ComponentName = "active-" + active.ComponentName;
        active.ComponentType = ComponentType.Terraform;
        active.TerraformSourceType = TerraformSourceType.Catalog;
        active.TerraformTemplateName = "active-module";
        active.TerraformTemplateVersion = "1.0.0";
        active.ScriptPath = "";
        var skippedAName = "skipped-a-" + Guid.NewGuid().ToString("N");
        var skippedBName = "skipped-b-" + Guid.NewGuid().ToString("N");
        document.Components.Add(new ComponentApiModel
        {
            ComponentId = 0, ComponentName = skippedAName,
            ComponentType = ComponentType.Terraform, TerraformSourceType = TerraformSourceType.Catalog,
            TerraformTemplateName = "skipped-a", TerraformTemplateVersion = "1.0.0", IsEnabled = true
        });
        document.Components.Add(new ComponentApiModel
        {
            ComponentId = 0, ComponentName = skippedBName,
            ComponentType = ComponentType.Terraform, TerraformSourceType = TerraformSourceType.Catalog,
            TerraformTemplateName = "skipped-b", TerraformTemplateVersion = "1.0.0", IsEnabled = true
        });
        var envName = Guid.NewGuid().ToString("N");
        using (var context = factory.GetContext())
        {
            context.Database.ExecuteSqlInterpolated(
                $"INSERT [deploy].[Environment] ([Name], [ObjectId], [Secure], [IsProd]) VALUES ({envName}, NEWID(), 0, 0)");
            var environment = context.Environments.Single(e => e.Name == envName);
            environment.Projects.Add(context.Projects.Single(p => p.Id == document.Project.ProjectId));
            context.SaveChanges();
        }
        document.Project.TerraformStateBackends =
        [
            new TerraformStateBackendApiModel
            {
                StorageAccount = "stateacct1", ContainerName = "tfstate",
                SubscriptionId = "11111111-1111-1111-1111-111111111111"
            }
        ];
        document = definitions.Save(document, false, "editor", _ => true, () => { });
        var activeSaved = document.Components.Single(c => c.ComponentName == active.ComponentName);
        var skippedA = document.Components.Single(c => c.ComponentName == skippedAName);
        var skippedB = document.Components.Single(c => c.ComponentName == skippedBName);
        var detailsXml = $"<DeploymentRequestDetail xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" xmlns:xsd=\"http://www.w3.org/2001/XMLSchema\"><ComponentsToSkip><string>{skippedA.ComponentName}</string><string>{skippedB.ComponentName}</string></ComponentsToSkip></DeploymentRequestDetail>";
        var requests = new RequestsPersistentSource(factory, Substitute.For<IClaimsPrincipalReader>());
        var requestId = requests.SubmitRequest(new DeploymentRequest
        {
            Project = document.Project.ProjectName, Environment = envName,
            Components = string.Join('|', document.Components.Select(c => c.ComponentName)),
            RequestDetails = detailsXml,
            UserName = "requester", RequestedTime = DateTimeOffset.UtcNow, Status = "Complete"
        });
        var defs = requests.GetRequestDefinitions(requestId);
        var skippedSnapA = JsonSerializer.Deserialize<ProjectApiModel>(defs.Single(d => d.ComponentId == skippedA.ComponentId).ProjectJson)!;
        Assert.AreEqual(0, skippedSnapA.TerraformStateBackends.Count);
        var activeSnap = JsonSerializer.Deserialize<ProjectApiModel>(defs.Single(d => d.ComponentId == activeSaved.ComponentId).ProjectJson)!;
        Assert.AreEqual(1, activeSnap.TerraformStateBackends.Count);
    }

    [TestMethod]
    public void SharedScript_EditPathPreservesIndividualComponentIsEnabled()
    {
        var first = Create(); var second = Create();
        second.Components[0].IsEnabled = false;
        second = definitions.Save(second, false, "editor", _ => true, () => { });
        int scriptId;
        using (var context = factory.GetContext())
        {
            var a = context.Components.Include(c => c.Script).Single(c => c.Id == first.Components[0].ComponentId);
            var b = context.Components.Single(c => c.Id == second.Components[0].ComponentId);
            b.Script = a.Script; scriptId = a.Script!.Id; context.SaveChanges();
        }
        var scripts = new ScriptsPersistentSource(factory, Substitute.For<IClaimsPrincipalReader>(),
            Substitute.For<IScriptsAuditPersistentSource>());
        var global = scripts.GetScript(scriptId);
        Assert.IsTrue(global.IsEnabled);
        global.ConfirmImpact = true;
        global.Path = "updated-shared.ps1";
        definitions.EditScript(global, "editor", _ => true);
        Assert.IsTrue(definitions.Get(first.Project.ProjectId).Components[0].IsEnabled);
        Assert.IsFalse(definitions.Get(second.Project.ProjectId).Components[0].IsEnabled);
    }
}
