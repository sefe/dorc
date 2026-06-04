using Dorc.Api.Exceptions;
using Dorc.Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;

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
            var filter = new WorkerUnavailableExceptionFilter();
            var context = NewExceptionContext(new WorkerUnavailableException("reset-password"));

            filter.OnException(context);

            Assert.IsTrue(context.ExceptionHandled);
            var result = context.Result as ObjectResult;
            Assert.IsNotNull(result);
            Assert.AreEqual(StatusCodes.Status503ServiceUnavailable, result!.StatusCode);

            // Body is anonymous: {error="windows_worker_unavailable", endpoint="reset-password"}
            var body = result.Value!.GetType();
            Assert.AreEqual("windows_worker_unavailable", body.GetProperty("error")!.GetValue(result.Value));
            Assert.AreEqual("reset-password", body.GetProperty("endpoint")!.GetValue(result.Value));
        }

        [TestMethod]
        public void WorkerUnavailableExceptionFilter_IgnoresOtherExceptions()
        {
            var filter = new WorkerUnavailableExceptionFilter();
            var context = NewExceptionContext(new InvalidOperationException("something else"));

            filter.OnException(context);

            Assert.IsFalse(context.ExceptionHandled);
            Assert.IsNull(context.Result);
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
            public HttpRequestMessage? LastRequest { get; private set; }

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                LastRequest = request;
                return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK));
            }
        }
    }
}
