using Dorc.ApiModel;
using Dorc.PersistentData;
using Dorc.PersistentData.Contexts;
using Dorc.PersistentData.Model;
using Dorc.PersistentData.Security;
using Dorc.PersistentData.Sources;
using Dorc.PersistentData.Sources.Interfaces;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Environment = System.Environment;

namespace Dorc.Api.Tests.Sources;

[TestClass]
[TestCategory("Database")]
[DoNotParallelize]
public class CatalogComponentCreationTests
{
    private static string? _master;
    private static string? _database;
    private static string _connection = null!;
    private static DeploymentContextFactory _factory = null!;
    private static int _projectId;
    private static int _otherProjectId;

    [ClassInitialize]
    public static void Initialize(TestContext testContext)
    {
        var configured = Environment.GetEnvironmentVariable("ConnectionStrings__DOrcConnectionString");
        if (string.IsNullOrWhiteSpace(configured))
        {
            if (Environment.GetEnvironmentVariable("DORC_DB_TESTS_REQUIRED") == "1")
                Assert.Fail("DORC_DB_TESTS_REQUIRED=1 requires ConnectionStrings__DOrcConnectionString.");
            Assert.Inconclusive("Configure ConnectionStrings__DOrcConnectionString to run SQL transaction tests.");
        }
        var builder = new SqlConnectionStringBuilder(configured) { InitialCatalog = "master" };
        _master = builder.ConnectionString;
        _database = $"dorc_catalog_test_{Guid.NewGuid():N}";
        using (var connection = new SqlConnection(_master))
        {
            connection.Open();
            using var create = new SqlCommand($"CREATE DATABASE [{_database}] COLLATE SQL_Latin1_General_CP1_CI_AS", connection);
            create.ExecuteNonQuery();
        }
        builder.InitialCatalog = _database;
        _connection = builder.ConnectionString;
        _factory = new DeploymentContextFactory(_connection);
        using var context = _factory.GetContext();
        context.Database.EnsureCreated();
        // ObjectId is store-generated in the model; EnsureCreated omits the
        // default supplied by the deployment schema, so seed it explicitly.
        context.Database.ExecuteSqlRaw(
            "INSERT [deploy].[Project] ([Name], [ObjectId], [SourceControlType]) VALUES ('catalog-project', NEWID(), 0), ('other-project', NEWID(), 0)");
        _projectId = context.Projects.Single(p => p.Name == "catalog-project").Id;
        _otherProjectId = context.Projects.Single(p => p.Name == "other-project").Id;
        context.Projects.Single(p => p.Id == _projectId).TerraformStateBackendsJson =
            """[{"StorageAccount":"teststate","ContainerName":"tfstate","SubscriptionId":"11111111-1111-1111-1111-111111111111"}]""";
        context.Database.ExecuteSqlRaw(
            "INSERT [deploy].[Environment] ([Name], [ObjectId], [Secure], [IsProd]) VALUES ('catalog-environment', NEWID(), 0, 0)");
        context.Environments.Single(e => e.Name == "catalog-environment").Projects.Add(context.Projects.Single(p => p.Id == _projectId));
        context.SaveChanges();
        Source().CreateCatalogComponent(Component("receipt-component"), _projectId, null, false);
    }

    [ClassCleanup]
    public static void Cleanup()
    {
        if (_master is null || _database is null) return;
        using var connection = new SqlConnection(_master);
        connection.Open();
        using var drop = new SqlCommand(
            $"ALTER DATABASE [{_database}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{_database}]", connection);
        drop.ExecuteNonQuery();
    }

    private static ManageProjectsPersistentSource Source(IDeploymentContextFactory? factory = null)
    {
        var allowList = Substitute.For<ISourceHostAllowList>();
        allowList.IsUnconfigured.Returns(true);
        return new(factory ?? _factory, Substitute.For<IRequestsPersistentSource>(),
            Substitute.For<IScriptsAuditPersistentSource>(), allowList);
    }

