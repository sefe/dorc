using Microsoft.Win32.SafeHandles;
using System.Runtime.InteropServices;
using System.Security.Principal;

namespace Dorc.Api.WindowsWorker.Services
{
    // Runs an action as the worker's OWN configured service account (HLPS D-3: the worker
    // never impersonates the original caller). The credential comes from
    // WindowsWorker:PasswordReset:{Username,Domain,Password}, provisioned at install time
    // by S-008. When no username is configured the action runs as the worker process
    // identity — the deployment where the Windows service itself runs as the delegated
    // account and no second logon is needed.
    public class ServiceAccountImpersonator
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<ServiceAccountImpersonator> _logger;

        public ServiceAccountImpersonator(IConfiguration configuration, ILogger<ServiceAccountImpersonator> logger)
        {
            _configuration = configuration;
            _logger = logger;
        }

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool LogonUser(string? lpszUsername, string? lpszDomain, string? lpszPassword,
            int dwLogonType, int dwLogonProvider, out SafeAccessTokenHandle phToken);

        public T RunAsPasswordResetAccount<T>(Func<T> action)
        {
            var username = _configuration["WindowsWorker:PasswordReset:Username"];
            if (string.IsNullOrWhiteSpace(username))
            {
                return action();
            }

            const int logon32ProviderDefault = 0;
            // This parameter causes LogonUser to create a primary token.
            const int logon32LogonInteractive = 2;

            bool returnValue = LogonUser(
                username,
                _configuration["WindowsWorker:PasswordReset:Domain"],
                _configuration["WindowsWorker:PasswordReset:Password"],
                logon32LogonInteractive, logon32ProviderDefault,
                out var safeAccessTokenHandle);

            if (false == returnValue)
            {
                int ret = Marshal.GetLastWin32Error();
                _logger.LogError("LogonUser for the password-reset service account failed with error code: {ErrorCode}", ret);
                throw new System.ComponentModel.Win32Exception(ret);
            }

            using (safeAccessTokenHandle)
            {
                return WindowsIdentity.RunImpersonated(safeAccessTokenHandle, action);
            }
        }
    }
}
