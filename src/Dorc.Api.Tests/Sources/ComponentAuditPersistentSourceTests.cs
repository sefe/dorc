using Dorc.Api.Tests.Mocks;
using Dorc.PersistentData.Contexts;
using Dorc.PersistentData.Model;
using Dorc.PersistentData.Sources;
using NSubstitute;

namespace Dorc.Api.Tests.Sources
{
    /// <summary>
    /// Audit insert behaviour for the environment-component audit sources (IS S-003):
    /// rows are written with the resolved action, and no-op updates are skipped,
    /// matching the DaemonAuditPersistentSource convention.
    /// </summary>
    [TestClass]
    public class ComponentAuditPersistentSourceTests
    {
        private IDeploymentContextFactory _contextFactory = null!;
        private IDeploymentContext _context = null!;
        private List<ContainerAudit> _containerAudits = null!;

        [TestInitialize]
        public void Setup()
        {
            _contextFactory = Substitute.For<IDeploymentContextFactory>();
            _context = Substitute.For<IDeploymentContext>();
            _contextFactory.GetContext().Returns(_context);

            var actionSet = DbContextMock.GetQueryableMockDbSet(
                Enum.GetValues<ActionType>()
                    .Select(a => new RefDataAuditAction { RefDataAuditActionId = (int)a + 1, Action = a })
                    .ToList());
            _context.RefDataAuditActions.Returns(actionSet);

            _containerAudits = new List<ContainerAudit>();
            var containerAuditSet = DbContextMock.GetQueryableMockDbSet(_containerAudits);
            _context.ContainerAudits.Returns(containerAuditSet);
        }

        [TestMethod]
        public void InsertContainerAudit_WritesRowWithResolvedAction()
        {
            var source = new ContainerAuditPersistentSource(_contextFactory);

            source.InsertContainerAudit(@"DOM\alice", ActionType.Create, 5, null, "{\"Name\":\"web\"}");

            Assert.AreEqual(1, _containerAudits.Count);
            Assert.AreEqual(5, _containerAudits[0].ContainerId);
            Assert.AreEqual(@"DOM\alice", _containerAudits[0].Username);
            Assert.AreEqual(ActionType.Create, _containerAudits[0].Action.Action);
            _context.Received(1).SaveChanges();
        }

        [TestMethod]
        public void InsertContainerAudit_NoOpUpdate_IsSkipped()
        {
            var source = new ContainerAuditPersistentSource(_contextFactory);

            source.InsertContainerAudit(@"DOM\alice", ActionType.Update, 5, "same", "same");

            Assert.AreEqual(0, _containerAudits.Count);
            _context.DidNotReceive().SaveChanges();
        }

        [TestMethod]
        public void InsertCloudResourceAudit_WritesRow()
        {
            var audits = new List<CloudResourceAudit>();
            var auditSet = DbContextMock.GetQueryableMockDbSet(audits);
            _context.CloudResourceAudits.Returns(auditSet);
            var source = new CloudResourceAuditPersistentSource(_contextFactory);

            source.InsertCloudResourceAudit(@"DOM\bob", ActionType.Attach, 3, null, "attached");

            Assert.AreEqual(1, audits.Count);
            Assert.AreEqual(ActionType.Attach, audits[0].Action.Action);
        }

        [TestMethod]
        public void InsertApiRegistrationAudit_WritesRow()
        {
            var audits = new List<ApiRegistrationAudit>();
            var auditSet = DbContextMock.GetQueryableMockDbSet(audits);
            _context.ApiRegistrationAudits.Returns(auditSet);
            var source = new ApiRegistrationAuditPersistentSource(_contextFactory);

            source.InsertApiRegistrationAudit(@"DOM\bob", ActionType.Delete, 7, "{}", null);

            Assert.AreEqual(1, audits.Count);
            Assert.AreEqual(ActionType.Delete, audits[0].Action.Action);
        }
    }
}