    private static ComponentApiModel Component(string? name = null) => new()
    {
        ComponentId = 0,
        ComponentName = name ?? Guid.NewGuid().ToString("N"),
        ComponentType = ComponentType.Terraform,
        TerraformSourceType = TerraformSourceType.Catalog,
        TerraformTemplateName = "catalog-module",
        TerraformTemplateVersion = "1.0.0",
        IsEnabled = true,
        StopOnFailure = true
    };

    [TestMethod]
    [DataRow(false, true, false)]
    [DataRow(true, true, false)]
    [DataRow(false, false, false)]
    [DataRow(false, true, true)]
    public async Task ConcurrentCreation_PreservesFirstComponentAndOwnership(
        bool otherProject, bool allowReuse, bool otherVersion)
    {
        var firstInput = Component();
        var secondInput = Component(firstInput.ComponentName.ToUpperInvariant());
        if (otherVersion) secondInput.TerraformTemplateVersion = "2.0.0";
        using var beforeInsert = new ManualResetEventSlim();
        using var releaseInsert = new ManualResetEventSlim();
        using var secondStarted = new ManualResetEventSlim();
        var pausingFactory = Substitute.For<IDeploymentContextFactory>();
        pausingFactory.GetContext().Returns(_ =>
        {
            var real = _factory.GetContext();
            var context = Substitute.For<IDeploymentContext>();
            context.Database.Returns(real.Database);
            context.Components.Returns(real.Components);
            context.Projects.Returns(real.Projects);
            context.CatalogEnvironmentInputs.Returns(real.CatalogEnvironmentInputs);
            context.DeploymentDefinitionChanges.Returns(real.DeploymentDefinitionChanges);
            context.DeploymentDefinitionRevisions.Returns(real.DeploymentDefinitionRevisions);
            context.RefDataAuditActions.Returns(real.RefDataAuditActions);
            context.RefDataAudits.Returns(real.RefDataAudits);
            context.SaveChanges().Returns(_ =>
            {
                beforeInsert.Set();
                Assert.IsTrue(releaseInsert.Wait(TimeSpan.FromSeconds(15)), "Release the name-range lock holder.");
                return real.SaveChanges();
            });
            context.When(c => c.Dispose()).Do(_ => real.Dispose());
            return context;
        });

        var first = Task.Run(() => Source(pausingFactory).CreateCatalogComponent(firstInput, _projectId, null, true));
        Task<CatalogComponentCreation>? second = null;
        try
        {
            Assert.IsTrue(beforeInsert.Wait(TimeSpan.FromSeconds(15)), "First caller must hold the absent-name range.");
            second = Task.Run(() =>
            {
                secondStarted.Set();
                return Source().CreateCatalogComponent(secondInput,
                    otherProject ? _otherProjectId : _projectId, null, allowReuse);
            });
            Assert.IsTrue(secondStarted.Wait(TimeSpan.FromSeconds(15)));
            Assert.AreNotSame(second, await Task.WhenAny(second, Task.Delay(250)),
                "The second creator must not decide from a stale absence while the first holds the range.");
        }
        finally
        {
            releaseInsert.Set();
            await first.WaitAsync(TimeSpan.FromSeconds(30));
            if (second is not null) await second.WaitAsync(TimeSpan.FromSeconds(30));
        }

        Assert.AreEqual(CatalogComponentCreationStatus.Created, first.Result.Status);
        var expected = otherProject || !allowReuse || otherVersion
            ? CatalogComponentCreationStatus.Conflict
            : CatalogComponentCreationStatus.Reused;
        Assert.AreEqual(expected, second!.Result.Status);
        if (expected == CatalogComponentCreationStatus.Reused)
        {
            Assert.AreEqual(first.Result.Component!.ComponentId, second.Result.Component!.ComponentId);
            Assert.AreEqual(firstInput.ComponentName, second.Result.Component.ComponentName);
        }
        using var check = _factory.GetContext();
        var stored = check.Components.Include(c => c.Projects)
            .Single(c => c.Name == firstInput.ComponentName);
        Assert.AreEqual(first.Result.Component!.ComponentId, stored.Id);
        Assert.AreEqual(firstInput.ComponentName, stored.Name);
        Assert.AreEqual("1.0.0", stored.TerraformTemplateVersion);
        Assert.IsTrue(stored.IsEnabled);
        Assert.IsTrue(stored.StopOnFailure);
        CollectionAssert.AreEqual(new[] { _projectId }, stored.Projects.Select(p => p.Id).ToArray());
    }

