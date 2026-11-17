using System.Text.Json.Serialization;

namespace Dorc.McpServer;

public sealed record McpToolOutcome<T>(
    [property: JsonPropertyName("succeeded")] bool Succeeded,
    [property: JsonPropertyName("data")] T? Data,
    [property: JsonPropertyName("error")] string? Error = null,
    [property: JsonPropertyName("httpStatusCode")] int? HttpStatusCode = null);

public static class McpToolOutcome
{
    public static McpToolOutcome<T> Success<T>(T data, int httpStatusCode = 200) =>
        new(true, data, null, httpStatusCode);

    public static McpToolOutcome<T> Failure<T>(string error, int? httpStatusCode = null) =>
        new(false, default, error, httpStatusCode);
}
