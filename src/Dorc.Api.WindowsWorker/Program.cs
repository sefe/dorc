using Dorc.Api.WindowsWorker;
using Dorc.Api.WindowsWorker.Authentication;
using Dorc.Api.WindowsWorker.Services;

var builder = WebApplication.CreateBuilder(args);

// U-12 decision (S-008): the worker is hosted as a Windows Service — matching the DOrc
// Monitor model, no IIS dependency. No-op when launched from a console.
builder.Host.UseWindowsService(options => options.ServiceName = "DorcApiWindowsWorker");

var port = builder.Configuration.GetValue<int?>("WindowsWorker:Port") ?? 5005;
builder.WebHost.ConfigureKestrel(opts =>
{
    // Loopback-only bind — the security boundary in HLPS D-3's threat model.
    opts.Listen(System.Net.IPAddress.Loopback, port);
});

builder.Services.AddControllers();
builder.Services.AddHealthChecks();
builder.Services.AddSingleton<IDaemonServiceOperations, DaemonServiceOperations>();
builder.Services.AddSingleton<IRemoteServerOperatingSystemReader, RemoteRegistryOperatingSystemReader>();
builder.Services.AddSingleton<ServiceAccountImpersonator>();
builder.Services.AddSingleton<Dorc.Core.Interfaces.ISqlUserPasswordReset, Dorc.Core.SqlUserPasswordReset>();

builder.Services
    .AddAuthentication(WorkerKeyAuthenticationOptions.SchemeName)
    .AddScheme<WorkerKeyAuthenticationOptions, WorkerKeyAuthenticationHandler>(
        WorkerKeyAuthenticationOptions.SchemeName, _ => { });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(WorkerKeyAuthorizationPolicies.FromPrimary, policy =>
    {
        policy.AddAuthenticationSchemes(WorkerKeyAuthenticationOptions.SchemeName);
        policy.RequireAuthenticatedUser();
    });
});

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

// Health is intentionally unauthenticated — the primary uses it to detect liveness,
// and the loopback bind already prevents off-host access.
app.MapHealthChecks("/health");
app.MapControllers().RequireAuthorization(WorkerKeyAuthorizationPolicies.FromPrimary);

app.Run();

// Exposed as partial so WebApplicationFactory<Program> in the test project can find it.
public partial class Program { }
