using Dorc.Api.Controllers;
using Dorc.Api.Interfaces;
using Dorc.ApiModel;
using Dorc.Core.Interfaces;
using Dorc.PersistentData;
using Dorc.PersistentData.Sources.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using System.Security.Claims;
using System.Security.Principal;

namespace Dorc.Api.Tests
{
    // S-006: the primary's controller is an authz'ing pass-through — the impersonated SQL
    // reset itself lives on the Windows worker.
    [TestClass]
    public class ResetAppPasswordControllerTests
    {
        private IDatabasesPersistentSource _databases = null!;
        private ISecurityPrivilegesChecker _privileges = null!;
        private IClaimsPrincipalReader _claimsReader = null!;
        private IWindowsWorkerClient _workerClient = null!;
        private ResetAppPasswordController _controller = null!;

        [TestInitialize]
        public void Setup()
        {
            _databases = Substitute.For<IDatabasesPersistentSource>();
            _privileges = Substitute.For<ISecurityPrivilegesChecker>();
            _claimsReader = Substitute.For<IClaimsPrincipalReader>();
            _workerClient = Substitute.For<IWindowsWorkerClient>();

            _controller = new ResetAppPasswordController(
                _databases,
                NullLogger<ResetAppPasswordController>.Instance,
                _privileges,
                _claimsReader,
                _workerClient)
            {
                ControllerContext = new ControllerContext
                {
                    HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity()) }
                }
            };
        }

        [TestMethod]
        public async Task UnauthorizedCaller_Gets403_WithoutTouchingTheWorker()
        {
            _privileges.CanModifyEnvironment(Arg.Any<ClaimsPrincipal>(), "EnvA").Returns(false);

            var result = await _controller.Put("SQL", "EnvA", "app_user");

            var status = Assert.IsInstanceOfType<ObjectResult>(result);
            Assert.AreEqual(StatusCodes.Status403Forbidden, status.StatusCode);
            await _workerClient.DidNotReceiveWithAnyArgs().ResetAppPasswordAsync(default!, default);
        }

        [TestMethod]
        public async Task NoApplicationDatabase_AnswersFalse_WithoutTouchingTheWorker()
        {
            _privileges.CanModifyEnvironment(Arg.Any<ClaimsPrincipal>(), "EnvA").Returns(true);
            _databases.GetApplicationDatabaseForEnvFilter("app_user", "SQL", "EnvA").Returns((DatabaseApiModel?)null);

            var result = await _controller.Put("SQL", "EnvA", "app_user");

            var ok = Assert.IsInstanceOfType<OkObjectResult>(result);
            var body = Assert.IsInstanceOfType<ApiBoolResult>(ok.Value);
            Assert.IsFalse(body.Result);
            await _workerClient.DidNotReceiveWithAnyArgs().ResetAppPasswordAsync(default!, default);
        }

        [TestMethod]
        public async Task AuthorizedCaller_ForwardsServerUserAndCallerIdentityToTheWorker()
        {
            _privileges.CanModifyEnvironment(Arg.Any<ClaimsPrincipal>(), "EnvA").Returns(true);
            _databases.GetApplicationDatabaseForEnvFilter("app_user", "SQL", "EnvA")
                .Returns(new DatabaseApiModel { ServerName = "SQLSRV01" });
            _claimsReader.GetUserFullDomainName(Arg.Any<IPrincipal>()).Returns(@"CORP\alice");

            WorkerPasswordResetRequestApiModel? seen = null;
            _workerClient.ResetAppPasswordAsync(Arg.Any<WorkerPasswordResetRequestApiModel>(), Arg.Any<CancellationToken>())
                .Returns(ci =>
                {
                    seen = ci.Arg<WorkerPasswordResetRequestApiModel>();
                    return Task.FromResult(new ApiBoolResult { Result = true });
                });

            var result = await _controller.Put("SQL", "EnvA", "app_user");

            var ok = Assert.IsInstanceOfType<OkObjectResult>(result);
            var body = Assert.IsInstanceOfType<ApiBoolResult>(ok.Value);
            Assert.IsTrue(body.Result);

            Assert.IsNotNull(seen);
            Assert.AreEqual("SQLSRV01", seen!.ServerName);
            Assert.AreEqual("app_user", seen.Username);
            Assert.AreEqual(@"CORP\alice", seen.CallerIdentity);
        }
    }
}
