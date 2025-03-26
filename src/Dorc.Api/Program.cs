using System.Text.Json.Serialization;
using AspNetCoreRateLimit;
using Dorc.Api.Security;
using Dorc.Api.Services;
using Dorc.Core;
using Dorc.Core.Configuration;
using Dorc.Core.Interfaces;
using Dorc.Core.Lamar;
using Dorc.Core.VariableResolution;
using Dorc.PersistentData;
using Dorc.PersistentData.Contexts;
using Dorc.PersistentData.Extensions;
using Lamar.Microsoft.DependencyInjection;
using log4net.Config;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authentication.Negotiate;
using Microsoft.AspNetCore.Mvc.Formatters;
using Microsoft.OpenApi.Models;

const string dorcCorsRefDataPolicy = "DOrcCORSRefData";
const string dorcApiResourceName = "dorc-api";
const string dorcApiScope = "dorc-api.manage";
const string apiScopeAuthorizationPolicy = "ApiScopeAuthorizationPolicy";

var builder = WebApplication.CreateBuilder(args);
var configBuilder = new ConfigurationBuilder().AddJsonFile("appsettings.json").Build();
var configurationSettings = new ConfigurationSettings(configBuilder);

var allowedCorsLocations = configurationSettings.GetAllowedCorsLocations();
UserExtensions.CacheDuration = configurationSettings.GetADUserCacheTimeSpan();

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

builder.Logging.AddLog4Net();
string? authenticationScheme = configurationSettings.GetAuthenticationScheme();
switch (authenticationScheme)
{
    case "OAuth":
        ConfigureOAuth(builder, configurationSettings);
        break;
    case "WinAuth":
        ConfigureWinAuth(builder);
        break;
    case "Both":
        ConfigureBoth(builder, configurationSettings);
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
        .AddTransient<IClaimsTransformation, ClaimsTransformer>()
        .AddAuthentication(NegotiateDefaults.AuthenticationScheme)
        .AddNegotiate();
}

static void ConfigureOAuth(WebApplicationBuilder builder, ConfigurationSettings configurationSettings, bool registerOwnReader = true)
{
    if (registerOwnReader)
    {
        builder.Services.AddTransient<IClaimsPrincipalReader, OAuthClaimsPrincipalReader>();
    }

    string? authority = configurationSettings.GetOAuthAuthority();
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
                NameClaimType = "samAccountName"
            };
            options.MapInboundClaims = false;
            options.ForwardDefaultSelector = ReferenceTokenSelector.ForwardReferenceToken();
        })
        // Enabling Reference tokens
        .AddOAuth2Introspection("introspection", options =>
        {
            options.Authority = authority;
            // this maps to the "API resource" name and secret
            options.ClientId = dorcApiResourceName;
            options.ClientSecret = GetDorcApiSecret();
        });

    builder.Services.AddAuthorization(options =>
    {
        // Define Authorization Policy (check for required scope [see 'dorcApiScope' constant])
        options.AddPolicy(apiScopeAuthorizationPolicy, policy =>
        {
            policy.RequireAuthenticatedUser();
            policy.RequireClaim("scope", dorcApiScope);
        });
    });
}
static void ConfigureBoth(WebApplicationBuilder builder, ConfigurationSettings configurationSettings)
{
    builder.Services.AddSingleton<IHttpContextAccessor, HttpContextAccessor>();
    builder.Services.AddTransient<IClaimsPrincipalReader, ClaimsPrincipalReaderFactory>();

    ConfigureWinAuth(builder, false);
    ConfigureOAuth(builder, configurationSettings, false);

    // Add a Policy Scheme to dynamically select the authentication scheme
    builder.Services.AddAuthentication(options =>
    {
        options.DefaultScheme = "DynamicScheme";
        options.DefaultChallengeScheme = "DynamicScheme";
    })
    .AddPolicyScheme("DynamicScheme", "Dynamic Authentication Scheme", options =>
    {
        options.ForwardDefaultSelector = context =>
        {
            // Check if the request contains a Bearer token
            string? authHeader = context.Request.Headers["Authorization"].FirstOrDefault();
            if (authHeader != null && authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                return JwtBearerDefaults.AuthenticationScheme; // Use OAuth (JWT Bearer)
            }

            // Otherwise, fall back to Windows Authentication
            return NegotiateDefaults.AuthenticationScheme;
        };
    });
}

static string GetDorcApiSecret()
{
    // Not implemented yet. TO-DO: Get the secret from a secure location
    return "";
}

static void AddSwaggerGen(IServiceCollection services, string? authenticationScheme)
{
    if (authenticationScheme is not "OAuth")
    {
        services.AddSwaggerGen();
    }
    else
    {
        services.AddSwaggerGen(options =>
        {
            options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
            {
                Name = "Authorization",
                Type = SecuritySchemeType.Http,
                Scheme = "Bearer",
                BearerFormat = "JWT",
                In = ParameterLocation.Header,
                Description = "Enter 'Bearer {your JWT token}' to authenticate."
            });

            options.AddSecurityRequirement(new OpenApiSecurityRequirement
                {
                    {
                        new OpenApiSecurityScheme
                        {
                            Reference = new OpenApiReference
                            {
                                Type = ReferenceType.SecurityScheme,
                                Id = "Bearer"
                            }
                        },
                        new string[] { }
                    }
                });
        });
    }
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
        opts.JsonSerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles;
    });

builder.Services.AddEndpointsApiExplorer();
AddSwaggerGen(builder.Services, authenticationScheme);
builder.Services.AddExceptionHandler<DefaultExceptionHandler>()
    .ConfigureHttpJsonOptions(opts => opts.SerializerOptions.PropertyNamingPolicy = null);

builder.Host.UseLamar((context, registry) =>
{
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

builder.Services.AddTransient<IConfigurationRoot>(_ => configBuilder);
builder.Services.AddTransient<IConfigurationSettings, ConfigurationSettings>(_ => configurationSettings);

builder.Services.AddMemoryCache();
builder.Services.AddSingleton<IActiveDirectoryUserGroupReader, ActiveDirectoryUserGroupReader>();

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
app.UseSwaggerUI();
app.UseExceptionHandler(_ => { }); // empty lambda is required until https://github.com/dotnet/aspnetcore/issues/51888 is fixed
app.UseCors(dorcCorsRefDataPolicy);

//app.UseHsts();
//app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();

var endpointConventionBuilder = app.MapControllers();
if (authenticationScheme is "OAuth")
{
    // Enforce Authorization Policy [see constant 'apiScopeAuthorizationPolicy'] to all the Controllers
    endpointConventionBuilder.RequireAuthorization(apiScopeAuthorizationPolicy);
}

app.Run();