using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Dorc.McpServer;

[ExcludeFromCodeCoverage]
internal static class Program
{
    public static async Task Main(string[] args)
    {
        var probeConfig = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true)
            .AddEnvironmentVariables()
            .AddCommandLine(args)
            .Build();

        var mcpOptions = DorcMcpHostConfiguration.ResolveMcpOptions(probeConfig);
        if (DorcMcpHostConfiguration.IsHttpTransport(mcpOptions.Transport))
        {
            await using var webApp = DorcMcpHostConfiguration.CreateWebApplication(args);
            await webApp.RunAsync().ConfigureAwait(false);
            return;
        }

        using var host = DorcMcpHostConfiguration.CreateHostBuilder(args).Build();
        await host.RunAsync().ConfigureAwait(false);
    }
}
