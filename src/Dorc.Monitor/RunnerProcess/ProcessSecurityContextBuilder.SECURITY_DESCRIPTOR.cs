using System.Runtime.InteropServices;

namespace Dorc.Monitor.RunnerProcess
{
    internal partial class ProcessSecurityContextBuilder
    {
        /// <summary>
        /// For more details see <see cref="https://learn.microsoft.com/en-gb/windows/win32/api/winnt/ns-winnt-security_descriptor?redirectedfrom=MSDN"/>
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        internal struct SECURITY_DESCRIPTOR
        {
            public readonly byte revision;
            public readonly byte size;
            public readonly short control;
            public readonly IntPtr owner;
            public readonly IntPtr group;
            public readonly IntPtr sacl;
            public readonly IntPtr dacl;
        }
    }
}
