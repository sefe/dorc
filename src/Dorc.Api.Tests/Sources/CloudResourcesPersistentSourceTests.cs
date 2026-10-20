using Dorc.Api.Tests.Mocks;
using Dorc.ApiModel;
using Dorc.PersistentData.Contexts;
using Dorc.PersistentData.Model;
using Dorc.PersistentData.Sources;
using NSubstitute;
using Environment = Dorc.PersistentData.Model.Environment;

namespace Dorc.Api.Tests.Sources
{
    /// <summary>
    /// Parity tests with ContainersPersistentSourceTests (pattern-setter) covering the
    /// do-not-copy defects and attach/detach outcomes for cloud resources.
    /// </summary>
    [TestClass]
    public class CloudResourcesPersistentSourceTests
    {
        private IDeploymentContextFactory _contextFactory = null!;
        private IDeploymentContext _context = null!;
        private CloudResourcesPersistentSource _source = null!;

        [TestInitialize]
        public void Setup()
        {
            _contextFactory = Substitute.For<IDeploymentContextFactory>();
            _context = Substitute.For<IDeploymentContext>();
            _contextFactory.GetContext().Returns(_context);
            _source = new CloudResourcesPersistentSource(_contextFactory);
        }

        private List<CloudResource> SetupCloudResources(params CloudResource[] items)
        {
            var list = items.ToList();
            var dbSet = DbContextMock.GetQueryableMockDbSet(list);
            _context.CloudResources.Returns(dbSet);
            return list;
        }

        private static Environment Env(int id, string name) => new() { Id = id, Name = name };

        [TestMethod]
        public void Delete_UnknownId_ReturnsFalseWithoutThrowing()
        {
            SetupCloudResources();

            Assert.IsFalse(_source.Delete(999));
            _context.DidNotReceive().SaveChanges();
        }

        [TestMethod]
        public void GetById_PopulatesEnvironmentNamesAndFields()
        {
            SetupCloudResources(new CloudResource
            {
                Id = 3,
                Name = "kv",
                Provider = "Azure",
                ResourceType = "KeyVault",
                ResourceIdentifier = "/subs/1/kv",
                Subscription = "sub-1",
                Tags = "secure",
                Environments = new List<Environment> { Env(1, "DV 01") }
            });

            var result = _source.GetById(3);

            Assert.IsNotNull(result);
            Assert.AreEqual("Azure", result.Provider);
            Assert.AreEqual("KeyVault", result.ResourceType);
            Assert.AreEqual("/subs/1/kv", result.ResourceIdentifier);
            Assert.AreEqual("sub-1", result.Subscription);
            CollectionAssert.AreEqual(new[] { "DV 01" }, result.EnvironmentNames);
        }

        [TestMethod]
        public void GetByIdentity_ArmTypeDriftAndCaseAreMetadata_NotNewIdentity()
        {
            SetupCloudResources(new CloudResource
            {
                Id = 3, Name = "renamed", Provider = "Azure", ResourceType = "legacy-type",
                ResourceIdentifier = "/SUBSCRIPTIONS/1/RESOURCEGROUPS/DEMO", Tags = "owner",
                Environments = new List<Environment> { Env(1, "existing") }
            });

            var match = _source.GetByIdentity("Azure", "azurerm_resource_group", "/subscriptions/1/resourceGroups/demo").Single();

            Assert.AreEqual(3, match.Id);
            Assert.AreEqual("renamed", match.Name);
            Assert.AreEqual("owner", match.Tags);
            CollectionAssert.AreEqual(new[] { "existing" }, match.EnvironmentNames);
        }

        [TestMethod]
        public void GetByIdentity_SeparatesProviderTypeAndCase_AndReturnsAllDuplicates()
        {
            SetupCloudResources(
                new CloudResource { Id = 1, Name = "one", Provider = "Aiven", ResourceType = "aiven_kafka_topic", ResourceIdentifier = "p/s/Topic" },
                new CloudResource { Id = 2, Name = "duplicate", Provider = "Aiven", ResourceType = "aiven_kafka_topic", ResourceIdentifier = "p/s/Topic" },
                new CloudResource { Id = 3, Name = "case", Provider = "Aiven", ResourceType = "aiven_kafka_topic", ResourceIdentifier = "p/s/topic" },
                new CloudResource { Id = 4, Name = "type", Provider = "Aiven", ResourceType = "aiven_pg_database", ResourceIdentifier = "p/s/Topic" },
                new CloudResource { Id = 5, Name = "provider", Provider = "Other", ResourceType = "aiven_kafka_topic", ResourceIdentifier = "p/s/Topic" });

            var matches = _source.GetByIdentity("Aiven", "aiven_kafka_topic", "p/s/Topic");

            CollectionAssert.AreEquivalent(new[] { 1, 2 }, matches.Select(model => model.Id).ToArray());
        }

        [TestMethod]
        public void Update_ExistingItem_MapsAllFields()
        {
            SetupCloudResources(new CloudResource
            { Id = 3, Name = "old", Provider = "p", ResourceType = "t", ResourceIdentifier = "i" });

            var result = _source.Update(3, new CloudResourceApiModel
            {
                Name = "new",
                Provider = "AWS",
                ResourceType = "S3",
                ResourceIdentifier = "arn:s3",
                Subscription = "acct",
                Tags = "t"
            });

            Assert.IsNotNull(result);
            Assert.AreEqual("new", result.Name);
            Assert.AreEqual("AWS", result.Provider);
            Assert.AreEqual("S3", result.ResourceType);
            Assert.AreEqual("arn:s3", result.ResourceIdentifier);
            Assert.AreEqual("acct", result.Subscription);
            _context.Received(1).SaveChanges();
        }

        [TestMethod]
        public void Attach_Duplicate_ReturnsAlreadyAttachedWithoutSaving()
        {
            var env = Env(1, "DV 01");
            SetupCloudResources(new CloudResource
            {
                Id = 3, Name = "kv", Provider = "p", ResourceType = "t", ResourceIdentifier = "i",
                Environments = new List<Environment> { env }
            });
            var envSet = DbContextMock.GetQueryableMockDbSet(new List<Environment> { env });
            _context.Environments.Returns(envSet);

            Assert.AreEqual(EnvironmentAttachmentOutcome.AlreadyAttached, _source.AttachToEnvironment(3, 1));
            _context.DidNotReceive().SaveChanges();
        }

        [TestMethod]
        public void AttachThenDetach_HappyPaths()
        {
            var item = new CloudResource
            { Id = 3, Name = "kv", Provider = "p", ResourceType = "t", ResourceIdentifier = "i" };
            SetupCloudResources(item);
            var envSet = DbContextMock.GetQueryableMockDbSet(new List<Environment> { Env(1, "DV 01") });
            _context.Environments.Returns(envSet);

            Assert.AreEqual(EnvironmentAttachmentOutcome.Attached, _source.AttachToEnvironment(3, 1));
            Assert.AreEqual(EnvironmentAttachmentOutcome.Detached, _source.DetachFromEnvironment(3, 1));
            Assert.AreEqual(0, item.Environments.Count);
        }
    }
}
