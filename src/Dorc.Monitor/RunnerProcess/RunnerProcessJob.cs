using Microsoft.Win32.SafeHandles;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Dorc.Monitor.RunnerProcess;

// Assigned while the runner is suspended so no child can escape containment.
// Closing the Monitor's handle also kills descendants after Monitor failure.
internal sealed class RunnerProcessJob : SafeHandleZeroOrMinusOneIsInvalid
{
    private RunnerProcessJob() : base(true) { }

    public static RunnerProcessJob Create()
    {
        var job = CreateJobObject(IntPtr.Zero, null);
        if (job.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error());
        var information = new ExtendedLimitInformation();
        information.BasicLimitInformation.LimitFlags = 0x2000; // KILL_ON_JOB_CLOSE
        if (!SetInformationJobObject(job, 9, ref information, (uint)Marshal.SizeOf<ExtendedLimitInformation>()))
        {
            var error = Marshal.GetLastWin32Error();
            job.Dispose();
            throw new Win32Exception(error);
        }
        return job;
    }

    public void Assign(IntPtr process)
    {
        if (!AssignProcessToJobObject(this, process))
            throw new Win32Exception(Marshal.GetLastWin32Error());
    }

    public void Terminate()
    {
        if (!TerminateJobObject(this, RunnerProcess.ProcessTerminatedExitCode))
            throw new Win32Exception(Marshal.GetLastWin32Error());
    }

    public static void Resume(IntPtr thread)
    {
        if (ResumeThread(thread) == uint.MaxValue)
            throw new Win32Exception(Marshal.GetLastWin32Error());
    }

    protected override bool ReleaseHandle() => CloseHandle(handle);

    [StructLayout(LayoutKind.Sequential)]
    private struct BasicLimitInformation
    {
        public long PerProcessUserTimeLimit, PerJobUserTimeLimit;
        public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize, MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint PriorityClass, SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IoCounters
    {
        public ulong ReadOperationCount, WriteOperationCount, OtherOperationCount;
        public ulong ReadTransferCount, WriteTransferCount, OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ExtendedLimitInformation
    {
        public BasicLimitInformation BasicLimitInformation;
        public IoCounters IoInfo;
        public UIntPtr ProcessMemoryLimit, JobMemoryLimit, PeakProcessMemoryUsed, PeakJobMemoryUsed;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern RunnerProcessJob CreateJobObject(IntPtr attributes, string? name);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetInformationJobObject(RunnerProcessJob job, int infoClass,
        ref ExtendedLimitInformation information, uint length);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AssignProcessToJobObject(RunnerProcessJob job, IntPtr process);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool TerminateJobObject(RunnerProcessJob job, uint exitCode);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint ResumeThread(IntPtr thread);
    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr handle);
}
