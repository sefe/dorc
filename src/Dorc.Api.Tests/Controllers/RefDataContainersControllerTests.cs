using System.Security.Claims;
using Dorc.Api.Controllers;
using Dorc.ApiModel;
using Dorc.Core.Interfaces;
using Dorc.PersistentData;
using Dorc.PersistentData.Model;
using Dorc.PersistentData.Sources;
using Dorc.PersistentData.Sources.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace Dorc.Api.Tests.Controllers
{
    /// <summary>
    /// Pattern-setting authorization/outcome tests for the environment-component
    /// controllers (IS S-004, HLPS §5.3): every write endpoint is exercised for the
    /// privileged and unprivileged caller, the unattached-item fallback, conflict
    /// outcomes, the composite-PK race backstop, and audit emission.
    /// </summary>
    [TestClass]
    public class RefDataContainersControllerTests
    {
        private IContainersPersistentSource _source = null!;
        private IContainerAuditPersistentSource _audit = null!;
        private IEnvironmentsPersistentSource _environments = null!;
        private IRolePrivilegesChecker _roles = null!;
        private ISecurityPrivilegesChecker _security = null!;
        private IClaimsPrincipalReader _claims = null!;
        private RefDataContainersController _controller = null!;

        [TestInitialize]
        public void Setup()
        {
            _source = Substitute.For<IContainersPersistentSource>();
            _audit = Substitute.For<IContainerAuditPersistentSource>();
            _environments = Substitute.For<IEnvironmentsPersistentSource>();
            _roles = Substitute.For<IRolePrivilegesChecker>();
            _security = Substitute.For<ISecurityPrivilegesChecker>();
            _claims = Substitute.For<IClaimsPrincipalReader>();
            _claims.GetUserFullDomainName(Arg.Any<ClaimsPrincipal>()).Returns(@"DOM\alice");

            _controller = new RefDataContainersController(
                _source, _audit, _environments, _roles, _security, _claims)
            {
                ControllerContext = new ControllerContext
                {
                    HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity()) }
                }
            };
        }

        private static int? StatusOf(IActionResult result) => (result as IStatusCodeActionResult)?.StatusCode
            ?? (result as ObjectResult)?.StatusCode;

        private void MakePowerUser() => _roles.IsPowerUser(Arg.Any<ClaimsPrincipal>()).Returns(true);

        private void GrantEnvWrite(string envName) =>
            _security.CanModifyEnvironment(Arg.Any<ClaimsPrincipal>(), envName).Returns(true);

        // ---- Create ----

        [TestMethod]
        public void Post_WithoutPrivilege_Returns403_AndDoesNotCreate()
        {
            var result = _controller.Post(new ContainerApiModel { Name = "web" });

            Assert.AreEqual(403, StatusOf(result));
            _source.DidNotReceive().Add(Arg.Any<ContainerApiModel>());
            _audit.DidNotReceiveWithAnyArgs().InsertContainerAudit(default!, default, default, default, default);
        }

        [TestMethod]
        public void Post_DuplicateName_Returns409()
        {
            MakePowerUser();
            _source.GetByName("web").Returns(new ContainerApiModel { Id = 1, Name = "web" });

            var result = _controller.Post(new ContainerApiModel { Name = "web" });

            Assert.AreEqual(409, StatusOf(result));
            _source.DidNotReceive().Add(Arg.Any<ContainerApiModel>());
        }

        [TestMethod]
        public void Post_AsPowerUser_CreatesAndAudits()
        {
            MakePowerUser();
            _source.GetByName("web").Returns((ContainerApiModel?)null);
            _source.Add(Arg.Any<ContainerApiModel>()).Returns(new ContainerApiModel { Id = 9, Name = "web" });

            var result = _controller.Post(new ContainerApiModel { Name = "web" });

            Assert.AreEqual(200, StatusOf(result));
            _audit.Received(1).InsertContainerAudit(@"DOM\alice", ActionType.Create, 9,
                Arg.Is<string?>(s => s == null), Arg.Is<string>(s => s.Contains("web")));
        }

        // ---- Update / Delete gating ----

        [TestMethod]
        public void Put_UnattachedItem_WithoutRole_Returns403()
        {
            _source.GetEnvironmentNamesForId(9).Returns(new List<string>());

            var result = _controller.Put(9, new ContainerApiModel { Name = "web" });

            Assert.AreEqual(403, StatusOf(result));
            _source.DidNotReceive().Update(Arg.Any<int>(), Arg.Any<ContainerApiModel>());
        }

        [TestMethod]
        public void Put_AttachedItem_WithoutEnvWrite_Returns403()
        {
            _source.GetEnvironmentNamesForId(9).Returns(new List<string> { "DV 01" });

            var result = _controller.Put(9, new ContainerApiModel { Name = "web" });

            Assert.AreEqual(403, StatusOf(result));
        }

        [TestMethod]
        public void Put_AttachedItem_RequiresWriteOnEveryMappedEnvironment()
        {
            _source.GetEnvironmentNamesForId(9).Returns(new List<string> { "DV 01", "DV 02" });
            GrantEnvWrite("DV 01"); // DV 02 not granted

            var result = _controller.Put(9, new ContainerApiModel { Name = "web" });

            Assert.AreEqual(403, StatusOf(result));
        }

        [TestMethod]
        public void Put_WithEnvWrite_UpdatesAndAuditsBeforeAfter()
        {
            _source.GetEnvironmentNamesForId(9).Returns(new List<string> { "DV 01" });
            GrantEnvWrite("DV 01");
            _source.GetById(9).Returns(new ContainerApiModel { Id = 9, Name = "old" });
            _source.GetByName("new").Returns((ContainerApiModel?)null);
            _source.Update(9, Arg.Any<ContainerApiModel>())
                .Returns(new ContainerApiModel { Id = 9, Name = "new" });

            var result = _controller.Put(9, new ContainerApiModel { Name = "new" });

            Assert.AreEqual(200, StatusOf(result));
            _audit.Received(1).InsertContainerAudit(@"DOM\alice", ActionType.Update, 9,
                Arg.Is<string>(s => s.Contains("old")), Arg.Is<string>(s => s.Contains("new")));
        }

        [TestMethod]
        public void Put_UnknownId_Returns404()
        {
            MakePowerUser();
            _source.GetEnvironmentNamesForId(9).Returns(new List<string>());
            _source.GetById(9).Returns((ContainerApiModel?)null);

            Assert.AreEqual(404, StatusOf(_controller.Put(9, new ContainerApiModel { Name = "x" })));
        }

        [TestMethod]
        public void Put_NameConflictWithOtherItem_Returns409()
        {
            MakePowerUser();
            _source.GetEnvironmentNamesForId(9).Returns(new List<string>());
            _source.GetById(9).Returns(new ContainerApiModel { Id = 9, Name = "old" });
            _source.GetByName("taken").Returns(new ContainerApiModel { Id = 1, Name = "taken" });

            Assert.AreEqual(409, StatusOf(_controller.Put(9, new ContainerApiModel { Name = "taken" })));
        }

        [TestMethod]
        public void Delete_WithoutPrivilege_Returns403()
        {
            _source.GetEnvironmentNamesForId(9).Returns(new List<string>());

            Assert.AreEqual(403, StatusOf(_controller.Delete(9)));
            _source.DidNotReceive().Delete(Arg.Any<int>());
        }

        [TestMethod]
        public void Delete_WithEnvWrite_DeletesAndAudits()
        {
            _source.GetEnvironmentNamesForId(9).Returns(new List<string> { "DV 01" });
            GrantEnvWrite("DV 01");
            _source.GetById(9).Returns(new ContainerApiModel { Id = 9, Name = "web" });
            _source.Delete(9).Returns(true);

            var result = _controller.Delete(9);

            Assert.AreEqual(200, StatusOf(result));
            _audit.Received(1).InsertContainerAudit(@"DOM\alice", ActionType.Delete, 9,
                Arg.Is<string>(s => s.Contains("web")), Arg.Is<string?>(s => s == null));
        }

        [TestMethod]
        public void Delete_UnknownId_Returns404()
        {
            MakePowerUser();
            _source.GetEnvironmentNamesForId(9).Returns(new List<string>());
            _source.GetById(9).Returns((ContainerApiModel?)null);

            Assert.AreEqual(404, StatusOf(_controller.Delete(9)));
        }

        [TestMethod]
        public void Delete_RaceVanishedItem_Returns404NotFalse()
        {
            MakePowerUser();
            _source.GetEnvironmentNamesForId(9).Returns(new List<string>());
            _source.GetById(9).Returns(new ContainerApiModel { Id = 9, Name = "web" });
            _source.Delete(9).Returns(false);

            Assert.AreEqual(404, StatusOf(_controller.Delete(9)));
            _audit.DidNotReceiveWithAnyArgs().InsertContainerAudit(default!, default, default, default, default);
        }

        // ---- Attach / Detach ----

        private void SetupEnvironment(int envId = 1, string name = "DV 01")
        {
            _environments.GetEnvironment(envId, Arg.Any<ClaimsPrincipal>())
                .Returns(new EnvironmentApiModel { EnvironmentId = envId, EnvironmentName = name });
        }

        [TestMethod]
        public void Attach_WithoutEnvWrite_Returns403()
        {
            SetupEnvironment();

            Assert.AreEqual(403, StatusOf(_controller.Attach(9, 1)));
            _source.DidNotReceive().AttachToEnvironment(Arg.Any<int>(), Arg.Any<int>());
        }

        [TestMethod]
        public void Attach_UnknownEnvironment_Returns404()
        {
            _environments.GetEnvironment(1, Arg.Any<ClaimsPrincipal>()).Returns((EnvironmentApiModel?)null);

            Assert.AreEqual(404, StatusOf(_controller.Attach(9, 1)));
        }

        [TestMethod]
        public void Attach_Duplicate_Returns409()
        {
            SetupEnvironment();
            GrantEnvWrite("DV 01");
            _source.AttachToEnvironment(9, 1).Returns(EnvironmentAttachmentOutcome.AlreadyAttached);

            Assert.AreEqual(409, StatusOf(_controller.Attach(9, 1)));
        }

        [TestMethod]
        public void Attach_CompositePkRace_Returns409NotServerError()
        {
            SetupEnvironment();
            GrantEnvWrite("DV 01");
            _source.AttachToEnvironment(9, 1).Returns(_ => throw new DbUpdateException("PK violation"));

            Assert.AreEqual(409, StatusOf(_controller.Attach(9, 1)));
        }

        [TestMethod]
        public void Attach_UnknownItem_Returns404()
        {
            SetupEnvironment();
            GrantEnvWrite("DV 01");
            _source.AttachToEnvironment(9, 1).Returns(EnvironmentAttachmentOutcome.ItemNotFound);

            Assert.AreEqual(404, StatusOf(_controller.Attach(9, 1)));
        }

        [TestMethod]
        public void Attach_HappyPath_AttachesAndAudits()
        {
            SetupEnvironment();
            GrantEnvWrite("DV 01");
            _source.AttachToEnvironment(9, 1).Returns(EnvironmentAttachmentOutcome.Attached);

            var result = _controller.Attach(9, 1);

            Assert.AreEqual(200, StatusOf(result));
            _audit.Received(1).InsertContainerAudit(@"DOM\alice", ActionType.Attach, 9,
                Arg.Is<string?>(s => s == null), Arg.Is<string>(s => s.Contains("DV 01")));
        }

        [TestMethod]
        public void Detach_WithoutEnvWrite_Returns403()
        {
            SetupEnvironment();

            Assert.AreEqual(403, StatusOf(_controller.Detach(9, 1)));
            _source.DidNotReceive().DetachFromEnvironment(Arg.Any<int>(), Arg.Any<int>());
        }

        [TestMethod]
        public void Detach_NotAttached_Returns409()
        {
            SetupEnvironment();
            GrantEnvWrite("DV 01");
            _source.DetachFromEnvironment(9, 1).Returns(EnvironmentAttachmentOutcome.NotAttached);

            Assert.AreEqual(409, StatusOf(_controller.Detach(9, 1)));
        }

        [TestMethod]
        public void Detach_HappyPath_DetachesAndAudits()
        {
            SetupEnvironment();
            GrantEnvWrite("DV 01");
            _source.DetachFromEnvironment(9, 1).Returns(EnvironmentAttachmentOutcome.Detached);

            var result = _controller.Detach(9, 1);

            Assert.AreEqual(200, StatusOf(result));
            _audit.Received(1).InsertContainerAudit(@"DOM\alice", ActionType.Detach, 9,
                Arg.Is<string>(s => s.Contains("DV 01")), Arg.Is<string?>(s => s == null));
        }

        // ---- Reads ----

        [TestMethod]
        public void GetById_Unknown_Returns404()
        {
            _source.GetById(9).Returns((ContainerApiModel?)null);

            Assert.AreEqual(404, StatusOf(_controller.GetById(9)));
        }

        [TestMethod]
        public void GetByEnvId_DelegatesToSource()
        {
            _source.GetForEnvironmentId(1).Returns(new[] { new ContainerApiModel { Id = 1, Name = "web" } });

            var actionResult = _controller.GetByEnvId(1);

            Assert.IsInstanceOfType(actionResult, typeof(OkObjectResult));
            var items = (List<ContainerApiModel>)((OkObjectResult)actionResult).Value!;
            Assert.AreEqual(1, items.Count);
        }
    }
}
