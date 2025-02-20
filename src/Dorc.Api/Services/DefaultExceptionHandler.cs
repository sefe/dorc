using log4net;
using Microsoft.AspNetCore.Diagnostics;

namespace Dorc.Api.Services
{
    public sealed class DefaultExceptionHandler : IExceptionHandler
    {
        private readonly ILog _log;

        public DefaultExceptionHandler(ILog log)
        {
            _log = LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod()?.DeclaringType ?? typeof(DefaultExceptionHandler));
        }

        public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
        {
            var result = new
            {
                Type = exception.GetType().Name,
                Message = "An unexpected error occurred",
                ExceptionMessage = exception.Message,
            };

            _log.Error(Newtonsoft.Json.JsonConvert.SerializeObject(result), exception);

            await httpContext.Response.WriteAsJsonAsync(result, cancellationToken: cancellationToken);
            return true;
        }
    }
}
