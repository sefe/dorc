using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Dorc.McpServer.Tests;

[TestClass]
public sealed class DorcMcpHostConfigurationTests
{
    [TestMethod]
    public void Options_Properties_WorkAsExpected()
    {
        var apiOptions = new DorcApiOptions
        {
            BaseUrl = "https://dorc.example.com",
            ClientId = "cid",
            ClientSecret = "csecret",
            Scope = "custom.scope"
        };
        Assert.AreEqual("https://dorc.example.com", apiOptions.BaseUrl);
        Assert.AreEqual("cid", apiOptions.ClientId);
        Assert.AreEqual("csecret", apiOptions.ClientSecret);
        Assert.AreEqual("custom.scope", apiOptions.Scope);

        var mcpOptions = new DorcMcpOptions
        {
            ReadOnly = false,
            AllowProd = true,
            Transport = "Http",
            EndpointPath = "/custom-mcp",
            OAuthAuthority = "https://idp.example.com",
            OAuthApiResourceName = "dorc-api",
            OAuthApiGlobalScope = "dorc-api.manage"
        };
        Assert.IsFalse(mcpOptions.ReadOnly);
        Assert.IsTrue(mcpOptions.AllowProd);
        Assert.AreEqual("Http", mcpOptions.Transport);
        Assert.AreEqual("/custom-mcp", mcpOptions.EndpointPath);
        Assert.AreEqual("https://idp.example.com", mcpOptions.OAuthAuthority);
        Assert.AreEqual("dorc-api", mcpOptions.OAuthApiResourceName);
        Assert.AreEqual("dorc-api.manage", mcpOptions.OAuthApiGlobalScope);
    }

