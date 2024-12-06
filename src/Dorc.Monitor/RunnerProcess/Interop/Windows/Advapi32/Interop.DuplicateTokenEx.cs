using System.Runtime.InteropServices;
using static Dorc.Monitor.RunnerProcess.ProcessSecurityContextBuilder;

namespace Dorc.Monitor.RunnerProcess.Interop.Windows.Advapi32;

internal static partial class Interop
{
    internal static partial class Advapi32
    {
        /// <summary>
        /// For more details see <see cref="https://learn.microsoft.com/en-us/windows/win32/api/securitybaseapi/nf-securitybaseapi-duplicatetokenex"/>
        /// </summary>
        /// <param name="hExistingToken"></param>
        /// <param name="dwDesiredAccess"></param>
        /// <param name="lpTokenAttributes"></param>
        /// <param name="ImpersonationLevel"></param>
        /// <param name="TokenType"></param>
        /// <param name="phNewToken"></param>
        /// <returns></returns>
        [DllImport(Windows.Interop.Libraries.Advapi32, CharSet = CharSet.Auto, SetLastError = true)]      
        internal static extern bool DuplicateTokenEx(
            IntPtr hExistingToken,
            uint dwDesiredAccess,
            ref Kernel32.Interop.Kernel32.SECURITY_ATTRIBUTES lpTokenAttributes,
            SECURITY_IMPERSONATION_LEVEL ImpersonationLevel,
            TOKEN_TYPE TokenType,
            out IntPtr phNewToken);
    }
}