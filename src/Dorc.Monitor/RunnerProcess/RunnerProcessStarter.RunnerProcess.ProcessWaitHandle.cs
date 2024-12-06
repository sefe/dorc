using Microsoft.Win32.SafeHandles;

namespace Dorc.Monitor.RunnerProcess
{
    internal partial class RunnerProcessStarter
    {
        public partial class RunnerProcess
        {
            internal class ProcessWaitHandle : WaitHandle
            {
                public ProcessWaitHandle(IntPtr processHandle)
                {
                    SafeWaitHandle = new SafeWaitHandle(processHandle, false);
                }
            }
        }
    }
}
