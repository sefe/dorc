using System.Text;
using System.Text.Json;

namespace Dorc.McpServer;

public sealed class DorcApiGateway : IDorcApiGateway
{
    private readonly HttpClient _httpClient;
    private readonly JsonSerializerOptions _jsonOptions;

    public DorcApiGateway(HttpClient httpClient)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _jsonOptions = DorcJsonSerialization.CreateOptions();
    }

    public async Task<McpToolOutcome<TResponse>> GetAsync<TResponse>(
        string relativePath,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, relativePath);
        return await SendAndDeserializeAsync<TResponse>(request, cancellationToken).ConfigureAwait(false);
    }

    public async Task<McpToolOutcome<string>> GetStringAsync(
        string relativePath,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, relativePath);
        return await SendAndReadStringAsync(request, cancellationToken).ConfigureAwait(false);
    }

    public async Task<McpToolOutcome<TResponse>> PostAsync<TRequest, TResponse>(
        string relativePath,
        TRequest? body,
        CancellationToken cancellationToken)
    {
        using var request = CreateJsonRequest(HttpMethod.Post, relativePath, body);
        return await SendAndDeserializeAsync<TResponse>(request, cancellationToken).ConfigureAwait(false);
    }

    public async Task<McpToolOutcome<string>> PostWithoutResponseBodyAsync<TRequest>(
        string relativePath,
        TRequest? body,
        CancellationToken cancellationToken)
    {
        using var request = CreateJsonRequest(HttpMethod.Post, relativePath, body);
        return await SendAndReadStringAsync(request, cancellationToken).ConfigureAwait(false);
    }

    public async Task<McpToolOutcome<TResponse>> PutAsync<TRequest, TResponse>(
        string relativePath,
        TRequest? body,
        CancellationToken cancellationToken)
    {
        using var request = CreateJsonRequest(HttpMethod.Put, relativePath, body);
        return await SendAndDeserializeAsync<TResponse>(request, cancellationToken).ConfigureAwait(false);
    }

    public async Task<McpToolOutcome<string>> PutStringResponseAsync<TRequest>(
        string relativePath,
        TRequest? body,
        CancellationToken cancellationToken)
    {
        using var request = CreateJsonRequest(HttpMethod.Put, relativePath, body);
        return await SendAndReadStringAsync(request, cancellationToken).ConfigureAwait(false);
    }

    public async Task<McpToolOutcome<TResponse>> DeleteAsync<TRequest, TResponse>(
        string relativePath,
        TRequest? body,
        CancellationToken cancellationToken)
    {
        using var request = CreateJsonRequest(HttpMethod.Delete, relativePath, body);
        return await SendAndDeserializeAsync<TResponse>(request, cancellationToken).ConfigureAwait(false);
    }

    public async Task<McpToolOutcome<string>> DeleteWithoutResponseBodyAsync(
        string relativePath,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, relativePath);
        return await SendAndReadStringAsync(request, cancellationToken).ConfigureAwait(false);
    }

    private HttpRequestMessage CreateJsonRequest<TRequest>(
        HttpMethod method,
        string relativePath,
        TRequest? body)
    {
        var request = new HttpRequestMessage(method, relativePath);
        if (body is not null)
        {
            var json = JsonSerializer.Serialize(body, _jsonOptions);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        return request;
    }

    private async Task<McpToolOutcome<TResponse>> SendAndDeserializeAsync<TResponse>(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var statusCode = (int)response.StatusCode;
        var rawContent = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var errorText = string.IsNullOrWhiteSpace(rawContent)
                ? $"HTTP {statusCode} ({response.ReasonPhrase})"
                : $"HTTP {statusCode}: {LogTruncator.TruncateTail(rawContent, 1024)}";
            return McpToolOutcome.Failure<TResponse>(errorText, statusCode);
        }

        if (string.IsNullOrWhiteSpace(rawContent))
        {
            return McpToolOutcome.Success<TResponse>(default!, statusCode);
        }

        try
        {
            var deserialized = JsonSerializer.Deserialize<TResponse>(rawContent, _jsonOptions);
            return McpToolOutcome.Success(deserialized!, statusCode);
        }
        catch (JsonException ex)
        {
            return McpToolOutcome.Failure<TResponse>(
                $"HTTP {statusCode}: Invalid JSON response from DOrc API ({ex.Message}).",
                statusCode);
        }
    }

    private async Task<McpToolOutcome<string>> SendAndReadStringAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var statusCode = (int)response.StatusCode;
        var rawContent = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var errorText = string.IsNullOrWhiteSpace(rawContent)
                ? $"HTTP {statusCode} ({response.ReasonPhrase})"
                : $"HTTP {statusCode}: {LogTruncator.TruncateTail(rawContent, 1024)}";
            return McpToolOutcome.Failure<string>(errorText, statusCode);
        }

        if (string.IsNullOrEmpty(rawContent))
        {
            return McpToolOutcome.Success(string.Empty, statusCode);
        }

        var trimmed = rawContent.Trim();
        var isPlainText = string.Equals(
            response.Content.Headers.ContentType?.MediaType,
            "text/plain",
            StringComparison.OrdinalIgnoreCase);

        if (!isPlainText &&
            trimmed.Length >= 2 &&
            trimmed[0] == '"' &&
            trimmed[^1] == '"')
        {
            try
            {
                var decoded = JsonSerializer.Deserialize<string>(trimmed, _jsonOptions)!;
                return McpToolOutcome.Success(decoded, statusCode);
            }
            catch (JsonException ex)
            {
                return McpToolOutcome.Failure<string>(
                    $"HTTP {statusCode}: Invalid JSON string response from DOrc API ({ex.Message}).",
                    statusCode);
            }
        }

        return McpToolOutcome.Success(rawContent, statusCode);
    }
}
