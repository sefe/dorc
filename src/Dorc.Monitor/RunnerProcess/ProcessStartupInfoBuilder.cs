using log4net;
using Microsoft.Win32.SafeHandles;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Dorc.Monitor.RunnerProcess
{
    internal partial class ProcessStartupInfoBuilder
    {
        private const int STD_INPUT_HANDLE = -10;
        private static readonly nint INVALID_HANDLE_VALUE = -1;

        private static readonly HandleRef NullHandleRef = new HandleRef(null, nint.Zero);

        private readonly ILog logger;

        private ProcessStartupInfoBuilder() { }

        internal ProcessStartupInfoBuilder(ILog logger)
        {
            this.logger = logger;
        }

        public Interop.Windows.Kernel32.Interop.Kernel32.STARTUPINFO Build()
        {
            var stdinHandle = Interop.Windows.Kernel32.Interop.Kernel32.GetStdHandle(STD_INPUT_HANDLE);
            CreatePipe(out var stdoutReadHandle, out var stdoutWriteHandle, false);
            Interop.Windows.Kernel32.Interop.Kernel32.SetHandleInformation(stdoutReadHandle, Interop.Windows.Kernel32.Interop.Kernel32.HandleFlags.HANDLE_FLAG_INHERIT, Interop.Windows.Kernel32.Interop.Kernel32.HandleFlags.HANDLE_FLAG_INHERIT);

            Interop.Windows.Kernel32.Interop.Kernel32.STARTUPINFO nativeStartupInfo = new Interop.Windows.Kernel32.Interop.Kernel32.STARTUPINFO
            {
                lpDesktop = string.Empty,
                dwFlags = Interop.Windows.Advapi32.Interop.Advapi32.StartupInfoOptions.STARTF_USESTDHANDLES,
                hStdInput = stdinHandle,
                hStdOutput = stdoutWriteHandle,
                hStdError = stdoutWriteHandle
            };

            return nativeStartupInfo;
        }

        private static void CreatePipe(out SafeFileHandle parentHandle, out SafeFileHandle childHandle, bool parentInputs)
        {
            var pipename = @"\\.\pipe\" + Guid.NewGuid();

            parentHandle = Interop.Windows.Kernel32.Interop.Kernel32.CreateNamedPipe(pipename, 0x40000003, 0, 0xff, 0x1000, 0x1000, 0, nint.Zero);
            if (parentHandle.IsInvalid)
                throw new Win32Exception();

            var childAcc = 0x40000000;
            if (parentInputs)
                childAcc = -2147483648;
            childHandle = Interop.Windows.Kernel32.Interop.Kernel32.CreateFile(pipename, childAcc, 3, nint.Zero, 3, 0x40000080, NullHandleRef);
            if (childHandle.IsInvalid)
                throw new Win32Exception();
        }
    }
}
