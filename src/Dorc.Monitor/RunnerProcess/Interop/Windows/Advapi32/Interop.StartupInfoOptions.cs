namespace Dorc.Monitor.RunnerProcess.Interop.Windows.Advapi32;

internal static partial class Interop
{
    internal static partial class Advapi32
    {
        internal static partial class StartupInfoOptions
        {
            internal const int STARTF_USESHOWWINDOW = 0x00000001;
            internal const int STARTF_USESTDHANDLES = 0x00000100;
            internal const int CREATE_UNICODE_ENVIRONMENT = 0x00000400;
            internal const int CREATE_NO_WINDOW = 0x08000000;
        }
    }
}