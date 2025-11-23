using Dorc.Api.Windows.Security;
using Dorc.Api.Windows.Services;
using Dorc.Core.Configuration;
using Dorc.Core.Interfaces;
using Dorc.Core.Lamar;
using Dorc.Core.Security;
using Dorc.OpenSearchData;
using Dorc.PersistentData;
using Dorc.PersistentData.Contexts;
using Lamar.Microsoft.DependencyInjection;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Negotiate;
using Microsoft.AspNetCore.Mvc.Formatters;
using Serilog;
using Serilog.Extensions.Logging;
using System.Text.Json.Serialization;

// Windows API - Handles Windows-specific controllers that require Windows platform features
// This API should run on Windows OS alongside the main Dorc.Api
const string dorcCorsRefDataPolicy = "DOrcCORSRefData";

var builder = WebApplication.CreateBuilder(args);
var configBuilder = new ConfigurationBuilder()
    .AddJsonFile("appsettings.json")
    .AddJsonFile("loggerSettings.json", optional: false, reloadOnChange: true)
    .Build();

#region Logging Configuration
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
Log.Logger = new LoggerConfiguration()
    .Enrich.WithThreadId()
    .ReadFrom.Configuration(configBuilder)
    .CreateLogger();
builder.Logging.AddSerilog(Log.Logger);
#endregion

var configurationSettings = new ConfigurationSettings(configBuilder);

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

// Configure Windows Authentication
builder.Services.AddTransient<IClaimsPrincipalReader, WinAuthClaimsPrincipalReader>();
builder.Services
    .AddSingleton<IHttpContextAccessor, HttpContextAccessor>()
    .AddTransient<IClaimsTransformation, ClaimsTransformer>()
    .AddAuthentication(NegotiateDefaults.AuthenticationScheme)
    .AddNegotiate();

builder.Services
    .AddControllers(opts =>
    {
        opts.OutputFormatters.RemoveType<StringOutputFormatter>(); // never return plain text
    })
    .AddJsonOptions(opts =>
    {
        opts.JsonSerializerOptions.PropertyNamingPolicy = null;
        opts.JsonSerializerOptions.MaxDepth = 64;
        opts.JsonSerializerOptions.IncludeFields = true;
        opts.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
        opts.JsonSerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles;
    });

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddMemoryCache();
builder.Services.AddTransient<IConfigurationRoot>(_ => configBuilder);
builder.Services.AddTransient<IConfigurationSettings, ConfigurationSettings>(_ => configurationSettings);

builder.Host.UseLamar((context, registry) =>
{
    registry.IncludeRegistry<OpenSearchDataRegistry>();
    registry.IncludeRegistry<PersistentDataRegistry>();
    registry.IncludeRegistry<CoreRegistry>();
    registry.IncludeRegistry<ApiRegistry>();

    registry.AddControllers();
});

var cxnString = configurationSettings.GetDorcConnectionString();
builder.Services.AddScoped<DeploymentContext>(_ => new DeploymentContext(cxnString));

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();
app.UseCors(dorcCorsRefDataPolicy);

app.UseAuthentication();
app.UseAuthorization();
app.UseMiddleware<WinAuthLoggingMiddleware>();

app.MapControllers();

app.Run();
