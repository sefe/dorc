using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Dorc.Monitor.RunnerProcess.Interop.Windows.Kernel32;

internal partial class Interop
{
    internal partial class Kernel32
    {
        [DllImport(Windows.Interop.Libraries.Kernel32, CharSet = CharSet.Auto, SetLastError = true)]   
        public static extern SafeFileHandle CreateNamedPipe(string name, int openMode, int pipeMode, int maxInstances,
            int outBufSize, int inBufSize, int timeout, IntPtr lpPipeAttributes);
    }
}