    [TestMethod]
    [DataRow("disabled", CatalogComponentCreationStatus.Disabled)]
    [DataRow("version", CatalogComponentCreationStatus.Conflict)]
    [DataRow("template", CatalogComponentCreationStatus.Conflict)]
    [DataRow("powershell", CatalogComponentCreationStatus.Conflict)]
    [DataRow("source", CatalogComponentCreationStatus.Conflict)]
    [DataRow("orphan", CatalogComponentCreationStatus.Conflict)]
    [DataRow("shared", CatalogComponentCreationStatus.Conflict)]
    [DataRow("different-project", CatalogComponentCreationStatus.Conflict)]
    [DataRow("create-only", CatalogComponentCreationStatus.Conflict)]
    public void ExistingComponent_IsNeverEditedToMakeItReusable(string difference, CatalogComponentCreationStatus expected)
    {
        var input = Component();
        var created = Source().CreateCatalogComponent(input, _projectId, null, false);
        using (var context = _factory.GetContext())
        {
            var existing = context.Components.Include(c => c.Projects).Single(c => c.Id == created.Component!.ComponentId);
            switch (difference)
            {
                case "disabled": existing.IsEnabled = false; break;
                case "version": existing.TerraformTemplateVersion = "0.9.0"; break;
                case "template": existing.TerraformTemplateName = "other-module"; break;
                case "powershell": existing.ComponentType = ComponentType.PowerShell; break;
                case "source": existing.TerraformSourceType = TerraformSourceType.SharedFolder; break;
                case "orphan": existing.Projects.Clear(); break;
                case "shared": existing.Projects.Add(context.Projects.Single(p => p.Id == _otherProjectId)); break;
            }
            existing.Description = "must remain untouched";
            context.SaveChanges();
        }
        var before = Snapshot(created.Component!.ComponentId!.Value);
        var result = Source().CreateCatalogComponent(input,
            difference == "different-project" ? _otherProjectId : _projectId, null, difference != "create-only");

        Assert.AreEqual(expected, result.Status);
        Assert.AreEqual(before, Snapshot(created.Component.ComponentId.Value));
    }

    [TestMethod]
    public void ParentAndEnabledState_AreValidatedInsideCreationBoundary()
    {
        var parent = Source().CreateCatalogComponent(Component(), _projectId, null, false).Component!;
        var otherParent = Source().CreateCatalogComponent(Component(), _otherProjectId, null, false).Component!;
        var input = Component();
        var source = Source();
        Assert.AreEqual(CatalogComponentCreationStatus.InvalidParent,
            source.CreateCatalogComponent(input, _projectId, otherParent.ComponentId, false).Status);
        var created = source.CreateCatalogComponent(input, _projectId, parent.ComponentId, false);
        Assert.AreEqual(parent.ComponentId, created.Component!.ParentId);
        Assert.AreEqual(CatalogComponentCreationStatus.Conflict,
            source.CreateCatalogComponent(input, _projectId, null, true).Status,
            "An omitted parent means root, not any existing parent.");
        Assert.AreEqual(CatalogComponentCreationStatus.Reused,
            source.CreateCatalogComponent(input, _projectId, parent.ComponentId, true).Status);

        using (var context = _factory.GetContext())
        {
            var component = context.Components.Single(c => c.Id == created.Component.ComponentId);
            component.IsEnabled = false;
            context.SaveChanges();
        }
        Assert.AreEqual(CatalogComponentCreationStatus.Disabled,
            source.CreateCatalogComponent(input, _projectId, parent.ComponentId, true).Status);
        using (var context = _factory.GetContext())
        {
            var component = context.Components.Single(c => c.Id == created.Component.ComponentId);
            component.IsEnabled = true;
            context.SaveChanges();
        }
        Assert.AreEqual(CatalogComponentCreationStatus.Reused,
            source.CreateCatalogComponent(input, _projectId, parent.ComponentId, true).Status);
    }

