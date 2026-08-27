using Dorc.Api.WindowsWorker.Controllers;
using Dorc.Api.WindowsWorker.Services;
using Dorc.ApiModel;
using Dorc.Core.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Dorc.Api.WindowsWorker.Tests
{
    // S-006: the worker's password-reset endpoint. With no service-account credential
    // configured, ServiceAccountImpersonator runs the reset as the process identity, so
    // the request-validation and pass-through behaviour is testable cross-platform.
    [TestClass]
    public class PasswordResetControllerTests
    {
        private sealed class FakeReset : ISqlUserPasswordReset
        {
            public string? SeenServer;
            public string? SeenUser;
            public ApiBoolResult Result { get; init; } = new() { Result = true };

            public ApiBoolResult ResetSqlUserPassword(string targetDbServer, string username)
            {
                SeenServer = targetDbServer;
                SeenUser = username;
                return Result;
            }
        }

        private static PasswordResetController NewController(FakeReset reset)
        {
            var emptyConfig = new ConfigurationBuilder().Build();
            var impersonator = new ServiceAccountImpersonator(
                emptyConfig, NullLogger<ServiceAccountImpersonator>.Instance);
            return new PasswordResetController(
                reset, impersonator, NullLogger<PasswordResetController>.Instance);
        }

        [TestMethod]
        public void MissingServerOrUser_Answers400()
        {
            var controller = NewController(new FakeReset());

            var result = controller.Reset(new WorkerPasswordResetRequestApiModel
            {
                ServerName = "",
                Username = "app_user"
            });

            Assert.IsInstanceOfType<BadRequestObjectResult>(result.Result);
        }

        [TestMethod]
        public void ValidRequest_RunsTheResetAgainstTheSuppliedServerAndUser()
        {
            var reset = new FakeReset();
            var controller = NewController(reset);

            var result = controller.Reset(new WorkerPasswordResetRequestApiModel
            {
                ServerName = "SQLSRV01",
                Username = "app_user",
                CallerIdentity = @"CORP\alice"
            });

            var ok = Assert.IsInstanceOfType<OkObjectResult>(result.Result);
            var body = Assert.IsInstanceOfType<ApiBoolResult>(ok.Value);
            Assert.IsTrue(body.Result);
            Assert.AreEqual("SQLSRV01", reset.SeenServer);
            Assert.AreEqual("app_user", reset.SeenUser);
        }
    }
}
