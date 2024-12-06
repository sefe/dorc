using System.Runtime.InteropServices;

namespace Dorc.Monitor.RunnerProcess.Interop.Windows.Advapi32;

internal static partial class Interop
{
    internal static partial class Advapi32
    {
        /// <summary>
        /// For more details see <see cref="https://learn.microsoft.com/en-us/windows/win32/api/securitybaseapi/nf-securitybaseapi-initializesecuritydescriptor"/>
        /// </summary>
        /// <param name="pSecurityDescriptor"></param>
        /// <param name="dwRevision"></param>
        /// <returns></returns>
        [DllImport(Windows.Interop.Libraries.Advapi32, SetLastError = true)]
        internal static extern bool InitializeSecurityDescriptor(IntPtr pSecurityDescriptor, uint dwRevision);
    }
}