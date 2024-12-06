using System.Runtime.InteropServices;

namespace Dorc.Monitor.RunnerProcess.Interop.Windows.Kernel32;

internal static partial class Interop
{
    internal static partial class Kernel32
    {
        /// <summary>
        /// For more details see <see cref="https://learn.microsoft.com/en-us/windows/win32/api/processthreadsapi/ns-processthreadsapi-process_information"/>
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        internal struct PROCESS_INFORMATION
        {
            public readonly IntPtr Process;
            public readonly IntPtr Thread;
            public readonly uint ProcessId;
            public readonly uint ThreadId;
        }
    }
}