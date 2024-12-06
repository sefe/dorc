namespace Dorc.Monitor.RunnerProcess.Interop.Windows.Kernel32;

internal static partial class Interop
{
    internal static partial class Kernel32
    {
        internal static partial class HandleOptions
        {
            internal const int DUPLICATE_SAME_ACCESS = 2;
            internal const int STILL_ACTIVE = 0x00000103;
            internal const int TOKEN_ADJUST_PRIVILEGES = 0x20;
        }
    }
}