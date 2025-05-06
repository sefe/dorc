using System.Collections.Concurrent;
using Dorc.Runner.Logger;

namespace Dorc.PowerShell
{
    public class OutputProcessor : IDisposable
    {
        private readonly int deploymentResultId;
        private IRunnerLogger logger;
        private Timer logTimer;
        private ConcurrentQueue<string> logMessages;
        private bool disposedValue;

        public OutputProcessor(IRunnerLogger logger, int deploymentResultId, int flushEverySec = 30)
        {
            this.logger = logger;
            this.deploymentResultId = deploymentResultId;
            this.logMessages = new ConcurrentQueue<string>();

            logTimer = new Timer(OnLogTimerElapsed, null, flushEverySec, flushEverySec * 1000); // 30 seconds
        }

        private void OnLogTimerElapsed(object? state)
        {
            FlushLogMessages();
        }

        public void AddLogMessage(string msg)
        {
            logMessages.Enqueue(msg);
        }

        public void FlushLogMessages()
        {
            if (logMessages.IsEmpty) return;

            var logList = new List<string>();
            while (logMessages.TryDequeue(out var log))
            {
                logList.Add(log);
            }

            if (logList.Count > 0)
            {
                var combinedLog = string.Join(Environment.NewLine, logList);
                this.logger.UpdateLog(deploymentResultId, combinedLog);
            }
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    logTimer.Dispose();
                }

                disposedValue = true;
            }
        }

        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }
}
