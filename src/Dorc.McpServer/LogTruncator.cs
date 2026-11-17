namespace Dorc.McpServer;

public static class LogTruncator
{
    public const int DefaultMaxCharacters = 32768;

    public static string TruncateTail(string? logContent, int maxCharacters = DefaultMaxCharacters)
    {
        if (string.IsNullOrEmpty(logContent))
        {
            return string.Empty;
        }

        if (maxCharacters <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxCharacters), "maxCharacters must be greater than zero.");
        }

        if (logContent.Length <= maxCharacters)
        {
            return logContent;
        }

        var omittedCount = logContent.Length - maxCharacters;
        var prefix = $"[...truncated {omittedCount} leading characters...]\n";
        if (prefix.Length >= maxCharacters)
        {
            return logContent.Substring(logContent.Length - maxCharacters, maxCharacters);
        }

        var tailLength = maxCharacters - prefix.Length;
        return string.Concat(prefix, logContent.AsSpan(logContent.Length - tailLength, tailLength));
    }
}
