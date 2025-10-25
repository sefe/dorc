using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Diagnostics;

namespace Dorc.Api.Services
{
    public sealed class DefaultExceptionHandler : IExceptionHandler
    {
        private readonly ILogger<DefaultExceptionHandler> _log;

        public DefaultExceptionHandler(ILogger<DefaultExceptionHandler> log)
        {
            _log = log;
        }

        public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
        {
            var result = new
            {
                Type = exception.GetType().Name,
                Message = "An unexpected error occurred",
                ExceptionMessage = exception.Message,
            };

            var logMessage = result.ExceptionMessage;
            var user = httpContext?.User;
            if (user != null)
            {
                logMessage += Environment.NewLine + $"User: {user.Identity?.Name}";
            }
            var request = httpContext?.Request;
            if (request != null)
            {
                logMessage += Environment.NewLine + $"{GetRequestInfo(request)}";
            }

            _log.LogError(exception, logMessage);

            await httpContext.Response.WriteAsJsonAsync(result, cancellationToken: cancellationToken);
            return true;
        }

        private string GetRequestInfo(HttpRequest request)
        {
            return request.Method + " " + request.Scheme + ":/" + request.Path + request.QueryString;
        }
    }
}
