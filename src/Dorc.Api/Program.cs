using Dorc.Api.Services;
using Dorc.Core.Configuration;
using Dorc.Core.Lamar;
using Dorc.PersistentData;
using Dorc.PersistentData.Contexts;
using Lamar.Microsoft.DependencyInjection;
using log4net.Config;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Negotiate;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Formatters;
using System.Net;
using System.Text.Json.Serialization;
using Dorc.Core.VariableResolution;

const string dorcCorsRefDataPolicy = "DOrcCORSRefData";

var builder = WebApplication.CreateBuilder(args);

var allowedCorsLocations = new ConfigurationBuilder().AddJsonFile("appsettings.json").Build().GetSection("AppSettings")[
    "AllowedCORSLocations"]?.Split(",");

builder.Services.AddCors(options =>
{
    options.AddPolicy(dorcCorsRefDataPolicy,
        policy =>
        {
            if (allowedCorsLocations != null)
                policy.SetIsOriginAllowed(origin => allowedCorsLocations.Any(loc => loc.StartsWith(origin)))
                    .AllowAnyMethod()
                    .AllowAnyHeader().AllowCredentials();
        });
});
// Add services to the container.
builder.Services.AddAuthentication(NegotiateDefaults.AuthenticationScheme)
    .AddNegotiate();

builder.Services
    .AddControllers(opts =>
    {
        opts.OutputFormatters.RemoveType<StringOutputFormatter>(); // never return plain text
        opts.Filters.Add(new RequireHttpsAttribute());
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
builder.Services.AddSwaggerGen();
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

var cxnString = builder.Configuration.GetConnectionString("DOrcConnectionString");
builder.Services.AddScoped<DeploymentContext>(_ => new DeploymentContext(cxnString));
builder.Services.AddTransient<IClaimsTransformation, ClaimsTransformer>();

builder.Services.AddTransient<IConfigurationRoot>(_ => new ConfigurationBuilder().AddJsonFile("appsettings.json").Build());
builder.Services.AddTransient<IConfigurationSettings, ConfigurationSettings>();

//builder.Services.AddHttpsRedirection(options =>
//{
//    options.RedirectStatusCode = (int)HttpStatusCode.PermanentRedirect;
//    options.HttpsPort = 7159;
//});

builder.Services.AddHsts(options =>
{
    options.Preload = true;
    options.IncludeSubDomains = true;
    options.MaxAge = TimeSpan.FromDays(60);
});

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();
app.UseExceptionHandler(_ => { }); // empty lambda is required until https://github.com/dotnet/aspnetcore/issues/51888 is fixed

app.UseMiddleware<OptionsMiddleware>();
app.UseCors(dorcCorsRefDataPolicy);

app.UseHsts();
//app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();