namespace Dorc.Monitor.RunnerProcess.Interop.Windows.Kernel32;

internal static partial class Interop
{
    internal static partial class Kernel32
    {
        /// <summary>
        /// For more details see <see cref="https://learn.microsoft.com/en-us/previous-versions/windows/desktop/legacy/aa379560(v=vs.85)"/>
        /// </summary>
        //[StructLayout(LayoutKind.Sequential)]
        internal struct SECURITY_ATTRIBUTES
        {
            public uint nLength;
            public IntPtr lpSecurityDescriptor;
            public bool bInheritHandle;
        }
    }
}