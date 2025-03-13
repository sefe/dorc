using log4net;
using System.Diagnostics;

namespace Dorc.PersistentData.Utils
{
    public class TimeProfiler : IDisposable
    {
        private readonly Stopwatch _stopwatch;
        private readonly ILog _log;
        private readonly string _methodName;
        private readonly int _threshold;

        public TimeProfiler(ILog log, string methodName, int threshold = 3000)
        {
            _log = log;
            _methodName = methodName;
            _threshold = threshold;
            _stopwatch = Stopwatch.StartNew();
        }

        public void LogTime(string methodName)
        {
            _stopwatch.Stop();
            if (_stopwatch.ElapsedMilliseconds > _threshold)
            {
                _log.Warn($"[Profiler] {_methodName}.({methodName}) took {_stopwatch.ElapsedMilliseconds} ms");
            }
            _stopwatch.Restart();
        }

        public void Dispose()
        {
            if (!_stopwatch.IsRunning) return;

            _stopwatch.Stop();
            if (_stopwatch.ElapsedMilliseconds > _threshold)
            {
                _log.Warn($"[Profiler] last part of {_methodName} took {_stopwatch.ElapsedMilliseconds} ms");
            }
        }
    }
}
