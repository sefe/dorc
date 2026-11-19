namespace Dorc.McpServer;

public interface IDorcApiGateway
{
    Task<McpToolOutcome<TResponse>> GetAsync<TResponse>(string relativePath, CancellationToken cancellationToken);

    Task<McpToolOutcome<string>> GetStringAsync(string relativePath, CancellationToken cancellationToken);

    Task<McpToolOutcome<TResponse>> PostAsync<TRequest, TResponse>(
        string relativePath,
        TRequest? body,
        CancellationToken cancellationToken);

    Task<McpToolOutcome<string>> PostWithoutResponseBodyAsync<TRequest>(
        string relativePath,
        TRequest? body,
        CancellationToken cancellationToken);

    Task<McpToolOutcome<TResponse>> PutAsync<TRequest, TResponse>(
        string relativePath,
        TRequest? body,
        CancellationToken cancellationToken);

    Task<McpToolOutcome<string>> PutStringResponseAsync<TRequest>(
        string relativePath,
        TRequest? body,
        CancellationToken cancellationToken);

    Task<McpToolOutcome<TResponse>> DeleteAsync<TRequest, TResponse>(
        string relativePath,
        TRequest? body,
        CancellationToken cancellationToken);

    Task<McpToolOutcome<string>> DeleteWithoutResponseBodyAsync(
        string relativePath,
        CancellationToken cancellationToken);
}
