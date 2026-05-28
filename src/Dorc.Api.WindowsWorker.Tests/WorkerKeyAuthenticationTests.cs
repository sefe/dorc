using System.Net;
using System.Text;
using System.Text.Encodings.Web;
using Dorc.Api.WindowsWorker.Authentication;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Dorc.Api.WindowsWorker.Tests
{
    // Two layers of coverage:
    //   1. Handler-level unit tests (instantiate the handler directly with a
    //      DefaultHttpContext and a stub IConfiguration). These exercise the actual
    //      auth logic — header parsing, constant-time comparison, success/failure
    //      ticket shapes, and the 401 body emitted by HandleChallengeAsync.
    //   2. Host-level smoke tests against /health via WebApplicationFactory.
    //      Confirms the worker binds, the unauthenticated /health endpoint is
    //      reachable, and the auth scheme registration doesn't break startup.
    //
    // Full-pipeline coverage of protected controllers will come in S-004/S-005/S-006
    // when those endpoints are added.
    [TestClass]
    public class WorkerKeyAuthenticationTests
    {
        private const string TestSharedKey = "test-shared-key-1234567890ABCDEF";

        // -------------------- Handler-level unit tests --------------------

        [TestMethod]
        public async Task Handler_NoHeader_NoResult_ThenChallengeWritesDocumentedBody()
        {
            var handler = await NewHandler(configuredKey: TestSharedKey, withHeader: null);

            var auth = await handler.AuthenticateAsync();
            Assert.IsFalse(auth.Succeeded);
            Assert.IsNull(auth.Failure, "missing header should be NoResult, not Fail");

            await handler.ChallengeAsync(new AuthenticationProperties());

            var (status, body) = ReadResponse(handler);
            Assert.AreEqual(StatusCodes.Status401Unauthorized, status);
            Assert.AreEqual("{\"error\":\"worker_key_invalid\"}", body);
        }

        [TestMethod]
        public async Task Handler_WrongKey_FailsThenChallengeWritesDocumentedBody()
        {
            var handler = await NewHandler(configuredKey: TestSharedKey, withHeader: "nope-wrong-key");

            var auth = await handler.AuthenticateAsync();
            Assert.IsFalse(auth.Succeeded);
            Assert.IsNotNull(auth.Failure);
            StringAssert.Contains(auth.Failure!.Message, "worker_key_invalid");

            await handler.ChallengeAsync(new AuthenticationProperties());

            var (status, body) = ReadResponse(handler);
            Assert.AreEqual(StatusCodes.Status401Unauthorized, status);
            Assert.AreEqual("{\"error\":\"worker_key_invalid\"}", body);
        }

        [TestMethod]
        public async Task Handler_DifferentLengthKey_Fails()
        {
            // FixedTimeEquals requires equal length — the constant-time check is
            // bypassed for mismatched lengths but must still reject.
            var handler = await NewHandler(configuredKey: TestSharedKey, withHeader: "short");

            var auth = await handler.AuthenticateAsync();
            Assert.IsFalse(auth.Succeeded);
            Assert.IsNotNull(auth.Failure);
        }

        [TestMethod]
        public async Task Handler_CorrectKey_Succeeds_WithCallerClaim()
        {
            var handler = await NewHandler(configuredKey: TestSharedKey, withHeader: TestSharedKey);

            var auth = await handler.AuthenticateAsync();
            Assert.IsTrue(auth.Succeeded);
            Assert.IsNotNull(auth.Principal);
            Assert.IsTrue(auth.Principal!.HasClaim(
                WorkerKeyAuthenticationHandler.CallerClaimType,
                WorkerKeyAuthenticationHandler.CallerClaimValue));
            Assert.AreEqual(WorkerKeyAuthenticationOptions.SchemeName, auth.Ticket!.AuthenticationScheme);
        }

        [TestMethod]
        public async Task Handler_NoConfiguredSecret_FailsClosed()
        {
            // If the host is misconfigured (empty SharedKey), even matching a header
            // value must fail. Fail-closed is the correct behaviour.
            var handler = await NewHandler(configuredKey: null, withHeader: "anything");

            var auth = await handler.AuthenticateAsync();
            Assert.IsFalse(auth.Succeeded);
            Assert.IsNotNull(auth.Failure);
        }

        // -------------------- Host smoke tests --------------------

        [TestMethod]
        public async Task Health_NoHeader_Returns200()
        {
            using var factory = NewFactory();
            using var client = factory.CreateClient();

            var resp = await client.GetAsync("/health");
            Assert.AreEqual(HttpStatusCode.OK, resp.StatusCode);
        }

        [TestMethod]
        public async Task Health_AnyHeader_Returns200()
        {
            using var factory = NewFactory();
            using var client = factory.CreateClient();
            client.DefaultRequestHeaders.Add(WorkerKeyAuthenticationOptions.HeaderName, "anything");

            var resp = await client.GetAsync("/health");
            Assert.AreEqual(HttpStatusCode.OK, resp.StatusCode);
        }

        // ---------- Helpers ----------

        private static async Task<WorkerKeyAuthenticationHandler> NewHandler(string? configuredKey, string? withHeader)
        {
            var configDict = new Dictionary<string, string?>();
            if (configuredKey != null) configDict["WindowsWorker:SharedKey"] = configuredKey;
            var config = new ConfigurationBuilder().AddInMemoryCollection(configDict).Build();

            var options = Options.Create(new WorkerKeyAuthenticationOptions());
            var optionsMonitor = new TestOptionsMonitor<WorkerKeyAuthenticationOptions>(options.Value);

            var handler = new WorkerKeyAuthenticationHandler(
                optionsMonitor,
                NullLoggerFactory.Instance,
                UrlEncoder.Default,
                config);

            var httpContext = new DefaultHttpContext();
            httpContext.Response.Body = new MemoryStream();
            if (withHeader != null)
            {
                httpContext.Request.Headers[WorkerKeyAuthenticationOptions.HeaderName] = withHeader;
            }

            var scheme = new AuthenticationScheme(
                WorkerKeyAuthenticationOptions.SchemeName,
                WorkerKeyAuthenticationOptions.SchemeName,
                typeof(WorkerKeyAuthenticationHandler));
            await handler.InitializeAsync(scheme, httpContext);
            return handler;
        }

        private static (int status, string body) ReadResponse(WorkerKeyAuthenticationHandler handler)
        {
            // Pull the captured response from the handler's HttpContext via reflection
            // on its `Context` property (protected). Cleaner than re-routing the stream.
            var context = (HttpContext)handler.GetType()
                .GetProperty("Context", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)!
                .GetValue(handler)!;

            context.Response.Body.Position = 0;
            using var reader = new StreamReader(context.Response.Body, Encoding.UTF8);
            return (context.Response.StatusCode, reader.ReadToEnd());
        }

        private static WorkerWebAppFactory NewFactory() => new();

        private sealed class WorkerWebAppFactory : WebApplicationFactory<Program>
        {
            protected override IHost CreateHost(IHostBuilder builder)
            {
                builder.ConfigureHostConfiguration(cfg =>
                {
                    cfg.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["WindowsWorker:SharedKey"] = TestSharedKey,
                    });
                });
                return base.CreateHost(builder);
            }
        }

        private sealed class TestOptionsMonitor<T> : IOptionsMonitor<T> where T : class
        {
            private readonly T _value;
            public TestOptionsMonitor(T value) { _value = value; }
            public T CurrentValue => _value;
            public T Get(string? name) => _value;
            public IDisposable? OnChange(Action<T, string?> listener) => null;
        }
    }
}
