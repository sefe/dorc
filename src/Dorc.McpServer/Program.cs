using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Hosting;

namespace Dorc.McpServer;

[ExcludeFromCodeCoverage]
internal static class Program
{
    public static async Task Main(string[] args)
    {
        using var host = DorcMcpHostConfiguration.CreateHostBuilder(args).Build();
        await host.RunAsync().ConfigureAwait(false);
    }
}
