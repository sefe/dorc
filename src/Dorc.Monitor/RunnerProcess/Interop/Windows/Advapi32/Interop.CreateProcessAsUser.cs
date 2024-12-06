using System.Runtime.InteropServices;

namespace Dorc.Monitor.RunnerProcess.Interop.Windows.Advapi32;

internal static partial class Interop
{
    internal static partial class Advapi32
    {
        /// <summary>
        /// For more details see <see cref="https://learn.microsoft.com/en-us/windows/win32/api/processthreadsapi/nf-processthreadsapi-createprocessasuserw"/>
        /// </summary>
        /// <param name="Token"></param>
        /// <param name="ApplicationName"></param>
        /// <param name="CommandLine"></param>
        /// <param name="ProcessAttributes"></param>
        /// <param name="ThreadAttributes"></param>
        /// <param name="InheritHandles"></param>
        /// <param name="CreationFlags"></param>
        /// <param name="Environment"></param>
        /// <param name="CurrentDirectory"></param>
        /// <param name="StartupInfo"></param>
        /// <param name="ProcessInformation"></param>
        /// <returns></returns>
        [DllImport(Windows.Interop.Libraries.Advapi32, CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern bool CreateProcessAsUser(
            IntPtr Token,
            [MarshalAs(UnmanagedType.LPTStr)] string ApplicationName,
            [MarshalAs(UnmanagedType.LPTStr)] string CommandLine,
            ref Kernel32.Interop.Kernel32.SECURITY_ATTRIBUTES ProcessAttributes,
            ref Kernel32.Interop.Kernel32.SECURITY_ATTRIBUTES ThreadAttributes,
            bool InheritHandles,
            uint CreationFlags,
            IntPtr Environment,
            [MarshalAs(UnmanagedType.LPTStr)] string CurrentDirectory,
            ref Kernel32.Interop.Kernel32.STARTUPINFO StartupInfo,
            out Kernel32.Interop.Kernel32.PROCESS_INFORMATION ProcessInformation);
    }
}