using System.Text.Json;
using System.Transactions;
using Dorc.ApiModel;
using Dorc.Core.Terraform;
using Dorc.PersistentData.Contexts;
using Dorc.PersistentData.Model;
using Dorc.PersistentData.Sources;
using Dorc.PersistentData.Sources.Interfaces;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Environment = System.Environment;

namespace Dorc.Monitor.Tests.Terraform;

[TestClass]
[TestCategory("Database")]
[DoNotParallelize]
public class TerraformCloudResourceRegistrationSqlTests
{
    private static string? _master;
    private static string? _database;
    private static string _connection = null!;
    private static DeploymentContextFactory _factory = null!;
    private static int _environmentId;
    private static int _otherEnvironmentId;
    private string _file = null!;

    [ClassInitialize]
    public static void Initialize(TestContext testContext)
    {
        var configured = Environment.GetEnvironmentVariable("ConnectionStrings__DOrcConnectionString");
        if (string.IsNullOrWhiteSpace(configured))
        {
            if (Environment.GetEnvironmentVariable("DORC_DB_TESTS_REQUIRED") == "1")
                Assert.Fail("DORC_DB_TESTS_REQUIRED=1 requires ConnectionStrings__DOrcConnectionString.");
            Assert.Inconclusive("Configure ConnectionStrings__DOrcConnectionString to run SQL registration tests.");
        }
        var builder = new SqlConnectionStringBuilder(configured) { InitialCatalog = "master" };
        _master = builder.ConnectionString;
        _database = $"dorc_registration_test_{Guid.NewGuid():N}";
        using (var connection = new SqlConnection(_master))
        {
            connection.Open();
            using var create = new SqlCommand($"CREATE DATABASE [{_database}] COLLATE SQL_Latin1_General_CP1_CI_AS", connection);
            create.ExecuteNonQuery();
        }
        builder.InitialCatalog = _database;
        _connection = builder.ConnectionString;
        _factory = new DeploymentContextFactory(_connection);
        using var context = new DeploymentContext(_connection);
        context.Database.EnsureCreated();
        var environment = new Dorc.PersistentData.Model.Environment { Name = "target", ObjectId = Guid.NewGuid() };
        var other = new Dorc.PersistentData.Model.Environment { Name = "other", ObjectId = Guid.NewGuid() };
        context.Environments.AddRange(environment, other);
        context.RefDataAuditActions.AddRange(Enum.GetValues<ActionType>().Select(action => new Dorc.PersistentData.Model.RefDataAuditAction { Action = action }));
        context.SaveChanges();
        _environmentId = environment.Id;
        _otherEnvironmentId = other.Id;
    }

    [ClassCleanup]
    public static void CleanupDatabase()
    {
        if (_master == null || _database == null) return;
        SqlConnection.ClearAllPools();
        using var connection = new SqlConnection(_master);
        connection.Open();
        using var drop = new SqlCommand(
            $"ALTER DATABASE [{_database}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{_database}]", connection);
        drop.ExecuteNonQuery();
    }

    [TestInitialize]
    public void InitializeFile() => _file = Path.Combine(Directory.GetCurrentDirectory(), $"applied-sql-{Guid.NewGuid():N}.json");

    [TestCleanup]
    public void CleanupFile()
    {
        if (File.Exists(_file)) File.Delete(_file);
    }

    private static CloudResourceApiModel Resource() => new()
    {
        Name = Guid.NewGuid().ToString("N"), Provider = "Azure", ResourceType = "azurerm_resource_group",
        ResourceIdentifier = $"/subscriptions/sub/resourceGroups/{Guid.NewGuid():N}", Subscription = "sub", Tags = ""
    };

