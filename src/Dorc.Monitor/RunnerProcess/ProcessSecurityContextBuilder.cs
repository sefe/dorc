using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Principal;
using Microsoft.Extensions.Logging;

namespace Dorc.Monitor.RunnerProcess
{
    [SupportedOSPlatform("windows")]
    internal partial class ProcessSecurityContextBuilder
    {
        private readonly ILogger logger;

        public string UserName { get; set; } = string.Empty;
        public string Domain { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;

        private ProcessSecurityContextBuilder() { }

        internal ProcessSecurityContextBuilder(ILogger logger)
        {
            this.logger = logger;
        }

        /// <summary>
        /// Logs the deployment account on and assembles what CreateProcessAsUser needs to
        /// start a Runner as it.
        /// </summary>
        /// <remarks>
        /// Client code is responsible for disposing of the built instance of <see cref="ProcessSecurityContext"/>.
        ///
        /// The logon type remains LOGON32_LOGON_NETWORK_CLEARTEXT, reassessed rather than
        /// changed. Deployment scripts reach target servers and script shares over the network
        /// as this account, which requires a logon that retains the credential for outbound
        /// authentication; the alternatives either drop it or demand the interactive logon
        /// right on every Monitor host. The credential's residence in a managed string that
        /// cannot be zeroed is a storage-layer problem, recorded against S-020 rather than
        /// papered over here.
        /// </remarks>
        public ProcessSecurityContext Build()
        {
            #region Verification
            if (string.IsNullOrEmpty(this.UserName))
            {
                throw new Exception("Runner process SecurityContext can't be anonymous. 'UserName' should be specified.");
            }

            if (string.IsNullOrEmpty(this.Domain))
            {
                throw new Exception("Runner process SecurityContext can't be anonymous. 'Domain' should be specified.");
            }

            if (string.IsNullOrEmpty(this.Password))
            {
                throw new Exception("Runner process SecurityContext can't be anonymous. 'Password' should be specified.");
            }
            #endregion

            var logonToken = LogOn(this.UserName, this.Domain, this.Password);

            try
            {
                var primaryToken = DuplicateAsPrimaryToken(logonToken);

                try
                {
                    var descriptor = DescribeProcessAccess(logonToken);

                    try
                    {
                        return new ProcessSecurityContext(
                            primaryToken,
                            logonToken,
                            descriptor,
                            ProcessAttributes(descriptor),
                            ThreadAttributes());
                    }
                    catch
                    {
                        descriptor.Dispose();
                        throw;
                    }
                }
                catch
                {
                    Interop.Windows.Kernel32.Interop.Kernel32.CloseHandle(primaryToken);
                    throw;
                }
            }
            catch
            {
                // The logon token was previously never closed at all: DuplicateTokenEx produced
                // the primary token that got disposed, and this one was simply dropped. One
                // handle, and one live logon session, leaked per script group dispatched.
                Interop.Windows.Kernel32.Interop.Kernel32.CloseHandle(logonToken);
                throw;
            }
        }

        private IntPtr LogOn(string userName, string domain, string password)
        {
            var result = Interop.Windows.Advapi32.Interop.Advapi32.LogonUser(
                userName,
                domain,
                password,
                (int)LOGON_TYPE.LOGON32_LOGON_NETWORK_CLEARTEXT,
                (int)LOGON_PROVIDER.LOGON32_PROVIDER_DEFAULT,
                out var token);

            if (!result)
            {
                var winError = Marshal.GetLastWin32Error();
                this.logger.LogError($"LogonUser failed with win32 error: {winError}");
                throw new Exception($"Cannot process request under account {userName}");
            }

            this.logger.LogInformation("Logon as {UserName} succeeded", SanitizeForLog(userName));

            return token;
        }

        private static string SanitizeForLog(string value)
        {
            return value.Replace("\r", string.Empty).Replace("\n", string.Empty);
        }

        private IntPtr DuplicateAsPrimaryToken(IntPtr logonToken)
        {
            var tokenAttributes = new Interop.Windows.Kernel32.Interop.Kernel32.SECURITY_ATTRIBUTES();

            var result = Interop.Windows.Advapi32.Interop.Advapi32.DuplicateTokenEx(
                logonToken,
                0, // Zero requests the same access rights as the existing token.
                ref tokenAttributes,
                SECURITY_IMPERSONATION_LEVEL.SecurityImpersonation,
                TOKEN_TYPE.TokenPrimary,
                out var primaryToken);

            if (!result)
            {
                // Previously logged and allowed to continue, leaving primaryToken at zero and
                // surfacing as a confusing constructor failure two frames later.
                var winError = Marshal.GetLastWin32Error();
                this.logger.LogError($"DuplicateTokenEx failed with win32 error: {winError}");
                throw new Win32Exception(winError, "Could not derive a primary token for the Runner process.");
            }

            return primaryToken;
        }

        /// <summary>
        /// Builds the process descriptor from the logon token itself rather than by resolving
        /// the configured account name. The token is what the process will actually run as, so
        /// its user is the right principal by construction — and it needs no directory lookup,
        /// which keeps a transient directory-service fault from failing a deployment.
        /// </summary>
        private static RunnerProcessSecurityDescriptor DescribeProcessAccess(IntPtr logonToken)
        {
            using var runner = new WindowsIdentity(logonToken);

            return RunnerProcessSecurityDescriptor.AdmittingOnly(
                runner.User!,
                WindowsIdentity.GetCurrent().User!);
        }

        private static Interop.Windows.Kernel32.Interop.Kernel32.SECURITY_ATTRIBUTES ProcessAttributes(
            RunnerProcessSecurityDescriptor descriptor)
        {
            return new Interop.Windows.Kernel32.Interop.Kernel32.SECURITY_ATTRIBUTES
            {
                // The size of the attributes structure, which is what the field means. It was
                // previously set to the size of the security descriptor.
                nLength = (uint)Marshal.SizeOf<Interop.Windows.Kernel32.Interop.Kernel32.SECURITY_ATTRIBUTES>(),
                lpSecurityDescriptor = descriptor.Handle,
                bInheritHandle = true
            };
        }

        private static Interop.Windows.Kernel32.Interop.Kernel32.SECURITY_ATTRIBUTES ThreadAttributes()
        {
            return new Interop.Windows.Kernel32.Interop.Kernel32.SECURITY_ATTRIBUTES
            {
                nLength = (uint)Marshal.SizeOf<Interop.Windows.Kernel32.Interop.Kernel32.SECURITY_ATTRIBUTES>(),
                lpSecurityDescriptor = IntPtr.Zero,
                bInheritHandle = false
            };
        }
    }
}
