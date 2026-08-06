using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.Data.SqlClient;

namespace Dorc.Api.Services
{
    public sealed class DefaultExceptionHandler : IExceptionHandler
    {
        /// <summary>
        /// SqlException.Number reported when a command exceeds its timeout.
        /// </summary>
        private const int SqlTimeoutErrorNumber = -2;

        private readonly ILogger _log;

        public DefaultExceptionHandler(ILogger<DefaultExceptionHandler> log)
        {
            _log = log;
        }

        public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
        {
            var result = new
            {
                Type = exception.GetType().Name,
                Message = DescribeForUser(exception),
                ExceptionMessage = exception.Message,
            };

            httpContext.Response.StatusCode = StatusCodeFor(exception);

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

        /// <summary>
        /// A short description of what went wrong, in terms a user of the site can act on. The web
        /// client prefers this over <c>ExceptionMessage</c>, so returning a fixed string here made
        /// every failure in the UI read "An unexpected error occurred" whatever the cause.
        /// </summary>
        private static string DescribeForUser(Exception exception)
        {
            if (IsDatabaseTimeout(exception))
                return "The database did not respond within the time allowed and the operation was abandoned. " +
                       "Please try again - if it keeps happening the database is likely busy, or the data involved has grown too large for the operation to finish in time.";

            if (FindSqlException(exception) != null)
                return "The database refused the operation.";

            if (exception is UnauthorizedAccessException)
                return "You do not have sufficient privileges for this operation.";

            return "An unexpected error occurred";
        }

        /// <summary>
        /// Distinguishes the causes the caller can do something about from a genuine server fault,
        /// rather than reporting every failure as a 500.
        /// </summary>
        private static int StatusCodeFor(Exception exception)
        {
            if (IsDatabaseTimeout(exception))
                return StatusCodes.Status504GatewayTimeout;

            if (exception is UnauthorizedAccessException)
                return StatusCodes.Status403Forbidden;

            return StatusCodes.Status500InternalServerError;
        }

        private static bool IsDatabaseTimeout(Exception exception)
        {
            return FindSqlException(exception)?.Number == SqlTimeoutErrorNumber;
        }

        /// <summary>
        /// Walks the exception chain, because Entity Framework reports a failure raised while
        /// saving as a DbUpdateException wrapping the SqlException that actually describes it.
        /// </summary>
        private static SqlException? FindSqlException(Exception exception)
        {
            for (var current = exception; current != null; current = current.InnerException)
            {
                if (current is SqlException sqlException)
                    return sqlException;
            }

            return null;
        }
    }
}
