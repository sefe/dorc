using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Dorc.Monitor.RunnerProcess.Interop.Windows.Kernel32;

internal static partial class Interop
{
    internal static partial class Kernel32
    {
        /// <summary>
        /// For more details see <see cref="https://learn.microsoft.com/en-us/windows/win32/api/processthreadsapi/ns-processthreadsapi-startupinfoa"/>
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        internal struct STARTUPINFO
        {
            public uint cb;
            [MarshalAs(UnmanagedType.LPStr)]
            public string lpReserved;
            [MarshalAs(UnmanagedType.LPStr)]
            public string lpDesktop;
            [MarshalAs(UnmanagedType.LPStr)]
            public string lpTitle;
            public uint dwX;
            public uint dwY;
            public uint dwXSize;
            public uint dwYSize;
            public uint dwXCountChars;
            public uint dwYCountChars;
            public uint dwFillAttribute;
            public uint dwFlags;
            public ushort wShowWindow;
            public ushort cbReserved2;
            public IntPtr lpReserved2;  
            public SafeFileHandle hStdInput;
            public SafeFileHandle hStdOutput;
            public SafeFileHandle hStdError;
        }
    }
}