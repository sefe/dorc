using Dorc.ApiModel;
using Dorc.Monitor.Terraform;
using Dorc.PersistentData.Model;
using Dorc.PersistentData.Sources;
using Dorc.PersistentData.Sources.Interfaces;
using Microsoft.Extensions.Logging;
using NSubstitute;
using System.Text.Json;

namespace Dorc.Monitor.Tests.Terraform
{
    [TestClass]
    public class TerraformCloudResourceRegistrarTests
    {
        private ICloudResourcesPersistentSource _cloudResources = null!;
        private ICloudResourceAuditPersistentSource _audit = null!;
        private IEnvironmentsPersistentSource _environments = null!;
        private IDatabasesPersistentSource _databases = null!;
        private TerraformCloudResourceRegistrar _registrar = null!;
        private string _file = null!;

        private static readonly EnvironmentApiModel Environment = new()
        {
            EnvironmentId = 2598,
            EnvironmentName = "Terraform Dev"
        };

        [TestInitialize]
        public void Setup()
        {
            _cloudResources = Substitute.For<ICloudResourcesPersistentSource>();
            _audit = Substitute.For<ICloudResourceAuditPersistentSource>();
            _environments = Substitute.For<IEnvironmentsPersistentSource>();
            _databases = Substitute.For<IDatabasesPersistentSource>();
            _registrar = new TerraformCloudResourceRegistrar(
                Substitute.For<ILogger>(), _cloudResources, _audit, _environments, _databases);
            _file = Path.Combine(Path.GetTempPath(), $"applied-{Guid.NewGuid():N}.json");

            _environments.GetEnvironment("Terraform Dev").Returns(Environment);
            _cloudResources.Add(Arg.Any<CloudResourceApiModel>())
                .Returns(call =>
                {
                    var model = call.Arg<CloudResourceApiModel>();
                    model.Id = 42;
                    return model;
                });
            _cloudResources.AttachToEnvironment(Arg.Any<int>(), Arg.Any<int>())
                .Returns(EnvironmentAttachmentOutcome.Attached);
        }

        [TestCleanup]
        public void Cleanup()
        {
            if (File.Exists(_file)) File.Delete(_file);
        }

        private void WriteResources(params CloudResourceApiModel[] resources) =>
            File.WriteAllText(_file, JsonSerializer.Serialize(resources));

        private static CloudResourceApiModel AppliedResourceGroup() => new()
        {
            Name = "rg-sh-dv-demo",
            Provider = "Azure",
            ResourceType = "azurerm_resource_group",
            ResourceIdentifier = "/subscriptions/7c7c1f8f-f295-456c-81e4-5d508579d93e/resourceGroups/rg-sh-dv-demo",
            Subscription = "7c7c1f8f-f295-456c-81e4-5d508579d93e",
            Tags = string.Empty
        };

        [TestMethod]
        public void MissingFile_DoesNothing()
        {
            _registrar.RegisterAppliedResources(_file, "Terraform Dev", "user");

            _environments.DidNotReceiveWithAnyArgs().GetEnvironment(default(string)!);
            _cloudResources.DidNotReceiveWithAnyArgs().Add(default!);
        }

        [TestMethod]
        public void NewResource_IsCreatedAttachedAndAudited()
        {
            WriteResources(AppliedResourceGroup());
            _cloudResources.GetByName("rg-sh-dv-demo").Returns((CloudResourceApiModel?)null);

            _registrar.RegisterAppliedResources(_file, "Terraform Dev", "adentis\\bhegarty");

            _cloudResources.Received(1).Add(Arg.Is<CloudResourceApiModel>(m =>
                m.Name == "rg-sh-dv-demo" && m.ResourceType == "azurerm_resource_group"));
            _cloudResources.Received(1).AttachToEnvironment(42, 2598);
            _audit.Received(1).InsertCloudResourceAudit("adentis\\bhegarty", ActionType.Create, 42, Arg.Is<string?>(v => v == null), Arg.Any<string?>());
            _audit.Received(1).InsertCloudResourceAudit("adentis\\bhegarty", ActionType.Attach, 42, Arg.Is<string?>(v => v == null), Arg.Any<string?>());
        }

        [TestMethod]
        public void ExistingResourceSameIdentifier_IsReusedNotDuplicated()
        {
            var applied = AppliedResourceGroup();
            WriteResources(applied);
            _cloudResources.GetByName("rg-sh-dv-demo").Returns(new CloudResourceApiModel
            {
                Id = 7,
                Name = applied.Name,
                Provider = applied.Provider,
                ResourceType = applied.ResourceType,
                ResourceIdentifier = applied.ResourceIdentifier.ToUpperInvariant(),
                Subscription = applied.Subscription,
                Tags = "owner=team"
            });

            _registrar.RegisterAppliedResources(_file, "Terraform Dev", "user");

            _cloudResources.DidNotReceiveWithAnyArgs().Add(default!);
            _cloudResources.DidNotReceiveWithAnyArgs().Update(default, default!);
            _cloudResources.Received(1).AttachToEnvironment(7, 2598);
        }

