using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using PROCESS_INFORMATION = Dorc.Monitor.RunnerProcess.Interop.Windows.Kernel32.Interop.Kernel32.PROCESS_INFORMATION;

namespace Dorc.Monitor.Tests
{
    /// <summary>
    /// Tests for the CancellationToken.Register + Kill pattern used in ScriptDispatcher
    /// to terminate the Runner process when a deployment is cancelled.
    /// These tests use real processes and Win32 handles; they only run on Windows.
    /// </summary>
    [TestClass]
    [SupportedOSPlatform("windows")]
    public class RunnerProcessCancellationTests
    {
        // OpenProcess is used instead of Process.Handle to obtain an independent handle.
        // RunnerProcess.Dispose() calls CloseHandle on its handle, which would invalidate
        // Process.Handle if they shared the same underlying OS handle.
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, uint dwProcessId);

        private const uint PROCESS_ALL_ACCESS = 0x001F0FFF;

        /// <summary>
        /// Mutable mirror of PROCESS_INFORMATION for test setup.
        /// The real struct has readonly fields, so we reinterpret via Unsafe.As.
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        private struct MutableProcessInfo
        {
            public IntPtr Process;
            public IntPtr Thread;
            public uint ProcessId;
            public uint ThreadId;
        }

        private static PROCESS_INFORMATION CreateProcessInfo(IntPtr processHandle, uint processId)
        {
            var mutable = new MutableProcessInfo
            {
                Process = processHandle,
                Thread = IntPtr.Zero,
                ProcessId = processId,
                ThreadId = 0
            };
            return Unsafe.As<MutableProcessInfo, PROCESS_INFORMATION>(ref mutable);
        }

        private static (RunnerProcess.RunnerProcess runner, Process system) StartLongRunningProcess()
        {
            var systemProcess = Process.Start(new ProcessStartInfo
            {
                FileName = "ping.exe",
                Arguments = "-n 300 127.0.0.1",
                CreateNoWindow = true,
                UseShellExecute = false
            })!;

            var handle = OpenProcess(PROCESS_ALL_ACCESS, false, (uint)systemProcess.Id);
            if (handle == IntPtr.Zero)
                throw new InvalidOperationException(
                    $"OpenProcess failed for PID {systemProcess.Id}, error: {Marshal.GetLastWin32Error()}");

            var info = CreateProcessInfo(handle, (uint)systemProcess.Id);
            var runner = new RunnerProcess.RunnerProcess(info);

            return (runner, systemProcess);
        }

        [TestMethod]
        [Timeout(10_000, CooperativeCancellation = true)]
        public void WaitForExit_WhenCancelledDuringWait_KillsProcessAndReturnsTerminatedExitCode()
        {
            // Arrange: start a long-running process (pings for ~5 minutes)
            var (runner, systemProcess) = StartLongRunningProcess();
            try
            {
                using var cts = new CancellationTokenSource();

                // Same pattern as ScriptDispatcher: register kill-on-cancel before WaitForExit
                using var killOnCancel = cts.Token.Register(() =>
                {
                    try { runner.Kill(); }
                    catch (Exception) { /* Best-effort: mirrors ScriptDispatcher callback */ }
                });

                // Cancel after a short delay to simulate user cancellation
                cts.CancelAfter(TimeSpan.FromMilliseconds(500));

                // Act: WaitForExit should unblock when Kill is called
                var resultCode = runner.WaitForExit();

                // Assert: process was terminated with the expected exit code
                Assert.AreEqual(RunnerProcess.RunnerProcess.ProcessTerminatedExitCode, resultCode);
            }
            finally
            {
                try { systemProcess.Kill(); } catch (Exception) { /* Best-effort cleanup: process may have already exited */ }
                systemProcess.Dispose();
            }
        }

        [TestMethod]
        [Timeout(15_000, CooperativeCancellation = true)]
        public void WaitForExit_WhenProcessExitsNormally_RegistrationDoesNotInterfere()
        {
            // Arrange: start a process that exits on its own after ~2 pings
            var systemProcess = Process.Start(new ProcessStartInfo
            {
                FileName = "ping.exe",
                Arguments = "-n 3 127.0.0.1",
                CreateNoWindow = true,
                UseShellExecute = false
            })!;

            try
            {
                var handle = OpenProcess(PROCESS_ALL_ACCESS, false, (uint)systemProcess.Id);
                Assert.AreNotEqual(IntPtr.Zero, handle, "Failed to open process handle");

                var info = CreateProcessInfo(handle, (uint)systemProcess.Id);
                var runner = new RunnerProcess.RunnerProcess(info);

                using var cts = new CancellationTokenSource();
                bool callbackFired = false;

                // Same pattern — token is never cancelled, so callback should never fire
                using var killOnCancel = cts.Token.Register(() =>
                {
                    callbackFired = true;
                    try { runner.Kill(); }
                    catch (Exception) { /* Best-effort: process may have already exited */ }
                });

                // Act: process exits normally
                var resultCode = runner.WaitForExit();

                // Assert
                Assert.IsFalse(callbackFired, "Cancellation callback should not have fired");
                Assert.AreNotEqual(RunnerProcess.RunnerProcess.ProcessTerminatedExitCode, resultCode,
                    "Process should not have been terminated by cancellation");
            }
            finally
            {
                try { systemProcess.Kill(); } catch (Exception) { /* Best-effort cleanup: process may have already exited */ }
                systemProcess.Dispose();
            }
        }

        [TestMethod]
        [Timeout(10_000, CooperativeCancellation = true)]
        public void CancellationCallback_WhenProcessAlreadyExited_DoesNotThrow()
        {
            // Arrange: start a long process, then kill it via System.Diagnostics.Process
            // so it's fully exited before the cancellation callback fires
            var (runner, systemProcess) = StartLongRunningProcess();
            try
            {
                // Kill via System.Diagnostics.Process (uses its own handle, not RunnerProcess's)
                systemProcess.Kill();
                systemProcess.WaitForExit();

                using var cts = new CancellationTokenSource();
                bool callbackCompleted = false;

                // Same pattern — callback fires on an already-exited process
                using var killOnCancel = cts.Token.Register(() =>
                {
                    try
                    {
                        runner.Kill();
                    }
                    catch (Exception)
                    {
                        // ScriptDispatcher catches this and logs it
                    }
                    callbackCompleted = true;
                });

                // Act: cancel the token, triggering the callback
                cts.Cancel();

                // Assert: callback completed without hanging or crashing
                Assert.IsTrue(callbackCompleted, "Callback should complete without hanging");
            }
            finally
            {
                try { systemProcess.Kill(); } catch (Exception) { /* Best-effort cleanup: process may have already exited */ }
                systemProcess.Dispose();
            }
        }
    }
}
