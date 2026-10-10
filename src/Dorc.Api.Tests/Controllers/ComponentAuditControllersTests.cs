using Dorc.Api.Controllers;
using Dorc.ApiModel;
using Dorc.PersistentData.Sources.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;

namespace Dorc.Api.Tests.Controllers
{
    /// <summary>
    /// The three environment-component audit read endpoints route to the
    /// per-record query when the component id is supplied and to the
    /// cross-record feed otherwise, mirroring DaemonAuditController.
    /// </summary>
    [TestClass]
    public class ComponentAuditControllersTests
    {
        private static readonly PagedDataOperators Operators = new();

        private static readonly GetComponentAuditListResponseDto PerRecord = new() { TotalItems = 1 };
        private static readonly GetComponentAuditListResponseDto CrossRecord = new() { TotalItems = 2 };

        [TestMethod]
        public void ContainerAudit_WithContainerId_UsesPerRecordQuery()
        {
            var source = Substitute.For<IContainerAuditPersistentSource>();
            source.GetContainerAuditByContainerId(5, 50, 1, Operators).Returns(PerRecord);
            var controller = new ContainerAuditController(source);

            var result = Assert.IsInstanceOfType<ObjectResult>(controller.Put(Operators, containerId: 5));
            Assert.AreEqual(StatusCodes.Status200OK, result.StatusCode);
            Assert.AreSame(PerRecord, result.Value);
            source.DidNotReceive().GetContainerAudit(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<PagedDataOperators>());
        }

        [TestMethod]
        public void ContainerAudit_WithoutContainerId_UsesCrossRecordQuery()
        {
            var source = Substitute.For<IContainerAuditPersistentSource>();
            source.GetContainerAudit(50, 1, Operators).Returns(CrossRecord);
            var controller = new ContainerAuditController(source);

            var result = Assert.IsInstanceOfType<ObjectResult>(controller.Put(Operators));
            Assert.AreEqual(StatusCodes.Status200OK, result.StatusCode);
            Assert.AreSame(CrossRecord, result.Value);
            source.DidNotReceive().GetContainerAuditByContainerId(
                Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<PagedDataOperators>());
        }

        [TestMethod]
        public void CloudResourceAudit_WithCloudResourceId_UsesPerRecordQuery()
        {
            var source = Substitute.For<ICloudResourceAuditPersistentSource>();
            source.GetCloudResourceAuditByCloudResourceId(7, 50, 1, Operators).Returns(PerRecord);
            var controller = new CloudResourceAuditController(source);

            var result = Assert.IsInstanceOfType<ObjectResult>(controller.Put(Operators, cloudResourceId: 7));
            Assert.AreEqual(StatusCodes.Status200OK, result.StatusCode);
            Assert.AreSame(PerRecord, result.Value);
        }

        [TestMethod]
        public void CloudResourceAudit_WithoutCloudResourceId_UsesCrossRecordQuery()
        {
            var source = Substitute.For<ICloudResourceAuditPersistentSource>();
            source.GetCloudResourceAudit(50, 1, Operators).Returns(CrossRecord);
            var controller = new CloudResourceAuditController(source);

            var result = Assert.IsInstanceOfType<ObjectResult>(controller.Put(Operators));
            Assert.AreEqual(StatusCodes.Status200OK, result.StatusCode);
            Assert.AreSame(CrossRecord, result.Value);
        }

        [TestMethod]
        public void ApiRegistrationAudit_WithApiRegistrationId_UsesPerRecordQuery()
        {
            var source = Substitute.For<IApiRegistrationAuditPersistentSource>();
            source.GetApiRegistrationAuditByApiRegistrationId(9, 50, 1, Operators).Returns(PerRecord);
            var controller = new ApiRegistrationAuditController(source);

            var result = Assert.IsInstanceOfType<ObjectResult>(controller.Put(Operators, apiRegistrationId: 9));
            Assert.AreEqual(StatusCodes.Status200OK, result.StatusCode);
            Assert.AreSame(PerRecord, result.Value);
        }

        [TestMethod]
        public void ApiRegistrationAudit_WithoutApiRegistrationId_UsesCrossRecordQuery()
        {
            var source = Substitute.For<IApiRegistrationAuditPersistentSource>();
            source.GetApiRegistrationAudit(50, 1, Operators).Returns(CrossRecord);
            var controller = new ApiRegistrationAuditController(source);

            var result = Assert.IsInstanceOfType<ObjectResult>(controller.Put(Operators));
            Assert.AreEqual(StatusCodes.Status200OK, result.StatusCode);
            Assert.AreSame(CrossRecord, result.Value);
        }
    }
}