    [TestMethod]
    public async Task DorcMcpHostConfiguration_ResolvesOptionsAndBuildsHost()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() => DorcMcpHostConfiguration.ResolveApiOptions(null!));
        Assert.ThrowsExactly<ArgumentNullException>(() => DorcMcpHostConfiguration.ResolveMcpOptions(null!));
        var emptyConfig = new ConfigurationBuilder().AddInMemoryCollection([]).Build();
        Assert.ThrowsExactly<ArgumentNullException>(() => DorcMcpHostConfiguration.ConfigureDorcMcpServices(null!, emptyConfig, false));
        Assert.ThrowsExactly<ArgumentNullException>(() => DorcMcpHostConfiguration.ConfigureDorcMcpServices(new ServiceCollection(), null!, false));

        Assert.IsTrue(DorcMcpHostConfiguration.IsHttpTransport("Http"));
        Assert.IsTrue(DorcMcpHostConfiguration.IsHttpTransport("http"));
        Assert.IsFalse(DorcMcpHostConfiguration.IsHttpTransport("Stdio"));
        Assert.IsFalse(DorcMcpHostConfiguration.IsHttpTransport(null));

        var configFull = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DORC_API_BASE_URL"] = " https://dorc.env.local/ ",
                ["DORC_API_CLIENT_ID"] = " env-client ",
                ["DORC_API_CLIENT_SECRET"] = " env-secret ",
                ["DORC_API_SCOPE"] = " env-scope ",
                ["DORC_MCP_READ_ONLY"] = "false",
                ["DORC_MCP_ALLOW_PROD"] = "true",
                ["DORC_MCP_TRANSPORT"] = " Http ",
                ["DORC_MCP_ENDPOINT_PATH"] = " /api/mcp ",
                ["DORC_OAUTH_AUTHORITY"] = " https://identityserver:5200/ ",
                ["DORC_OAUTH_API_RESOURCE_NAME"] = " dorc-api-np ",
                ["DORC_OAUTH_API_GLOBAL_SCOPE"] = " dorc-api-np.manage "
            })
            .Build();

        var apiOpts = DorcMcpHostConfiguration.ResolveApiOptions(configFull);
        Assert.AreEqual("https://dorc.env.local/", apiOpts.BaseUrl);
        Assert.AreEqual("env-client", apiOpts.ClientId);
        Assert.AreEqual("env-secret", apiOpts.ClientSecret);
        Assert.AreEqual("env-scope", apiOpts.Scope);

        var mcpOpts = DorcMcpHostConfiguration.ResolveMcpOptions(configFull);
        Assert.IsFalse(mcpOpts.ReadOnly);
        Assert.IsTrue(mcpOpts.AllowProd);
        Assert.AreEqual("Http", mcpOpts.Transport);
        Assert.AreEqual("/api/mcp", mcpOpts.EndpointPath);
        Assert.AreEqual("https://identityserver:5200/", mcpOpts.OAuthAuthority);
        Assert.AreEqual("dorc-api-np", mcpOpts.OAuthApiResourceName);
        Assert.AreEqual("dorc-api-np.manage", mcpOpts.OAuthApiGlobalScope);

        var defaultApiOpts = DorcMcpHostConfiguration.ResolveApiOptions(emptyConfig);
        Assert.AreEqual("dorc-api.manage", defaultApiOpts.Scope);
        var defaultMcpOpts = DorcMcpHostConfiguration.ResolveMcpOptions(emptyConfig);
        Assert.IsTrue(defaultMcpOpts.ReadOnly);
        Assert.IsFalse(defaultMcpOpts.AllowProd);
        Assert.AreEqual("Stdio", defaultMcpOpts.Transport);
        Assert.AreEqual("/mcp", defaultMcpOpts.EndpointPath);
        Assert.AreEqual(string.Empty, defaultMcpOpts.OAuthAuthority);
        Assert.AreEqual(string.Empty, defaultMcpOpts.OAuthApiResourceName);
        Assert.AreEqual("dorc-api.manage", defaultMcpOpts.OAuthApiGlobalScope);

        // Test CreateHostBuilder with both non-empty and empty BaseUrl, OAuth credentials present vs absent, and ReadOnly vs non-ReadOnly
        using var hostWithUrl = DorcMcpHostConfiguration.CreateHostBuilder([
            "--DorcApi:BaseUrl=https://dorc.local",
            "--DorcApi:ClientId=cid",
            "--DorcApi:ClientSecret=csec",
            "--DorcMcp:ReadOnly=false"
        ]).Build();
        var gatewayWithUrl = hostWithUrl.Services.GetRequiredService<IDorcApiGateway>();
        Assert.IsNotNull(gatewayWithUrl);
        var writableTools = hostWithUrl.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<ModelContextProtocol.Server.McpServerOptions>>().Value.ToolCollection;
        Assert.IsNotNull(writableTools);
        Assert.IsTrue(writableTools.Any(t => t.ProtocolTool.Annotations?.ReadOnlyHint != true));

        using var hostPartialOAuth = DorcMcpHostConfiguration.CreateHostBuilder([
            "--DorcApi:BaseUrl=https://dorc.local",
            "--DorcApi:ClientId=",
            "--DorcApi:ClientSecret=csec",
            "--DorcMcp:ReadOnly=true"
        ]).Build();
        Assert.IsNotNull(hostPartialOAuth.Services.GetRequiredService<IDorcApiGateway>());
        var readOnlyTools = hostPartialOAuth.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<ModelContextProtocol.Server.McpServerOptions>>().Value.ToolCollection;
        Assert.IsNotNull(readOnlyTools);
        Assert.IsFalse(readOnlyTools.Any(t => t.ProtocolTool.Annotations?.ReadOnlyHint != true));

        using var hostWithoutUrl = DorcMcpHostConfiguration.CreateHostBuilder([
            "--DorcApi:BaseUrl=",
            "--DorcApi:ClientId=",
            "--DorcApi:ClientSecret="
        ]).Build();
        var gatewayWithoutUrl = hostWithoutUrl.Services.GetRequiredService<IDorcApiGateway>();
        Assert.IsNotNull(gatewayWithoutUrl);

        // Test CreateWebApplication with missing OAuthAuthority throws InvalidOperationException
        Assert.ThrowsExactly<InvalidOperationException>(() => DorcMcpHostConfiguration.CreateWebApplication([
            "--DorcApi:BaseUrl=https://dorc.local",
            "--DorcMcp:Transport=Http",
            "--DorcMcp:EndpointPath=/mcp",
            "--DorcMcp:OAuthAuthority="
        ]));

        // Test CreateWebApplication with custom and blank EndpointPath, and with OAuthAuthority (both with resource/scope and without resource/scope)
        await using var webAppCustom = DorcMcpHostConfiguration.CreateWebApplication([
            "--DorcApi:BaseUrl=https://dorc.local",
            "--DorcMcp:Transport=Http",
            "--DorcMcp:EndpointPath=/mcp",
            "--DorcMcp:OAuthAuthority=https://identityserver:5200/"
        ]);
        Assert.IsNotNull(webAppCustom.Services.GetRequiredService<IDorcApiGateway>());

        await using var webAppBlankPath = DorcMcpHostConfiguration.CreateWebApplication([
            "--DorcApi:BaseUrl=https://dorc.local",
            "--DorcMcp:Transport=Http",
            "--DorcMcp:EndpointPath=   ",
            "--DorcMcp:OAuthAuthority=https://identityserver:5200/",
            "--DorcMcp:OAuthApiResourceName=dorc-api-np",
            "--DorcMcp:OAuthApiGlobalScope=dorc-api-np.manage"
        ]);
        Assert.IsNotNull(webAppBlankPath.Services.GetRequiredService<IDorcApiGateway>());
        var authSchemeProvider = webAppBlankPath.Services.GetRequiredService<IAuthenticationSchemeProvider>();
        var defaultAuthScheme = await authSchemeProvider.GetDefaultAuthenticateSchemeAsync();
        Assert.AreEqual(Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerDefaults.AuthenticationScheme, defaultAuthScheme?.Name);
        var defaultChallengeScheme = await authSchemeProvider.GetDefaultChallengeSchemeAsync();
        Assert.AreEqual(ModelContextProtocol.AspNetCore.Authentication.McpAuthenticationDefaults.AuthenticationScheme, defaultChallengeScheme?.Name);
        var jwtOptsWithRes = webAppBlankPath.Services.GetRequiredService<Microsoft.Extensions.Options.IOptionsMonitor<Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerOptions>>()
            .Get(Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerDefaults.AuthenticationScheme);
        Assert.IsTrue(jwtOptsWithRes.TokenValidationParameters.ValidateAudience);
        Assert.AreEqual("dorc-api-np", jwtOptsWithRes.TokenValidationParameters.ValidAudience);
        var mcpAuthOptsWithScope = webAppBlankPath.Services.GetRequiredService<Microsoft.Extensions.Options.IOptionsMonitor<ModelContextProtocol.AspNetCore.Authentication.McpAuthenticationOptions>>()
            .Get(ModelContextProtocol.AspNetCore.Authentication.McpAuthenticationDefaults.AuthenticationScheme);
        CollectionAssert.Contains(mcpAuthOptsWithScope.ResourceMetadata!.ScopesSupported.ToArray(), "dorc-api-np.manage");
        var authPolicyProvider = webAppBlankPath.Services.GetRequiredService<Microsoft.AspNetCore.Authorization.IAuthorizationPolicyProvider>();
        Assert.IsNotNull(await authPolicyProvider.GetPolicyAsync(DorcMcpHostConfiguration.McpScopeAuthorizationPolicy));

        await using var webAppOAuthWithoutResourceOrScope = DorcMcpHostConfiguration.CreateWebApplication([
            "--DorcApi:BaseUrl=https://dorc.local",
            "--DorcMcp:Transport=Http",
            "--DorcMcp:OAuthAuthority=https://identityserver:5200",
            "--DorcMcp:OAuthApiResourceName=   ",
            "--DorcMcp:OAuthApiGlobalScope=   "
        ]);
        var jwtOptsNoRes = webAppOAuthWithoutResourceOrScope.Services.GetRequiredService<Microsoft.Extensions.Options.IOptionsMonitor<Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerOptions>>()
            .Get(Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerDefaults.AuthenticationScheme);
        Assert.IsFalse(jwtOptsNoRes.TokenValidationParameters.ValidateAudience);
        Assert.IsNull(jwtOptsNoRes.TokenValidationParameters.ValidAudience);
        var mcpAuthOptsNoScope = webAppOAuthWithoutResourceOrScope.Services.GetRequiredService<Microsoft.Extensions.Options.IOptionsMonitor<ModelContextProtocol.AspNetCore.Authentication.McpAuthenticationOptions>>()
            .Get(ModelContextProtocol.AspNetCore.Authentication.McpAuthenticationDefaults.AuthenticationScheme);
        Assert.IsEmpty(mcpAuthOptsNoScope.ResourceMetadata!.ScopesSupported);
        var authPolicyProviderNoScope = webAppOAuthWithoutResourceOrScope.Services.GetRequiredService<Microsoft.AspNetCore.Authorization.IAuthorizationPolicyProvider>();
        Assert.IsNotNull(await authPolicyProviderNoScope.GetPolicyAsync(DorcMcpHostConfiguration.McpScopeAuthorizationPolicy));
    }
}
