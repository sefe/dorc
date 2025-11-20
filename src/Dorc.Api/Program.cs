using AspNetCoreRateLimit;
using Dorc.Api.Events;
using Dorc.Api.Interfaces;
using Dorc.Api.Security;
using Dorc.Api.Services;
using Dorc.Core;
using Dorc.Core.AzureStorageAccount;
using Dorc.Core.Configuration;
using Dorc.Core.Events;
using Dorc.Core.Interfaces;
using Dorc.Core.Lamar;
using Dorc.Core.Security;
using Dorc.Core.VariableResolution;
using Dorc.OpenSearchData;
using Dorc.PersistentData;
using Dorc.PersistentData.Contexts;
using Lamar.Microsoft.DependencyInjection;
using log4net.Config;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authentication.Negotiate;
using Microsoft.AspNetCore.Mvc.Formatters;
using Microsoft.OpenApi.Models;
using System.Text.Json.Serialization;

const string dorcCorsRefDataPolicy = "DOrcCORSRefData";
const string apiScopeAuthorizationPolicy = "ApiGlobalScopeAuthorizationPolicy";

var builder = WebApplication.CreateBuilder(args);
var configBuilder = new ConfigurationBuilder().AddJsonFile("appsettings.json").Build();

builder.Logging.AddLog4Net();

var configurationSettings = new ConfigurationSettings(configBuilder);
var secretsReader = new OnePasswordSecretsReader(configurationSettings);

builder.Services.AddSingleton<IConfigurationSecretsReader>(secretsReader);

var allowedCorsLocations = configurationSettings.GetAllowedCorsLocations();

builder.Services.AddCors(options =>
{
    options.AddPolicy(dorcCorsRefDataPolicy,
        policy =>
        {
            if (allowedCorsLocations != null)
            {
                policy.WithOrigins(allowedCorsLocations)
                    .AllowAnyMethod()
                    .AllowAnyHeader().AllowCredentials();
            }
        });
});


static void ConfigureOAuth(WebApplicationBuilder builder, IConfigurationSettings configurationSettings, IConfigurationSecretsReader secretsReader, bool registerOwnReader = true)
{
    if (registerOwnReader)
    {
        builder.Services.AddTransient(ctx => ctx.GetService<IUserGroupsReaderFactory>().GetOAuthUserGroupsReader());
        builder.Services.AddTransient<IClaimsPrincipalReader, OAuthClaimsPrincipalReader>();
    }

    string? authority = configurationSettings.GetOAuthAuthority();
    string dorcApiResourceName = configurationSettings.GetOAuthApiResourceName();
    string dorcApiGlobalScope = configurationSettings.GetOAuthApiGlobalScope();
    
    // Get DORC API secret from secrets manager
    string dorcApiSecret = secretsReader.GetDorcApiSecret();

    builder.Services
        .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddJwtBearer(JwtBearerDefaults.AuthenticationScheme, options =>
        {
            options.Authority = authority;
            options.TokenValidationParameters = new()
            {
                ValidateIssuer = true,
                ValidIssuer = authority,
                ValidateAudience = true,
                ValidAudience = dorcApiResourceName,
                ValidateLifetime = true,
                RoleClaimType = "role",
                NameClaimType = "name"
            };
            options.MapInboundClaims = false;
            options.ForwardDefaultSelector = ReferenceTokenSelector.ForwardReferenceToken();

            options.Events = new JwtBearerEvents
            {
                OnMessageReceived = context =>
                {
                    // Support SignalR WebSocket / SSE where token is passed as query string
                    var accessToken = context.Request.Query["access_token"];
                    var path = context.HttpContext.Request.Path;
                    if (!string.IsNullOrEmpty(accessToken) &&
                        path.StartsWithSegments("/hubs"))
                    {
                        context.Token = accessToken;
                    }
                    return Task.CompletedTask;
                }
            };
        })
        // Enabling Reference tokens
        .AddOAuth2Introspection("introspection", options =>
        {
            options.Authority = authority;
            // this maps to the "API resource" name and secret
            options.ClientId = dorcApiResourceName;
            options.ClientSecret = dorcApiSecret;
            options.NameClaimType = "name";
        });

    builder.Services.AddAuthorization(options =>
    {
        // Define Authorization Policy (check for required scope [see 'dorcApiScope' constant])
        options.AddPolicy(apiScopeAuthorizationPolicy, policy =>
        {
            policy.RequireAuthenticatedUser();
            policy.RequireClaim("scope", dorcApiGlobalScope);
        });
    });
}


static void AddSwaggerGen(IServiceCollection services, IConfigurationSettings configurationSettings)
{
    services.AddSwaggerGen(c =>
    {
        // Define OAuth2 scheme
        c.AddSecurityDefinition("oauth2", new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.OAuth2,
            Flows = new OpenApiOAuthFlows
            {
                AuthorizationCode = new OpenApiOAuthFlow
                {
                    AuthorizationUrl = new Uri($"{configurationSettings.GetOAuthAuthority()}/connect/authorize"),
                    TokenUrl = new Uri($"{configurationSettings.GetOAuthAuthority()}/connect/token"),
                    Scopes = new Dictionary<string, string>
                    {
                        { configurationSettings.GetOAuthApiGlobalScope(), "Access to the DORC API" },
                    }
                }
            }
        });

        c.AddSecurityRequirement(new OpenApiSecurityRequirement
        {
            {
                new OpenApiSecurityScheme
                {
                    Reference = new OpenApiReference
                    {
                        Type = ReferenceType.SecurityScheme,
                        Id = "oauth2"
                    }
                },
                new string[] { }
            }
        });
    });
}

