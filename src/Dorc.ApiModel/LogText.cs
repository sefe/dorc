namespace Dorc.ApiModel
{
    /// <summary>
    /// Prepares caller-supplied text for a log message.
    ///
    /// Account names, paths, URLs and reasons all reach log messages as unvalidated strings.
    /// A value carrying CR or LF would let whoever set it forge whole log entries, which is
    /// what CodeQL's log-forging rule flags. Everything else about the value is preserved so
    /// that a misconfigured value is still recognisable in the log that reports it.
    ///
    /// Lives in the shared model assembly so that every process - API, Monitor and the
    /// runners on both frameworks - uses the one definition.
    /// </summary>
    public static class LogText
    {
        public static string SingleLine(string value)
        {
            return string.IsNullOrEmpty(value)
                ? string.Empty
                : value.Replace("\r", string.Empty).Replace("\n", string.Empty);
        }
    }
}
