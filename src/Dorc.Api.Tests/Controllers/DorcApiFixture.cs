using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Dorc.Api.Interfaces;
using Dorc.Api.Security;
using Dorc.Api.Services;
using Dorc.ApiModel;
using Dorc.Core.Account;
using Dorc.Core.Account.Models;
using Dorc.Core.Interfaces;
using Dorc.Core.Lamar;
using Dorc.Core.Security;
using Dorc.OpenSearchData;
using Dorc.OpenSearchData.Sources.Interfaces;
using Dorc.PersistentData;
using Dorc.PersistentData.Contexts;
using Dorc.PersistentData.Model;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authentication.Negotiate;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Environment = System.Environment;

namespace Dorc.Api.Tests.Controllers;

public sealed class DorcApiFixture : IDisposable
{
    public string MasterConnectionString { get; }
    public string DatabaseName { get; }
    public string DatabaseConnectionString { get; }
    public DeploymentContextFactory ContextFactory { get; }
    public string RepoRoot { get; }
    public string CatalogManifestsDir { get; }
    public WebApplicationFactory<Program> HostFactory { get; }
    public JsonSerializerOptions JsonOptions { get; }
    public Dorc.Core.AzureStorageAccount.IAzureStorageAccountWorker AzureStorageWorker { get; }

