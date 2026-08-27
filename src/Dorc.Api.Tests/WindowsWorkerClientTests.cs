using Dorc.Api.Exceptions;
using Dorc.Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using System.Net;
using System.Text;

namespace Dorc.Api.Tests
{
    [TestClass]
    public class WindowsWorkerClientTests
    {
        [TestMethod]
        public async Task WorkerKeyDelegatingHandler_AddsXWorkerKeyHeader()
        {
            const string secret = "the-shared-secret-from-config";
            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["WindowsWorker:SharedKey"] = secret })
                .Build();

            var capture = new HeaderCaptureHandler();
            var handler = new WorkerKeyDelegatingHandler(config) { InnerHandler = capture };

            using var client = new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") };
            await client.GetAsync("/health");

            Assert.IsNotNull(capture.LastRequest);
            Assert.IsTrue(capture.LastRequest.Headers.TryGetValues(WorkerKeyDelegatingHandler.HeaderName, out var values));
            CollectionAssert.AreEqual(new[] { secret }, values!.ToArray());
        }

        [TestMethod]
        public async Task WorkerKeyDelegatingHandler_OmitsHeaderWhenSecretIsEmpty()
        {
            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["WindowsWorker:SharedKey"] = "" })
                .Build();

            var capture = new HeaderCaptureHandler();
            var handler = new WorkerKeyDelegatingHandler(config) { InnerHandler = capture };
            using var client = new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") };

            await client.GetAsync("/health");

            Assert.IsFalse(capture.LastRequest!.Headers.Contains(WorkerKeyDelegatingHandler.HeaderName));
        }

        [TestMethod]
        public void WorkerUnavailableExceptionFilter_Translates503_WithDocumentedBody()
        {
            var filter = NewFilter();
            var context = NewExceptionContext(new WorkerUnavailableException("reset-password"));

            filter.OnException(context);

            Assert.IsTrue(context.ExceptionHandled);
            var result = Assert.IsInstanceOfType<ObjectResult>(context.Result);
            Assert.AreEqual(StatusCodes.Status503ServiceUnavailable, result.StatusCode);

            // Body is anonymous: {error="windows_worker_unavailable", endpoint="reset-password"}
            var body = result.Value!.GetType();
            Assert.AreEqual("windows_worker_unavailable", body.GetProperty("error")!.GetValue(result.Value));
            Assert.AreEqual("reset-password", body.GetProperty("endpoint")!.GetValue(result.Value));
        }

        [TestMethod]
        public void WorkerUnavailableExceptionFilter_IgnoresOtherExceptions()
        {
            var filter = NewFilter();
            var context = NewExceptionContext(new InvalidOperationException("something else"));

            filter.OnException(context);

            Assert.IsFalse(context.ExceptionHandled);
            Assert.IsNull(context.Result);
        }

        [TestMethod]
        public async Task Transport_ConnectionFailure_ThrowsWorkerUnavailable()
        {
            using var client = NewTransportClient(new ThrowingHandler(new HttpRequestException("Connection refused")));

            var exception = await Assert.ThrowsExactlyAsync<WorkerUnavailableException>(
                () => client.SendTestRequestAsync("remote-server/operating-system"));

            Assert.AreEqual("remote-server/operating-system", exception.Endpoint);
        }

        [TestMethod]
        public async Task Transport_Timeout_ThrowsWorkerUnavailable()
        {
            using var client = NewTransportClient(new ThrowingHandler(new TaskCanceledException("Timed out")));

            await Assert.ThrowsExactlyAsync<WorkerUnavailableException>(
                () => client.SendTestRequestAsync("daemons/probe"));
        }

        [TestMethod]
        public async Task Transport_BadRequest_UnwrapsWorkerError()
        {
            using var client = NewTransportClient(new CannedHandler(
                HttpStatusCode.BadRequest,
                "{\"error\":\"Unable to open the target machine\"}"));

            var exception = await Assert.ThrowsExactlyAsync<WorkerRequestRejectedException>(
                () => client.SendTestRequestAsync("remote-server/operating-system"));

            Assert.AreEqual("Unable to open the target machine", exception.Message);
        }

        [TestMethod]
        public async Task Transport_ServerError_ThrowsWorkerUnavailable()
        {
            using var client = NewTransportClient(new CannedHandler(
                HttpStatusCode.InternalServerError,
                "{\"error\":\"worker fault\"}"));

            await Assert.ThrowsExactlyAsync<WorkerUnavailableException>(
                () => client.SendTestRequestAsync("password-reset"));
        }

        [TestMethod]
        public async Task Transport_EmptySuccessBody_IsContractFailure()
        {
            using var client = NewTransportClient(new CannedHandler(HttpStatusCode.OK, ""));

            await Assert.ThrowsExactlyAsync<InvalidOperationException>(
                () => client.SendTestRequestAsync("daemons/probe"));
        }

        [TestMethod]
        public void WorkerUnavailableExceptionFilter_RejectionBecomes400()
        {
            var filter = NewFilter();
            var context = NewExceptionContext(
                new WorkerRequestRejectedException("Unable to open the target machine"));

            filter.OnException(context);

            Assert.IsTrue(context.ExceptionHandled);
            var result = Assert.IsInstanceOfType<BadRequestObjectResult>(context.Result);
            Assert.AreEqual(StatusCodes.Status400BadRequest, result.StatusCode);
        }

        private static WorkerUnavailableExceptionFilter NewFilter()
            => new(NullLogger<WorkerUnavailableExceptionFilter>.Instance);

        private static TestHttpWindowsWorkerClient NewTransportClient(HttpMessageHandler handler)
            => new(new HttpClient(handler) { BaseAddress = new Uri("http://127.0.0.1:5005/") });

        private static ExceptionContext NewExceptionContext(Exception ex)
        {
            var actionContext = new ActionContext(
                new DefaultHttpContext(),
                new RouteData(),
                new ActionDescriptor(),
                new ModelStateDictionary());
            return new ExceptionContext(actionContext, new List<IFilterMetadata>())
            {
                Exception = ex
            };
        }

        private sealed class TestHttpWindowsWorkerClient : HttpWindowsWorkerClient, IDisposable
        {
            private readonly HttpClient _httpClient;

            public TestHttpWindowsWorkerClient(HttpClient httpClient)
                : base(httpClient)
            {
                _httpClient = httpClient;
            }

            public Task<TestResponse> SendTestRequestAsync(string endpoint)
                => SendAsync<TestResponse>(
                    endpoint,
                    cancellationToken => _httpClient.GetAsync(endpoint, cancellationToken));

            public void Dispose() => _httpClient.Dispose();
        }

        private sealed class TestResponse
        {
            public bool Result { get; set; }
        }

        private sealed class ThrowingHandler : HttpMessageHandler
        {
            private readonly Exception _exception;

            public ThrowingHandler(Exception exception)
            {
                _exception = exception;
            }

            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken)
                => Task.FromException<HttpResponseMessage>(_exception);
        }

        private sealed class CannedHandler : HttpMessageHandler
        {
            private readonly HttpStatusCode _statusCode;
            private readonly string _body;
            private readonly List<HttpResponseMessage> _issuedResponses = new();

            public CannedHandler(HttpStatusCode statusCode, string body)
            {
                _statusCode = statusCode;
                _body = body;
            }

            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken)
            {
                var response = new HttpResponseMessage(_statusCode)
                {
                    Content = new StringContent(_body, Encoding.UTF8, "application/json")
                };
                _issuedResponses.Add(response);
                return Task.FromResult(response);
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    foreach (var response in _issuedResponses)
                    {
                        response.Dispose();
                    }
                    _issuedResponses.Clear();
                }

                base.Dispose(disposing);
            }
        }

        private sealed class HeaderCaptureHandler : DelegatingHandler
        {
            private readonly List<HttpResponseMessage> _issuedResponses = new();

            public HttpRequestMessage? LastRequest { get; private set; }

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                LastRequest = request;
                var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK);
                _issuedResponses.Add(response);
                return Task.FromResult(response);
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    foreach (var response in _issuedResponses)
                    {
                        response.Dispose();
                    }
                    _issuedResponses.Clear();
                }

                base.Dispose(disposing);
            }
        }
    }
}
