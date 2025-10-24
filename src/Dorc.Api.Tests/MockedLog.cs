using Microsoft.Extensions.Logging;

namespace Dorc.Api.Tests
{
    public class MockedLog : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            Console.WriteLine(formatter(state, exception));
        }
    }

    // Legacy mock for compatibility - remove all old log4net methods
    public class OldMockedLog
    {
        public void Debug(object format)
        {
            Console.WriteLine(format);
        }

        public void Debug(object format, Exception exception)
        {
            Console.WriteLine(format);
        }

        public void DebugFormat(string format, params object[] args)
        {
            Console.WriteLine(format);
        }

        public void DebugFormat(string format, object arg0)
        {
            Console.WriteLine(format);
        }

        public void DebugFormat(string format, object arg0, object arg1)
        {
            Console.WriteLine(format);
        }

        public void DebugFormat(string format, object arg0, object arg1, object arg2)
        {
            Console.WriteLine(format);
        }

        public void DebugFormat(IFormatProvider provider, string format, params object[] args)
        {
            Console.WriteLine(format);
        }

        public void Info(object format)
        {
            Console.WriteLine(format);
        }

        public void Info(object format, Exception exception)
        {
            Console.WriteLine(format);
        }

        public void InfoFormat(string format, params object[] args)
        {
            Console.WriteLine(format);
        }

        public void InfoFormat(string format, object arg0)
        {
            Console.WriteLine(format);
        }

        public void InfoFormat(string format, object arg0, object arg1)
        {
            Console.WriteLine(format);
        }

        public void InfoFormat(string format, object arg0, object arg1, object arg2)
        {
            Console.WriteLine(format);
        }

        public void InfoFormat(IFormatProvider provider, string format, params object[] args)
        {
            Console.WriteLine(format);
        }

        public void Warn(object format)
        {
            Console.WriteLine(format);
        }

        public void Warn(object format, Exception exception)
        {
            Console.WriteLine(format);
        }

        public void WarnFormat(string format, params object[] args)
        {
            Console.WriteLine(format);
        }

        public void WarnFormat(string format, object arg0)
        {
            Console.WriteLine(format);
        }

        public void WarnFormat(string format, object arg0, object arg1)
        {
            Console.WriteLine(format);
        }

        public void WarnFormat(string format, object arg0, object arg1, object arg2)
        {
            Console.WriteLine(format);
        }

        public void WarnFormat(IFormatProvider provider, string format, params object[] args)
        {
            Console.WriteLine(format);
        }

        public void Error(object format)
        {
            Console.WriteLine(format);
        }

        public void Error(object format, Exception exception)
        {
            Console.WriteLine(format);
        }

        public void ErrorFormat(string format, params object[] args)
        {
            Console.WriteLine(format);
        }

        public void ErrorFormat(string format, object arg0)
        {
            Console.WriteLine(format);
        }

        public void ErrorFormat(string format, object arg0, object arg1)
        {
            Console.WriteLine(format);
        }

        public void ErrorFormat(string format, object arg0, object arg1, object arg2)
        {
            Console.WriteLine(format);
        }

        public void ErrorFormat(IFormatProvider provider, string format, params object[] args)
        {
            Console.WriteLine(format);
        }

        public void Fatal(object format)
        {
            Console.WriteLine(format);
        }

        public void Fatal(object format, Exception exception)
        {
            Console.WriteLine(format);
        }

        public void FatalFormat(string format, params object[] args)
        {
            Console.WriteLine(format);
        }

        public void FatalFormat(string format, object arg0)
        {
            Console.WriteLine(format);
        }

        public void FatalFormat(string format, object arg0, object arg1)
        {
            Console.WriteLine(format);
        }

        public void FatalFormat(string format, object arg0, object arg1, object arg2)
        {
            Console.WriteLine(format);
        }

        public void FatalFormat(IFormatProvider provider, string format, params object[] args)
        {
            Console.WriteLine(format);
        }
    }
}
