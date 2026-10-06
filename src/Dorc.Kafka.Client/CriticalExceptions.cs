namespace Dorc.Kafka.Client;

/// <summary>
/// The process-fatal exception set that must never be swallowed by the
/// substrate's never-throw resilience boundaries. Every broad
/// <c>catch (Exception ex) when (!CriticalExceptions.IsCritical(ex))</c>
/// filter in the Kafka client/events/lock components consults this single
/// definition so the "always let process-fatal exceptions escape" contract
/// cannot drift between copies: these exceptions indicate corrupted process
/// state, and the runtime must be allowed to observe them and restart the
/// host cleanly.
/// </summary>
public static class CriticalExceptions
{
    public static bool IsCritical(Exception ex) =>
        ex is OutOfMemoryException
            or StackOverflowException
            or AccessViolationException
            or System.Threading.ThreadAbortException;
}
