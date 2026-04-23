using Serilog;

namespace Dorc.Api.Security
{
    public class ApiAccessAuditMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly Serilog.ILogger _auditLogger;

        public ApiAccessAuditMiddleware(RequestDelegate next)
        {
            _next = next;

            // Dedicated Serilog logger that writes only to the audit folder
            _auditLogger = new LoggerConfiguration()
                .WriteTo.File(
                    path: @"c:\Log\DOrc\Deploy\Web\Api\audit\Dorc.Api.Audit.log",
                    rollingInterval: RollingInterval.Day,
                    outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} {Message:lj}{NewLine}")
                .CreateLogger();
        }

        public async Task Invoke(HttpContext context)
        {
            var startTime = DateTime.UtcNow;

            await _next(context);

            var userName = context.User?.Identity?.Name ?? "Unknown";
            var sourceIp = context.Connection.RemoteIpAddress?.ToString() ?? "Unknown";
            var method = context.Request.Method;
            var path = context.Request.Path.Value;
            var statusCode = context.Response.StatusCode;
            var duration = (DateTime.UtcNow - startTime).TotalMilliseconds;

            _auditLogger.Information(
                "User: {User} | SourceIP: {SourceIP} | Method: {Method} | " +
                "Resource: {Resource} | Status: {StatusCode} | Duration: {Duration:F1}ms | Server: {Server}",
                userName, sourceIp, method, path, statusCode, duration, Environment.MachineName);
        }
    }
}