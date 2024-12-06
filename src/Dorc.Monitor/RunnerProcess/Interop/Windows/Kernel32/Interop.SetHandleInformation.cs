using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Dorc.Monitor.RunnerProcess.Interop.Windows.Kernel32;

internal partial class Interop
{
    internal partial class Kernel32
    {
        [DllImport(Windows.Interop.Libraries.Kernel32, SetLastError = true)]   
        internal static extern bool SetHandleInformation(SafeFileHandle hObject, HandleFlags dwMask, HandleFlags dwFlags);
    }
}