    private void Register(CloudResourceApiModel resource, ActionType? failAudit = null, bool missingEnvironment = false)
    {
        var environments = Substitute.For<IEnvironmentsPersistentSource>();
        environments.GetEnvironment("target").Returns(new EnvironmentApiModel
        {
            EnvironmentId = missingEnvironment ? int.MaxValue : _environmentId, EnvironmentName = "target"
        });
        var realAudit = new CloudResourceAuditPersistentSource(_factory);
        var audit = Substitute.For<ICloudResourceAuditPersistentSource>();
        audit.WhenForAnyArgs(x => x.InsertCloudResourceAudit(default!, default, default, default, default)).Do(call =>
        {
            realAudit.InsertCloudResourceAudit(call.ArgAt<string>(0), call.ArgAt<ActionType>(1), call.ArgAt<int?>(2),
                call.ArgAt<string?>(3), call.ArgAt<string?>(4));
            if (call.ArgAt<ActionType>(1) == failAudit)
                throw new DbUpdateException("Simulated failure after audit SaveChanges");
        });
        File.WriteAllText(_file, JsonSerializer.Serialize(new[] { resource }));
        new TerraformCloudResourceRegistrar(Substitute.For<ILogger>(), new CloudResourcesPersistentSource(_factory),
            audit, environments, Substitute.For<IDatabasesPersistentSource>())
            .RegisterAppliedResources(_file, "target", "registration-test");
    }

    [TestMethod]
    [DataRow("rename")]
    [DataRow("typed")]
    [DataRow("hashed")]
    public void ReapplyAfterRenameOrCollisionRemoval_PreservesDefinitionAndMappings(string scenario)
    {
        var source = new CloudResourcesPersistentSource(_factory);
        var incoming = Resource();
        var blockers = new List<int>();
        if (scenario != "rename")
        {
            var blocker = Resource();
            blocker.Name = incoming.Name;
            blockers.Add(source.Add(blocker).Id);
            if (scenario == "hashed")
            {
                blocker = Resource();
                blocker.Name = $"{incoming.Name} ({incoming.ResourceType})";
                blockers.Add(source.Add(blocker).Id);
            }
        }
        Register(incoming);
        var persisted = source.GetByIdentity(incoming.Provider, incoming.ResourceType, incoming.ResourceIdentifier).Single();
        if (scenario == "rename") persisted.Name = "friendly-" + incoming.Name;
        persisted.Tags = "operator-owned";
        persisted.ResourceType = "legacy-type";
        source.Update(persisted.Id, persisted);
        source.AttachToEnvironment(persisted.Id, _otherEnvironmentId);
        foreach (var blocker in blockers) source.Delete(blocker);
        incoming.ResourceIdentifier = incoming.ResourceIdentifier.ToUpperInvariant();

        Register(incoming);
        Register(incoming);

        var result = source.GetByIdentity(incoming.Provider, incoming.ResourceType, incoming.ResourceIdentifier).Single();
        Assert.AreEqual(persisted.Id, result.Id);
        Assert.AreEqual(persisted.Name, result.Name);
        Assert.AreEqual("operator-owned", result.Tags);
        Assert.AreEqual(incoming.ResourceType, result.ResourceType);
        CollectionAssert.AreEquivalent(new[] { "target", "other" }, result.EnvironmentNames);
        using var context = _factory.GetContext();
        Assert.AreEqual(3, context.CloudResourceAudits.Count(a => a.CloudResourceId == result.Id),
            "Only initial create/attach and the single metadata correction should be audited.");
    }

    [TestMethod]
    public void CaseInsensitiveSqlCollation_DoesNotCollapseOpaqueCaseProviderOrType()
    {
        var source = new CloudResourcesPersistentSource(_factory);
        var identifier = $"{Guid.NewGuid():N}/service/Topic";
        var resources = new[]
        {
            new CloudResourceApiModel { Name = Guid.NewGuid().ToString("N"), Provider = "Aiven", ResourceType = "aiven_kafka_topic", ResourceIdentifier = identifier },
            new CloudResourceApiModel { Name = Guid.NewGuid().ToString("N"), Provider = "Aiven", ResourceType = "aiven_kafka_topic", ResourceIdentifier = identifier.ToLowerInvariant() },
            new CloudResourceApiModel { Name = Guid.NewGuid().ToString("N"), Provider = "Aiven", ResourceType = "aiven_pg_database", ResourceIdentifier = identifier },
            new CloudResourceApiModel { Name = Guid.NewGuid().ToString("N"), Provider = "Other", ResourceType = "aiven_kafka_topic", ResourceIdentifier = identifier }
        };
        foreach (var resource in resources)
        {
            Register(resource);
            Register(resource);
        }
        var matches = resources.Select(resource => source.GetByIdentity(resource.Provider, resource.ResourceType, resource.ResourceIdentifier).Single()).ToList();
        Assert.AreEqual(4, matches.Select(model => model.Id).Distinct().Count());
        Assert.IsTrue(matches.All(model => model.EnvironmentNames.Contains("target")));
    }

