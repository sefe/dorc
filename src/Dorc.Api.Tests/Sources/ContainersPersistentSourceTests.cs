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
    /// Pattern-setting tests for the environment-component sources (IS step S-003),
    /// covering the HLPS do-not-copy defects: null-guarded deletes, eager-loaded
    /// projections, and behavioural attach/detach outcomes.
    /// </summary>
    [TestClass]
    public class ContainersPersistentSourceTests
    {
        private IDeploymentContextFactory _contextFactory = null!;
        private IDeploymentContext _context = null!;
        private ContainersPersistentSource _source = null!;

        [TestInitialize]
        public void Setup()
        {
            _contextFactory = Substitute.For<IDeploymentContextFactory>();
            _context = Substitute.For<IDeploymentContext>();
            _contextFactory.GetContext().Returns(_context);
            _source = new ContainersPersistentSource(_contextFactory);
        }

        private List<Container> SetupContainers(params Container[] containers)
        {
            var list = containers.ToList();
            var dbSet = DbContextMock.GetQueryableMockDbSet(list);
            _context.Containers.Returns(dbSet);
            return list;
        }

        private void SetupEnvironments(params Environment[] environments)
        {
            var dbSet = DbContextMock.GetQueryableMockDbSet(environments.ToList());
            _context.Environments.Returns(dbSet);
        }

        private static Environment Env(int id, string name) => new() { Id = id, Name = name };

        [TestMethod]
        public void Delete_UnknownId_ReturnsFalseWithoutThrowing()
        {
            SetupContainers();

            var result = _source.Delete(999);

            Assert.IsFalse(result);
            _context.DidNotReceive().SaveChanges();
        }

        [TestMethod]
        public void Delete_ExistingItem_DetachesEnvironmentsAndRemoves()
        {
            var env = Env(1, "DV 01");
            var container = new Container
            { Id = 5, Name = "web", Image = "img", Environments = new List<Environment> { env } };
            var list = SetupContainers(container);

            var result = _source.Delete(5);

            Assert.IsTrue(result);
            Assert.AreEqual(0, list.Count);
            _context.Received(1).SaveChanges();
        }

        [TestMethod]
        public void GetById_PopulatesEnvironmentNames()
        {
            SetupContainers(new Container
            {
                Id = 5,
                Name = "web",
                Image = "nginx:1.25",
                Registry = "registry.local",
                HostServerName = "host01",
                Tags = "edge;web",
                Environments = new List<Environment> { Env(1, "DV 01"), Env(2, "DV 02") }
            });

            var result = _source.GetById(5);

            Assert.IsNotNull(result);
            Assert.AreEqual("web", result.Name);
            Assert.AreEqual("nginx:1.25", result.Image);
            Assert.AreEqual("registry.local", result.Registry);
            Assert.AreEqual("host01", result.HostServerName);
            Assert.AreEqual("edge;web", result.Tags);
            CollectionAssert.AreEqual(new[] { "DV 01", "DV 02" }, result.EnvironmentNames);
        }

        [TestMethod]
        public void GetById_UnknownId_ReturnsNull()
        {
            SetupContainers();

            Assert.IsNull(_source.GetById(42));
        }

        [TestMethod]
        public void Add_MapsAllFields()
        {
            var list = SetupContainers();

            var result = _source.Add(new ContainerApiModel
            { Name = "web", Image = "img:1", Registry = "reg", HostServerName = "h1", Tags = "t1" });

            Assert.AreEqual(1, list.Count);
            Assert.AreEqual("web", list[0].Name);
            Assert.AreEqual("img:1", list[0].Image);
            Assert.AreEqual("reg", list[0].Registry);
            Assert.AreEqual("h1", list[0].HostServerName);
            Assert.AreEqual("t1", list[0].Tags);
            Assert.AreEqual("web", result.Name);
            _context.Received(1).SaveChanges();
        }

        [TestMethod]
        public void Update_UnknownId_ReturnsNullWithoutSaving()
        {
            SetupContainers();

            Assert.IsNull(_source.Update(9, new ContainerApiModel { Name = "x", Image = "y" }));
            _context.DidNotReceive().SaveChanges();
        }

        [TestMethod]
        public void Update_ExistingItem_MapsAllFields()
        {
            SetupContainers(new Container { Id = 5, Name = "old", Image = "old-img" });

            var result = _source.Update(5, new ContainerApiModel
            { Name = "new", Image = "new-img", Registry = "r2", HostServerName = "h2", Tags = "t2" });

            Assert.IsNotNull(result);
            Assert.AreEqual("new", result.Name);
            Assert.AreEqual("new-img", result.Image);
            Assert.AreEqual("r2", result.Registry);
            Assert.AreEqual("h2", result.HostServerName);
            Assert.AreEqual("t2", result.Tags);
            _context.Received(1).SaveChanges();
        }

        [TestMethod]
        public void GetForEnvironmentId_FiltersByAttachment()
        {
            SetupContainers(
                new Container { Id = 1, Name = "a", Image = "i", Environments = new List<Environment> { Env(1, "DV 01") } },
                new Container { Id = 2, Name = "b", Image = "i", Environments = new List<Environment> { Env(2, "DV 02") } });

            var result = _source.GetForEnvironmentId(1).ToList();

            Assert.AreEqual(1, result.Count);
            Assert.AreEqual("a", result[0].Name);
        }

        [TestMethod]
        public void Attach_UnknownItem_ReturnsItemNotFound()
        {
            SetupContainers();
            SetupEnvironments(Env(1, "DV 01"));

            Assert.AreEqual(EnvironmentAttachmentOutcome.ItemNotFound, _source.AttachToEnvironment(9, 1));
        }

        [TestMethod]
        public void Attach_UnknownEnvironment_ReturnsEnvironmentNotFound()
        {
            SetupContainers(new Container { Id = 5, Name = "web", Image = "i" });
            SetupEnvironments();

            Assert.AreEqual(EnvironmentAttachmentOutcome.EnvironmentNotFound, _source.AttachToEnvironment(5, 9));
        }

        [TestMethod]
        public void Attach_Duplicate_ReturnsAlreadyAttachedWithoutSaving()
        {
            var env = Env(1, "DV 01");
            SetupContainers(new Container
            { Id = 5, Name = "web", Image = "i", Environments = new List<Environment> { env } });
            SetupEnvironments(env);

            Assert.AreEqual(EnvironmentAttachmentOutcome.AlreadyAttached, _source.AttachToEnvironment(5, 1));
            _context.DidNotReceive().SaveChanges();
        }

        [TestMethod]
        public void Attach_HappyPath_AddsEnvironmentAndSaves()
        {
            var container = new Container { Id = 5, Name = "web", Image = "i" };
            SetupContainers(container);
            SetupEnvironments(Env(1, "DV 01"));

            Assert.AreEqual(EnvironmentAttachmentOutcome.Attached, _source.AttachToEnvironment(5, 1));
            Assert.AreEqual(1, container.Environments.Count);
            _context.Received(1).SaveChanges();
        }

        [TestMethod]
        public void Detach_NotAttached_ReturnsNotAttachedWithoutSaving()
        {
            SetupContainers(new Container { Id = 5, Name = "web", Image = "i" });

            Assert.AreEqual(EnvironmentAttachmentOutcome.NotAttached, _source.DetachFromEnvironment(5, 1));
            _context.DidNotReceive().SaveChanges();
        }

        [TestMethod]
        public void Detach_HappyPath_RemovesEnvironmentAndSaves()
        {
            var env = Env(1, "DV 01");
            var container = new Container
            { Id = 5, Name = "web", Image = "i", Environments = new List<Environment> { env } };
            SetupContainers(container);

            Assert.AreEqual(EnvironmentAttachmentOutcome.Detached, _source.DetachFromEnvironment(5, 1));
            Assert.AreEqual(0, container.Environments.Count);
            _context.Received(1).SaveChanges();
        }
    }
}
