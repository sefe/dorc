using AspNetCoreRateLimit;
using Dorc.Api.Events;
using Dorc.Api.Interfaces;
using Dorc.Api.Security;
using Dorc.Api.Services;
using Dorc.Core.AzureStorageAccount;
using Dorc.Core.Configuration;
using Dorc.Core.Interfaces;
using Dorc.Core.Lamar;
using Dorc.Core.Security;
using Dorc.Core.VariableResolution;
using Dorc.OpenSearchData;
using Dorc.PersistentData;
using Dorc.PersistentData.Contexts;
using Lamar.Microsoft.DependencyInjection;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authentication.Negotiate;
using Microsoft.AspNetCore.Mvc.Formatters;
using Microsoft.OpenApi.Models;
using OpenTelemetry.Logs;
using OpenTelemetry.Resources;
using Serilog;
using Serilog.Extensions.Logging;
using System.Reflection;
using System.Text.Json.Serialization;

const string dorcCorsRefDataPolicy = "DOrcCORSRefData";
const string apiScopeAuthorizationPolicy = "ApiGlobalScopeAuthorizationPolicy";

var builder = WebApplication.CreateBuilder(args);
var configBuilder = new ConfigurationBuilder()
    .AddJsonFile("appsettings.json")
    .AddJsonFile("loggerSettings.json", optional:false, reloadOnChange: true)
    .Build();

var configurationSettings = new ConfigurationSettings(configBuilder);

#region Logging Configuration
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
Log.Logger = new LoggerConfiguration()
    .Enrich.WithThreadId()
    .ReadFrom.Configuration(configBuilder)
    .CreateLogger();
builder.Logging.AddSerilog(Log.Logger);

var otlpEndpoint = configBuilder.GetValue<string>("OpenTelemetry:OtlpEndpoint");
if (!string.IsNullOrEmpty(otlpEndpoint))
{
    builder.Logging.AddOpenTelemetry(logging =>
    {
        logging.SetResourceBuilder(ResourceBuilder.CreateDefault()
            .AddService("Dorc.Api", serviceVersion: Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "1.0.0")
            .AddAttributes(new Dictionary<string, object>
            {
                ["service.namespace"] = "DOrc",
                ["deployment.environment"] = configurationSettings.GetEnvironment(),
                ["host.name"] = Environment.MachineName
            }));

        logging.AddOtlpExporter(options =>
        {
            options.Endpoint = new Uri(otlpEndpoint);
        });
    });
}
#endregion

// Create logger factory early for secrets reader
var secretsReaderLogger = new SerilogLoggerFactory()
        .CreateLogger<OnePasswordSecretsReader>();
var secretsReader = new OnePasswordSecretsReader(configurationSettings, secretsReaderLogger);

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

string? authenticationScheme = configurationSettings.GetAuthenticationScheme();
switch (authenticationScheme)
{
    case ConfigAuthScheme.OAuth:
        ConfigureOAuth(builder, configurationSettings, secretsReader);
        break;
    case ConfigAuthScheme.WinAuth:
        ConfigureWinAuth(builder);
        break;
    case ConfigAuthScheme.Both:
        ConfigureBoth(builder, configurationSettings, secretsReader);
        break;
    default:
        ConfigureWinAuth(builder);
        break;
}

static void ConfigureWinAuth(WebApplicationBuilder builder, bool registerOwnReader = true)
{
    if (registerOwnReader)
    {
        builder.Services.AddTransient<IClaimsPrincipalReader, WinAuthClaimsPrincipalReader>();
    }

    builder.Services
        .AddSingleton<IHttpContextAccessor, HttpContextAccessor>()
        .AddTransient<IClaimsTransformation, ClaimsTransformer>()
        .AddAuthentication(NegotiateDefaults.AuthenticationScheme)
        .AddNegotiate();
}

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

static void ConfigureBoth(WebApplicationBuilder builder, IConfigurationSettings configurationSettings, IConfigurationSecretsReader secretsReader)
{
    builder.Services.AddSingleton<IHttpContextAccessor, HttpContextAccessor>();
    builder.Services.AddTransient<IClaimsPrincipalReader, ClaimsPrincipalReaderFactory>();

    ConfigureWinAuth(builder, false);
    ConfigureOAuth(builder, configurationSettings, secretsReader, false);

    // Add a Policy Scheme to dynamically select the authentication scheme
    builder.Services.AddAuthentication(options =>
    {
        options.DefaultScheme = "DynamicScheme";
        options.DefaultChallengeScheme = "DynamicScheme";
    })
    .AddPolicyScheme("DynamicScheme", "Dynamic Authentication Scheme", options =>
    {
        options.ForwardDefaultSelector = context => context.GetAuthenticationScheme();
    });
}

static void AddSwaggerGen(IServiceCollection services, IConfigurationSettings configurationSettings)
{
    string? authority = configurationSettings.GetOAuthAuthority();
    string dorcApiGlobalScope = configurationSettings.GetOAuthApiGlobalScope();
    
    services.AddSwaggerGen(options =>
    {
        // Always configure OAuth2 for Swagger UI regardless of auth scheme
        options.AddSecurityDefinition("oauth2", new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.OAuth2,
            Flows = new OpenApiOAuthFlows
            {
                AuthorizationCode = new OpenApiOAuthFlow
                {
                    AuthorizationUrl = new Uri($"{authority}/connect/authorize"),
                    TokenUrl = new Uri($"{authority}/connect/token"),
                    Scopes = new Dictionary<string, string>
                    {
                        { dorcApiGlobalScope, "Access to DORC API" }
                    }
                }
            }
        });

        options.AddSecurityRequirement(new OpenApiSecurityRequirement
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
                new[] { dorcApiGlobalScope }
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
AddSwaggerGen(builder.Services, configurationSettings);
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
    c.OAuthClientId(configurationSettings.GetOAuthUiClientId());
    c.OAuthAppName("DORC API");
    c.OAuthUsePkce();
    c.OAuthScopes(configurationSettings.GetOAuthApiGlobalScope());
});
app.UseExceptionHandler(_ => { }); // empty lambda is required until https://github.com/dotnet/aspnetcore/issues/51888 is fixed
app.UseCors(dorcCorsRefDataPolicy);

//app.UseHsts();
//app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();
app.UseMiddleware<WinAuthLoggingMiddleware>();

var endpointConventionBuilder = app.MapControllers();
if (authenticationScheme is ConfigAuthScheme.OAuth)
{
    // Enforce Authorization Policy [see constant 'apiScopeAuthorizationPolicy'] to all the Controllers
    endpointConventionBuilder.RequireAuthorization(apiScopeAuthorizationPolicy);
}

// Map SignalR hub
app.MapHub<DeploymentsHub>("/hubs/deployments");

app.Run();