using System.ComponentModel;
using System.Runtime.InteropServices;
using log4net;

namespace Dorc.Monitor.RunnerProcess
{
    internal partial class ProcessSecurityContextBuilder
    {
        private readonly ILog logger;

        private const uint SECURITY_DESCRIPTOR_REVISION = 1;

        public string UserName { get; set; } = string.Empty;
        public string Domain { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;

        private ProcessSecurityContextBuilder() { }

        internal ProcessSecurityContextBuilder(ILog logger)
        {
            this.logger = logger;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        /// <exception cref="Win32Exception"></exception>
        /// <remarks>
        /// Client code is responsible for disposing of the built instance of <see cref="ProcessSecurityContext"/>.
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

            Interop.Windows.Kernel32.Interop.Kernel32.SECURITY_ATTRIBUTES processAttributes = new Interop.Windows.Kernel32.Interop.Kernel32.SECURITY_ATTRIBUTES();

            ProcessSecurityContext securityContext = new ProcessSecurityContext(
                this.GetLogOnToken(
                    this.UserName,
                    this.Domain,
                    this.Password,
                    out processAttributes),
                processAttributes,
                this.GetThreadAttributes()
                );

            return securityContext;
        }

        private IntPtr GetLogOnToken(
            string userName,
            string domain,
            string password,
            out Interop.Windows.Kernel32.Interop.Kernel32.SECURITY_ATTRIBUTES processAttributes)
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
                this.logger.Error($"LogonUser failed with win32 error: {winError}");
                throw new Exception($"Cannot process request under account {userName}");
            }
            this.logger.Info($"Logon as {userName} succeeded");

            #region security attributes

            processAttributes = new Interop.Windows.Kernel32.Interop.Kernel32.SECURITY_ATTRIBUTES();

            SECURITY_DESCRIPTOR securityDescriptor = new SECURITY_DESCRIPTOR();
            var securityDescriptorPointer = Marshal.AllocCoTaskMem(Marshal.SizeOf(securityDescriptor));
            Marshal.StructureToPtr(securityDescriptor, securityDescriptorPointer, false);
            Interop.Windows.Advapi32.Interop.Advapi32.InitializeSecurityDescriptor(securityDescriptorPointer, SECURITY_DESCRIPTOR_REVISION);
            securityDescriptor = (SECURITY_DESCRIPTOR)Marshal.PtrToStructure(securityDescriptorPointer, typeof(SECURITY_DESCRIPTOR))!;

            result = Interop.Windows.Advapi32.Interop.Advapi32.SetSecurityDescriptorDacl(ref securityDescriptor, true, IntPtr.Zero, false);
            if (!result)
            {
                var winError = Marshal.GetLastWin32Error();
                this.logger.Error($"SetSecurityDescriptorDacl failed with win32 error: {winError}");
            }

            result = Interop.Windows.Advapi32.Interop.Advapi32.DuplicateTokenEx(
                token,
                0, // https://learn.microsoft.com/en-us/windows/win32/secauthz/access-rights-for-access-token-objects
                ref processAttributes,
                SECURITY_IMPERSONATION_LEVEL.SecurityImpersonation,
                TOKEN_TYPE.TokenPrimary,
                out var primaryToken);

            if (!result)
            {
                var winError = Marshal.GetLastWin32Error();
                this.logger.Error($"DuplicateTokenEx failed with win32 error: {winError}");
            }

            processAttributes.lpSecurityDescriptor = securityDescriptorPointer;
            processAttributes.nLength = (uint)Marshal.SizeOf(securityDescriptor);
            processAttributes.bInheritHandle = true;
            #endregion

            return primaryToken;
        }

        private Interop.Windows.Kernel32.Interop.Kernel32.SECURITY_ATTRIBUTES GetThreadAttributes()
        {
            return new Interop.Windows.Kernel32.Interop.Kernel32.SECURITY_ATTRIBUTES
            {
                nLength = 0,
                lpSecurityDescriptor = IntPtr.Zero,
                bInheritHandle = false
            };
        }
    }
}
