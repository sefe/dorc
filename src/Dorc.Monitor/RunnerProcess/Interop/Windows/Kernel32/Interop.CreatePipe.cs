using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Dorc.Monitor.RunnerProcess.Interop.Windows.Kernel32;

internal partial class Interop
{
    internal partial class Kernel32
    {
        [DllImport(Windows.Interop.Libraries.Kernel32, CharSet = CharSet.Auto, SetLastError = true)]
        //[ResourceExposure(ResourceScope.Process)]
        public static extern bool CreatePipe(
            out SafeFileHandle hReadPipe,
            out SafeFileHandle hWritePipe,
            SECURITY_ATTRIBUTES lpPipeAttributes,
            int nSize);
    }
}