    [TestMethod]
    public void ExistingDuplicates_AreNotSilentlySelectedOrMerged()
    {
        var source = new CloudResourcesPersistentSource(_factory);
        var incoming = Resource();
        var first = source.Add(incoming);
        incoming.Name += "-duplicate";
        var second = source.Add(incoming);
        source.AttachToEnvironment(first.Id, _otherEnvironmentId);
        using var before = _factory.GetContext();
        var audits = before.CloudResourceAudits.Count();

        Register(incoming);

        Assert.HasCount(2, source.GetByIdentity(incoming.Provider, incoming.ResourceType, incoming.ResourceIdentifier));
        Assert.IsEmpty(source.GetById(second.Id)!.EnvironmentNames);
        CollectionAssert.AreEqual(new[] { "other" }, source.GetById(first.Id)!.EnvironmentNames);
        using var after = _factory.GetContext();
        Assert.AreEqual(audits, after.CloudResourceAudits.Count());
    }

    [TestMethod]
    [DataRow(ActionType.Create)]
    [DataRow(ActionType.Attach)]
    [DataRow(ActionType.Update)]
    public void AuditFailure_RollsBackDefinitionMembershipAndAudit_WithoutFailingApply(ActionType action)
    {
        var source = new CloudResourcesPersistentSource(_factory);
        var incoming = Resource();
        CloudResourceApiModel? existing = null;
        if (action == ActionType.Update)
        {
            existing = source.Add(incoming);
            existing.ResourceType = "legacy-type";
            source.Update(existing.Id, existing);
        }
        using var before = _factory.GetContext();
        var audits = before.CloudResourceAudits.Count();

        Register(incoming, failAudit: action);

        var matches = source.GetByIdentity(incoming.Provider, incoming.ResourceType, incoming.ResourceIdentifier);
        Assert.HasCount(existing == null ? 0 : 1, matches);
        if (existing != null)
        {
            Assert.AreEqual("legacy-type", matches.Single().ResourceType);
            Assert.IsEmpty(matches.Single().EnvironmentNames);
        }
        using var after = _factory.GetContext();
        Assert.AreEqual(audits, after.CloudResourceAudits.Count());
        Assert.IsNull(Transaction.Current);
    }

    [TestMethod]
    public void DisappearedEnvironment_RollsBackNewDefinitionAndAudit()
    {
        var source = new CloudResourcesPersistentSource(_factory);
        var incoming = Resource();
        using var before = _factory.GetContext();
        var audits = before.CloudResourceAudits.Count();

        Register(incoming, missingEnvironment: true);

        Assert.IsEmpty(source.GetByIdentity(incoming.Provider, incoming.ResourceType, incoming.ResourceIdentifier));
        using var after = _factory.GetContext();
        Assert.AreEqual(audits, after.CloudResourceAudits.Count());
    }

    [TestMethod]
    public async Task IdentityLookup_HoldsSerializableRangeAgainstConcurrentInsert()
    {
        var source = new CloudResourcesPersistentSource(_factory);
        var incoming = Resource();
        using var read = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var lookup = Task.Run(() => source.ExecuteMutation(() =>
        {
            var matches = source.GetByIdentity(incoming.Provider, incoming.ResourceType, incoming.ResourceIdentifier);
            read.Set();
            Assert.IsTrue(release.Wait(TimeSpan.FromSeconds(15)));
            Assert.IsEmpty(source.GetByIdentity(incoming.Provider, incoming.ResourceType, incoming.ResourceIdentifier));
            return matches.Count;
        }));
        Task? insert = null;
        try
        {
            Assert.IsTrue(read.Wait(TimeSpan.FromSeconds(15)));
            using var started = new ManualResetEventSlim();
            insert = Task.Run(() =>
            {
                started.Set();
                source.Add(incoming);
            });
            Assert.IsTrue(started.Wait(TimeSpan.FromSeconds(5)));
            await Task.Delay(300);
            Assert.IsFalse(insert.IsCompleted, "Identity range must remain stable until mutation commits");
        }
        finally
        {
            release.Set();
            await lookup;
            if (insert != null) await insert;
        }
        Assert.HasCount(1, source.GetByIdentity(incoming.Provider, incoming.ResourceType, incoming.ResourceIdentifier));
    }
}
