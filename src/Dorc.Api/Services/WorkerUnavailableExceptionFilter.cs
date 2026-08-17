using Dorc.Api.Exceptions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Logging;

namespace Dorc.Api.Services
{
    // Catches WorkerUnavailableException from any controller and renders the
    // documented 503 body. Registered globally in Program.cs.
    public class WorkerUnavailableExceptionFilter : IExceptionFilter
    {
        private readonly ILogger<WorkerUnavailableExceptionFilter> _log;

        public WorkerUnavailableExceptionFilter(ILogger<WorkerUnavailableExceptionFilter> log)
        {
            _log = log;
        }

        public void OnException(ExceptionContext context)
        {
            if (context.Exception is WorkerRequestRejectedException rejected)
            {
                context.Result = new BadRequestObjectResult(new { error = rejected.Message });
                context.ExceptionHandled = true;
                return;
            }

            if (context.Exception is not WorkerUnavailableException ex) return;

            // Without this, every worker-dependent call on a host with no worker becomes a
            // silent 503 with no server-side trace of why.
            _log.LogWarning(ex, "Windows worker unavailable for endpoint {Endpoint}", ex.Endpoint);

            context.Result = new ObjectResult(new
            {
                error = "windows_worker_unavailable",
                endpoint = ex.Endpoint
            })
            {
                StatusCode = StatusCodes.Status503ServiceUnavailable
            };
            context.ExceptionHandled = true;
        }
    }
}
