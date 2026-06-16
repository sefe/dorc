namespace Dorc.Core
{
    // Strips ASCII control characters from values that flow into log statements so
    // user-controlled input cannot forge new log lines or smuggle terminal escape
    // sequences. Caps length defensively. Intentionally lives in Dorc.Core so both
    // the primary API and (later) the Dorc.Api.WindowsWorker can reuse it.
    public static class LogSanitizer
    {
        private const int MaxLength = 512;

        public static string? Sanitize(string? value)
        {
            if (string.IsNullOrEmpty(value)) return value;

            var source = value.Length > MaxLength ? value.AsSpan(0, MaxLength) : value.AsSpan();
            Span<char> buffer = source.Length <= 256 ? stackalloc char[source.Length] : new char[source.Length];

            var write = 0;
            foreach (var c in source)
            {
                // Keep space (0x20) and printable ASCII / non-ASCII; replace control chars (incl. \r, \n, \t, ESC, DEL).
                if (c == ' ' || (c >= 0x20 && c != 0x7F))
                {
                    buffer[write++] = c;
                }
                else
                {
                    buffer[write++] = '_';
                }
            }

            return new string(buffer[..write]);
        }
    }
}
