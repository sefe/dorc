namespace Dorc.McpServer;

public static class LogTruncator
{
    public const int DefaultMaxCharacters = 32768;
    public const int AbsoluteMaxCharacters = 262144;

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

        var effectiveMax = Math.Min(maxCharacters, AbsoluteMaxCharacters);
        if (logContent.Length <= effectiveMax)
        {
            return logContent;
        }

        var maxMarkerLength = $"[...truncated {logContent.Length} leading characters...]\n".Length;
        if (maxMarkerLength >= effectiveMax)
        {
            return logContent.Substring(logContent.Length - effectiveMax, effectiveMax);
        }

        var tailLength = effectiveMax - maxMarkerLength;
        var omittedCount = logContent.Length - tailLength;
        var prefix = $"[...truncated {omittedCount} leading characters...]\n";
        tailLength = effectiveMax - prefix.Length;
        omittedCount = logContent.Length - tailLength;
        prefix = $"[...truncated {omittedCount} leading characters...]\n";

        return string.Concat(prefix, logContent.AsSpan(logContent.Length - tailLength, tailLength));
    }
}
