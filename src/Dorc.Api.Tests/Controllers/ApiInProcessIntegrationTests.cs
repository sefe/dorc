using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Dorc.Api.Interfaces;
using Dorc.ApiModel;
using Dorc.ApiModel.MonitorRunnerApi;
using Dorc.Core.Interfaces;
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

[TestClass]
[TestCategory("Database")]
[DoNotParallelize]
public class ApiInProcessIntegrationTests
{
    private static string _masterConnectionString = null!;
    private static string _databaseName = null!;
    private static string _databaseConnectionString = null!;
    private static DeploymentContextFactory _factory = null!;
    private static string _repoRoot = null!;
    private static string _catalogManifestsDir = null!;
    private static DorcTestWebApplicationFactory _webAppFactory = null!;
    private static JsonSerializerOptions _jsonOptions = null!;

    [ClassInitialize]
    public static void Initialize(TestContext testContext)
    {
        var connection = Environment.GetEnvironmentVariable("ConnectionStrings__DOrcConnectionString")
            ?? "Server=(localdb)\\MSSQLLocalDB;Database=master;Integrated Security=True;TrustServerCertificate=True;";

        var builder = new SqlConnectionStringBuilder(connection) { InitialCatalog = "master" };
        try
        {
            using var probe = new SqlConnection(builder.ConnectionString);
            probe.Open();
        }
        catch (Exception ex)
        {
            if (Environment.GetEnvironmentVariable("DORC_DB_TESTS_REQUIRED") == "1")
                Assert.Fail($"SQL Server is required for Database category tests: {ex.Message}");
            Assert.Inconclusive($"SQL Server is not reachable ({ex.Message}).");
        }

        _masterConnectionString = builder.ConnectionString;
        _databaseName = "dorc_api_inproc_" + Guid.NewGuid().ToString("N");

        using (var sql = new SqlConnection(_masterConnectionString))
        {
            sql.Open();
            using var cmd = new SqlCommand($"CREATE DATABASE [{_databaseName}] COLLATE SQL_Latin1_General_CP1_CI_AS", sql);
            cmd.ExecuteNonQuery();
        }

        builder.InitialCatalog = _databaseName;
        _databaseConnectionString = builder.ConnectionString;
        _factory = new DeploymentContextFactory(_databaseConnectionString);

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "src", "Dorc.sln")))
        {
            dir = dir.Parent;
        }
        _repoRoot = dir!.FullName;

        using (var context = _factory.GetContext())
        {
            context.Database.EnsureCreated();
            context.Database.ExecuteSqlRaw("ALTER TABLE [deploy].[Project] ADD DEFAULT NEWID() FOR [ObjectId]");
            context.Database.ExecuteSqlRaw("IF COL_LENGTH('deploy.Environment', 'Owner') IS NULL ALTER TABLE [deploy].[Environment] ADD [Owner] NVARCHAR(100) NULL;");
            context.Database.ExecuteSqlRaw("ALTER TABLE [deploy].[DeploymentRequest] ALTER COLUMN [RequestDetails] XML NULL;");
            context.Database.ExecuteSqlRaw("DROP TABLE IF EXISTS [deploy].[RequestStatusView];");
            context.Database.ExecuteSqlRaw("DROP TABLE IF EXISTS [deploy].[DeploymentDefinitionHistoryView];");
            context.Database.ExecuteSqlRaw("CREATE SCHEMA [archive]");

            ExecuteSqlFile(Path.Combine(_repoRoot, "src", "Dorc.Database", "Schema Objects", "Schemas", "archive", "Tables", "DeploymentRequest.table.sql"));
            ExecuteSqlFile(Path.Combine(_repoRoot, "src", "Dorc.Database", "Schema Objects", "Schemas", "archive", "Tables", "DeploymentResult.table.sql"));
            ExecuteSqlFile(Path.Combine(_repoRoot, "src", "Dorc.Database", "Schema Objects", "Schemas", "archive", "Tables", "EnvironmentComponentStatus.table.sql"));
            ExecuteSqlFile(Path.Combine(_repoRoot, "src", "Dorc.Database", "Schema Objects", "Schemas", "deploy", "Views", "DeploymentDefinitionHistoryView.view.sql"));
            ExecuteSqlFile(Path.Combine(_repoRoot, "src", "Dorc.Database", "deploy", "Views", "RequestStatusView.sql"));
            ExecuteSqlFile(Path.Combine(_repoRoot, "src", "Dorc.Database", "deploy", "Functions", "SplitTagString.sql"));
            ExecuteSqlFile(Path.Combine(_repoRoot, "src", "Dorc.Database", "deploy", "Stored Procedures", "GetFullEnvironmentChain.sql"));
            ExecuteSqlFile(Path.Combine(_repoRoot, "src", "Dorc.Database", "deploy", "Stored Procedures", "DuplicateEnvironmentPropertyValues.sql"));
            ExecuteSqlFile(Path.Combine(_repoRoot, "src", "Dorc.Database", "deploy", "Stored Procedures", "get_environment_properties.sql"));
            ExecuteSqlFile(Path.Combine(_repoRoot, "src", "Dorc.Database", "deploy", "Stored Procedures", "get_global_properties.sql"));
            ExecuteSqlFile(Path.Combine(_repoRoot, "src", "Dorc.Database", "deploy", "Stored Procedures", "get_property_values_by_PropertyName.sql"));
            ExecuteSqlFile(Path.Combine(_repoRoot, "src", "Dorc.Database", "deploy", "Stored Procedures", "get_property_values_for_user.sql"));
            ExecuteSqlFile(Path.Combine(_repoRoot, "src", "Dorc.Database", "deploy", "Stored Procedures", "get_property_values_for_user_with_inheritance.sql"));
            ExecuteSqlFile(Path.Combine(_repoRoot, "src", "Dorc.Database", "Scripts", "Post-Deployment", "SeedRefDataAuditActions.sql"));

            if (!context.PropertyFilters.Any())
            {
                context.Database.ExecuteSqlRaw("SET IDENTITY_INSERT [deploy].[PropertyFilter] ON; INSERT INTO [deploy].[PropertyFilter] ([Id], [Name], [Priority]) VALUES (1, N'Environment', 1); SET IDENTITY_INSERT [deploy].[PropertyFilter] OFF;");
            }

            if (!context.SecureKeys.Any())
            {
                context.SecureKeys.Add(new SecureKey
                {
                    IV = Convert.ToBase64String(RandomNumberGenerator.GetBytes(16)),
                    Key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
                });
                context.SaveChanges();
            }
        }

        _catalogManifestsDir = Path.Combine(Path.GetTempPath(), "dorc_api_catalog_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_catalogManifestsDir);
        File.WriteAllText(Path.Combine(_catalogManifestsDir, "azure-storage-account-1.0.0.yaml"), """
            name: "azure-storage-account"
            version: "1.0.0"
            description: "Provisions an Azure Storage Account"
            category: "Storage"
            source:
              kind: "git"
              locator: "https://github.com/sefe/dorc-terraform-modules.git"
              ref: "azure-storage-account/v1.0.0"
              sub_path: "modules/azure-storage-account"
              commit_sha: "0123456789abcdef0123456789abcdef01234567"
            parameters:
              - name: "storage_account_name"
                type: "String"
                required: true
                sensitive: false
                description: "Storage account name"
              - name: "account_replication_type"
                type: "String"
                required: false
                default: "LRS"
                sensitive: false
                description: "Replication type"
              - name: "access_key"
                type: "String"
                required: false
                sensitive: true
                description: "Optional sensitive key"
            """);

        _webAppFactory = new DorcTestWebApplicationFactory(_databaseConnectionString, _catalogManifestsDir);
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            IncludeFields = true,
            MaxDepth = 64,
            Converters = { new JsonStringEnumConverter() }
        };
    }

    [ClassCleanup]
    public static void Cleanup()
    {
        _webAppFactory?.Dispose();

        if (!string.IsNullOrEmpty(_catalogManifestsDir) && Directory.Exists(_catalogManifestsDir))
        {
            try { Directory.Delete(_catalogManifestsDir, true); } catch { }
        }

        if (!string.IsNullOrEmpty(_databaseName) && !string.IsNullOrEmpty(_masterConnectionString))
        {
            using var sql = new SqlConnection(_masterConnectionString);
            sql.Open();
            using var cmd = new SqlCommand($"ALTER DATABASE [{_databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{_databaseName}]", sql);
            cmd.ExecuteNonQuery();
        }
    }

    private static void ExecuteSqlFile(string filePath)
    {
        var script = File.ReadAllText(filePath);
        var batches = Regex.Split(script, @"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase);
        using var sql = new SqlConnection(_databaseConnectionString);
        sql.Open();
        foreach (var batch in batches)
        {
            if (string.IsNullOrWhiteSpace(batch))
                continue;
            using var cmd = new SqlCommand(batch, sql);
            cmd.ExecuteNonQuery();
        }
    }

    private static HttpClient CreateClientForUser(
        string userId = "admin-oid",
        string userName = "admin.user@sefe.eu",
        string role = "Admin",
        string? scope = "dorc-api-np.manage",
        bool useBearer = true)
    {
        var client = _webAppFactory.CreateClient();
        if (useBearer)
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "integration-test-token");
        }
        client.DefaultRequestHeaders.Add("X-Test-User-Id", userId);
        client.DefaultRequestHeaders.Add("X-Test-User-Name", userName);
        if (!string.IsNullOrEmpty(role))
        {
            client.DefaultRequestHeaders.Add("X-Test-Role", role);
        }
        if (scope != null)
        {
            client.DefaultRequestHeaders.Add("X-Test-Scope", scope);
        }
        return client;
    }

    [TestMethod]
    public async Task CombinedAuthPolicy_RequiresApiGlobalScopeForBearerTokens_AndAdmitsWinAuthWithoutScope()
    {
        // 1. Bearer token WITH valid scope succeeds
        using var validBearerClient = CreateClientForUser(scope: "dorc-api-np.manage", useBearer: true);
        var okResp = await validBearerClient.GetAsync("/Metadata");
        Assert.AreEqual(HttpStatusCode.OK, okResp.StatusCode);

        // 2. Bearer token WITHOUT valid scope is rejected with 403 Forbidden by ApiGlobalScopeAuthorizationPolicy
        using var wrongScopeClient = CreateClientForUser(scope: "wrong.scope", useBearer: true);
        var forbiddenResp = await wrongScopeClient.GetAsync("/Metadata");
        Assert.AreEqual(HttpStatusCode.Forbidden, forbiddenResp.StatusCode);

        // 3. Windows-authenticated request (no Bearer header, no scope claim) is admitted by combined policy
        using var winAuthClient = CreateClientForUser(userName: "DOMAIN\\winuser", scope: "", useBearer: false);
        var winAuthResp = await winAuthClient.GetAsync("/Metadata");
        Assert.AreEqual(HttpStatusCode.OK, winAuthResp.StatusCode);
    }

    [TestMethod]
    public async Task EnvironmentHierarchy_ParentChildAttachment_PropertyInheritance_AndCycleRejection_WorkEndToEnd()
    {
        using var adminClient = CreateClientForUser("admin-oid", "admin.user@sefe.eu", "Admin");

        var parentName = "EnvParent_" + Guid.NewGuid().ToString("N")[..8];
        var childName = "EnvChild_" + Guid.NewGuid().ToString("N")[..8];

        // Create parent environment
        var createParentResp = await adminClient.PostAsJsonAsync("/RefDataEnvironments", new EnvironmentApiModel
        {
            EnvironmentName = parentName,
            EnvironmentIsProd = false,
            EnvironmentSecure = false,
            Details = new EnvironmentDetailsApiModel { EnvironmentOwner = "admin.user@sefe.eu", Description = "Parent env" }
        }, _jsonOptions);
        Assert.AreEqual(HttpStatusCode.OK, createParentResp.StatusCode);
        var parentEnv = await createParentResp.Content.ReadFromJsonAsync<EnvironmentApiModel>(_jsonOptions);
        Assert.IsNotNull(parentEnv);
        Assert.IsTrue(parentEnv.EnvironmentId > 0);

        // Create child environment
        var createChildResp = await adminClient.PostAsJsonAsync("/RefDataEnvironments", new EnvironmentApiModel
        {
            EnvironmentName = childName,
            EnvironmentIsProd = false,
            EnvironmentSecure = false,
            Details = new EnvironmentDetailsApiModel { EnvironmentOwner = "admin.user@sefe.eu", Description = "Child env" }
        }, _jsonOptions);
        Assert.AreEqual(HttpStatusCode.OK, createChildResp.StatusCode);
        var childEnv = await createChildResp.Content.ReadFromJsonAsync<EnvironmentApiModel>(_jsonOptions);
        Assert.IsNotNull(childEnv);
        Assert.IsTrue(childEnv.EnvironmentId > 0);

        // Verify setting parent during POST creation is rejected with 400
        var invalidCreateWithParent = await adminClient.PostAsJsonAsync("/RefDataEnvironments", new EnvironmentApiModel
        {
            EnvironmentName = "InvalidParentOnCreate_" + Guid.NewGuid().ToString("N")[..6],
            ParentId = parentEnv.EnvironmentId,
            Details = new EnvironmentDetailsApiModel { EnvironmentOwner = "admin.user@sefe.eu" }
        }, _jsonOptions);
        Assert.AreEqual(HttpStatusCode.BadRequest, invalidCreateWithParent.StatusCode);

        // Attach child to parent via SetParentForEnvironment
        var setParentResp = await adminClient.PutAsync(
            $"/RefDataEnvironmentsDetails/SetParentForEnvironment?parentEnvId={parentEnv.EnvironmentId}&childEnvId={childEnv.EnvironmentId}",
            null);
        Assert.AreEqual(HttpStatusCode.OK, setParentResp.StatusCode);
        var setParentResult = await setParentResp.Content.ReadFromJsonAsync<ApiBoolResult>(_jsonOptions);
        Assert.IsNotNull(setParentResult);
        Assert.IsTrue(setParentResult.Result, setParentResult.Message);

        // Attempt to create a cycle (setting parentEnv's parent to childEnv) -> rejected
        var cycleResp = await adminClient.PutAsync(
            $"/RefDataEnvironmentsDetails/SetParentForEnvironment?parentEnvId={childEnv.EnvironmentId}&childEnvId={parentEnv.EnvironmentId}",
            null);
        var cycleResult = await cycleResp.Content.ReadFromJsonAsync<ApiBoolResult>(_jsonOptions);
        Assert.IsNotNull(cycleResult);
        Assert.IsFalse(cycleResult.Result);

        // Seed properties on Parent and Child and verify inheritance via /PropertyValues
        var inheritedPropName = "InheritedProp_" + Guid.NewGuid().ToString("N")[..8];
        var overriddenPropName = "OverriddenProp_" + Guid.NewGuid().ToString("N")[..8];
        var secureArrayPropName = "SecureArrayProp_" + Guid.NewGuid().ToString("N")[..8];

        var createPropsResp = await adminClient.PostAsJsonAsync("/Properties", new[]
        {
            new PropertyApiModel { Name = inheritedPropName, Secure = false, IsArray = false },
            new PropertyApiModel { Name = overriddenPropName, Secure = false, IsArray = false },
            new PropertyApiModel { Name = secureArrayPropName, Secure = true, IsArray = true }
        }, _jsonOptions);
        Assert.AreEqual(HttpStatusCode.OK, createPropsResp.StatusCode);

        var postValuesResp = await adminClient.PostAsJsonAsync("/PropertyValues", new[]
        {
            new PropertyValueDto
            {
                Property = new PropertyApiModel { Name = inheritedPropName, Secure = false },
                PropertyValueFilter = parentName,
                Value = "from-parent"
            },
            new PropertyValueDto
            {
                Property = new PropertyApiModel { Name = overriddenPropName, Secure = false },
                PropertyValueFilter = parentName,
                Value = "parent-default"
            },
            new PropertyValueDto
            {
                Property = new PropertyApiModel { Name = overriddenPropName, Secure = false },
                PropertyValueFilter = childName,
                Value = "child-override"
            },
            new PropertyValueDto
            {
                Property = new PropertyApiModel { Name = secureArrayPropName, Secure = true, IsArray = true },
                PropertyValueFilter = childName,
                Value = "[\"sec1\",\"sec2\"]"
            }
        }, _jsonOptions);
        Assert.AreEqual(HttpStatusCode.OK, postValuesResp.StatusCode);

        // Query child environment properties: should inherit inheritedPropName from parent and override overriddenPropName on child
        var childInheritedResp = await adminClient.GetFromJsonAsync<PropertyValueDto[]>(
            $"/PropertyValues?propertyName={inheritedPropName}&environmentName={childName}", _jsonOptions);
        Assert.IsNotNull(childInheritedResp);
        Assert.AreEqual(1, childInheritedResp.Length);
        Assert.AreEqual("from-parent", childInheritedResp[0].Value);
        Assert.AreEqual(parentName, childInheritedResp[0].PropertyValueFilter);

        var childOverrideResp = await adminClient.GetFromJsonAsync<PropertyValueDto[]>(
            $"/PropertyValues?propertyName={overriddenPropName}&environmentName={childName}", _jsonOptions);
        Assert.IsNotNull(childOverrideResp);
        Assert.AreEqual(1, childOverrideResp.Length);
        Assert.AreEqual("child-override", childOverrideResp[0].Value);
        Assert.AreEqual(childName, childOverrideResp[0].PropertyValueFilter);

        // Non-secure CSV export should include non-secure properties and exclude secure properties
        var csvExportResp = await adminClient.GetAsync($"/PropertyValues/non-secure-export?environmentName={childName}");
        Assert.AreEqual(HttpStatusCode.OK, csvExportResp.StatusCode);
        var csvText = await csvExportResp.Content.ReadAsStringAsync();
        StringAssert.Contains(csvText, inheritedPropName);
        StringAssert.Contains(csvText, overriddenPropName);
        Assert.IsFalse(csvText.Contains(secureArrayPropName, StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(csvText.Contains("sec1", StringComparison.OrdinalIgnoreCase));

        // Scoped property values endpoint masks secure property values
        var scopedResp = await adminClient.PutAsJsonAsync(
            $"/RefDataScopedPropertyValues?scope={childName}&page=1&limit=50",
            new PagedDataOperators(),
            _jsonOptions);
        Assert.AreEqual(HttpStatusCode.OK, scopedResp.StatusCode);
        var scopedDto = await scopedResp.Content.ReadFromJsonAsync<GetScopedPropertyValuesResponseDto>(_jsonOptions);
        Assert.IsNotNull(scopedDto);
        var maskedProp = scopedDto.Items.Single(i => i.Property == secureArrayPropName);
        Assert.IsTrue(maskedProp.Secure);
        Assert.AreEqual(string.Empty, maskedProp.PropertyValue);
    }

    [TestMethod]
    public async Task AccessControlAndPermissions_EnforceOwnerInvariants_CrossObjectIdorProtection_AndRoleChecks()
    {
        using var adminClient = CreateClientForUser("admin-oid", "admin.user@sefe.eu", "Admin");
        using var regularUserClient = CreateClientForUser("regular-oid", "regular.user@sefe.eu", "User");

        var envAName = "AclEnvA_" + Guid.NewGuid().ToString("N")[..8];
        var envBName = "AclEnvB_" + Guid.NewGuid().ToString("N")[..8];

        var envAResp = await adminClient.PostAsJsonAsync("/RefDataEnvironments", new EnvironmentApiModel
        {
            EnvironmentName = envAName,
            Details = new EnvironmentDetailsApiModel { EnvironmentOwner = "admin.user@sefe.eu" }
        }, _jsonOptions);
        var envA = await envAResp.Content.ReadFromJsonAsync<EnvironmentApiModel>(_jsonOptions);

        var envBResp = await adminClient.PostAsJsonAsync("/RefDataEnvironments", new EnvironmentApiModel
        {
            EnvironmentName = envBName,
            Details = new EnvironmentDetailsApiModel { EnvironmentOwner = "admin.user@sefe.eu" }
        }, _jsonOptions);
        var envB = await envBResp.Content.ReadFromJsonAsync<EnvironmentApiModel>(_jsonOptions);

        Assert.IsNotNull(envA);
        Assert.IsNotNull(envB);

        // Fetch ACL for EnvA and EnvB
        var aclA = await adminClient.GetFromJsonAsync<AccessSecureApiModel>(
            $"/AccessControl?accessControlType=Environment&accessControlName={envAName}", _jsonOptions);
        var aclB = await adminClient.GetFromJsonAsync<AccessSecureApiModel>(
            $"/AccessControl?accessControlType=Environment&accessControlName={envBName}", _jsonOptions);
        Assert.IsNotNull(aclA);
        Assert.IsNotNull(aclB);

        // 1. Removing all owners from an environment is rejected with 400 BadRequest
        var removeAllOwnersPayload = new AccessSecureApiModel
        {
            Name = envAName,
            ObjectId = aclA.ObjectId,
            Type = AccessControlType.Environment,
            Privileges = Array.Empty<AccessControlApiModel>()
        };
        var noOwnersResp = await adminClient.PutAsJsonAsync("/AccessControl", removeAllOwnersPayload, _jsonOptions);
        Assert.AreEqual(HttpStatusCode.BadRequest, noOwnersResp.StatusCode);

        // 2. Cross-object ACL entry ID tampering is rejected with 400 BadRequest
        var envBOwnerEntry = aclB.Privileges.First();
        var crossObjectPayload = new AccessSecureApiModel
        {
            Name = envAName,
            ObjectId = aclB.ObjectId, // even if caller tries to swap ObjectId
            Type = AccessControlType.Environment,
            Privileges = new[]
            {
                aclA.Privileges.First(),
                new AccessControlApiModel
                {
                    Id = envBOwnerEntry.Id,
                    Name = envBOwnerEntry.Name,
                    Sid = envBOwnerEntry.Sid,
                    Pid = envBOwnerEntry.Pid,
                    Allow = 4,
                    Deny = 0
                }
            }
        };
        var crossObjectResp = await adminClient.PutAsJsonAsync("/AccessControl", crossObjectPayload, _jsonOptions);
        Assert.AreEqual(HttpStatusCode.BadRequest, crossObjectResp.StatusCode);

        // 3. Regular user without write/owner rights cannot edit ACLs (403 Forbidden)
        var regularUserPutResp = await regularUserClient.PutAsJsonAsync("/AccessControl", aclA, _jsonOptions);
        Assert.AreEqual(HttpStatusCode.Forbidden, regularUserPutResp.StatusCode);

        // 4. Non-Admin cannot create Production environment (403 Forbidden)
        using var powerUserClient = CreateClientForUser("power-oid", "power.user@sefe.eu", "PowerUser");
        var prodCreateResp = await powerUserClient.PostAsJsonAsync("/RefDataEnvironments", new EnvironmentApiModel
        {
            EnvironmentName = "ProdEnv_" + Guid.NewGuid().ToString("N")[..6],
            EnvironmentIsProd = true,
            Details = new EnvironmentDetailsApiModel { EnvironmentOwner = "power.user@sefe.eu" }
        }, _jsonOptions);
        Assert.AreEqual(HttpStatusCode.Forbidden, prodCreateResp.StatusCode);

        // 5. RefDataPermission CRUD role checks
        var permName = "CustomPerm_" + Guid.NewGuid().ToString("N")[..6];
        var regularPermResp = await regularUserClient.PostAsJsonAsync("/RefDataPermission", new PermissionDto
        {
            DisplayName = permName,
            PermissionName = permName
        }, _jsonOptions);
        Assert.AreEqual(HttpStatusCode.Forbidden, regularPermResp.StatusCode);

        var adminPermResp = await adminClient.PostAsJsonAsync("/RefDataPermission", new PermissionDto
        {
            DisplayName = permName,
            PermissionName = permName
        }, _jsonOptions);
        Assert.AreEqual(HttpStatusCode.OK, adminPermResp.StatusCode);
    }

    [TestMethod]
    public async Task TerraformCatalog_TemplateDiscovery_Instantiation_SavedConfiguration_AndDeploymentDefinitions_WorkEndToEnd()
    {
        using var adminClient = CreateClientForUser("admin-oid", "admin.user@sefe.eu", "Admin");

        // 1. Discover templates from catalog
        var templates = await adminClient.GetFromJsonAsync<List<Dorc.Terraform.Catalog.TerraformTemplateManifest>>(
            "/Terraform/templates", _jsonOptions);
        Assert.IsNotNull(templates);
        Assert.IsTrue(templates.Any(t => t.Name == "azure-storage-account" && t.Version == "1.0.0"));

        var latestTemplate = await adminClient.GetFromJsonAsync<Dorc.Terraform.Catalog.TerraformTemplateManifest>(
            "/Terraform/templates/azure-storage-account", _jsonOptions);
        Assert.IsNotNull(latestTemplate);
        Assert.AreEqual("1.0.0", latestTemplate.Version);

        // 2. Create project and environment, and map them
        var projectName = "CatProj_" + Guid.NewGuid().ToString("N")[..8];
        var envName = "CatEnv_" + Guid.NewGuid().ToString("N")[..8];

        var envResp = await adminClient.PostAsJsonAsync("/RefDataEnvironments", new EnvironmentApiModel
        {
            EnvironmentName = envName,
            Details = new EnvironmentDetailsApiModel { EnvironmentOwner = "admin.user@sefe.eu" }
        }, _jsonOptions);
        Assert.AreEqual(HttpStatusCode.OK, envResp.StatusCode);
        var env = await envResp.Content.ReadFromJsonAsync<EnvironmentApiModel>(_jsonOptions);
        Assert.IsNotNull(env);

        var createProjResp = await adminClient.PostAsJsonAsync("/RefData", new RefDataApiModel
        {
            Project = new ProjectApiModel
            {
                ProjectName = projectName,
                ProjectDescription = "Catalog E2E project",
                ArtefactsUrl = "https://github.com/sefe/dorc-terraform-modules",
                ArtefactsSubPaths = ".github/workflows/ci.yml",
                ArtefactsBuildRegex = ".*",
                SourceControlType = SourceControlType.GitHub,
                TerraformStateBackends = new List<TerraformStateBackendApiModel>
                {
                    new()
                    {
                        SubscriptionId = Guid.NewGuid().ToString(),
                        ResourceGroup = "rg-tfstate",
                        StorageAccount = "sttfstate01",
                        ContainerName = "tfstate"
                    }
                }
            },
            Components = new List<ComponentApiModel>()
        }, _jsonOptions);
        Assert.AreEqual(HttpStatusCode.OK, createProjResp.StatusCode);

        var mapResp = await adminClient.PostAsync(
            $"/RefDataProjectEnvironmentMappings?project={Uri.EscapeDataString(projectName)}&environment={Uri.EscapeDataString(envName)}",
            null);
        Assert.AreEqual(HttpStatusCode.OK, mapResp.StatusCode);

        int projectId;
        using (var context = _factory.GetContext())
        {
            projectId = context.Projects.Single(p => p.Name == projectName).Id;
        }

        // 3. Instantiate template in create-only mode
        var instantiateResp = await adminClient.PostAsJsonAsync(
            "/Terraform/templates/azure-storage-account/1.0.0/instantiate",
            new TerraformTemplateInstantiateRequestApiModel
            {
                ProjectId = projectId,
                ComponentName = "storage_acct_comp"
            }, _jsonOptions);
        Assert.AreEqual(HttpStatusCode.OK, instantiateResp.StatusCode);
        var instantiated = await instantiateResp.Content.ReadFromJsonAsync<TerraformTemplateInstantiateResponseApiModel>(_jsonOptions);
        Assert.IsNotNull(instantiated);
        Assert.IsNotNull(instantiated.Component);
        Assert.IsTrue(instantiated.Component.ComponentId > 0);
        Assert.AreEqual(ComponentType.Terraform, instantiated.Component.ComponentType);
        Assert.AreEqual(TerraformSourceType.Catalog, instantiated.Component.TerraformSourceType);

        var componentId = instantiated.Component.ComponentId!.Value;

        // 4. Save environment inputs with optimistic concurrency
        var saveConfigResp = await adminClient.PutAsJsonAsync(
            $"/Terraform/templates/azure-storage-account/1.0.0/configuration?projectId={projectId}&componentId={componentId}&environmentName={Uri.EscapeDataString(envName)}",
            new SaveCatalogEnvironmentInputsApiModel
            {
                ExpectedRevision = 0,
                Inputs = new Dictionary<string, string>
                {
                    ["storage_account_name"] = "stdorc" + Guid.NewGuid().ToString("N")[..10],
                    ["account_replication_type"] = "GRS"
                }
            }, _jsonOptions);
        Assert.AreEqual(HttpStatusCode.OK, saveConfigResp.StatusCode);
        var savedConfig = await saveConfigResp.Content.ReadFromJsonAsync<CatalogEnvironmentInputsApiModel>(_jsonOptions);
        Assert.IsNotNull(savedConfig);
        Assert.AreEqual(1, savedConfig.Revision);

        // Stale revision (ExpectedRevision = 0 instead of 1) must return 409 Conflict
        var staleSaveResp = await adminClient.PutAsJsonAsync(
            $"/Terraform/templates/azure-storage-account/1.0.0/configuration?projectId={projectId}&componentId={componentId}&environmentName={Uri.EscapeDataString(envName)}",
            new SaveCatalogEnvironmentInputsApiModel
            {
                ExpectedRevision = 0,
                Inputs = new Dictionary<string, string> { ["storage_account_name"] = "stdorcstale01" }
            }, _jsonOptions);
        Assert.AreEqual(HttpStatusCode.Conflict, staleSaveResp.StatusCode);

        // Saving sensitive input to CatalogEnvironmentInputs must be rejected with 400 BadRequest
        var sensitiveSaveResp = await adminClient.PutAsJsonAsync(
            $"/Terraform/templates/azure-storage-account/1.0.0/configuration?projectId={projectId}&componentId={componentId}&environmentName={Uri.EscapeDataString(envName)}",
            new SaveCatalogEnvironmentInputsApiModel
            {
                ExpectedRevision = 1,
                Inputs = new Dictionary<string, string> { ["access_key"] = "plain-secret-key" }
            }, _jsonOptions);
        Assert.AreEqual(HttpStatusCode.BadRequest, sensitiveSaveResp.StatusCode);

        // 5. Verify resolution endpoint reflects SavedConfiguration
        var resolutionResp = await adminClient.GetFromJsonAsync<TerraformTemplateResolutionApiModel>(
            $"/Terraform/templates/azure-storage-account/1.0.0/resolution?projectId={projectId}&environmentName={Uri.EscapeDataString(envName)}&componentId={componentId}",
            _jsonOptions);
        Assert.IsNotNull(resolutionResp);
        Assert.AreEqual(1, resolutionResp.SavedConfiguration.Revision);
        var storageNameParam = resolutionResp.Parameters.Single(p => p.Name == "storage_account_name");
        Assert.AreEqual(TerraformParameterResolutionStatus.SavedConfiguration, storageNameParam.Status);

        // 6. Verify DeploymentDefinitions endpoint lists the Catalog component and its history
        var definitionsResp = await adminClient.GetFromJsonAsync<GetDeploymentDefinitionsResponse>(
            $"/DeploymentDefinitions?projectId={projectId}&componentType=Terraform&sourceType=Catalog", _jsonOptions);
        Assert.IsNotNull(definitionsResp);
        Assert.AreEqual(1, definitionsResp.TotalItems);
        Assert.AreEqual("Catalog azure-storage-account@1.0.0", definitionsResp.Items.Single().SourceSummary);

        var historyResp = await adminClient.GetFromJsonAsync<GetDeploymentDefinitionHistoryResponse>(
            $"/DeploymentDefinitions/history?projectId={projectId}", _jsonOptions);
        Assert.IsNotNull(historyResp);
        Assert.IsTrue(historyResp.TotalItems >= 1);
    }

    [TestMethod]
    public async Task RequestLifecycle_PauseResumeCancelRestart_AndSecretRedaction_WorkEndToEnd()
    {
        using var adminClient = CreateClientForUser("admin-oid", "admin.user@sefe.eu", "Admin");

        var envName = "ReqLifecycleEnv_" + Guid.NewGuid().ToString("N")[..8];
        var projName = "ReqLifecycleProj_" + Guid.NewGuid().ToString("N")[..8];

        await adminClient.PostAsJsonAsync("/RefDataEnvironments", new EnvironmentApiModel
        {
            EnvironmentName = envName,
            Details = new EnvironmentDetailsApiModel { EnvironmentOwner = "admin.user@sefe.eu" }
        }, _jsonOptions);

        int requestId;
        int resultId;
        using (var context = _factory.GetContext())
        {
            var proj = new Project { Name = projName, ObjectId = Guid.NewGuid(), ArtefactsSubPaths = "" };
            var comp = new Component { Name = "LifecycleComp_" + Guid.NewGuid().ToString("N")[..6], IsEnabled = true };
            context.Projects.Add(proj);
            context.Components.Add(comp);
            context.SaveChanges();

            var req = new DeploymentRequest
            {
                RequestDetails = """
                    <DeploymentRequestDetail xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" xmlns:xsd="http://www.w3.org/2001/XMLSchema">
                      <EnvironmentName>
                    """ + envName + """
                    </EnvironmentName>
                      <BuildDetail>
                        <Project>
                    """ + projName + """
                    </Project>
                        <BuildNumber>1.0.42</BuildNumber>
                      </BuildDetail>
                      <Components>
                        <string>
                    """ + comp.Name + """
                    </string>
                      </Components>
                      <Properties>
                        <PropertyPair>
                          <Name>ApiPassword</Name>
                          <Value>SuperSecretCleartext123!</Value>
                          <IsSensitive>true</IsSensitive>
                        </PropertyPair>
                      </Properties>
                    </DeploymentRequestDetail>
                    """,
                UserName = "admin.user@sefe.eu",
                RequestedTime = DateTimeOffset.UtcNow,
                Status = DeploymentRequestStatus.Pending.ToString(),
                IsProd = false,
                Project = projName,
                Environment = envName,
                BuildNumber = "1.0.42",
                Components = comp.Name
            };
            context.DeploymentRequests.Add(req);
            context.SaveChanges();
            requestId = req.Id;

            var res = new DeploymentResult
            {
                DeploymentRequest = req,
                Component = comp,
                Status = DeploymentResultStatus.Pending.ToString(),
                Log = "Initial component log"
            };
            context.DeploymentResults.Add(res);
            context.SaveChanges();
            resultId = res.Id;
        }

        // 1. RequestStatuses GET and PUT redact <Properties> XML values at the emission boundary
        var statusGetResp = await adminClient.GetFromJsonAsync<DeploymentRequestApiModel>(
            $"/RequestStatuses?requestId={requestId}", _jsonOptions);
        Assert.IsNotNull(statusGetResp);
        Assert.IsFalse(statusGetResp.RequestDetails.Contains("SuperSecretCleartext123!", StringComparison.Ordinal));

        var statusListResp = await adminClient.PutAsJsonAsync(
            "/RequestStatuses?page=1&limit=50",
            new PagedDataOperators
            {
                Filters = new List<PagedDataFilter>
                {
                    new() { Path = "Id", FilterValue = requestId.ToString() }
                }
            }, _jsonOptions);
        Assert.AreEqual(HttpStatusCode.OK, statusListResp.StatusCode);
        var statusList = await statusListResp.Content.ReadFromJsonAsync<GetRequestStatusesListResponseDto>(_jsonOptions);
        Assert.IsNotNull(statusList);
        var listedReq = statusList.Items.Single(i => i.Id == requestId);
        Assert.IsFalse(listedReq.RequestDetails.Contains("SuperSecretCleartext123!", StringComparison.Ordinal));

        // 2. Pause -> Paused
        var pauseResp = await adminClient.PutAsync($"/Request/pause?requestId={requestId}", null);
        Assert.AreEqual(HttpStatusCode.OK, pauseResp.StatusCode);
        var pausedDto = await pauseResp.Content.ReadFromJsonAsync<RequestStatusDto>(_jsonOptions);
        Assert.AreEqual(DeploymentRequestStatus.Paused.ToString(), pausedDto!.Status);

        // 3. Resume -> Pending
        var resumeResp = await adminClient.PutAsync($"/Request/resume?requestId={requestId}", null);
        Assert.AreEqual(HttpStatusCode.OK, resumeResp.StatusCode);
        var resumedDto = await resumeResp.Content.ReadFromJsonAsync<RequestStatusDto>(_jsonOptions);
        Assert.AreEqual(DeploymentRequestStatus.Pending.ToString(), resumedDto!.Status);

        // 4. Cancel Pending request -> Cancelled
        var cancelResp = await adminClient.PutAsync($"/Request/cancel?requestId={requestId}", null);
        Assert.AreEqual(HttpStatusCode.OK, cancelResp.StatusCode);
        var cancelledDto = await cancelResp.Content.ReadFromJsonAsync<RequestStatusDto>(_jsonOptions);
        Assert.AreEqual(DeploymentRequestStatus.Cancelled.ToString(), cancelledDto!.Status);

        // 5. Restart Cancelled request -> archives attempt and sets status to Restarting
        var restartResp = await adminClient.PostAsync($"/Request/restart?requestId={requestId}", null);
        Assert.AreEqual(HttpStatusCode.OK, restartResp.StatusCode);
        var restartedDto = await restartResp.Content.ReadFromJsonAsync<RequestStatusDto>(_jsonOptions);
        Assert.AreEqual(DeploymentRequestStatus.Restarting.ToString(), restartedDto!.Status);

        // 6. Verify attempts history was archived and is retrievable via /Request/{id}/attempts
        var attemptsResp = await adminClient.GetFromJsonAsync<List<DeploymentRequestAttemptApiModel>>(
            $"/Request/{requestId}/attempts", _jsonOptions);
        Assert.IsNotNull(attemptsResp);
        Assert.AreEqual(1, attemptsResp.Count);

        // 7. ResultStatuses returns component results enriched with log preview
        var resultsResp = await adminClient.GetFromJsonAsync<List<DeploymentResultApiModel>>(
            $"/ResultStatuses?requestId={requestId}", _jsonOptions);
        Assert.IsNotNull(resultsResp);
        Assert.AreEqual(1, resultsResp.Count);
        Assert.AreEqual(resultId, resultsResp[0].Id);
    }

    private sealed class DorcTestWebApplicationFactory : WebApplicationFactory<Program>
    {
        private readonly string _connectionString;
        private readonly string _manifestsDir;

        public DorcTestWebApplicationFactory(string connectionString, string manifestsDir)
        {
            _connectionString = connectionString;
            _manifestsDir = manifestsDir;
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
                // Replace authentication handlers for JwtBearer and Negotiate with deterministic test handlers
                services.Configure<AuthenticationOptions>(options =>
                {
                    options.SchemeMap[JwtBearerDefaults.AuthenticationScheme].HandlerType = typeof(TestBearerAuthHandler);
                    options.SchemeMap[NegotiateDefaults.AuthenticationScheme].HandlerType = typeof(TestWinAuthHandler);
                });
                services.AddTransient<TestBearerAuthHandler>();
                services.AddTransient<TestWinAuthHandler>();

                services.RemoveAll<Dorc.Terraform.Catalog.ITemplateCatalog>();
                services.AddSingleton<Dorc.Terraform.Catalog.ITemplateCatalog>(sp =>
                    new Dorc.Terraform.Catalog.GitTemplateCatalog(
                        _manifestsDir,
                        sp.GetRequiredService<ILogger<Dorc.Terraform.Catalog.GitTemplateCatalog>>()));

                // Mock external AD/Entra directory & group readers so tests never touch live AD/Entra
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

                // Replace OpenSearch log service with deterministic mock
                var fakeLogService = Substitute.For<IDeploymentLogService>();
                fakeLogService.GetLogsForSingleResult(Arg.Any<int>(), Arg.Any<int>()).Returns("Mocked OpenSearch full log");
                services.RemoveAll<IDeploymentLogService>();
                services.AddSingleton(fakeLogService);

                var fakeAzureStorageWorker = Substitute.For<Dorc.Core.AzureStorageAccount.IAzureStorageAccountWorker>();
                services.RemoveAll<Dorc.Core.AzureStorageAccount.IAzureStorageAccountWorker>();
                services.AddSingleton(fakeAzureStorageWorker);
            });
        }
    }

    private sealed class TestBearerAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        public TestBearerAuthHandler(
            IOptionsMonitor<AuthenticationSchemeOptions> options,
            ILoggerFactory logger,
            UrlEncoder encoder) : base(options, logger, encoder)
        {
        }

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var userId = Request.Headers["X-Test-User-Id"].FirstOrDefault() ?? "admin-oid";
            var userName = Request.Headers["X-Test-User-Name"].FirstOrDefault() ?? "admin.user@sefe.eu";
            var role = Request.Headers["X-Test-Role"].FirstOrDefault();
            var scope = Request.Headers["X-Test-Scope"].FirstOrDefault() ?? "dorc-api-np.manage";

            var claims = new List<Claim>
            {
                new(ClaimTypes.Name, userName),
                new("name", userName),
                new("email", userName),
                new("oid", userId),
                new("samAccountName", userId)
            };

            if (!string.IsNullOrEmpty(role))
            {
                claims.Add(new Claim(ClaimTypes.Role, role));
                claims.Add(new Claim("role", role));
            }

            if (!string.IsNullOrEmpty(scope))
            {
                claims.Add(new Claim("scope", scope));
            }

            var identity = new ClaimsIdentity(claims, JwtBearerDefaults.AuthenticationScheme);
            var principal = new ClaimsPrincipal(identity);
            var ticket = new AuthenticationTicket(principal, JwtBearerDefaults.AuthenticationScheme);
            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
    }

    private sealed class TestWinAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        public TestWinAuthHandler(
            IOptionsMonitor<AuthenticationSchemeOptions> options,
            ILoggerFactory logger,
            UrlEncoder encoder) : base(options, logger, encoder)
        {
        }

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var userName = Request.Headers["X-Test-User-Name"].FirstOrDefault() ?? "DOMAIN\\winuser";
            var role = Request.Headers["X-Test-Role"].FirstOrDefault() ?? "Admin";

            var claims = new List<Claim>
            {
                new(ClaimTypes.Name, userName)
            };
            if (!string.IsNullOrEmpty(role))
            {
                claims.Add(new Claim(ClaimTypes.Role, role));
            }

            var identity = new ClaimsIdentity(claims, NegotiateDefaults.AuthenticationScheme);
            var principal = new ClaimsPrincipal(identity);
            var ticket = new AuthenticationTicket(principal, NegotiateDefaults.AuthenticationScheme);
            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
    }
}
