using Dorc.ApiModel;
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
    public void LegacyCreation_RetainsItsExistingReplacementBehavior()
    {
        var input = Component();
        var first = Source().CreateCatalogComponent(input, _projectId, null, false).Component!;
        var replacement = Component(input.ComponentName);
        Source().CreateComponent(replacement, _projectId, null, "test");
        using var context = _factory.GetContext();
        Assert.AreNotEqual(first.ComponentId, replacement.ComponentId);
        Assert.AreNotEqual(input.ComponentName, context.Components.Single(c => c.Id == first.ComponentId).Name);
        Assert.AreEqual(input.ComponentName, context.Components.Single(c => c.Id == replacement.ComponentId).Name);
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
}
