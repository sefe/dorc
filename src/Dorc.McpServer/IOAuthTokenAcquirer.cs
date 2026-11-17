namespace Dorc.McpServer;

public interface IOAuthTokenAcquirer
{
    Task<string> GetAccessTokenAsync(bool forceRefresh, CancellationToken cancellationToken);
}