        [TestMethod]
        public void ExistingResourceWithDrift_IsUpdatedPreservingTags()
        {
            var applied = AppliedResourceGroup();
            WriteResources(applied);
            var existing = new CloudResourceApiModel
            {
                Id = 7,
                Name = applied.Name,
                Provider = "Azure",
                ResourceType = "legacy-type",
                ResourceIdentifier = applied.ResourceIdentifier,
                Subscription = applied.Subscription,
                Tags = "owner=team"
            };
            _cloudResources.GetByName("rg-sh-dv-demo").Returns(existing);
            _cloudResources.Update(7, Arg.Any<CloudResourceApiModel>())
                .Returns(call => call.Arg<CloudResourceApiModel>());

            _registrar.RegisterAppliedResources(_file, "Terraform Dev", "user");

            _cloudResources.Received(1).Update(7, Arg.Is<CloudResourceApiModel>(m =>
                m.ResourceType == "azurerm_resource_group" && m.Tags == "owner=team"));
            _audit.Received(1).InsertCloudResourceAudit("user", ActionType.Update, 7, Arg.Any<string?>(), Arg.Any<string?>());
        }

        [TestMethod]
        public void NameClashWithDifferentIdentifier_FallsBackToDisambiguatedName()
        {
            var applied = AppliedResourceGroup();
            WriteResources(applied);
            _cloudResources.GetByName("rg-sh-dv-demo").Returns(new CloudResourceApiModel
            {
                Id = 7,
                Name = applied.Name,
                ResourceIdentifier = "/subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/other",
            });
            _cloudResources.GetByName("rg-sh-dv-demo (azurerm_resource_group)").Returns((CloudResourceApiModel?)null);

            _registrar.RegisterAppliedResources(_file, "Terraform Dev", "user");

            _cloudResources.Received(1).Add(Arg.Is<CloudResourceApiModel>(m =>
                m.Name == "rg-sh-dv-demo (azurerm_resource_group)"));
        }

        [TestMethod]
        public void NameClashOnBothCandidates_SkipsResourceWithoutThrowing()
        {
            var applied = AppliedResourceGroup();
            WriteResources(applied);
            _cloudResources.GetByName(Arg.Any<string>()).Returns(new CloudResourceApiModel
            {
                Id = 7,
                ResourceIdentifier = "different",
            });

            _registrar.RegisterAppliedResources(_file, "Terraform Dev", "user");

            _cloudResources.DidNotReceiveWithAnyArgs().Add(default!);
            _cloudResources.DidNotReceiveWithAnyArgs().AttachToEnvironment(default, default);
        }

        [TestMethod]
        public void AlreadyAttached_DoesNotWriteAttachAudit()
        {
            WriteResources(AppliedResourceGroup());
            _cloudResources.GetByName("rg-sh-dv-demo").Returns((CloudResourceApiModel?)null);
            _cloudResources.AttachToEnvironment(42, 2598).Returns(EnvironmentAttachmentOutcome.AlreadyAttached);

            _registrar.RegisterAppliedResources(_file, "Terraform Dev", "user");

            _audit.DidNotReceive().InsertCloudResourceAudit(Arg.Any<string>(), ActionType.Attach, Arg.Any<int?>(), Arg.Any<string?>(), Arg.Any<string?>());
        }

        [TestMethod]
        public void UnknownEnvironment_SkipsWithoutThrowing()
        {
            WriteResources(AppliedResourceGroup());
            _environments.GetEnvironment("Missing Env").Returns((EnvironmentApiModel?)null);

            _registrar.RegisterAppliedResources(_file, "Missing Env", "user");

            _cloudResources.DidNotReceiveWithAnyArgs().Add(default!);
        }

        [TestMethod]
        public void CosmosDatabase_IsAlsoRegisteredAsEnvironmentDatabase()
        {
            WriteResources(new CloudResourceApiModel
            {
                Name = "orders-db",
                Provider = "Azure",
                ResourceType = "azurerm_cosmosdb_sql_database",
                ResourceIdentifier = "/subscriptions/7c7c1f8f-f295-456c-81e4-5d508579d93e/resourceGroups/rg/providers/Microsoft.DocumentDB/databaseAccounts/cosmos-sh-dv/sqlDatabases/orders-db",
                Subscription = "7c7c1f8f-f295-456c-81e4-5d508579d93e",
                Tags = string.Empty
            });
            _cloudResources.GetByName(Arg.Any<string>()).Returns((CloudResourceApiModel?)null);
            _databases.GetDatabases("orders-db", "cosmos-sh-dv").Returns(Enumerable.Empty<DatabaseApiModel>());
            _databases.AddDatabase(Arg.Any<DatabaseApiModel>())
                .Returns(call =>
                {
                    var db = call.Arg<DatabaseApiModel>();
                    db.Id = 11;
                    return db;
                });

            _registrar.RegisterAppliedResources(_file, "Terraform Dev", "user");

            _databases.Received(1).AddDatabase(Arg.Is<DatabaseApiModel>(d =>
                d.Name == "orders-db" && d.Type == "CosmosDB" && d.ServerName == "cosmos-sh-dv"));
            _environments.Received(1).AttachDatabaseToEnv(2598, 11, Arg.Any<System.Security.Claims.ClaimsPrincipal>());
        }

