using log4net;
using log4net.Core;

namespace Dorc.Api.Tests
{
    public class MockedLog : ILog
    {
        public ILogger Logger { get; }
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

        public bool IsDebugEnabled { get; }
        public bool IsInfoEnabled { get; }
        public bool IsWarnEnabled { get; }
        public bool IsErrorEnabled { get; }
        public bool IsFatalEnabled { get; }
    }
}
