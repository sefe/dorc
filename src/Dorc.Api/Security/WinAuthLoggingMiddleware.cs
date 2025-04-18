﻿using log4net;
using Microsoft.AspNetCore.Authentication.Negotiate;

namespace Dorc.Api.Security
{
    public class WinAuthLoggingMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILog _logger;

        private static readonly HashSet<string> LoggedUsers = new HashSet<string>();

        public WinAuthLoggingMiddleware(RequestDelegate next, ILog logger)
        {
            _next = next;
            _logger = logger;
        }

        public async Task Invoke(HttpContext context)
        {
            string? authenticationScheme = context.GetAuthenticationScheme();
            var user = context.User?.Identity;
            if (authenticationScheme == NegotiateDefaults.AuthenticationScheme && (user?.IsAuthenticated == true))
            {
                string userName = user?.Name ?? "Unknown User";
                
                // Log only if the user hasn't been logged yet after restart
                if (LoggedUsers.Add(userName))
                {
                    _logger.Warn($"User '{userName}' is using Windows Authentication (NTLM/Kerberos)");
                }
            }

            await _next(context);
        }
    }
}
