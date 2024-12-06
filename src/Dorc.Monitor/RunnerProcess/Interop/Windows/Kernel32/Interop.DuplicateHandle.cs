using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Dorc.Monitor.RunnerProcess.Interop.Windows.Kernel32;

internal partial class Interop
{
    internal partial class Kernel32
    {
        [DllImport(Windows.Interop.Libraries.Kernel32, CharSet = CharSet.Auto, SetLastError = true, BestFitMapping = false)]
        //[ResourceExposure(ResourceScope.Machine)]
        public static extern bool DuplicateHandle(
            //HandleRef hSourceProcessHandle,
            IntPtr hSourceProcessHandle,
            SafeHandle hSourceHandle,
            //HandleRef hTargetProcessHandle,
            IntPtr hTargetProcessHandle,
            out SafeFileHandle lpTargetHandle,
            int dwDesiredAccess,
            bool bInheritHandle,
            int dwOptions);
    }
}