        [TestMethod]
        public void ClickHouseDatabase_IsRegisteredWithServiceAsServer()
        {
            WriteResources(new CloudResourceApiModel
            {
                Name = "analytics",
                Provider = "Aiven",
                ResourceType = "aiven_clickhouse_database",
                ResourceIdentifier = "trading-traveler/clickhouse-dev/analytics",
                Tags = string.Empty
            });
            _cloudResources.GetByName(Arg.Any<string>()).Returns((CloudResourceApiModel?)null);
            _databases.GetDatabases("analytics", "clickhouse-dev").Returns(Enumerable.Empty<DatabaseApiModel>());
            _databases.AddDatabase(Arg.Any<DatabaseApiModel>())
                .Returns(call =>
                {
                    var db = call.Arg<DatabaseApiModel>();
                    db.Id = 12;
                    return db;
                });

            _registrar.RegisterAppliedResources(_file, "Terraform Dev", "user");

            _databases.Received(1).AddDatabase(Arg.Is<DatabaseApiModel>(d =>
                d.Name == "analytics" && d.Type == "ClickHouse" && d.ServerName == "clickhouse-dev"));
            _environments.Received(1).AttachDatabaseToEnv(2598, 12, Arg.Any<System.Security.Claims.ClaimsPrincipal>());
        }

        [TestMethod]
        public void ExistingDatabase_IsReusedAndAlreadyAttachedIsBenign()
        {
            WriteResources(new CloudResourceApiModel
            {
                Name = "orders-db",
                Provider = "Azure",
                ResourceType = "azurerm_cosmosdb_sql_database",
                ResourceIdentifier = "/subscriptions/7c7c1f8f-f295-456c-81e4-5d508579d93e/resourceGroups/rg/providers/Microsoft.DocumentDB/databaseAccounts/cosmos-sh-dv/sqlDatabases/orders-db",
                Tags = string.Empty
            });
            _cloudResources.GetByName(Arg.Any<string>()).Returns((CloudResourceApiModel?)null);
            _databases.GetDatabases("orders-db", "cosmos-sh-dv")
                .Returns(new[] { new DatabaseApiModel { Id = 11, Name = "orders-db", ServerName = "cosmos-sh-dv" } });
            _environments.AttachDatabaseToEnv(2598, 11, Arg.Any<System.Security.Claims.ClaimsPrincipal>())
                .Returns(_ => throw new ArgumentException("The specified database is already attached to the specified environment."));

            _registrar.RegisterAppliedResources(_file, "Terraform Dev", "user");

            _databases.DidNotReceiveWithAnyArgs().AddDatabase(default!);
            // The cloud-resource half still registered despite the attach throw.
            _cloudResources.Received(1).Add(Arg.Any<CloudResourceApiModel>());
        }

        [TestMethod]
        public void NonDatabaseResource_DoesNotTouchDatabases()
        {
            WriteResources(AppliedResourceGroup());
            _cloudResources.GetByName(Arg.Any<string>()).Returns((CloudResourceApiModel?)null);

            _registrar.RegisterAppliedResources(_file, "Terraform Dev", "user");

            _databases.DidNotReceiveWithAnyArgs().AddDatabase(default!);
            _environments.DidNotReceiveWithAnyArgs().AttachDatabaseToEnv(default, default, default!);
        }

        [TestMethod]
        public void PersistenceFailureForOneResource_DoesNotStopOthersOrThrow()
        {
            var second = AppliedResourceGroup();
            second.Name = "rg-second";
            second.ResourceIdentifier = "/subscriptions/7c7c1f8f-f295-456c-81e4-5d508579d93e/resourceGroups/rg-second";
            WriteResources(AppliedResourceGroup(), second);
            _cloudResources.GetByName("rg-sh-dv-demo").Returns(_ => throw new InvalidOperationException("db down"));
            _cloudResources.GetByName("rg-second").Returns((CloudResourceApiModel?)null);

            _registrar.RegisterAppliedResources(_file, "Terraform Dev", "user");

            _cloudResources.Received(1).Add(Arg.Is<CloudResourceApiModel>(m => m.Name == "rg-second"));
        }
    }
}
