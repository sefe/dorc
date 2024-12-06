using System.Runtime.InteropServices;
using static Dorc.Monitor.RunnerProcess.ProcessSecurityContextBuilder;

namespace Dorc.Monitor.RunnerProcess.Interop.Windows.Advapi32;

internal static partial class Interop
{
    internal static partial class Advapi32
    {
        /// <summary>
        /// For more details see <see cref="https://learn.microsoft.com/en-us/windows/win32/api/securitybaseapi/nf-securitybaseapi-setsecuritydescriptordacl"/>
        /// </summary>
        /// <param name="sd"></param>
        /// <param name="daclPresent"></param>
        /// <param name="dacl"></param>
        /// <param name="daclDefaulted"></param>
        /// <returns></returns>
        [DllImport(Windows.Interop.Libraries.Advapi32, SetLastError = true)]
        internal static extern bool SetSecurityDescriptorDacl(
            ref SECURITY_DESCRIPTOR sd, 
            bool daclPresent, 
            IntPtr dacl,
            bool daclDefaulted);
    }
}