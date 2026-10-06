using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security;
using Microsoft.Win32.SafeHandles;

namespace Dorc.Core.Security
{
    /// <summary>The dwLogonType values DOrc uses. Names and values are Windows' own.</summary>
    public enum LogonType
    {
        /// <summary>LOGON32_LOGON_INTERACTIVE: a primary token for impersonation on this host.</summary>
        Interactive = 2,

        /// <summary>
        /// LOGON32_LOGON_NETWORK_CLEARTEXT: a token that can also authenticate to remote
        /// servers, which is what a Runner process started under it needs.
        /// </summary>
        NetworkCleartext = 8
    }

    /// <summary>The dwLogonProvider values DOrc uses.</summary>
    public enum LogonProvider
    {
        Default = 0
    }

    /// <summary>
    /// Logs a deployment account on to Windows and hands back its token.
    ///
    /// There is no managed API for logging on with a password, so this is the one place the
    /// LogonUser interop is declared. Three callers - the password-reset endpoint, the daemon
    /// status probe and the Runner process builder - each used to carry their own declaration,
    /// two of them public, and their own copy of the marshalling around it.
    ///
    /// The password never exists as a managed string here: it is marshalled from its
    /// <see cref="SecureString"/> into unmanaged memory for the duration of the call and that
    /// memory is zeroed before this returns, whether the call succeeded or not.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public static class WindowsLogon
    {
        public static SafeAccessTokenHandle LogOn(
            DeploymentCredential credential,
            string domain,
            LogonType logonType,
            LogonProvider logonProvider)
        {
            return LogOn(credential.UserName, domain, credential.Password, logonType, logonProvider);
        }

        /// <exception cref="Win32Exception">The logon was refused; NativeErrorCode says why.</exception>
        public static SafeAccessTokenHandle LogOn(
            string userName,
            string domain,
            SecureString password,
            LogonType logonType,
            LogonProvider logonProvider)
        {
            var passwordPointer = Marshal.SecureStringToGlobalAllocUnicode(password);
            try
            {
                if (LogonUser(userName, domain, passwordPointer, (int)logonType, (int)logonProvider, out var token))
                {
                    return token;
                }

                // Read before anything else can overwrite it.
                var error = Marshal.GetLastWin32Error();
                token.Dispose();
                throw new Win32Exception(error);
            }
            finally
            {
                Marshal.ZeroFreeGlobalAllocUnicode(passwordPointer);
            }
        }

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool LogonUser(
            string lpszUsername,
            string lpszDomain,
            IntPtr lpszPassword,
            int dwLogonType,
            int dwLogonProvider,
            out SafeAccessTokenHandle phToken);
    }
}
