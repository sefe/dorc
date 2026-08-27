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
            var filter = new WorkerUnavailableExceptionFilter(NullLogger<WorkerUnavailableExceptionFilter>.Instance);
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
            var filter = new WorkerUnavailableExceptionFilter(NullLogger<WorkerUnavailableExceptionFilter>.Instance);
            var context = NewExceptionContext(new InvalidOperationException("something else"));

            filter.OnException(context);

            Assert.IsFalse(context.ExceptionHandled);
            Assert.IsNull(context.Result);
        }

        [TestMethod]
        public async Task HttpWindowsWorkerClient_ConnectionFailure_ThrowsWorkerUnavailable()
        {
            // Worker configured (Enabled=true) but the process is not listening. Previously
            // EnsureSuccessStatusCode/HttpRequestException escaped as a 500, so the documented
            // 503 only ever fired for the config-disabled case.
            var handler = new ThrowingHandler(new HttpRequestException("Connection refused"));
            var http = new HttpClient(handler) { BaseAddress = new Uri("http://127.0.0.1:5005/") };
            var client = new HttpWindowsWorkerClient(http);

            var ex = await Assert.ThrowsExactlyAsync<WorkerUnavailableException>(
                () => client.GetServerOperatingSystemAsync("SERVER01"));
            Assert.AreEqual("remote-server/operating-system", ex.Endpoint);
        }

        [TestMethod]
        public async Task HttpWindowsWorkerClient_Timeout_ThrowsWorkerUnavailable()
        {
            var handler = new ThrowingHandler(new TaskCanceledException("timed out"));
            var http = new HttpClient(handler) { BaseAddress = new Uri("http://127.0.0.1:5005/") };
            var client = new HttpWindowsWorkerClient(http);

            await Assert.ThrowsExactlyAsync<WorkerUnavailableException>(
                () => client.GetServerOperatingSystemAsync("SERVER01"));
        }

        [TestMethod]
        public async Task HttpWindowsWorkerClient_WorkerBadRequest_SurfacesAsRejection()
        {
            // The worker answers 400 for real user-facing failures; collapsing that into a
            // 500 loses the actionable message the endpoint documents.
            var handler = new CannedHandler(System.Net.HttpStatusCode.BadRequest,
                "{\"error\":\"Unable to open the target machine\"}");
            var http = new HttpClient(handler) { BaseAddress = new Uri("http://127.0.0.1:5005/") };
            var client = new HttpWindowsWorkerClient(http);

            var ex = await Assert.ThrowsExactlyAsync<WorkerRequestRejectedException>(
                () => client.GetServerOperatingSystemAsync("SERVER01"));

            // The worker's body is already {"error":"..."} — the message must be the inner
            // text, not the whole JSON, or the filter re-wraps it into
            // {"error":"{\"error\":\"...\"}"} and the client shows escaped JSON to the user.
            Assert.AreEqual("Unable to open the target machine", ex.Message);
        }

        [TestMethod]
        public void WorkerUnavailableExceptionFilter_RejectionBecomes400()
        {
            var filter = new WorkerUnavailableExceptionFilter(NullLogger<WorkerUnavailableExceptionFilter>.Instance);
            var context = NewExceptionContext(new WorkerRequestRejectedException("Unable to open the target machine"));

            filter.OnException(context);

            Assert.IsTrue(context.ExceptionHandled);
            var result = context.Result as BadRequestObjectResult;
            Assert.IsNotNull(result);
            Assert.AreEqual(StatusCodes.Status400BadRequest, result!.StatusCode);
        }

        private sealed class ThrowingHandler : HttpMessageHandler
        {
            private readonly Exception _ex;
            public ThrowingHandler(Exception ex) => _ex = ex;
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
                => Task.FromException<HttpResponseMessage>(_ex);
        }

        private sealed class CannedHandler : HttpMessageHandler
        {
            private readonly System.Net.HttpStatusCode _status;
            private readonly string _body;
            private readonly List<HttpResponseMessage> _issuedResponses = new();

            public CannedHandler(System.Net.HttpStatusCode status, string body) { _status = status; _body = body; }

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                var response = new HttpResponseMessage(_status)
                {
                    Content = new StringContent(_body, System.Text.Encoding.UTF8, "application/json")
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
