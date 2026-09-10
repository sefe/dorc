namespace Dorc.Monitor.Notifications
{
    // Notification failures must never take the Monitor down, so callers log and swallow
    // everything short of a process-fatal CLR exception.
    internal static class FatalExceptions
    {
        public static bool Is(Exception ex) =>
            ex is OutOfMemoryException
               or StackOverflowException
               or AccessViolationException
               or AppDomainUnloadedException
               or BadImageFormatException
               or CannotUnloadAppDomainException
               or InvalidProgramException
               or ThreadAbortException;
    }
}