    [TestMethod]
    public void NameValidationAndMissingProject_DoNotPersistComponents()
    {
        var input = Component("invalid\nname");
        Assert.Throws<ArgumentException>(() => Source().CreateCatalogComponent(input, _projectId, null, false));
        input.ComponentName = new string('a', 65);
        Assert.Throws<ArgumentException>(() => Source().CreateCatalogComponent(input, _projectId, null, false));
        input.ComponentName = Guid.NewGuid().ToString("N");
        Assert.AreEqual(CatalogComponentCreationStatus.ProjectNotFound,
            Source().CreateCatalogComponent(input, int.MaxValue, null, false).Status);
        using var check = _factory.GetContext();
        Assert.IsFalse(check.Components.Any(c => c.Name == input.ComponentName));
    }

    [TestMethod]
    public void ReferenceData_CannotReplaceRenameOrRepinCatalogIdentity()
    {
        var input = Component();
        var first = Source().CreateCatalogComponent(input, _projectId, null, false).Component!;
        var replacement = Component(input.ComponentName);
        var before = Snapshot(first.ComponentId!.Value);
        Assert.Throws<ArgumentException>(() => Source().CreateComponent(replacement, _projectId, null, "test"));
        first.TerraformTemplateVersion = "2.0.0";
        Assert.Throws<ArgumentException>(() => Source().UpdateComponent(first, _projectId, null, "test"));
        Assert.AreEqual(before, Snapshot(first.ComponentId.Value));
    }

    private static string Snapshot(int id)
    {
        using var context = _factory.GetContext();
        return System.Text.Json.JsonSerializer.Serialize(context.Components.Where(c => c.Id == id)
            .Select(c => new
            {
                c.Id, c.Name, c.ObjectId, c.Description, c.IsEnabled, c.StopOnFailure,
                c.ComponentType, c.TerraformSourceType, c.TerraformTemplateName, c.TerraformTemplateVersion,
                ParentId = (int?)c.Parent!.Id,
                Projects = c.Projects.Select(p => p.Id).OrderBy(p => p).ToArray()
            }).Single());
    }

    private static CatalogSubmissionIdentity Submission(string? caller = null) => new()
    {
        Caller = caller ?? @"DOMAIN\catalog-user",
        Key = Guid.NewGuid(),
        CanonicalPayload = "{\"secret\":\"one-off-value\",\"template\":\"module@1\"}",
        ComponentJson = System.Text.Json.JsonSerializer.Serialize(Component("receipt-component"))
    };

    private static DeploymentRequest Request() => new()
    {
        UserName = "catalog-user",
        Project = "catalog-project",
        Environment = "catalog-environment",
        Components = "receipt-component",
        RequestDetails = "<DeploymentRequestDetail />",
        RequestedTime = DateTimeOffset.UtcNow
    };

