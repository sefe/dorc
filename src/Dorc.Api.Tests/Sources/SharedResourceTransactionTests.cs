using System.Security.Claims;
using System.Text.Json;
using System.Transactions;
using Dorc.Api.Controllers;
using Dorc.ApiModel;
using Dorc.Core.Interfaces;
using Dorc.PersistentData.Contexts;
using Dorc.PersistentData;
using Dorc.PersistentData.Model;
using Dorc.PersistentData.Sources;
using Dorc.PersistentData.Sources.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Environment = System.Environment;

namespace Dorc.Api.Tests.Sources;

[TestClass]
[TestCategory("Database")]
[DoNotParallelize]
public class SharedResourceTransactionTests
{
    private static string? _connection;
    private static string? _master;
    private static string? _database;
    private static DeploymentContextFactory _factory = null!;
    private static int _allowedEnvironment;
    private static int _otherEnvironment;

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
        _database = $"dorc_shared_test_{Guid.NewGuid():N}";
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
        var allowed = new Dorc.PersistentData.Model.Environment { Name = "allowed", ObjectId = Guid.NewGuid() };
        var other = new Dorc.PersistentData.Model.Environment { Name = "other", ObjectId = Guid.NewGuid() };
        context.Environments.AddRange(allowed, other);
        context.RefDataAuditActions.AddRange(Enum.GetValues<ActionType>().Select(action => new Dorc.PersistentData.Model.RefDataAuditAction { Action = action }));
        context.SaveChanges();
        _allowedEnvironment = allowed.Id;
        _otherEnvironment = other.Id;
    }

    [ClassCleanup]
    public static void Cleanup()
    {
        if (_master == null || _database == null) return;
        SqlConnection.ClearAllPools();
        using var connection = new SqlConnection(_master);
        connection.Open();
        using var drop = new SqlCommand(
            $"ALTER DATABASE [{_database}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{_database}]", connection);
        drop.ExecuteNonQuery();
    }

    private static dynamic Source(int kind) => kind switch
    {
        0 => new ContainersPersistentSource(_factory),
        1 => new CloudResourcesPersistentSource(_factory),
        _ => (object)new ApiRegistrationsPersistentSource(_factory)
    };

    private static dynamic Model(int kind, string name) => kind switch
    {
        0 => new ContainerApiModel { Name = name, Image = "image", Tags = "old" },
        1 => new CloudResourceApiModel { Name = name, Provider = "Azure", ResourceType = "KeyVault", ResourceIdentifier = "id", Tags = "old" },
        _ => (object)new ApiRegistrationApiModel { Name = name, BaseUrl = "https://example.test", Tags = "old" }
    };

    private static dynamic Controller(int kind, bool failAudit = false, Action? authorizing = null, bool authorizeRole = false)
    {
        var environments = Substitute.For<IEnvironmentsPersistentSource>();
        environments.GetEnvironment(Arg.Any<int>(), Arg.Any<ClaimsPrincipal>())
            .Returns(call => new EnvironmentApiModel
            {
                EnvironmentId = call.Arg<int>(),
                EnvironmentName = call.Arg<int>() == _allowedEnvironment ? "allowed" : "other"
            });
        var roles = Substitute.For<IRolePrivilegesChecker>();
        roles.IsPowerUser(Arg.Any<ClaimsPrincipal>()).Returns(_ =>
        {
            if (authorizeRole) authorizing?.Invoke();
            return true;
        });
        var security = Substitute.For<ISecurityPrivilegesChecker>();
        security.CanModifyEnvironment(Arg.Any<ClaimsPrincipal>(), Arg.Any<string>()).Returns(call =>
        {
            authorizing?.Invoke();
            return call.Arg<string>() == "allowed";
        });
        var claims = Substitute.For<IClaimsPrincipalReader>();
        claims.GetUserFullDomainName(Arg.Any<ClaimsPrincipal>()).Returns("transaction-test");
        ControllerBase controller;
        if (kind == 0)
        {
            var realAudit = new ContainerAuditPersistentSource(_factory);
            var audit = Substitute.For<IContainerAuditPersistentSource>();
            audit.WhenForAnyArgs(x => x.InsertContainerAudit(default!, default, default, default, default)).Do(call =>
            {
                realAudit.InsertContainerAudit(call.ArgAt<string>(0), call.ArgAt<ActionType>(1), call.ArgAt<int?>(2),
                    call.ArgAt<string?>(3), call.ArgAt<string?>(4));
                if (failAudit) throw new DbUpdateException("Simulated failure after audit SaveChanges");
            });
            controller = new RefDataContainersController(new ContainersPersistentSource(_factory), audit, environments, roles, security, claims);
        }
        else if (kind == 1)
        {
            var realAudit = new CloudResourceAuditPersistentSource(_factory);
            var audit = Substitute.For<ICloudResourceAuditPersistentSource>();
            audit.WhenForAnyArgs(x => x.InsertCloudResourceAudit(default!, default, default, default, default)).Do(call =>
            {
                realAudit.InsertCloudResourceAudit(call.ArgAt<string>(0), call.ArgAt<ActionType>(1), call.ArgAt<int?>(2),
                    call.ArgAt<string?>(3), call.ArgAt<string?>(4));
                if (failAudit) throw new DbUpdateException("Simulated failure after audit SaveChanges");
            });
            controller = new RefDataCloudResourcesController(new CloudResourcesPersistentSource(_factory), audit, environments, roles, security, claims);
        }
        else
        {
            var realAudit = new ApiRegistrationAuditPersistentSource(_factory);
            var audit = Substitute.For<IApiRegistrationAuditPersistentSource>();
            audit.WhenForAnyArgs(x => x.InsertApiRegistrationAudit(default!, default, default, default, default)).Do(call =>
            {
                realAudit.InsertApiRegistrationAudit(call.ArgAt<string>(0), call.ArgAt<ActionType>(1), call.ArgAt<int?>(2),
                    call.ArgAt<string?>(3), call.ArgAt<string?>(4));
                if (failAudit) throw new DbUpdateException("Simulated failure after audit SaveChanges");
            });
            controller = new RefDataApiRegistrationsController(new ApiRegistrationsPersistentSource(_factory), audit, environments, roles, security, claims);
        }
        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };
        return controller;
    }

    private static int AuditCount(int kind)
    {
        using var context = _factory.GetContext();
        return kind switch
        {
            0 => context.ContainerAudits.Count(),
            1 => context.CloudResourceAudits.Count(),
            _ => context.ApiRegistrationAudits.Count()
        };
    }

    [DataTestMethod]
    [DataRow(0, "create")]
    [DataRow(0, "update")]
    [DataRow(0, "tags")]
    [DataRow(0, "delete")]
    [DataRow(0, "attach")]
    [DataRow(0, "detach")]
    [DataRow(1, "create")]
    [DataRow(1, "update")]
    [DataRow(1, "tags")]
    [DataRow(1, "delete")]
    [DataRow(1, "attach")]
    [DataRow(1, "detach")]
    [DataRow(2, "create")]
    [DataRow(2, "update")]
    [DataRow(2, "tags")]
    [DataRow(2, "delete")]
    [DataRow(2, "attach")]
    [DataRow(2, "detach")]
    public void AuditFailure_RollsBackMutationAndAuditAcrossSeparateContexts(int kind, string operation)
    {
        dynamic source = Source(kind);
        var name = Guid.NewGuid().ToString("N");
        dynamic item = source.Add(Model(kind, name));
        int id = item.Id;
        if (operation == "detach") source.AttachToEnvironment(id, _allowedEnvironment);
        var before = JsonSerializer.Serialize((object)source.GetById(id));
        var auditCount = AuditCount(kind);
        dynamic controller = Controller(kind, failAudit: true);
        Assert.ThrowsExactly<DbUpdateException>(() =>
        {
            switch (operation)
            {
                case "create": controller.Post(Model(kind, name + "-created")); break;
                case "update": item.Name += "-updated"; controller.Put(id, item); break;
                case "tags": controller.PutTags(id, new ResourceTagsUpdate { ExpectedTags = "old", Tags = "new" }); break;
                case "delete": controller.Delete(id); break;
                case "attach": controller.Attach(id, _allowedEnvironment); break;
                case "detach": controller.Detach(id, _allowedEnvironment); break;
            }
        });
        Assert.AreEqual(before, JsonSerializer.Serialize((object)source.GetById(id)));
        Assert.IsNull((object?)source.GetByName(name + "-created"));
        Assert.AreEqual(auditCount, AuditCount(kind));
        Assert.IsNull(Transaction.Current);
    }

    [DataTestMethod]
    [DataRow(0, false, true)]
    [DataRow(0, true, true)]
    [DataRow(0, false, false)]
    [DataRow(1, false, true)]
    [DataRow(1, true, true)]
    [DataRow(1, false, false)]
    [DataRow(2, false, true)]
    [DataRow(2, true, true)]
    [DataRow(2, false, false)]
    public async Task AuthorizationMembershipRange_BlocksNonCooperatingConcurrentMembershipChange(
        int kind, bool detach, bool initiallyAttached)
    {
        dynamic source = Source(kind);
        dynamic item = source.Add(Model(kind, Guid.NewGuid().ToString("N")));
        int id = item.Id;
        if (initiallyAttached) source.AttachToEnvironment(id, _allowedEnvironment);
        item = source.GetById(id);
        item.Name += "-edited";
        using var authorized = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        dynamic controller = Controller(kind, authorizing: () =>
        {
            authorized.Set();
            Assert.IsTrue(release.Wait(TimeSpan.FromSeconds(15)), "Release authorization callback");
        }, authorizeRole: !initiallyAttached);
        var mutation = Task.Run(() => (IActionResult)controller.Put(id, item));
        Task? attach = null;
        try
        {
            Assert.IsTrue(authorized.Wait(TimeSpan.FromSeconds(15)), "Authorization must read membership before mutation");
            var resource = kind == 0 ? "Container" : kind == 1 ? "CloudResource" : "ApiRegistration";
            using var started = new ManualResetEventSlim();
            attach = Task.Run(() =>
            {
                // Direct SQL represents clone/registrar paths: no shared application
                // lock or transaction helper, only the database membership FK/range.
                using var connection = new SqlConnection(_connection);
                connection.Open();
                using var command = new SqlCommand(detach
                    ? $"DELETE deploy.Environment{resource} WHERE EnvId = @env AND {resource}Id = @id"
                    : $"INSERT deploy.Environment{resource} (EnvId, {resource}Id) VALUES (@env, @id)", connection);
                command.Parameters.AddWithValue("@env", detach ? _allowedEnvironment : _otherEnvironment);
                command.Parameters.AddWithValue("@id", id);
                started.Set();
                command.ExecuteNonQuery();
            });
            Assert.IsTrue(started.Wait(TimeSpan.FromSeconds(5)));
            await Task.Delay(300);
            Assert.IsFalse(attach.IsCompleted, "Membership cannot change between authorization and mutation");
        }
        finally
        {
            release.Set();
            await mutation;
            if (attach != null) await attach;
        }
        Assert.IsInstanceOfType<OkObjectResult>(await mutation);
        if (!detach)
        {
            var denied = (ObjectResult)Controller(kind).PutTags(id, new ResourceTagsUpdate { ExpectedTags = "old", Tags = "forbidden" });
            Assert.AreEqual(403, denied.StatusCode, "After attachment commits the same caller must be rejected");
            dynamic visible = ((OkObjectResult)Controller(kind).GetById(id)).Value!;
            Assert.IsFalse((bool)visible.CanEditDefinition, "UI capability must require every mapped environment too");
        }
    }

    [DataTestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(2)]
    public void Tags_PreserveConcurrentDefinitionEdit_AndRejectStaleTagsAndFullDefinition(int kind)
    {
        dynamic source = Source(kind);
        dynamic stale = source.Add(Model(kind, Guid.NewGuid().ToString("N")));
        int id = stale.Id;
        dynamic latest = source.GetById(id);
        latest.Name += "-new";
        source.Update(id, latest);
        dynamic controller = Controller(kind);
        var result = (ObjectResult)controller.PutTags(id, new ResourceTagsUpdate { ExpectedTags = "old", Tags = "new" });
        Assert.AreEqual(200, result.StatusCode);
        dynamic actual = source.GetById(id);
        Assert.AreEqual((string)latest.Name, (string)actual.Name);
        Assert.AreEqual("new", (string)actual.Tags);
        Assert.AreEqual(409, ((ObjectResult)controller.PutTags(id, new ResourceTagsUpdate { ExpectedTags = "old", Tags = "lost" })).StatusCode);
        Assert.AreEqual(409, ((ObjectResult)controller.Put(id, stale)).StatusCode);
    }

    [DataTestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(2)]
    public async Task ConcurrentAttach_IsRetriedAsAUnit_AndProducesOneAudit(int kind)
    {
        dynamic source = Source(kind);
        dynamic item = source.Add(Model(kind, Guid.NewGuid().ToString("N")));
        int id = item.Id;
        var count = AuditCount(kind);
        using var start = new ManualResetEventSlim();
        var attempts = Enumerable.Range(0, 2).Select(_ => Task.Run(() =>
        {
            start.Wait();
            return (ObjectResult)Controller(kind).Attach(id, _allowedEnvironment);
        })).ToArray();
        start.Set();
        var results = await Task.WhenAll(attempts);
        CollectionAssert.AreEquivalent(new[] { 200, 409 }, results.Select(result => result.StatusCode!.Value).ToArray());
        Assert.AreEqual(count + 1, AuditCount(kind));
    }
}
