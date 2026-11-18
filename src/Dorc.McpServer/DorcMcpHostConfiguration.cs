using Dorc.McpServer.Tools;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.AspNetCore;
using ModelContextProtocol.Server;

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

        var transportEnv = configuration["DORC_MCP_TRANSPORT"];
        if (!string.IsNullOrWhiteSpace(transportEnv))
        {
            options.Transport = transportEnv.Trim();
        }

        var endpointPathEnv = configuration["DORC_MCP_ENDPOINT_PATH"];
        if (!string.IsNullOrWhiteSpace(endpointPathEnv))
        {
            options.EndpointPath = endpointPathEnv.Trim();
        }

        return options;
    }

    public static bool IsHttpTransport(string? transport)
    {
        return string.Equals(transport, "Http", StringComparison.OrdinalIgnoreCase);
    }

    public static HostApplicationBuilder CreateHostBuilder(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            Args = args,
            ContentRootPath = AppContext.BaseDirectory
        });

        builder.Logging.ClearProviders();
        builder.Logging.AddConsole(options =>
        {
            options.LogToStandardErrorThreshold = LogLevel.Trace;
        });

        ConfigureDorcMcpServices(builder.Services, builder.Configuration, useHttpTransport: false);
        return builder;
    }

    public static WebApplication CreateWebApplication(string[] args)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            Args = args,
            ContentRootPath = AppContext.BaseDirectory
        });

        var mcpOptions = ConfigureDorcMcpServices(builder.Services, builder.Configuration, useHttpTransport: true);

        var app = builder.Build();
        var endpointPath = string.IsNullOrWhiteSpace(mcpOptions.EndpointPath)
            ? "/mcp"
            : mcpOptions.EndpointPath.Trim();

        app.MapMcp(endpointPath);
        return app;
    }

    public static DorcMcpOptions ConfigureDorcMcpServices(
        IServiceCollection services,
        IConfiguration configuration,
        bool useHttpTransport)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var apiOptions = ResolveApiOptions(configuration);
        var mcpOptions = ResolveMcpOptions(configuration);

        services.AddSingleton(apiOptions);
        services.AddSingleton(mcpOptions);
        services.AddSingleton<ProductionEnvironmentGuard>();

        services.AddHttpContextAccessor();
        services.AddTransient<CallerAuthorizationForwardingHandler>();
        services.AddHttpClient<IOAuthTokenAcquirer, OAuthTokenAcquirer>();
        services.AddTransient<BearerTokenRefreshingHandler>();

        var hasOAuthCredentials = !string.IsNullOrWhiteSpace(apiOptions.ClientId) ||
                                  !string.IsNullOrWhiteSpace(apiOptions.ClientSecret);

        var gatewayHttpBuilder = services.AddHttpClient<IDorcApiGateway, DorcApiGateway>((sp, client) =>
        {
            var opts = sp.GetRequiredService<DorcApiOptions>();
            if (!string.IsNullOrWhiteSpace(opts.BaseUrl))
            {
                client.BaseAddress = new Uri(opts.BaseUrl.TrimEnd('/') + "/", UriKind.Absolute);
            }
        });

        gatewayHttpBuilder.AddHttpMessageHandler<CallerAuthorizationForwardingHandler>();

        if (hasOAuthCredentials)
        {
            gatewayHttpBuilder.AddHttpMessageHandler<BearerTokenRefreshingHandler>();
        }
        else
        {
            gatewayHttpBuilder.ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
            {
                UseDefaultCredentials = true
            });
        }

        var mcpBuilder = services
            .AddMcpServer()
            .WithTools<DeploymentRequestTools>()
            .WithTools<ProjectArtifactTools>()
            .WithTools<EnvironmentCatalogTools>()
            .WithTools<PropertyResolutionTools>()
            .WithTools<TerraformCatalogTools>()
            .WithTools<DaemonOperationsTools>();

        if (useHttpTransport)
        {
            mcpBuilder.WithHttpTransport(options =>
            {
                options.SessionMode = HttpServerSessionMode.StatefulForInitializeClients;
            });
        }
        else
        {
            mcpBuilder.WithStdioServerTransport();
        }

        if (mcpOptions.ReadOnly)
        {
            services.PostConfigure<McpServerOptions>(options =>
            {
                var mutatingTools = options.ToolCollection!
                    .Where(t => t.ProtocolTool.Annotations!.ReadOnlyHint != true)
                    .ToList();

                foreach (var tool in mutatingTools)
                {
                    options.ToolCollection!.Remove(tool);
                }
            });
        }

        return mcpOptions;
    }
}