    [TestMethod]
    public async Task CatalogSubmission_ConcurrentRetryCreatesExactlyOneRequestAndReturnsOriginalReceipt()
    {
        var submission = Submission();
        var source = new RequestsPersistentSource(_factory, Substitute.For<IClaimsPrincipalReader>());
        var ids = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(
            () => source.SubmitCatalogRequest(Request(), submission))));
        Assert.HasCount(1, ids.Distinct());
        var response = source.FindCatalogSubmission(submission)!;
        Assert.AreEqual(ids[0], response.RequestId);
        Assert.AreEqual("receipt-component", response.Component.ComponentName);
        using var context = _factory.GetContext();
        Assert.AreEqual(1, context.CatalogSubmissions.Count(row => row.DeploymentRequestId == ids[0]));
        Assert.AreEqual(1, context.DeploymentRequests.Count(row => row.Id == ids[0]));
        var stored = context.CatalogSubmissions.Single(row => row.DeploymentRequestId == ids[0]);
        Assert.AreEqual(32, stored.PayloadFingerprint.Length);
        Assert.IsFalse(stored.ComponentJson.Contains("one-off-value"));
    }

    [TestMethod]
    public void CatalogSubmission_ConflictingPayloadRefusesButOtherCallerHasIndependentKey()
    {
        var submission = Submission();
        var source = new RequestsPersistentSource(_factory, Substitute.For<IClaimsPrincipalReader>());
        var id = source.SubmitCatalogRequest(Request(), submission);
        submission.CanonicalPayload += "changed";
        Assert.Throws<CatalogSubmissionConflictException>(() => source.FindCatalogSubmission(submission));
        Assert.Throws<CatalogSubmissionConflictException>(() => source.SubmitCatalogRequest(Request(), submission));
        submission.Caller = @"DOMAIN\other-user";
        Assert.IsNull(source.FindCatalogSubmission(submission));
        Assert.AreNotEqual(id, source.SubmitCatalogRequest(Request(), submission));
    }

    [TestMethod]
    public void CatalogSubmission_FailedClaimRollsBackRequestAndRetrySucceeds()
    {
        var submission = Submission();
        var factory = Substitute.For<IDeploymentContextFactory>();
        factory.GetContext().Returns(_ =>
        {
            var real = _factory.GetContext();
            var context = Substitute.For<IDeploymentContext>();
            context.Database.Returns(real.Database);
            context.CatalogSubmissions.Returns(real.CatalogSubmissions);
            context.Components.Returns(real.Components);
            context.CatalogEnvironmentInputs.Returns(real.CatalogEnvironmentInputs);
            context.CatalogRequestInputs.Returns(real.CatalogRequestInputs);
            context.Projects.Returns(real.Projects);
            context.DeploymentRequestDefinitions.Returns(real.DeploymentRequestDefinitions);
            context.DeploymentDefinitionRevisions.Returns(real.DeploymentDefinitionRevisions);
            context.Properties.Returns(real.Properties);
            context.ConfigValues.Returns(real.ConfigValues);
            context.DeploymentRequests.Returns(real.DeploymentRequests);
            context.Environments.Returns(real.Environments);
            var saves = 0;
            context.SaveChanges().Returns(_ =>
            {
                if (++saves == 2) throw new InvalidOperationException("simulated failure before claim persistence");
                return real.SaveChanges();
            });
            context.When(c => c.Dispose()).Do(_ => real.Dispose());
            return context;
        });
        var request = Request();
        Assert.Throws<InvalidOperationException>(() =>
            new RequestsPersistentSource(factory, Substitute.For<IClaimsPrincipalReader>())
                .SubmitCatalogRequest(request, submission));
        using (var context = _factory.GetContext())
            Assert.IsFalse(context.DeploymentRequests.Any(row => row.Id == request.Id));
        var source = new RequestsPersistentSource(_factory, Substitute.For<IClaimsPrincipalReader>());
        Assert.IsNull(source.FindCatalogSubmission(submission));
        Assert.IsGreaterThan(0, source.SubmitCatalogRequest(Request(), submission));
    }

    [TestMethod]
    public void TerraformReceipt_IsAppendOnlyAndSurvivesMutableResultRemoval()
    {
        var source = new RequestsPersistentSource(_factory, Substitute.For<IClaimsPrincipalReader>());
        var requestId = source.SubmitRequest(Request());
        var component = Source().CreateCatalogComponent(Component(), _projectId, null, false).Component!;
        var result = source.CreateDeploymentResult(component.ComponentId!.Value, requestId);
        var receipt = new TerraformDeploymentReceipt
        {
            DeploymentResultId = result.Id,
            DeploymentRequestId = requestId,
            ComponentId = component.ComponentId.Value,
            ComponentName = component.ComponentName,
            ProjectName = "catalog-project",
            EnvironmentName = "catalog-environment",
            PlannedAt = DateTimeOffset.UtcNow,
            TemplateName = "original-module",
            TemplateVersion = "1.0.0",
            GitCommitSha = new string('a', 40),
            SourceSha256 = new string('B', 64),
            PlanContentSha256 = new string('C', 64)
        };
        source.SaveTerraformDeploymentReceipt(receipt);
        source.ArchiveCurrentAttempt(requestId);
        source.ClearAllDeploymentResults(new List<int> { requestId });
        Assert.IsNull(source.GetDeploymentResults(result.Id));
        Assert.AreEqual(requestId, source.GetArchivedDeploymentResult(result.Id)!.RequestId);
        Assert.AreEqual("1.0.0", source.GetTerraformDeploymentReceipt(result.Id)!.TemplateVersion);
        Assert.Throws<DbUpdateException>(() => source.SaveTerraformDeploymentReceipt(receipt));
    }

    [TestMethod]
    public void SavedCatalogInputs_AreScopedRevisionedAndSnapshottedForCommonRequests()
    {
        var component = Source().CreateCatalogComponent(Component(), _projectId, null, false).Component!;
        var environmentName = "saved-inputs-" + Guid.NewGuid().ToString("N");
        int environmentId;
        using (var context = _factory.GetContext())
        {
            context.Database.ExecuteSqlInterpolated(
                $"INSERT [deploy].[Environment] ([Name], [ObjectId], [Secure], [IsProd]) VALUES ({environmentName}, NEWID(), 0, 0)");
            var environment = context.Environments.Single(row => row.Name == environmentName);
            environment.Projects.Add(context.Projects.Single(row => row.Id == _projectId));
            context.SaveChanges();
            environmentId = environment.Id;
        }
        var source = new RequestsPersistentSource(_factory, Substitute.For<IClaimsPrincipalReader>());
        var componentId = component.ComponentId!.Value;
        Assert.AreEqual(1L, source.SaveCatalogEnvironmentInputs(componentId, _projectId, environmentId, "1.0.0", 0,
            new Dictionary<string, string> { ["create_service"] = "false" }));
        var request = Request();
        request.Environment = environmentName;
        request.Components = component.ComponentName;
        var requestId = source.SubmitRequest(request);
        Assert.AreEqual(2L, source.SaveCatalogEnvironmentInputs(componentId, _projectId, environmentId, "1.0.0", 1,
            new Dictionary<string, string> { ["create_service"] = "true" }));
        var snapshot = source.GetCatalogRequestInputs(requestId, componentId)!;
        Assert.AreEqual(1L, snapshot.ConfigurationRevision);
        StringAssert.Contains(snapshot.InputsJson, "\"false\"");
        Assert.AreEqual("1.0.0", snapshot.TemplateVersion);
        Assert.IsNull(source.GetCatalogEnvironmentInputs(componentId, int.MaxValue));
        Assert.Throws<InvalidOperationException>(() => source.SaveCatalogEnvironmentInputs(componentId, _projectId,
            environmentId, "1.0.0", 1, new Dictionary<string, string>()));
        Assert.Throws<InvalidOperationException>(() => source.SaveCatalogEnvironmentInputs(componentId, _projectId,
            environmentId, "1.0.0", 2, new Dictionary<string, string> { ["api_token"] = "never-store-this" }));
        using (var context = _factory.GetContext())
        {
            context.Properties.Add(new Property { Name = "create_service", Secure = true });
            context.SaveChanges();
        }
        Assert.Throws<InvalidOperationException>(() => source.SubmitRequest(request));
    }

    [TestMethod]
    public async Task PostApplyRecovery_IsRevisionSerializedAndRollsBackFailedRecovery()
    {
        var source = new RequestsPersistentSource(_factory, Substitute.For<IClaimsPrincipalReader>());
        var id = Random.Shared.Next(1000000, 2000000);
        source.SaveTerraformDeploymentReceipt(new TerraformDeploymentReceipt
        {
            DeploymentResultId = id, DeploymentRequestId = id, ComponentId = id,
            ProjectName = "recovery", EnvironmentName = "recovery", ComponentName = "recovery",
            PlannedAt = DateTimeOffset.UtcNow
        });
        source.SaveTerraformDeploymentOutcome(new TerraformDeploymentOutcome
        {
            DeploymentResultId = id, ApplyOutcome = "Succeeded", AppliedAt = DateTimeOffset.UtcNow
        });
        Assert.Throws<InvalidOperationException>(() => source.RecoverTerraformPostApply(id, 0, outcome =>
        {
            outcome.ArchiveOutcome = "Succeeded";
            throw new InvalidOperationException("simulated unavailable artifact");
        }));
        Assert.AreEqual("NotAttempted", source.GetTerraformDeploymentOutcome(id)!.ArchiveOutcome);
        var outcomes = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => Task.Run(() =>
        {
            try
            {
                source.RecoverTerraformPostApply(id, 0, outcome => outcome.ArchiveOutcome = "Succeeded");
                return true;
            }
            catch (InvalidOperationException) { return false; }
        })));
        Assert.AreEqual(1, outcomes.Count(success => success));
        Assert.AreEqual("Succeeded", source.GetTerraformDeploymentOutcome(id)!.ApplyOutcome);
        Assert.AreEqual(1L, source.GetTerraformDeploymentOutcome(id)!.Revision);
    }

    [TestMethod]
    public void InventoryReconciliation_PreservesOtherOwnersAndRequiresCompleteExportAndCurrentReview()
    {
        var source = new RequestsPersistentSource(_factory, Substitute.For<IClaimsPrincipalReader>());
        var component = Random.Shared.Next(2000001, 3000000);
        var now = DateTimeOffset.UtcNow;
        var seen = new List<TerraformInventoryObservation>
        {
            new() { CloudResourceId = 33, DatabaseId = 44, ResourceIdentifier = "project/service/database" }
        };
        source.RecordTerraformInventory(component, 5, 11, now, seen, true);
        source.RecordTerraformInventory(component + 1, 5, 12, now, seen, true);
        source.RecordTerraformInventory(component, 6, 13, now, seen, true);
        source.RecordTerraformInventory(component, 5, 14, now.AddMinutes(1), [], false);
        Assert.AreEqual("Observed", source.GetTerraformInventory(component, 5).Single().ReconciliationState);
        source.RecordTerraformInventory(component, 5, 15, now.AddMinutes(2), [], true);
        var missing = source.GetTerraformInventory(component, 5).Single();
        Assert.AreEqual("MissingFromLatestState", missing.ReconciliationState);
        Assert.AreEqual(44, missing.DatabaseId);
        Assert.AreEqual("Observed", source.GetTerraformInventory(component + 1, 5).Single().ReconciliationState);
        Assert.AreEqual("Observed", source.GetTerraformInventory(component, 6).Single().ReconciliationState);
        Assert.IsFalse(source.ReviewTerraformInventory(component, 5, 33, missing.Revision - 1));
        Assert.IsTrue(source.ReviewTerraformInventory(component, 5, 33, missing.Revision));
        Assert.AreEqual("ReviewedMissing", source.GetTerraformInventory(component, 5).Single().ReconciliationState);
    }

    [TestMethod]
    public async Task CatalogUpgrade_RejectsActiveRequestsAndConcurrentChangesWithoutChangingStateIdentity()
    {
        var projectName = "upgrade-" + Guid.NewGuid().ToString("N");
        var environmentName = projectName + "-env";
        int projectId, environmentId;
        using (var context = _factory.GetContext())
        {
            context.Database.ExecuteSqlInterpolated(
                $"INSERT [deploy].[Project] ([Name], [ObjectId], [SourceControlType]) VALUES ({projectName}, NEWID(), 0)");
            context.Database.ExecuteSqlInterpolated(
                $"INSERT [deploy].[Environment] ([Name], [ObjectId], [Secure], [IsProd]) VALUES ({environmentName}, NEWID(), 0, 0)");
            var project = context.Projects.Single(row => row.Name == projectName);
            project.TerraformStateBackendsJson =
                """[{"StorageAccount":"upgradestate","ContainerName":"tfstate","SubscriptionId":"11111111-1111-1111-1111-111111111111"}]""";
            var environment = context.Environments.Single(row => row.Name == environmentName);
            environment.Projects.Add(project);
            context.SaveChanges();
            projectId = project.Id;
            environmentId = environment.Id;
        }
        var component = Source().CreateCatalogComponent(Component(), projectId, null, false).Component!;
        var componentId = component.ComponentId!.Value;
        var source = new RequestsPersistentSource(_factory, Substitute.For<IClaimsPrincipalReader>());
        source.SaveCatalogEnvironmentInputs(componentId, projectId, environmentId, "1.0.0", 0,
            new Dictionary<string, string> { ["region"] = "west" });
        var expected = new CatalogUpgradeRequestApiModel
        {
            ExpectedVersion = "1.0.0", ExpectedRevision = 0, ConfirmImpact = true,
            EnvironmentIds = new List<int> { environmentId },
            ConfigurationRevisions = new Dictionary<string, long> { [environmentId.ToString()] = 1 }
        };
        var inputs = new Dictionary<int, Dictionary<string, string>>
        {
            [environmentId] = new() { ["region"] = "west" }
        };
        var request = Request();
        request.Project = projectName;
        request.Environment = environmentName;
        request.Components = component.ComponentName;
        request.Status = DeploymentRequestStatus.WaitingConfirmation.ToString();
        var requestId = source.SubmitRequest(request);
        var before = Snapshot(componentId);
        Assert.Throws<InvalidOperationException>(() =>
            source.UpgradeCatalogComponent(componentId, projectId, "2.0.0", expected, inputs, "approver"));
        Assert.AreEqual(before, Snapshot(componentId));
        using (var context = _factory.GetContext())
        {
            context.DeploymentRequests.Single(row => row.Id == requestId).Status = DeploymentRequestStatus.Completed.ToString();
            context.SaveChanges();
        }
        var attempts = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => Task.Run(() =>
        {
            try { source.UpgradeCatalogComponent(componentId, projectId, "2.0.0", expected, inputs, "approver"); return true; }
            catch (InvalidOperationException) { return false; }
        })));
        Assert.AreEqual(1, attempts.Count(success => success));
        using var check = _factory.GetContext();
        var updated = check.Components.Single(row => row.Id == componentId);
        Assert.AreEqual(component.ComponentName, updated.Name);
        Assert.AreEqual("2.0.0", updated.TerraformTemplateVersion);
        Assert.AreEqual(1, check.CatalogModuleUpgrades.Count(row => row.ComponentId == componentId));
        Assert.AreEqual("1.0.0", source.GetCatalogRequestInputs(requestId, componentId)!.TemplateVersion);
        Assert.AreEqual("2.0.0", source.GetCatalogEnvironmentInputs(componentId, environmentId)!.TemplateVersion);
    }
}
