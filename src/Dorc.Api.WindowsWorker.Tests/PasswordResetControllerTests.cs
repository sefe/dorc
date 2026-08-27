using System.Text.Json;
using Dorc.Api.WindowsWorker.Controllers;
using Dorc.Api.WindowsWorker.Services;
using Dorc.ApiModel;
using Dorc.Core.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
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

        private static PasswordResetController NewController(
            FakeReset reset,
            ILogger<PasswordResetController>? logger = null)
        {
            var emptyConfig = new ConfigurationBuilder().Build();
            var impersonator = new ServiceAccountImpersonator(
                emptyConfig, NullLogger<ServiceAccountImpersonator>.Instance);
            return new PasswordResetController(
                reset,
                impersonator,
                logger ?? NullLogger<PasswordResetController>.Instance);
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
            var fixture = LoadFixture();
            var reset = new FakeReset { Result = fixture.Response };
            var logger = new CapturingLogger<PasswordResetController>();
            var controller = NewController(reset, logger);

            var result = controller.Reset(fixture.Request);

            var ok = Assert.IsInstanceOfType<OkObjectResult>(result.Result);
            var body = Assert.IsInstanceOfType<ApiBoolResult>(ok.Value);
            Assert.AreEqual(fixture.Response.Result, body.Result);
            Assert.AreEqual(fixture.Request.ServerName, reset.SeenServer);
            Assert.AreEqual(fixture.Request.Username, reset.SeenUser);
            Assert.IsTrue(logger.Messages.Any(message =>
                message.Contains(fixture.Request.CallerIdentity, StringComparison.Ordinal)
                && message.Contains(fixture.Request.ServerName, StringComparison.Ordinal)
                && message.Contains(fixture.Request.Username, StringComparison.Ordinal)));
        }

        private static PasswordResetFixture LoadFixture()
        {
            var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "password-reset.json");
            return JsonSerializer.Deserialize<PasswordResetFixture>(
                       File.ReadAllText(path),
                       new JsonSerializerOptions(JsonSerializerDefaults.Web))
                   ?? throw new InvalidOperationException("Password-reset parity fixture is invalid.");
        }

        private sealed class PasswordResetFixture
        {
            public required WorkerPasswordResetRequestApiModel Request { get; init; }
            public required ApiBoolResult Response { get; init; }
        }

        private sealed class CapturingLogger<T> : ILogger<T>
        {
            public List<string> Messages { get; } = [];

            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter)
                => Messages.Add(formatter(state, exception));
        }
    }
}
