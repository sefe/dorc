using Dorc.Api.Exceptions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Dorc.Api.Services
{
    // Catches WorkerUnavailableException from any controller and renders the
    // documented 503 body. Registered globally in Program.cs.
    public class WorkerUnavailableExceptionFilter : IExceptionFilter
    {
        public void OnException(ExceptionContext context)
        {
            if (context.Exception is not WorkerUnavailableException ex) return;

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
