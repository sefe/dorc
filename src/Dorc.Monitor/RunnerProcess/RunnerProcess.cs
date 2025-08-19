using Microsoft.Win32.SafeHandles;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Dorc.Monitor.RunnerProcess
{
    public class RunnerProcess
    {
        public const uint ProcessTerminatedExitCode = 1067;

        #region Dispose pattern implementation
        private bool disposedValue;
        #endregion

        private IntPtr processHandle;
        private IntPtr primaryThreadHandle;
        private uint processId;
        private uint primaryThreadId;

        private ProcessWaitHandle completeEvent;

        public uint Id
        {
            get
            {
                return processId;
            }
        }

        // Enforcement to specify constructor parameters
        private RunnerProcess() { }

        internal RunnerProcess(
            Interop.Windows.Kernel32.Interop.Kernel32.PROCESS_INFORMATION processInfo)
        {
            this.processHandle = processInfo.Process;
            this.primaryThreadHandle = processInfo.Thread;

            this.processId = processInfo.ProcessId;
            this.primaryThreadId = processInfo.ThreadId;

            this.completeEvent = new ProcessWaitHandle(processInfo.Process);
        }

        public uint WaitForExit()
        {
            this.completeEvent.WaitOne();

            uint exitCode;
            if (Interop.Windows.Kernel32.Interop.Kernel32.GetExitCodeProcess(this.processHandle, out exitCode))
            {
                return exitCode;
            }

            Interop.Windows.Kernel32.Interop.Kernel32.CloseHandle(this.processHandle);

            var lastError = Marshal.GetLastWin32Error();
            throw new ExternalException("GetExitCodeProcess Error " + lastError, lastError);
        }

        public void Kill()
        {
            SafeProcessHandle? handle = null;
            try
            {
                handle = new SafeProcessHandle(this.processHandle, true);
                if (!Interop.Windows.Kernel32.Interop.Kernel32.TerminateProcess(handle, ProcessTerminatedExitCode))
                {
                    if (Marshal.GetLastWin32Error() != 0)
                    {
                        throw new Win32Exception(Marshal.GetLastWin32Error());
                    }
                    throw new Win32Exception("The RunnerProcess was not terminated successfully.");
                }
            }
            finally
            {
                handle?.Close();
            }
        }

        public void Close()
        {
            // We need to lock to ensure we don't run concurrently with CompletionCallback.
            // Without this lock we could reset _raisedOnExited which causes CompletionCallback to
            // raise the Exited event a second time for the same process.
            lock (this)
            {
                // This sets _waitHandle to null which causes CompletionCallback to not emit events.
                this.completeEvent.Close();
            }

            Interop.Windows.Kernel32.Interop.Kernel32.CloseHandle(this.processHandle);
            Interop.Windows.Kernel32.Interop.Kernel32.CloseHandle(this.primaryThreadHandle);

            this.processId = 0;
            this.primaryThreadId = 0;
        }

        #region Dispose pattern implementation
        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                this.Close();

                disposedValue = true;
            }
        }

        ~RunnerProcess()
        {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            Dispose(disposing: false);
        }

        public void Dispose()
        {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
        #endregion

        internal class ProcessWaitHandle : WaitHandle
        {
            public ProcessWaitHandle(IntPtr processHandle)
            {
                SafeWaitHandle = new SafeWaitHandle(processHandle, false);
            }
        }
    }
}
