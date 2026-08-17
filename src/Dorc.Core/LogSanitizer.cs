using System.Globalization;

namespace Dorc.Core
{
    // Strips control and line-breaking characters from values that flow into log
    // statements so user-controlled input cannot forge new log lines or smuggle terminal
    // escape sequences. Caps length defensively. Intentionally lives in Dorc.Core so both
    // the primary API and (later) the Dorc.Api.WindowsWorker can reuse it.
    public static class LogSanitizer
    {
        private const int MaxLength = 512;

        public static string? Sanitize(string? value)
        {
            if (string.IsNullOrEmpty(value)) return value;

            var length = value.Length;
            if (length > MaxLength)
            {
                length = MaxLength;
                // Do not slice through a surrogate pair — a lone surrogate serialises as
                // U+FFFD downstream and corrupts the tail of the message.
                if (char.IsHighSurrogate(value[length - 1])) length--;
            }

            var source = value.AsSpan(0, length);
            Span<char> buffer = source.Length <= 256 ? stackalloc char[source.Length] : new char[source.Length];

            var write = 0;
            foreach (var c in source)
            {
                buffer[write++] = IsUnsafeForLogs(c) ? '_' : c;
            }

            return new string(buffer[..write]);
        }

        private static bool IsUnsafeForLogs(char c)
        {
            // char.IsControl covers C0 (0x00-0x1F), DEL (0x7F) AND C1 (0x80-0x9F). C1 matters:
            // U+0085 NEL is a line terminator to many log consumers, and U+009B is the
            // single-character CSI, i.e. ANSI escape injection without needing ESC.
            if (char.IsControl(c)) return true;

            // Line/paragraph separators terminate a line for .NET Regex in multiline mode and
            // for every JavaScript-based log viewer, so they forge lines just like \n.
            if (c == '\u2028' || c == '\u2029') return true;

            // Format characters include the bidirectional overrides (U+202E RLO and friends)
            // that let an attacker visually reverse the remainder of an audit line.
            return CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.Format;
        }
    }
}