    public DorcApiFixture(string databasePrefix)
    {
        var connection = Environment.GetEnvironmentVariable("ConnectionStrings__DOrcConnectionString")
            ?? "Server=(localdb)\\MSSQLLocalDB;Database=master;Integrated Security=True;Encrypt=True;TrustServerCertificate=True;";

        var builder = new SqlConnectionStringBuilder(connection)
        {
            InitialCatalog = "master",
            Encrypt = SqlConnectionEncryptOption.Mandatory,
            TrustServerCertificate = true
        };
        try
        {
            using var probe = new SqlConnection(builder.ConnectionString);
            probe.Open();
        }
        catch (SqlException ex)
        {
            if (Environment.GetEnvironmentVariable("DORC_DB_TESTS_REQUIRED") == "1")
                Assert.Fail($"SQL Server is required for Database category tests: {ex.Message}");
            Assert.Inconclusive($"SQL Server is not reachable ({ex.Message}).");
        }

        MasterConnectionString = builder.ConnectionString;
        DatabaseName = $"{databasePrefix}_{Guid.NewGuid():N}";

        using (var sql = new SqlConnection(MasterConnectionString))
        {
            sql.Open();
            using var cmd = new SqlCommand($"CREATE DATABASE [{DatabaseName}] COLLATE SQL_Latin1_General_CP1_CI_AS", sql);
            cmd.ExecuteNonQuery();
        }

        builder.InitialCatalog = DatabaseName;
        DatabaseConnectionString = builder.ConnectionString;
        ContextFactory = new DeploymentContextFactory(DatabaseConnectionString);

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Join(dir.FullName, "src", "Dorc.sln")))
        {
            dir = dir.Parent;
        }
        RepoRoot = dir!.FullName;

        using (var context = ContextFactory.GetContext())
        {
            context.Database.EnsureCreated();
            context.Database.ExecuteSqlRaw("ALTER TABLE [deploy].[Project] ADD DEFAULT NEWID() FOR [ObjectId]");
            context.Database.ExecuteSqlRaw("IF COL_LENGTH('deploy.Environment', 'Owner') IS NULL ALTER TABLE [deploy].[Environment] ADD [Owner] NVARCHAR(100) NULL;");
            context.Database.ExecuteSqlRaw("CREATE SCHEMA [archive]");

            ExecuteSqlFile(Path.Join(RepoRoot, "src", "Dorc.Database", "Schema Objects", "Schemas", "archive", "Tables", "DeploymentRequest.table.sql"));
            ExecuteSqlFile(Path.Join(RepoRoot, "src", "Dorc.Database", "Schema Objects", "Schemas", "archive", "Tables", "DeploymentResult.table.sql"));
            ExecuteSqlFile(Path.Join(RepoRoot, "src", "Dorc.Database", "Schema Objects", "Schemas", "archive", "Tables", "EnvironmentComponentStatus.table.sql"));
            ExecuteSqlFile(Path.Join(RepoRoot, "src", "Dorc.Database", "Schema Objects", "Schemas", "deploy", "Views", "DeploymentDefinitionHistoryView.view.sql"));
            ExecuteSqlFile(Path.Join(RepoRoot, "src", "Dorc.Database", "deploy", "Functions", "SplitTagString.sql"));
            ExecuteSqlFile(Path.Join(RepoRoot, "src", "Dorc.Database", "deploy", "Stored Procedures", "GetFullEnvironmentChain.sql"));
            ExecuteSqlFile(Path.Join(RepoRoot, "src", "Dorc.Database", "deploy", "Stored Procedures", "DuplicateEnvironmentPropertyValues.sql"));
            ExecuteSqlFile(Path.Join(RepoRoot, "src", "Dorc.Database", "deploy", "Stored Procedures", "AppendRequestLog.sql"));
            ExecuteSqlFile(Path.Join(RepoRoot, "src", "Dorc.Database", "deploy", "Stored Procedures", "get_global_properties.sql"));
            ExecuteSqlFile(Path.Join(RepoRoot, "src", "Dorc.Database", "deploy", "Stored Procedures", "get_environment_properties.sql"));
            ExecuteSqlFile(Path.Join(RepoRoot, "src", "Dorc.Database", "deploy", "Stored Procedures", "get_property_values_by_PropertyName.sql"));
            ExecuteSqlFile(Path.Join(RepoRoot, "src", "Dorc.Database", "deploy", "Stored Procedures", "get_property_values_for_user.sql"));
            ExecuteSqlFile(Path.Join(RepoRoot, "src", "Dorc.Database", "deploy", "Stored Procedures", "get_property_values_for_user_with_inheritance.sql"));
            ExecuteSqlFile(Path.Join(RepoRoot, "src", "Dorc.Database", "Scripts", "Post-Deployment", "SeedRefDataAuditActions.sql"));

            context.Database.ExecuteSqlRaw("""
                IF NOT EXISTS (SELECT 1 FROM [deploy].[PropertyFilter] WHERE [Id] = 1)
                BEGIN
                    SET IDENTITY_INSERT [deploy].[PropertyFilter] ON;
                    INSERT INTO [deploy].[PropertyFilter] ([Id], [Name], [Priority]) VALUES (1, N'Environment', 1);
                    SET IDENTITY_INSERT [deploy].[PropertyFilter] OFF;
                END
                """);

            if (!context.SecureKeys.Any())
            {
                context.SecureKeys.Add(new SecureKey
                {
                    IV = Convert.ToBase64String(new byte[16]),
                    Key = Convert.ToBase64String(Enumerable.Range(1, 32).Select(i => (byte)i).ToArray())
                });
                context.SaveChanges();
            }
        }

        CatalogManifestsDir = Path.Join(Path.GetTempPath(), $"dorc_catalog_{Guid.NewGuid():N}");
        Directory.CreateDirectory(CatalogManifestsDir);
        SeedCatalogManifest(CatalogManifestsDir);

        JsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            Converters = { new JsonStringEnumConverter() }
        };

        AzureStorageWorker = Substitute.For<Dorc.Core.AzureStorageAccount.IAzureStorageAccountWorker>();
        HostFactory = new EmbeddedApiApplicationFactory(DatabaseConnectionString, CatalogManifestsDir, AzureStorageWorker);
    }

    public HttpClient CreateClientForUser(string userId, string email, string role = "Admin", string scheme = "Bearer")
    {
        var client = HostFactory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(scheme, "test-token");
        client.DefaultRequestHeaders.Add("X-Test-User-Id", userId);
        client.DefaultRequestHeaders.Add("X-Test-User-Email", email);
        client.DefaultRequestHeaders.Add("X-Test-User-Roles", role);
        return client;
    }

    public void Dispose()
    {
        HostFactory.Dispose();

        if (Directory.Exists(CatalogManifestsDir))
        {
            try
            {
                Directory.Delete(CatalogManifestsDir, true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Ignore transient cleanup failure on temp catalog directory
            }
        }

        if (!string.IsNullOrEmpty(DatabaseName) && !string.IsNullOrEmpty(MasterConnectionString))
        {
            using var sql = new SqlConnection(MasterConnectionString);
            sql.Open();
            using var cmd = new SqlCommand($"ALTER DATABASE [{DatabaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{DatabaseName}]", sql);
            cmd.ExecuteNonQuery();
        }
    }

    private void ExecuteSqlFile(string filePath)
    {
        var script = File.ReadAllText(filePath);
        var batches = Regex.Split(script, @"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase);
        using var sql = new SqlConnection(DatabaseConnectionString);
        sql.Open();
        foreach (var batch in batches.Where(b => !string.IsNullOrWhiteSpace(b)))
        {
            using var cmd = new SqlCommand(batch, sql);
            cmd.ExecuteNonQuery();
        }
    }

    private static void SeedCatalogManifest(string dir)
    {
        var manifestYaml = """
            name: azure-storage-account
            version: 1.0.0
            description: Standard Azure Storage Account stock module
            category: Storage
            required_terraform_version: ">= 1.9.0"
            source:
              kind: git
              locator: https://github.com/sefe/dorc-terraform-modules
              ref: main
              sub_path: modules/storage-account
              commit_sha: 0123456789abcdef0123456789abcdef01234567
            parameters:
              - name: storage_account_name
                type: String
                description: Name of the storage account
                required: true
                sensitive: false
              - name: account_replication_type
                type: String
                description: Replication strategy
                required: false
                default: LRS
                sensitive: false
              - name: access_key
                type: String
                description: Sensitive storage key
                required: false
                sensitive: true
            """;
        File.WriteAllText(Path.Join(dir, "azure-storage-account-1.0.0.yaml"), manifestYaml);
    }

    private sealed class EmbeddedApiApplicationFactory : WebApplicationFactory<Program>
    {
        private readonly string _connectionString;
        private readonly string _manifestsDir;
        private readonly Dorc.Core.AzureStorageAccount.IAzureStorageAccountWorker _azureStorageWorker;

        public EmbeddedApiApplicationFactory(
            string connectionString,
            string manifestsDir,
            Dorc.Core.AzureStorageAccount.IAzureStorageAccountWorker azureStorageWorker)
        {
            _connectionString = connectionString;
            _manifestsDir = manifestsDir;
            _azureStorageWorker = azureStorageWorker;
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:DOrcConnectionString"] = _connectionString,
                    ["AppSettings:AuthenticationScheme"] = "OAuth+WinAuth",
                    ["AppSettings:OAuth2:Authority"] = "https://identityserver.local",
                    ["AppSettings:OAuth2:ApiResourceName"] = "dorc-api-np",
                    ["AppSettings:OAuth2:ApiGlobalScope"] = "dorc-api-np.manage",
                    ["AppSettings:OAuth2:UiClientId"] = "dorc-ui-np",
                    ["AppSettings:PauseDeploymentEnabled"] = "true",
                    ["AppSettings:IsUseAdSidsForAccessControl"] = "false",
                    ["Terraform:Catalog:ManifestsDirectory"] = _manifestsDir,
                    ["Kafka:Enabled"] = "false"
                });
            });

            builder.ConfigureServices(services =>
            {
                services.Configure<AuthenticationOptions>(options =>
                {
                    options.SchemeMap[JwtBearerDefaults.AuthenticationScheme].HandlerType = typeof(HeaderBearerAuthenticationHandler);
                    options.SchemeMap[NegotiateDefaults.AuthenticationScheme].HandlerType = typeof(HeaderWindowsAuthenticationHandler);
                });
                services.AddTransient<HeaderBearerAuthenticationHandler>();
                services.AddTransient<HeaderWindowsAuthenticationHandler>();

                services.RemoveAll<Dorc.Terraform.Catalog.ITemplateCatalog>();
                services.AddSingleton<Dorc.Terraform.Catalog.ITemplateCatalog>(sp =>
                    new Dorc.Terraform.Catalog.GitTemplateCatalog(
                        _manifestsDir,
                        sp.GetRequiredService<ILogger<Dorc.Terraform.Catalog.GitTemplateCatalog>>()));

                var fakeGroupReader = Substitute.For<IUserGroupReader>();
                fakeGroupReader.GetSidsForUser(Arg.Any<string>()).Returns(call => new List<string>
                {
                    call.Arg<string>(),
                    "S-1-5-21-TEST-GROUP"
                });
                fakeGroupReader.GetUserMail(Arg.Any<string>()).Returns(call => $"{call.Arg<string>()}@sefe.eu");
                fakeGroupReader.GetUserData(Arg.Any<string>()).Returns(call => new UserElementApiModel
                {
                    Username = call.Arg<string>(),
                    DisplayName = call.Arg<string>(),
                    Pid = call.Arg<string>(),
                    Sid = call.Arg<string>(),
                    Email = $"{call.Arg<string>()}@sefe.eu"
                });

                var fakeGroupReaderFactory = Substitute.For<IUserGroupsReaderFactory>();
                fakeGroupReaderFactory.GetOAuthUserGroupsReader().Returns(fakeGroupReader);
                fakeGroupReaderFactory.GetWinAuthUserGroupsReader().Returns(fakeGroupReader);
                services.RemoveAll<IUserGroupsReaderFactory>();
                services.AddSingleton(fakeGroupReaderFactory);

                var fakeSearcher = Substitute.For<IActiveDirectorySearcher>();
                fakeSearcher.GetUserData(Arg.Any<string>()).Returns(call => new UserElementApiModel
                {
                    Username = call.Arg<string>(),
                    DisplayName = call.Arg<string>(),
                    Pid = "admin-oid",
                    Sid = "admin-oid",
                    Email = call.Arg<string>().Contains('@') ? call.Arg<string>() : $"{call.Arg<string>()}@sefe.eu",
                    IsGroup = false
                });
                fakeSearcher.GetUserDataById(Arg.Any<string>()).Returns(call => new UserElementApiModel
                {
                    Username = call.Arg<string>(),
                    DisplayName = call.Arg<string>(),
                    Pid = call.Arg<string>(),
                    Sid = call.Arg<string>(),
                    Email = "admin.user@sefe.eu",
                    IsGroup = false
                });
                fakeSearcher.Search(Arg.Any<string>()).Returns(new List<UserElementApiModel>
                {
                    new() { DisplayName = "Admin User", Username = "admin.user@sefe.eu", Pid = "admin-oid", Sid = "admin-oid", Email = "admin.user@sefe.eu" }
                });

                var fakeSearcherFactory = Substitute.For<IDirectorySearcherFactory>();
                fakeSearcherFactory.GetOAuthDirectorySearcher().Returns(fakeSearcher);
                fakeSearcherFactory.GetEntraSearcher().Returns(fakeSearcher);
                fakeSearcherFactory.GetActiveDirectorySearcher().Returns(fakeSearcher);
                services.RemoveAll<IDirectorySearcherFactory>();
                services.AddSingleton(fakeSearcherFactory);
                services.RemoveAll<IActiveDirectorySearcher>();
                services.AddSingleton(fakeSearcher);

                var fakeLogService = Substitute.For<IDeploymentLogService>();
                fakeLogService.GetLogsForSingleResult(Arg.Any<int>(), Arg.Any<int>()).Returns("Mocked OpenSearch full log");
                services.RemoveAll<IDeploymentLogService>();
                services.AddSingleton(fakeLogService);

                services.RemoveAll<Dorc.Core.AzureStorageAccount.IAzureStorageAccountWorker>();
                services.AddSingleton(_azureStorageWorker);
            });
        }
    }

    private sealed class HeaderBearerAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        public HeaderBearerAuthenticationHandler(
            IOptionsMonitor<AuthenticationSchemeOptions> options,
            ILoggerFactory logger,
            UrlEncoder encoder) : base(options, logger, encoder)
        {
        }

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue("Authorization", out var authHeader) ||
                !authHeader.ToString().StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var userId = Request.Headers.TryGetValue("X-Test-User-Id", out var idVal) ? idVal.ToString() : "admin-oid";
            var email = Request.Headers.TryGetValue("X-Test-User-Email", out var mailVal) ? mailVal.ToString() : "admin.user@sefe.eu";
            var roles = Request.Headers.TryGetValue("X-Test-User-Roles", out var roleVal)
                ? roleVal.ToString().Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                : new[] { "Admin" };

            var claims = new List<Claim>
            {
                new("oid", userId),
                new("sub", userId),
                new("preferred_username", email),
                new("email", email),
                new("name", email),
                new("scope", "dorc-api-np.manage")
            };
            foreach (var role in roles)
            {
                claims.Add(new Claim("role", role));
                claims.Add(new Claim(ClaimTypes.Role, role));
            }

            var identity = new ClaimsIdentity(claims, JwtBearerDefaults.AuthenticationScheme);
            var principal = new ClaimsPrincipal(identity);
            var ticket = new AuthenticationTicket(principal, JwtBearerDefaults.AuthenticationScheme);
            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
    }

    private sealed class HeaderWindowsAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        public HeaderWindowsAuthenticationHandler(
            IOptionsMonitor<AuthenticationSchemeOptions> options,
            ILoggerFactory logger,
            UrlEncoder encoder) : base(options, logger, encoder)
        {
        }

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue("Authorization", out var authHeader) ||
                !authHeader.ToString().StartsWith("Negotiate ", StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var username = Request.Headers.TryGetValue("X-Test-Win-User", out var userVal)
                ? userVal.ToString()
                : @"SEFE\svc-dorc-win";

            var claims = new List<Claim>
            {
                new(ClaimTypes.Name, username),
                new(ClaimTypes.PrimarySid, "S-1-5-21-WIN-TEST"),
                new(ClaimTypes.Role, "Admin")
            };

            var identity = new ClaimsIdentity(claims, NegotiateDefaults.AuthenticationScheme);
            var principal = new ClaimsPrincipal(identity);
            var ticket = new AuthenticationTicket(principal, NegotiateDefaults.AuthenticationScheme);
            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
    }
}
