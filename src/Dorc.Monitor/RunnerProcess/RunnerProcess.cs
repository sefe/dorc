using Microsoft.Win32.SafeHandles;
using System.Runtime.InteropServices;

namespace Dorc.Monitor.RunnerProcess
{
    public class RunnerProcess
    {
        public const uint ProcessTerminatedExitCode = 1067;

        #region Dispose pattern implementation
        private bool disposedValue;
        #endregion

        private readonly IntPtr processHandle;
        private readonly IntPtr primaryThreadHandle;
        private uint processId;
        private RunnerProcessJob? job;
        private int terminationRequested;

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
            Interop.Windows.Kernel32.Interop.Kernel32.PROCESS_INFORMATION processInfo,
            RunnerProcessJob? job = null)
            : this(processInfo.Process, processInfo.Thread, processInfo.ProcessId, job)
        {
        }

        internal RunnerProcess(IntPtr processHandle, IntPtr primaryThreadHandle, uint processId,
            RunnerProcessJob? job = null)
        {
            this.processHandle = processHandle;
            this.primaryThreadHandle = primaryThreadHandle;

            this.processId = processId;
            this.job = job;

            this.completeEvent = new ProcessWaitHandle(processHandle);
        }

        public uint WaitForExit()
        {
            this.completeEvent.WaitOne();

            uint exitCode;
            if (Interop.Windows.Kernel32.Interop.Kernel32.GetExitCodeProcess(this.processHandle, out exitCode))
            {
                // Managed whole-tree termination uses an OS-specific exit code.
                // Keep the cancellation contract used by ScriptDispatcher, even
                // when the waiter wakes before the Kill call has returned.
                return Volatile.Read(ref terminationRequested) != 0 ? ProcessTerminatedExitCode : exitCode;
            }

            Interop.Windows.Kernel32.Interop.Kernel32.CloseHandle(this.processHandle);

            var lastError = Marshal.GetLastWin32Error();
            throw new ExternalException("GetExitCodeProcess Error " + lastError, lastError);
        }

        public uint WaitForExit(CancellationToken cancellationToken, Action? requestCancellation = null)
        {
            if (WaitHandle.WaitAny(new[] { completeEvent, cancellationToken.WaitHandle }) == 1
                || cancellationToken.IsCancellationRequested)
            {
                try
                {
                    requestCancellation?.Invoke();
                    if (requestCancellation is not null)
                        completeEvent.WaitOne(TimeSpan.FromSeconds(5));
                }
                finally
                {
                    // Also terminate surviving descendants if the runner has
                    // exited, rather than relying on its parent-child relation.
                    Kill();
                }
                if (!completeEvent.WaitOne(TimeSpan.FromSeconds(5)))
                    throw new InvalidOperationException("Runner did not stop within the cancellation deadline.");
                throw new OperationCanceledException(cancellationToken);
            }
            cancellationToken.ThrowIfCancellationRequested();
            return WaitForExit();
        }

        public void Kill()
        {
            if (job is not null)
            {
                Volatile.Write(ref terminationRequested, 1);
                job.Terminate();
                return;
            }
            if (completeEvent.WaitOne(0)) return;
            try
            {
                using var process = System.Diagnostics.Process.GetProcessById((int)processId);
                if (process.HasExited) return;
                Volatile.Write(ref terminationRequested, 1);
                process.Kill(entireProcessTree: true);
            }
            catch (ArgumentException) when (completeEvent.WaitOne(0))
            {
                // Another cancellation caller terminated the process between
                // the exit check and opening its managed process wrapper.
            }
        }

        public void Close()
        {
            job?.Dispose();
            job = null;
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
