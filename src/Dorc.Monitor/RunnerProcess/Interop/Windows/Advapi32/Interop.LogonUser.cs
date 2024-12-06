using System.Runtime.InteropServices;

namespace Dorc.Monitor.RunnerProcess.Interop.Windows.Advapi32;

internal static partial class Interop
{
    internal static partial class Advapi32
    {
        [DllImport(Windows.Interop.Libraries.Advapi32, SetLastError = true)]
        internal static extern bool LogonUser(
            string lpszUsername,
            string lpszDomain,
            string lpszPassword,
            int dwLogonType,
            int dwLogonProvider,
            out IntPtr phToken
        );
    }
}