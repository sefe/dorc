namespace Dorc.Monitor.RunnerProcess.Interop.Windows.Kernel32;

internal static partial class Interop
{
    internal static partial class Kernel32
    {
        [Flags]
        internal enum HandleFlags
        {
            HANDLE_FLAG_INHERIT = 0x00000001,
            HANDLE_FLAG_PROTECT_FROM_CLOSE = 0x00000002
        }
    }
}