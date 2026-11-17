using System.Diagnostics.CodeAnalysis;
using Dorc.McpServer.Tools;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Dorc.McpServer;

public static class DorcMcpHostConfiguration
{
    public static DorcApiOptions ResolveApiOptions(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var options = new DorcApiOptions();
        configuration.GetSection(DorcApiOptions.SectionName).Bind(options);

        var baseUrlEnv = configuration["DORC_API_BASE_URL"];
        if (!string.IsNullOrWhiteSpace(baseUrlEnv))
        {
            options.BaseUrl = baseUrlEnv.Trim();
        }

        var clientIdEnv = configuration["DORC_API_CLIENT_ID"];
        if (!string.IsNullOrWhiteSpace(clientIdEnv))
        {
            options.ClientId = clientIdEnv.Trim();
        }

        var clientSecretEnv = configuration["DORC_API_CLIENT_SECRET"];
        if (!string.IsNullOrWhiteSpace(clientSecretEnv))
        {
            options.ClientSecret = clientSecretEnv.Trim();
        }

        var scopeEnv = configuration["DORC_API_SCOPE"];
        if (!string.IsNullOrWhiteSpace(scopeEnv))
        {
            options.Scope = scopeEnv.Trim();
        }

        return options;
    }

    public static DorcMcpOptions ResolveMcpOptions(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var options = new DorcMcpOptions();
        configuration.GetSection(DorcMcpOptions.SectionName).Bind(options);

        var readOnlyEnv = configuration["DORC_MCP_READ_ONLY"];
        if (bool.TryParse(readOnlyEnv, out var parsedReadOnly))
        {
            options.ReadOnly = parsedReadOnly;
        }

        var allowProdEnv = configuration["DORC_MCP_ALLOW_PROD"];
        if (bool.TryParse(allowProdEnv, out var parsedAllowProd))
        {
            options.AllowProd = parsedAllowProd;
        }

        return options;
    }

    public static HostApplicationBuilder CreateHostBuilder(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(args);

        builder.Logging.ClearProviders();
        builder.Logging.AddConsole(options =>
        {
            options.LogToStandardErrorThreshold = LogLevel.Trace;
        });

        var apiOptions = ResolveApiOptions(builder.Configuration);
        var mcpOptions = ResolveMcpOptions(builder.Configuration);

        builder.Services.AddSingleton(apiOptions);
        builder.Services.AddSingleton(mcpOptions);
        builder.Services.AddSingleton<ProductionEnvironmentGuard>();

        builder.Services.AddHttpClient<IOAuthTokenAcquirer, OAuthTokenAcquirer>();
        builder.Services.AddTransient<BearerTokenRefreshingHandler>();

        builder.Services.AddHttpClient<IDorcApiGateway, DorcApiGateway>((sp, client) =>
        {
            var opts = sp.GetRequiredService<DorcApiOptions>();
            if (!string.IsNullOrWhiteSpace(opts.BaseUrl))
            {
                client.BaseAddress = new Uri(opts.BaseUrl.TrimEnd('/') + "/", UriKind.Absolute);
            }
        }).AddHttpMessageHandler<BearerTokenRefreshingHandler>();

        builder.Services
            .AddMcpServer()
            .WithStdioServerTransport()
            .WithTools<DeploymentRequestTools>()
            .WithTools<ProjectArtifactTools>()
            .WithTools<EnvironmentCatalogTools>()
            .WithTools<PropertyResolutionTools>()
            .WithTools<TerraformCatalogTools>()
            .WithTools<DaemonOperationsTools>();

        return builder;
    }
}
