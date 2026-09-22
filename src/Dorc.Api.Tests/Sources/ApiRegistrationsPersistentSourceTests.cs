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
    /// do-not-copy defects and attach/detach outcomes for API registrations.
    /// </summary>
    [TestClass]
    public class ApiRegistrationsPersistentSourceTests
    {
        private IDeploymentContextFactory _contextFactory = null!;
        private IDeploymentContext _context = null!;
        private ApiRegistrationsPersistentSource _source = null!;

        [TestInitialize]
        public void Setup()
        {
            _contextFactory = Substitute.For<IDeploymentContextFactory>();
            _context = Substitute.For<IDeploymentContext>();
            _contextFactory.GetContext().Returns(_context);
            _source = new ApiRegistrationsPersistentSource(_contextFactory);
        }

        private List<ApiRegistration> SetupApiRegistrations(params ApiRegistration[] items)
        {
            var list = items.ToList();
            var dbSet = DbContextMock.GetQueryableMockDbSet(list);
            _context.ApiRegistrations.Returns(dbSet);
            return list;
        }

        private static Environment Env(int id, string name) => new() { Id = id, Name = name };

        [TestMethod]
        public void Delete_UnknownId_ReturnsFalseWithoutThrowing()
        {
            SetupApiRegistrations();

            Assert.IsFalse(_source.Delete(999));
            _context.DidNotReceive().SaveChanges();
        }

        [TestMethod]
        public void GetById_PopulatesEnvironmentNamesAndFields()
        {
            SetupApiRegistrations(new ApiRegistration
            {
                Id = 7,
                Name = "orders-api",
                BaseUrl = "https://orders.local",
                Version = "v2",
                HealthCheckUrl = "https://orders.local/health",
                Tags = "core",
                Environments = new List<Environment> { Env(1, "DV 01") }
            });

            var result = _source.GetById(7);

            Assert.IsNotNull(result);
            Assert.AreEqual("orders-api", result.Name);
            Assert.AreEqual("https://orders.local", result.BaseUrl);
            Assert.AreEqual("v2", result.Version);
            Assert.AreEqual("https://orders.local/health", result.HealthCheckUrl);
            CollectionAssert.AreEqual(new[] { "DV 01" }, result.EnvironmentNames);
        }

        [TestMethod]
        public void Update_ExistingItem_MapsAllFields()
        {
            SetupApiRegistrations(new ApiRegistration { Id = 7, Name = "old", BaseUrl = "http://x" });

            var result = _source.Update(7, new ApiRegistrationApiModel
            { Name = "new", BaseUrl = "https://y", Version = "v3", HealthCheckUrl = "https://y/hc", Tags = "t" });

            Assert.IsNotNull(result);
            Assert.AreEqual("new", result.Name);
            Assert.AreEqual("https://y", result.BaseUrl);
            Assert.AreEqual("v3", result.Version);
            Assert.AreEqual("https://y/hc", result.HealthCheckUrl);
            _context.Received(1).SaveChanges();
        }

        [TestMethod]
        public void Attach_Duplicate_ReturnsAlreadyAttachedWithoutSaving()
        {
            var env = Env(1, "DV 01");
            SetupApiRegistrations(new ApiRegistration
            { Id = 7, Name = "a", BaseUrl = "u", Environments = new List<Environment> { env } });
            var envSet = DbContextMock.GetQueryableMockDbSet(new List<Environment> { env });
            _context.Environments.Returns(envSet);

            Assert.AreEqual(EnvironmentAttachmentOutcome.AlreadyAttached, _source.AttachToEnvironment(7, 1));
            _context.DidNotReceive().SaveChanges();
        }

        [TestMethod]
        public void AttachThenDetach_HappyPaths()
        {
            var item = new ApiRegistration { Id = 7, Name = "a", BaseUrl = "u" };
            SetupApiRegistrations(item);
            var envSet = DbContextMock.GetQueryableMockDbSet(new List<Environment> { Env(1, "DV 01") });
            _context.Environments.Returns(envSet);

            Assert.AreEqual(EnvironmentAttachmentOutcome.Attached, _source.AttachToEnvironment(7, 1));
            Assert.AreEqual(EnvironmentAttachmentOutcome.Detached, _source.DetachFromEnvironment(7, 1));
            Assert.AreEqual(0, item.Environments.Count);
        }
    }
}