builder.Services
    .AddControllers(opts =>
    {
        opts.OutputFormatters.RemoveType<StringOutputFormatter>(); // never return plain text
        //opts.Filters.Add(new RequireHttpsAttribute());
    })
    .AddJsonOptions(opts =>
    {
        opts.JsonSerializerOptions.PropertyNamingPolicy = null;
        opts.JsonSerializerOptions.MaxDepth = 64;
        opts.JsonSerializerOptions.IncludeFields = true;
        opts.JsonSerializerOptions.Converters.Add(new ExceptionJsonConverter());
        opts.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
        opts.JsonSerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles;
    });

builder.Services.AddEndpointsApiExplorer();
AddSwaggerGen(builder.Services , configurationSettings);
builder.Services.AddExceptionHandler<DefaultExceptionHandler>()
    .ConfigureHttpJsonOptions(opts => opts.SerializerOptions.PropertyNamingPolicy = null);

// SignalR configuration
var signalRService = builder.Services.AddSignalR();
if (configBuilder.GetValue<bool>("Azure:SignalR:IsUseAzureSignalR"))
{
    signalRService.AddAzureSignalR(conf =>
    {
        conf.ApplicationName = configurationSettings.GetEnvironment(true);
    });
}
builder.Services.AddScoped<IDeploymentEventsPublisher, DirectDeploymentEventPublisher>();
builder.Services.AddSingleton<IDeploymentSubscriptionsGroupTracker, DeploymentSubscriptionsGroupTracker>();

builder.Services.AddMemoryCache();
builder.Services.AddTransient<IConfigurationRoot>(_ => configBuilder);
builder.Services.AddTransient<IConfigurationSettings, ConfigurationSettings>(_ => configurationSettings);
builder.Services.AddTransient<IAzureStorageAccountWorker, AzureStorageAccountWorker>();

builder.Host.UseLamar((context, registry) =>
{
    registry.IncludeRegistry<OpenSearchDataRegistry>();
    registry.IncludeRegistry<PersistentDataRegistry>();
    registry.IncludeRegistry<CoreRegistry>();
    registry.IncludeRegistry<ApiRegistry>();

    registry.AddScoped<IBundledRequestVariableLoader, BundledRequestVariableLoader>();
    registry.AddKeyedTransient<IVariableResolver, VariableResolver>("VariableResolver");
    registry.AddKeyedTransient<IVariableResolver, BundledRequestVariableResolver>("BundledRequestVariableResolver");

    registry.AddControllers();
});

XmlConfigurator.Configure(new FileInfo("log4net.config"));

var cxnString = configurationSettings.GetDorcConnectionString();
builder.Services.AddScoped<DeploymentContext>(_ => new DeploymentContext(cxnString));

// Enable throttling
builder.Services.AddOptions();
builder.Services.Configure<IpRateLimitOptions>(builder.Configuration.GetSection("IpRateLimiting"));
builder.Services.Configure<IpRateLimitPolicies>(builder.Configuration.GetSection("IpRateLimitPolicies"));
builder.Services.AddSingleton<IIpPolicyStore, MemoryCacheIpPolicyStore>();
builder.Services.AddSingleton<IRateLimitCounterStore, MemoryCacheRateLimitCounterStore>();
builder.Services.AddSingleton<IRateLimitConfiguration, RateLimitConfiguration>();
builder.Services.AddInMemoryRateLimiting();

//builder.Services.AddHttpsRedirection(options =>
//{
//    options.RedirectStatusCode = (int)HttpStatusCode.PermanentRedirect;
//    options.HttpsPort = 7159;
//});

//builder.Services.AddHsts(options =>
//{
//    options.Preload = true;
//    options.IncludeSubDomains = true;
//    options.MaxAge = TimeSpan.FromDays(60);
//});

var app = builder.Build();

app.UseIpRateLimiting();

app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.OAuthClientId(configurationSettings.GetOAuthSwaggerClientId());
    c.OAuthAppName("DORC API");
    c.OAuthUsePkce();
});
app.UseExceptionHandler(_ => { }); // empty lambda is required until https://github.com/dotnet/aspnetcore/issues/51888 is fixed
app.UseCors(dorcCorsRefDataPolicy);

//app.UseHsts();
//app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();
app.UseMiddleware<WinAuthLoggingMiddleware>();

var endpointConventionBuilder = app.MapControllers();
    // Enforce Authorization Policy [see constant 'apiScopeAuthorizationPolicy'] to all the Controllers
    endpointConventionBuilder.RequireAuthorization(apiScopeAuthorizationPolicy);

// Map SignalR hub
app.MapHub<DeploymentsHub>("/hubs/deployments");

app.Run();// Apply security requirement globally