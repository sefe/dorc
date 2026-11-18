using System.Net;
using System.Text;
using System.Text.Json;
using Dorc.ApiModel;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

[assembly: Parallelize(Scope = ExecutionScope.MethodLevel)]

namespace Dorc.McpServer.Tests;

internal sealed class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _responder;
    private readonly List<HttpResponseMessage> _createdResponses = [];

    public StubHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder)
    {
        _responder = responder;
    }

    public List<HttpRequestMessage> SentRequests { get; } = [];

    public static HttpResponseMessage CreateJsonResponse(string json, HttpStatusCode statusCode = HttpStatusCode.OK)
    {
        return new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }

    public static HttpResponseMessage CreateTextResponse(
        string text,
        HttpStatusCode statusCode = HttpStatusCode.OK,
        string mediaType = "text/plain",
        string? reasonPhrase = null)
    {
        var response = new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(text, Encoding.UTF8, mediaType)
        };
        if (reasonPhrase is not null)
        {
            response.ReasonPhrase = reasonPhrase;
        }

        return response;
    }

    public static HttpResponseMessage CreateStatusResponse(HttpStatusCode statusCode)
    {
        return new HttpResponseMessage(statusCode);
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        SentRequests.Add(request);
        var response = await _responder(request, cancellationToken).ConfigureAwait(false);
        _createdResponses.Add(response);
        return response;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            foreach (var response in _createdResponses)
            {
                response.Dispose();
            }

            _createdResponses.Clear();
        }

        base.Dispose(disposing);
    }
}

internal sealed class FakeTimeProvider : TimeProvider
{
    private DateTimeOffset _utcNow = new(2026, 4, 20, 12, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => _utcNow;

    public void Advance(TimeSpan delta) => _utcNow = _utcNow.Add(delta);
}

[TestClass]
public sealed class InfrastructureAndSecurityTests
{
    [TestMethod]
    public void Options_And_Outcome_Properties_WorkAsExpected()
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
            EndpointPath = "/custom-mcp"
        };
        Assert.IsFalse(mcpOptions.ReadOnly);
        Assert.IsTrue(mcpOptions.AllowProd);
        Assert.AreEqual("Http", mcpOptions.Transport);
        Assert.AreEqual("/custom-mcp", mcpOptions.EndpointPath);

        var ok = McpToolOutcome.Success("val", 201);
        Assert.IsTrue(ok.Succeeded);
        Assert.AreEqual("val", ok.Data);
        Assert.IsNull(ok.Error);
        Assert.AreEqual(201, ok.HttpStatusCode);

        var fail = McpToolOutcome.Failure<string>("boom", 500);
        Assert.IsFalse(fail.Succeeded);
        Assert.IsNull(fail.Data);
        Assert.AreEqual("boom", fail.Error);
        Assert.AreEqual(500, fail.HttpStatusCode);
    }

    [TestMethod]
    public void DorcJsonSerialization_PreservesPascalCaseAndStringEnums()
    {
        var options = DorcJsonSerialization.CreateOptions();
        var model = new RequestStatusDto { Id = 42, Status = "Running" };
        var json = JsonSerializer.Serialize(model, options);
        StringAssert.Contains(json, "\"Id\":42");
        StringAssert.Contains(json, "\"Status\":\"Running\"");
    }

    [TestMethod]
    public void LogTruncator_HandlesNullEmptyShortLongAndSmallLimits()
    {
        Assert.AreEqual(string.Empty, LogTruncator.TruncateTail(null));
        Assert.AreEqual(string.Empty, LogTruncator.TruncateTail(string.Empty));

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => LogTruncator.TruncateTail("abc", 0));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => LogTruncator.TruncateTail("abc", -5));

        Assert.AreEqual("hello", LogTruncator.TruncateTail("hello", 10));
        Assert.AreEqual("hello", LogTruncator.TruncateTail("hello", 5));

        // When maxCharacters is smaller than prefix length, returns raw tail of length maxCharacters
        var tinyTruncated = LogTruncator.TruncateTail("0123456789abcdef", 5);
        Assert.AreEqual("bcdef", tinyTruncated);

        // When maxCharacters accommodates prefix + tail
        var longText = new string('A', 200) + "TAIL_END";
        var truncated = LogTruncator.TruncateTail(longText, 80);
        Assert.IsLessThanOrEqualTo(80, truncated.Length);
        StringAssert.StartsWith(truncated, "[...truncated 128 leading characters...]\n");
        StringAssert.EndsWith(truncated, "TAIL_END");
    }

    [TestMethod]
    public void SecretRedactor_RedactsAllSensitiveAndAuditPayloads()
    {
        Assert.IsFalse(SecretRedactor.IsLikelySensitiveName(null));
        Assert.IsFalse(SecretRedactor.IsLikelySensitiveName("   "));
        Assert.IsFalse(SecretRedactor.IsLikelySensitiveName("AppUrl"));
        Assert.IsTrue(SecretRedactor.IsLikelySensitiveName("DatabasePassword"));
        Assert.IsTrue(SecretRedactor.IsLikelySensitiveName("AzureClientSecret"));
        Assert.IsTrue(SecretRedactor.IsLikelySensitiveName("StorageConnectionString"));

        Assert.IsEmpty(SecretRedactor.RedactPropertyValues(null));

        var propValues = new List<PropertyValueDto>
        {
            new()
            {
                Id = 1,
                Value = "plain",
                Property = new PropertyApiModel { Name = "SiteName", Secure = false }
            },
            new()
            {
                Id = 2,
                Value = "top-secret",
                Property = new PropertyApiModel { Name = "NormalName", Secure = true }
            },
            new()
            {
                Id = 3,
                Value = "heuristic-secret",
                Property = new PropertyApiModel { Name = "ApiToken", Secure = false }
            },
            new()
            {
                Id = 4,
                Value = "orphan-value",
                Property = null
            }
        };

        var redactedProps = SecretRedactor.RedactPropertyValues(propValues);
        Assert.AreEqual("plain", redactedProps[0].Value);
        Assert.AreEqual(SecretRedactor.RedactedMarker, redactedProps[1].Value);
        Assert.AreEqual(SecretRedactor.RedactedMarker, redactedProps[2].Value);
        Assert.AreEqual("orphan-value", redactedProps[3].Value);

        var nullScoped = SecretRedactor.RedactScopedPropertyValues(null);
        Assert.IsEmpty(nullScoped.Items);

        var emptyScoped = SecretRedactor.RedactScopedPropertyValues(new GetScopedPropertyValuesResponseDto { Items = null });
        Assert.IsEmpty(emptyScoped.Items);

        var scoped = new GetScopedPropertyValuesResponseDto
        {
            CurrentPage = 1,
            TotalItems = 3,
            TotalPages = 1,
            Items =
            [
                new FlatPropertyValueApiModel
                {
                    PropertyId = 10,
                    Property = "PublicSetting",
                    Secure = false,
                    PropertyValue = "visible",
                    PropertyArrayValue = ["a", "b"]
                },
                new FlatPropertyValueApiModel
                {
                    PropertyId = 11,
                    Property = "FlaggedSecret",
                    Secure = true,
                    PropertyValue = "secret1",
                    PropertyArrayValue = ["s1", "s2"]
                },
                new FlatPropertyValueApiModel
                {
                    PropertyId = 12,
                    Property = "DbPassword",
                    Secure = false,
                    PropertyValue = null,
                    PropertyArrayValue = null
                }
            ]
        };

        var redactedScoped = SecretRedactor.RedactScopedPropertyValues(scoped);
        Assert.AreEqual("visible", redactedScoped.Items[0].PropertyValue);
        CollectionAssert.AreEqual(new[] { "a", "b" }, redactedScoped.Items[0].PropertyArrayValue);
        Assert.AreEqual(SecretRedactor.RedactedMarker, redactedScoped.Items[1].PropertyValue);
        CollectionAssert.AreEqual(new[] { SecretRedactor.RedactedMarker }, redactedScoped.Items[1].PropertyArrayValue);
        Assert.IsNull(redactedScoped.Items[2].PropertyValue);
        Assert.IsNull(redactedScoped.Items[2].PropertyArrayValue);

        Assert.IsEmpty(SecretRedactor.RedactScopeOptions(null));
        var scopeOpts = SecretRedactor.RedactScopeOptions(
        [
            new PropertyValueScopeOptionApiModel { ValueOption = "Env1", SampleResolvedValue = "secret-sample" },
            new PropertyValueScopeOptionApiModel { ValueOption = "Env2", SampleResolvedValue = null }
        ]);
        Assert.AreEqual(SecretRedactor.RedactedMarker, scopeOpts[0].SampleResolvedValue);
        Assert.IsNull(scopeOpts[1].SampleResolvedValue);

        Assert.IsEmpty(SecretRedactor.RedactPropertyValuesAudit(null).Items);
        Assert.IsEmpty(SecretRedactor.RedactPropertyValuesAudit(new GetPropertyValuesAuditListResponseDto { Items = null }).Items);

        var audit = SecretRedactor.RedactPropertyValuesAudit(new GetPropertyValuesAuditListResponseDto
        {
            CurrentPage = 1,
            TotalItems = 2,
            TotalPages = 1,
            Items =
            [
                new PropertyValueAuditApiModel { Id = 1, PropertyName = "P1", FromValue = "old", ToValue = "new" },
                new PropertyValueAuditApiModel { Id = 2, PropertyName = "P2", FromValue = null, ToValue = null }
            ]
        });
        Assert.AreEqual(SecretRedactor.RedactedMarker, audit.Items[0].FromValue);
        Assert.AreEqual(SecretRedactor.RedactedMarker, audit.Items[0].ToValue);
        Assert.IsNull(audit.Items[1].FromValue);
        Assert.IsNull(audit.Items[1].ToValue);

        // Bundled requests redaction
        Assert.IsEmpty(SecretRedactor.RedactBundledRequests(null));
        Assert.IsNull(SecretRedactor.RedactBundledRequestJson(null));
        Assert.AreEqual("   ", SecretRedactor.RedactBundledRequestJson("   "));
        Assert.AreEqual(SecretRedactor.RedactedMarker, SecretRedactor.RedactBundledRequestJson("null"));
        Assert.AreEqual(SecretRedactor.RedactedMarker, SecretRedactor.RedactBundledRequestJson("[1,2,3]"));
        Assert.AreEqual(SecretRedactor.RedactedMarker, SecretRedactor.RedactBundledRequestJson("{not-valid-json"));

        var reqDtoWithNullPropsJson = "{\"Project\":\"P\",\"RequestProperties\":null}";
        var redactedNullProps = SecretRedactor.RedactBundledRequestJson(reqDtoWithNullPropsJson);
        StringAssert.Contains(redactedNullProps, "\"Project\":\"P\"");

        var copyEnvBuildJson = "{\"SourceEnvironmentName\":\"PROD-01\",\"ProjectName\":\"Proj1\",\"Components\":[10,20]}";
        var redactedCopyEnvBuild = SecretRedactor.RedactBundledRequestJson(copyEnvBuildJson);
        StringAssert.Contains(redactedCopyEnvBuild, "\"SourceEnvironmentName\":\"PROD-01\"");
        StringAssert.Contains(redactedCopyEnvBuild, "\"ProjectName\":\"Proj1\"");
        StringAssert.Contains(redactedCopyEnvBuild, "[10,20]");

        var edgeCasesPropsJson = "{\"RequestProperties\":[\"not-an-object\",{\"OtherProp\":123},{\"PropertyName\":\"ApiSecret\"},{\"PropertyName\":\"StringFlag\",\"PropertyValue\":\"sec-str\",\"IsSensitive\":\"true\"},{\"PropertyName\":\"FalseStrFlag\",\"PropertyValue\":\"pub-str\",\"IsSensitive\":\"false\"}]}";
        var redactedEdgeCases = SecretRedactor.RedactBundledRequestJson(edgeCasesPropsJson);
        Assert.IsFalse(redactedEdgeCases!.Contains("sec-str", StringComparison.Ordinal));
        StringAssert.Contains(redactedEdgeCases, "pub-str");

        var bundleRequestDto = new RequestDto
        {
            Project = "Proj1",
            Environment = "DEV",
            RequestProperties =
            [
                new RequestProperty { PropertyName = "PublicUrl", PropertyValue = "https://example.com", IsSensitive = false },
                new RequestProperty { PropertyName = "FlaggedKey", PropertyValue = "flag-secret", IsSensitive = true },
                new RequestProperty { PropertyName = "DbPassword", PropertyValue = "name-secret", IsSensitive = false }
            ]
        };
        var bundleJson = JsonSerializer.Serialize(bundleRequestDto, DorcJsonSerialization.CreateOptions());
        var redactedBundles = SecretRedactor.RedactBundledRequests(
        [
            new BundledRequestsApiModel
            {
                Id = 9,
                BundleName = "B1",
                ProjectId = 1,
                Type = BundledRequestType.JobRequest,
                RequestName = "R1",
                Sequence = 1,
                Request = bundleJson
            }
        ]);
        Assert.HasCount(1, redactedBundles);
        StringAssert.Contains(redactedBundles[0].Request, "https://example.com");
        Assert.IsFalse(redactedBundles[0].Request.Contains("flag-secret", StringComparison.Ordinal));
        Assert.IsFalse(redactedBundles[0].Request.Contains("name-secret", StringComparison.Ordinal));
        StringAssert.Contains(redactedBundles[0].Request, SecretRedactor.RedactedMarker);

        // RequestDetails XML redaction
        Assert.IsNull(SecretRedactor.RedactRequestDetailsXml(null));
        Assert.AreEqual("   ", SecretRedactor.RedactRequestDetailsXml("   "));
        Assert.AreEqual(SecretRedactor.RedactedMarker, SecretRedactor.RedactRequestDetailsXml("<unclosed"));

        var xmlPayload =
            "<DeploymentRequestDetail><Properties>" +
            "<PropertyPair><Name>PublicSetting</Name><Value>visible-val</Value><IsSensitive>false</IsSensitive></PropertyPair>" +
            "<PropertyPair><Name>FlaggedProp</Name><Value>flagged-secret</Value><IsSensitive>true</IsSensitive></PropertyPair>" +
            "<PropertyPair><Name>DatabasePassword</Name><Value>legacy-unflagged-secret</Value></PropertyPair>" +
            "<PropertyPair><Name>PasswordNoValue</Name><IsSensitive>true</IsSensitive></PropertyPair>" +
            "<PropertyPair><Value>orphan-val</Value></PropertyPair>" +
            "</Properties></DeploymentRequestDetail>";
        var redactedXml = SecretRedactor.RedactRequestDetailsXml(xmlPayload);
        StringAssert.Contains(redactedXml, "visible-val");
        Assert.IsFalse(redactedXml!.Contains("flagged-secret", StringComparison.Ordinal));
        Assert.IsFalse(redactedXml.Contains("legacy-unflagged-secret", StringComparison.Ordinal));
        StringAssert.Contains(redactedXml, SecretRedactor.RedactedMarker);
    }

    [TestMethod]
    public async Task ProductionEnvironmentGuard_CoversAllBranches()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() => new ProductionEnvironmentGuard(null!));

        Assert.IsFalse(ProductionEnvironmentGuard.IsProductionEnvironmentName(null));
        Assert.IsFalse(ProductionEnvironmentGuard.IsProductionEnvironmentName("   "));
        Assert.IsFalse(ProductionEnvironmentGuard.IsProductionEnvironmentName("DEV-01"));
        Assert.IsTrue(ProductionEnvironmentGuard.IsProductionEnvironmentName("EU-PROD-01"));
        Assert.IsTrue(ProductionEnvironmentGuard.IsProductionEnvironmentName("uk-prd-web"));

        var gateway = NSubstitute.Substitute.For<IDorcApiGateway>();

        // 1. ReadOnly = true blocks everything
        var readOnlyGuard = new ProductionEnvironmentGuard(new DorcMcpOptions { ReadOnly = true, AllowProd = false });
        Assert.IsTrue(readOnlyGuard.IsReadOnly);
        Assert.IsFalse(readOnlyGuard.AllowProd);
        StringAssert.Contains(readOnlyGuard.CheckMutationAllowed("op")!, "read-only mode");
        StringAssert.Contains(await readOnlyGuard.CheckEnvironmentMutationAllowedAsync("op", "DEV", gateway, CancellationToken.None), "read-only mode");
        StringAssert.Contains(await readOnlyGuard.CheckRequestMutationAllowedAsync("op", 1, gateway, CancellationToken.None), "read-only mode");

        // 2. ReadOnly = false, AllowProd = true allows everything without API lookups
        var allowProdGuard = new ProductionEnvironmentGuard(new DorcMcpOptions { ReadOnly = false, AllowProd = true });
        Assert.IsNull(allowProdGuard.CheckMutationAllowed("op"));
        Assert.IsNull(await allowProdGuard.CheckEnvironmentMutationAllowedAsync("op", "PROD", gateway, CancellationToken.None));
        Assert.IsNull(await allowProdGuard.CheckRequestMutationAllowedAsync("op", 99, gateway, CancellationToken.None));

        // 3. ReadOnly = false, AllowProd = false enforces prod checks
        var nonProdGuard = new ProductionEnvironmentGuard(new DorcMcpOptions { ReadOnly = false, AllowProd = false });

        // Empty env name
        StringAssert.Contains(await nonProdGuard.CheckEnvironmentMutationAllowedAsync("op", "  ", gateway, CancellationToken.None), "requires a non-empty target environment name");

        // Heuristic PROD name blocked immediately
        StringAssert.Contains(await nonProdGuard.CheckEnvironmentMutationAllowedAsync("op", "PROD-LON", gateway, CancellationToken.None), "DORC_MCP_ALLOW_PROD=false");

        // API failure when looking up environment
        gateway.GetAsync<List<EnvironmentApiModel>>("RefDataEnvironments?env=UAT-FAIL", Arg.Any<CancellationToken>())
            .Returns(McpToolOutcome.Failure<List<EnvironmentApiModel>>("timeout", null));
        StringAssert.Contains(await nonProdGuard.CheckEnvironmentMutationAllowedAsync("op", "UAT-FAIL", gateway, CancellationToken.None), "could not verify production status (HTTP error: timeout)");

        // API success with null data
        gateway.GetAsync<List<EnvironmentApiModel>>("RefDataEnvironments?env=UAT-NULL", Arg.Any<CancellationToken>())
            .Returns(new McpToolOutcome<List<EnvironmentApiModel>>(true, null, "none", 502));
        StringAssert.Contains(await nonProdGuard.CheckEnvironmentMutationAllowedAsync("op", "UAT-NULL", gateway, CancellationToken.None), "HTTP 502");

        // Environment not in returned list
        gateway.GetAsync<List<EnvironmentApiModel>>("RefDataEnvironments?env=UAT-MISSING", Arg.Any<CancellationToken>())
            .Returns(McpToolOutcome.Success(new List<EnvironmentApiModel> { new() { EnvironmentName = "OTHER" } }));
        StringAssert.Contains(await nonProdGuard.CheckEnvironmentMutationAllowedAsync("op", "UAT-MISSING", gateway, CancellationToken.None), "could not find environment");

        // Environment has EnvironmentIsProd = true
        gateway.GetAsync<List<EnvironmentApiModel>>("RefDataEnvironments?env=LIVE-01", Arg.Any<CancellationToken>())
            .Returns(McpToolOutcome.Success(new List<EnvironmentApiModel> { new() { EnvironmentName = "LIVE-01", EnvironmentIsProd = true } }));
        StringAssert.Contains(await nonProdGuard.CheckEnvironmentMutationAllowedAsync("op", "LIVE-01", gateway, CancellationToken.None), "EnvironmentIsProd=True");

        // Environment has EnvironmentSecure = true
        gateway.GetAsync<List<EnvironmentApiModel>>("RefDataEnvironments?env=SEC-01", Arg.Any<CancellationToken>())
            .Returns(McpToolOutcome.Success(new List<EnvironmentApiModel> { new() { EnvironmentName = "SEC-01", EnvironmentIsProd = false, EnvironmentSecure = true } }));
        StringAssert.Contains(await nonProdGuard.CheckEnvironmentMutationAllowedAsync("op", "SEC-01", gateway, CancellationToken.None), "EnvironmentSecure=True");

        // Environment has EnvironmentIsProd = false and EnvironmentSecure = false -> allowed
        gateway.GetAsync<List<EnvironmentApiModel>>("RefDataEnvironments?env=DEV-01", Arg.Any<CancellationToken>())
            .Returns(McpToolOutcome.Success(new List<EnvironmentApiModel> { new() { EnvironmentName = "DEV-01", EnvironmentIsProd = false, EnvironmentSecure = false } }));
        Assert.IsNull(await nonProdGuard.CheckEnvironmentMutationAllowedAsync("op", "DEV-01", gateway, CancellationToken.None));

        // Request mutation: request lookup fails (with and without status code)
        gateway.GetAsync<DeploymentRequestApiModel>("RequestStatuses?requestId=404", Arg.Any<CancellationToken>())
            .Returns(McpToolOutcome.Failure<DeploymentRequestApiModel>("not found", 404));
        StringAssert.Contains(await nonProdGuard.CheckRequestMutationAllowedAsync("op", 404, gateway, CancellationToken.None), "HTTP 404");

        gateway.GetAsync<DeploymentRequestApiModel>("RequestStatuses?requestId=405", Arg.Any<CancellationToken>())
            .Returns(new McpToolOutcome<DeploymentRequestApiModel>(true, null, "empty", null));
        StringAssert.Contains(await nonProdGuard.CheckRequestMutationAllowedAsync("op", 405, gateway, CancellationToken.None), "HTTP error");

        // Request mutation: request found for DEV-01 -> allowed
        gateway.GetAsync<DeploymentRequestApiModel>("RequestStatuses?requestId=100", Arg.Any<CancellationToken>())
            .Returns(McpToolOutcome.Success(new DeploymentRequestApiModel { Id = 100, EnvironmentName = "DEV-01" }));
        Assert.IsNull(await nonProdGuard.CheckRequestMutationAllowedAsync("op", 100, gateway, CancellationToken.None));

        // Terraform plan mutation checks:
        StringAssert.Contains(await readOnlyGuard.CheckTerraformPlanMutationAllowedAsync("op", 1, "DEV-01", gateway, CancellationToken.None), "read-only mode");
        Assert.IsNull(await allowProdGuard.CheckTerraformPlanMutationAllowedAsync("op", 1, "PROD", gateway, CancellationToken.None));

        gateway.GetAsync<TerraformPlanApiModel>("Terraform/plan/404", Arg.Any<CancellationToken>())
            .Returns(McpToolOutcome.Failure<TerraformPlanApiModel>("missing", 404));
        StringAssert.Contains(await nonProdGuard.CheckTerraformPlanMutationAllowedAsync("op", 404, "DEV-01", gateway, CancellationToken.None), "HTTP 404");

        gateway.GetAsync<TerraformPlanApiModel>("Terraform/plan/405", Arg.Any<CancellationToken>())
            .Returns(new McpToolOutcome<TerraformPlanApiModel>(true, null, "empty", null));
        StringAssert.Contains(await nonProdGuard.CheckTerraformPlanMutationAllowedAsync("op", 405, "DEV-01", gateway, CancellationToken.None), "HTTP error");

        gateway.GetAsync<TerraformPlanApiModel>("Terraform/plan/406", Arg.Any<CancellationToken>())
            .Returns(McpToolOutcome.Success(new TerraformPlanApiModel { Receipt = null! }));
        StringAssert.Contains(await nonProdGuard.CheckTerraformPlanMutationAllowedAsync("op", 406, "DEV-01", gateway, CancellationToken.None), "could not determine the authoritative environment");

        gateway.GetAsync<TerraformPlanApiModel>("Terraform/plan/407", Arg.Any<CancellationToken>())
            .Returns(McpToolOutcome.Success(new TerraformPlanApiModel { Receipt = new TerraformDeploymentReceiptApiModel { EnvironmentName = "  " } }));
        StringAssert.Contains(await nonProdGuard.CheckTerraformPlanMutationAllowedAsync("op", 407, "DEV-01", gateway, CancellationToken.None), "could not determine the authoritative environment");

        gateway.GetAsync<TerraformPlanApiModel>("Terraform/plan/408", Arg.Any<CancellationToken>())
            .Returns(McpToolOutcome.Success(new TerraformPlanApiModel { Receipt = new TerraformDeploymentReceiptApiModel { EnvironmentName = "PROD-01" } }));
        StringAssert.Contains(await nonProdGuard.CheckTerraformPlanMutationAllowedAsync("op", 408, "DEV-01", gateway, CancellationToken.None), "does not match the Terraform plan's authoritative environment");

        gateway.GetAsync<TerraformPlanApiModel>("Terraform/plan/409", Arg.Any<CancellationToken>())
            .Returns(McpToolOutcome.Success(new TerraformPlanApiModel { Receipt = new TerraformDeploymentReceiptApiModel { EnvironmentName = "DEV-01" } }));
        Assert.IsNull(await nonProdGuard.CheckTerraformPlanMutationAllowedAsync("op", 409, "DEV-01", gateway, CancellationToken.None));
        Assert.IsNull(await nonProdGuard.CheckTerraformPlanMutationAllowedAsync("op", 409, null, gateway, CancellationToken.None));

        // Daemon mutation checks:
        StringAssert.Contains(await readOnlyGuard.CheckDaemonMutationAllowedAsync("op", "DEV-01", "srv", "svc", gateway, CancellationToken.None), "read-only mode");
        StringAssert.Contains(await nonProdGuard.CheckDaemonMutationAllowedAsync("op", "  ", "srv", "svc", gateway, CancellationToken.None), "requires a non-empty target environment name");
        StringAssert.Contains(await nonProdGuard.CheckDaemonMutationAllowedAsync("op", "DEV-01", "  ", "svc", gateway, CancellationToken.None), "requires non-empty serverName and daemonName");
        StringAssert.Contains(await nonProdGuard.CheckDaemonMutationAllowedAsync("op", "DEV-01", "srv", "  ", gateway, CancellationToken.None), "requires non-empty serverName and daemonName");
        StringAssert.Contains(await nonProdGuard.CheckDaemonMutationAllowedAsync("op", "PROD-01", "srv", "svc", gateway, CancellationToken.None), "DORC_MCP_ALLOW_PROD=false");
        StringAssert.Contains(await nonProdGuard.CheckDaemonMutationAllowedAsync("op", "UAT-FAIL", "srv", "svc", gateway, CancellationToken.None), "HTTP error");
        StringAssert.Contains(await nonProdGuard.CheckDaemonMutationAllowedAsync("op", "UAT-NULL", "srv", "svc", gateway, CancellationToken.None), "HTTP 502");
        StringAssert.Contains(await nonProdGuard.CheckDaemonMutationAllowedAsync("op", "UAT-MISSING", "srv", "svc", gateway, CancellationToken.None), "could not find environment");
        StringAssert.Contains(await nonProdGuard.CheckDaemonMutationAllowedAsync("op", "LIVE-01", "srv", "svc", gateway, CancellationToken.None), "EnvironmentIsProd=True");
        StringAssert.Contains(await nonProdGuard.CheckDaemonMutationAllowedAsync("op", "SEC-01", "srv", "svc", gateway, CancellationToken.None), "EnvironmentSecure=True");

        // Details lookup failures (with status code, without status code, and null AppServers)
        gateway.GetAsync<List<EnvironmentApiModel>>("RefDataEnvironments?env=DEV-DET-ERR", Arg.Any<CancellationToken>())
            .Returns(McpToolOutcome.Success(new List<EnvironmentApiModel> { new() { EnvironmentId = 11, EnvironmentName = "DEV-DET-ERR" } }));
        gateway.GetAsync<EnvironmentContentApiModel>("RefDataEnvironmentsDetails/11", Arg.Any<CancellationToken>())
            .Returns(McpToolOutcome.Failure<EnvironmentContentApiModel>("err", 500));
        StringAssert.Contains(await nonProdGuard.CheckDaemonMutationAllowedAsync("op", "DEV-DET-ERR", "srv", "svc", gateway, CancellationToken.None), "HTTP 500");

        gateway.GetAsync<List<EnvironmentApiModel>>("RefDataEnvironments?env=DEV-DET-NULL", Arg.Any<CancellationToken>())
            .Returns(McpToolOutcome.Success(new List<EnvironmentApiModel> { new() { EnvironmentId = 12, EnvironmentName = "DEV-DET-NULL" } }));
        gateway.GetAsync<EnvironmentContentApiModel>("RefDataEnvironmentsDetails/12", Arg.Any<CancellationToken>())
            .Returns(new McpToolOutcome<EnvironmentContentApiModel>(true, null, "empty", null));
        StringAssert.Contains(await nonProdGuard.CheckDaemonMutationAllowedAsync("op", "DEV-DET-NULL", "srv", "svc", gateway, CancellationToken.None), "HTTP error");

        gateway.GetAsync<List<EnvironmentApiModel>>("RefDataEnvironments?env=DEV-DET-NOSRV", Arg.Any<CancellationToken>())
            .Returns(McpToolOutcome.Success(new List<EnvironmentApiModel> { new() { EnvironmentId = 13, EnvironmentName = "DEV-DET-NOSRV" } }));
        gateway.GetAsync<EnvironmentContentApiModel>("RefDataEnvironmentsDetails/13", Arg.Any<CancellationToken>())
            .Returns(McpToolOutcome.Success(new EnvironmentContentApiModel { AppServers = null! }));
        StringAssert.Contains(await nonProdGuard.CheckDaemonMutationAllowedAsync("op", "DEV-DET-NOSRV", "srv", "svc", gateway, CancellationToken.None), "could not retrieve servers");

        // Server not in environment, server with null/empty EnvironmentNames, server shared with PROD, server shared with another non-prod env
        gateway.GetAsync<List<EnvironmentApiModel>>("RefDataEnvironments?env=DEV-SRV", Arg.Any<CancellationToken>())
            .Returns(McpToolOutcome.Success(new List<EnvironmentApiModel> { new() { EnvironmentId = 14, EnvironmentName = "DEV-SRV" } }));
        gateway.GetAsync<EnvironmentContentApiModel>("RefDataEnvironmentsDetails/14", Arg.Any<CancellationToken>())
            .Returns(McpToolOutcome.Success(new EnvironmentContentApiModel
            {
                AppServers =
                [
                    new ServerApiModel { ServerId = 101, Name = "srv-null-envs", EnvironmentNames = null! },
                    new ServerApiModel { ServerId = 102, Name = "srv-empty-envs", EnvironmentNames = [] },
                    new ServerApiModel { ServerId = 103, Name = "srv-shared-prod", EnvironmentNames = ["DEV-SRV", "PROD-01"] },
                    new ServerApiModel { ServerId = 104, Name = "srv-daemons-err", EnvironmentNames = ["DEV-SRV", "DEV-01"] },
                    new ServerApiModel { ServerId = 105, Name = "srv-daemons-null", EnvironmentNames = ["DEV-SRV"] },
                    new ServerApiModel { ServerId = 106, Name = "srv-ok", EnvironmentNames = ["DEV-SRV"] }
                ]
            }));

        StringAssert.Contains(await nonProdGuard.CheckDaemonMutationAllowedAsync("op", "DEV-SRV", "missing-srv", "svc", gateway, CancellationToken.None), "does not belong to environment");
        StringAssert.Contains(await nonProdGuard.CheckDaemonMutationAllowedAsync("op", "DEV-SRV", "srv-null-envs", "svc", gateway, CancellationToken.None), "could not verify all environment associations");
        StringAssert.Contains(await nonProdGuard.CheckDaemonMutationAllowedAsync("op", "DEV-SRV", "srv-empty-envs", "svc", gateway, CancellationToken.None), "could not verify all environment associations");
        StringAssert.Contains(await nonProdGuard.CheckDaemonMutationAllowedAsync("op", "DEV-SRV", "srv-shared-prod", "svc", gateway, CancellationToken.None), "associated environment 'PROD-01' is protected");

        gateway.GetAsync<List<DaemonApiModel>>("ServerDaemons/104", Arg.Any<CancellationToken>())
            .Returns(McpToolOutcome.Failure<List<DaemonApiModel>>("err", 503));
        StringAssert.Contains(await nonProdGuard.CheckDaemonMutationAllowedAsync("op", "DEV-SRV", "srv-daemons-err", "svc", gateway, CancellationToken.None), "HTTP 503");

        gateway.GetAsync<List<DaemonApiModel>>("ServerDaemons/105", Arg.Any<CancellationToken>())
            .Returns(new McpToolOutcome<List<DaemonApiModel>>(true, null, "empty", null));
        StringAssert.Contains(await nonProdGuard.CheckDaemonMutationAllowedAsync("op", "DEV-SRV", "srv-daemons-null", "svc", gateway, CancellationToken.None), "HTTP error");

        gateway.GetAsync<List<DaemonApiModel>>("ServerDaemons/106", Arg.Any<CancellationToken>())
            .Returns(McpToolOutcome.Success(new List<DaemonApiModel> { new() { Name = "svc" } }));
        StringAssert.Contains(await nonProdGuard.CheckDaemonMutationAllowedAsync("op", "DEV-SRV", "srv-ok", "unmapped-svc", gateway, CancellationToken.None), "is not mapped to server");
        Assert.IsNull(await nonProdGuard.CheckDaemonMutationAllowedAsync("op", "DEV-SRV", "srv-ok", "svc", gateway, CancellationToken.None));

        // Also test allowProdGuard on LIVE-01 when server & daemon are mapped
        gateway.GetAsync<List<EnvironmentApiModel>>("RefDataEnvironments?env=PROD-OK", Arg.Any<CancellationToken>())
            .Returns(McpToolOutcome.Success(new List<EnvironmentApiModel> { new() { EnvironmentId = 15, EnvironmentName = "PROD-OK", EnvironmentIsProd = true, EnvironmentSecure = true } }));
        gateway.GetAsync<EnvironmentContentApiModel>("RefDataEnvironmentsDetails/15", Arg.Any<CancellationToken>())
            .Returns(McpToolOutcome.Success(new EnvironmentContentApiModel
            {
                AppServers = [new ServerApiModel { ServerId = 106, Name = "srv-ok", EnvironmentNames = null! }]
            }));
        Assert.IsNull(await allowProdGuard.CheckDaemonMutationAllowedAsync("op", "PROD-OK", "srv-ok", "svc", gateway, CancellationToken.None));
    }

    [TestMethod]
    public async Task OAuthTokenAcquirer_And_BearerHandler_CoverAllFlows()
    {
        using var dummyHttpClient = new HttpClient();
        Assert.ThrowsExactly<ArgumentNullException>(() => new OAuthTokenAcquirer(null!, new DorcApiOptions()));
        Assert.ThrowsExactly<ArgumentNullException>(() => new OAuthTokenAcquirer(dummyHttpClient, null!));
        Assert.ThrowsExactly<ArgumentNullException>(() => new BearerTokenRefreshingHandler(null!));

        // Missing BaseUrl
        using (var acquirerNoUrl = new OAuthTokenAcquirer(dummyHttpClient, new DorcApiOptions()))
        {
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
                acquirerNoUrl.GetAccessTokenAsync(false, CancellationToken.None));
        }

        // Missing ClientId / ClientSecret
        using (var acquirerNoClient = new OAuthTokenAcquirer(dummyHttpClient, new DorcApiOptions { BaseUrl = "https://dorc.local", ClientId = "id", ClientSecret = "" }))
        {
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
                acquirerNoClient.GetAccessTokenAsync(false, CancellationToken.None));
        }

        using (var acquirerNoClientId = new OAuthTokenAcquirer(dummyHttpClient, new DorcApiOptions { BaseUrl = "https://dorc.local", ClientId = "", ClientSecret = "sec" }))
        {
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
                acquirerNoClientId.GetAccessTokenAsync(false, CancellationToken.None));
        }

        // Invalid /ApiConfig (missing OAuthAuthority and blank OAuthAuthority)
        using (var badMetaHandler = new StubHttpMessageHandler((_, _) =>
            Task.FromResult(StubHttpMessageHandler.CreateJsonResponse("{}"))))
        using (var badMetaClient = new HttpClient(badMetaHandler))
        using (var badMetaAcquirer = new OAuthTokenAcquirer(badMetaClient, new DorcApiOptions { BaseUrl = "https://dorc.local", ClientId = "c", ClientSecret = "s" }))
        {
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
                badMetaAcquirer.GetAccessTokenAsync(false, CancellationToken.None));
        }

        using (var blankMetaHandler = new StubHttpMessageHandler((_, _) =>
            Task.FromResult(StubHttpMessageHandler.CreateJsonResponse("{\"OAuthAuthority\":\"   \"}"))))
        using (var blankMetaClient = new HttpClient(blankMetaHandler))
        using (var blankMetaAcquirer = new OAuthTokenAcquirer(blankMetaClient, new DorcApiOptions { BaseUrl = "https://dorc.local", ClientId = "c", ClientSecret = "s" }))
        {
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
                blankMetaAcquirer.GetAccessTokenAsync(false, CancellationToken.None));
        }

        // Invalid discovery (missing token_endpoint)
        using (var badDiscHandler = new StubHttpMessageHandler((req, _) =>
        {
            if (req.RequestUri!.AbsolutePath.EndsWith("/ApiConfig", StringComparison.Ordinal))
            {
                return Task.FromResult(StubHttpMessageHandler.CreateJsonResponse("{\"OAuthAuthority\":\"https://idp.local\"}"));
            }

            return Task.FromResult(StubHttpMessageHandler.CreateJsonResponse("{}"));
        }))
        using (var badDiscClient = new HttpClient(badDiscHandler))
        using (var badDiscAcquirer = new OAuthTokenAcquirer(badDiscClient, new DorcApiOptions { BaseUrl = "https://dorc.local", ClientId = "c", ClientSecret = "s" }))
        {
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
                badDiscAcquirer.GetAccessTokenAsync(false, CancellationToken.None));
        }

        // Invalid token response (missing access_token)
        using (var badTokenHandler = new StubHttpMessageHandler((req, _) =>
        {
            if (req.RequestUri!.AbsolutePath.EndsWith("/ApiConfig", StringComparison.Ordinal))
            {
                return Task.FromResult(StubHttpMessageHandler.CreateJsonResponse("{\"OAuthAuthority\":\"https://idp.local\"}"));
            }

            if (req.RequestUri.AbsolutePath.Contains("openid-configuration", StringComparison.Ordinal))
            {
                return Task.FromResult(StubHttpMessageHandler.CreateJsonResponse("{\"token_endpoint\":\"https://idp.local/connect/token\"}"));
            }

            return Task.FromResult(StubHttpMessageHandler.CreateJsonResponse("{}"));
        }))
        using (var badTokenClient = new HttpClient(badTokenHandler))
        using (var badTokenAcquirer = new OAuthTokenAcquirer(badTokenClient, new DorcApiOptions { BaseUrl = "https://dorc.local", ClientId = "c", ClientSecret = "s", Scope = "" }))
        {
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
                badTokenAcquirer.GetAccessTokenAsync(false, CancellationToken.None));
        }

        // Full happy path + caching + expiry + forceRefresh + default expires_in fallback
        var tokenCallCount = 0;
        var fakeTime = new FakeTimeProvider();
        using var oauthHandler = new StubHttpMessageHandler((req, _) =>
        {
            if (req.RequestUri!.AbsolutePath.EndsWith("/ApiConfig", StringComparison.Ordinal))
            {
                return Task.FromResult(StubHttpMessageHandler.CreateJsonResponse("{\"OAuthAuthority\":\"https://idp.local\"}"));
            }

            if (req.RequestUri.AbsolutePath.Contains("openid-configuration", StringComparison.Ordinal))
            {
                return Task.FromResult(StubHttpMessageHandler.CreateJsonResponse("{\"token_endpoint\":\"https://idp.local/connect/token\"}"));
            }

            tokenCallCount++;
            var body = tokenCallCount == 1
                ? "{\"access_token\":\"tok-1\",\"expires_in\":120}"
                : "{\"access_token\":\"tok-2\",\"expires_in\":\"not-a-number\"}";
            return Task.FromResult(StubHttpMessageHandler.CreateJsonResponse(body));
        });

        using var oauthHttpClient = new HttpClient(oauthHandler);
        using var acquirer = new OAuthTokenAcquirer(
            oauthHttpClient,
            new DorcApiOptions { BaseUrl = "https://dorc.local", ClientId = "cid+1", ClientSecret = "csec:2", Scope = "dorc-api.manage" },
            fakeTime);

        var t1 = await acquirer.GetAccessTokenAsync(false, CancellationToken.None);
        Assert.AreEqual("tok-1", t1);

        // Cached hit
        var t1Cached = await acquirer.GetAccessTokenAsync(false, CancellationToken.None);
        Assert.AreEqual("tok-1", t1Cached);
        Assert.AreEqual(1, tokenCallCount);

        // Force refresh hit (also reuses _cachedTokenEndpoint!)
        var t2 = await acquirer.GetAccessTokenAsync(true, CancellationToken.None);
        Assert.AreEqual("tok-2", t2);
        Assert.AreEqual(2, tokenCallCount);

        // Advance time past expiry and call with forceRefresh = false so now < _expiresAtUtc is false
        fakeTime.Advance(TimeSpan.FromHours(2));
        var t3 = await acquirer.GetAccessTokenAsync(false, CancellationToken.None);
        Assert.AreEqual("tok-2", t3);
        Assert.AreEqual(3, tokenCallCount);

        // BearerTokenRefreshingHandler: 200 without retry + 401 with retry (both with body & headers and without body)
        var apiAttempt = 0;
        var force200 = false;
        using var innerApiHandler = new StubHttpMessageHandler((req, _) =>
        {
            apiAttempt++;
            if (!force200 && apiAttempt == 1)
            {
                return Task.FromResult(StubHttpMessageHandler.CreateStatusResponse(HttpStatusCode.Unauthorized));
            }

            return Task.FromResult(StubHttpMessageHandler.CreateJsonResponse("{\"Id\":1,\"Status\":\"Ok\"}"));
        });

        using var refreshingHandler = new BearerTokenRefreshingHandler(acquirer)
        {
            InnerHandler = innerApiHandler
        };
        using var apiClient = new HttpClient(refreshingHandler) { BaseAddress = new Uri("https://dorc.local/") };

        // Direct 200 OK on first attempt
        force200 = true;
        using var directOkResp = await apiClient.GetAsync("Request");
        Assert.AreEqual(HttpStatusCode.OK, directOkResp.StatusCode);
        force200 = false;

        apiAttempt = 0;
        using var postReq = new HttpRequestMessage(HttpMethod.Post, "Request")
        {
            Content = new StringContent("{\"x\":1}", Encoding.UTF8, "application/json")
        };
        postReq.Headers.Add("X-Custom", "val");
        using var postResp = await apiClient.SendAsync(postReq);
        Assert.AreEqual(HttpStatusCode.OK, postResp.StatusCode);
        Assert.AreEqual(2, apiAttempt);

        // Also test 401 retry when request has NO content or content without ContentType
        apiAttempt = 0;
        using var getReq = new HttpRequestMessage(HttpMethod.Get, "Request");
        using var getResp = await apiClient.SendAsync(getReq);
        Assert.AreEqual(HttpStatusCode.OK, getResp.StatusCode);

        apiAttempt = 0;
        using var rawBytesReq = new HttpRequestMessage(HttpMethod.Post, "Request")
        {
            Content = new ByteArrayContent([1, 2, 3])
        };
        using var rawBytesResp = await apiClient.SendAsync(rawBytesReq);
        Assert.AreEqual(HttpStatusCode.OK, rawBytesResp.StatusCode);

        // When request already has an Authorization header (e.g., forwarded from caller), BearerTokenRefreshingHandler preserves it without acquiring a service token
        using var preAuthedReq = new HttpRequestMessage(HttpMethod.Get, "Request");
        preAuthedReq.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "caller-jwt");
        force200 = true;
        using var preAuthedResp = await apiClient.SendAsync(preAuthedReq);
        Assert.AreEqual(HttpStatusCode.OK, preAuthedResp.StatusCode);
        Assert.AreEqual("caller-jwt", innerApiHandler.SentRequests[^1].Headers.Authorization?.Parameter);

        // CallerAuthorizationForwardingHandler tests
        Assert.ThrowsExactly<ArgumentNullException>(() => new CallerAuthorizationForwardingHandler(null!));

        var httpContextAccessor = new HttpContextAccessor();
        using var forwardingHandler = new CallerAuthorizationForwardingHandler(httpContextAccessor)
        {
            InnerHandler = innerApiHandler
        };
        using var forwardingClient = new HttpClient(forwardingHandler) { BaseAddress = new Uri("https://dorc.local/") };

        // 1. Null HttpContext -> no header added
        httpContextAccessor.HttpContext = null;
        using var noCtxResp = await forwardingClient.GetAsync("Request");
        Assert.IsNull(innerApiHandler.SentRequests[^1].Headers.Authorization);

        // 2. HttpContext without Authorization header -> no header added
        httpContextAccessor.HttpContext = new DefaultHttpContext();
        using var noHeaderResp = await forwardingClient.GetAsync("Request");
        Assert.IsNull(innerApiHandler.SentRequests[^1].Headers.Authorization);

        // 3. HttpContext with invalid Authorization header -> ignored
        var invalidAuthContext = new DefaultHttpContext();
        invalidAuthContext.Request.Headers.Authorization = ":::not-a-valid-auth-header:::";
        httpContextAccessor.HttpContext = invalidAuthContext;
        using var invalidHeaderResp = await forwardingClient.GetAsync("Request");
        Assert.IsNull(innerApiHandler.SentRequests[^1].Headers.Authorization);

        // 4. HttpContext with valid Bearer Authorization header -> forwarded
        var validAuthContext = new DefaultHttpContext();
        validAuthContext.Request.Headers.Authorization = "  Bearer user-forwarded-token  ";
        httpContextAccessor.HttpContext = validAuthContext;
        using var validHeaderResp = await forwardingClient.GetAsync("Request");
        Assert.AreEqual("Bearer", innerApiHandler.SentRequests[^1].Headers.Authorization?.Scheme);
        Assert.AreEqual("user-forwarded-token", innerApiHandler.SentRequests[^1].Headers.Authorization?.Parameter);
    }

    [TestMethod]
    public async Task DorcApiGateway_CoversAllHttpVerbsAndResponseShapes()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() => new DorcApiGateway(null!));

        var mode = "json-ok";
        using var handler = new StubHttpMessageHandler((_, _) =>
        {
            return mode switch
            {
                "json-ok" => Task.FromResult(StubHttpMessageHandler.CreateJsonResponse("{\"Id\":7,\"Status\":\"Complete\"}")),
                "empty-ok" => Task.FromResult(StubHttpMessageHandler.CreateTextResponse(string.Empty, HttpStatusCode.NoContent)),
                "error-with-body" => Task.FromResult(StubHttpMessageHandler.CreateTextResponse("Bad input payload", HttpStatusCode.BadRequest)),
                "error-empty" => Task.FromResult(StubHttpMessageHandler.CreateTextResponse("   ", HttpStatusCode.Forbidden, "text/plain", "Forbidden")),
                "string-quoted" => Task.FromResult(StubHttpMessageHandler.CreateJsonResponse("\"Line 1\\nLine 2\"")),
                "string-null-literal" => Task.FromResult(StubHttpMessageHandler.CreateTextResponse("Plain unquoted text")),
                _ => Task.FromResult(StubHttpMessageHandler.CreateStatusResponse(HttpStatusCode.OK))
            };
        });

        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://dorc.local/") };
        var gateway = new DorcApiGateway(httpClient);

        mode = "json-ok";
        var getOk = await gateway.GetAsync<RequestStatusDto>("Request?id=7", CancellationToken.None);
        Assert.IsTrue(getOk.Succeeded);
        Assert.AreEqual(7, getOk.Data!.Id);

        var postOk = await gateway.PostAsync<RequestStatusDto, RequestStatusDto>("Request", new RequestStatusDto { Id = 7 }, CancellationToken.None);
        Assert.IsTrue(postOk.Succeeded);

        var putOk = await gateway.PutAsync< object?, RequestStatusDto>("Request/cancel?requestId=7", null, CancellationToken.None);
        Assert.IsTrue(putOk.Succeeded);

        mode = "empty-ok";
        var emptyJson = await gateway.GetAsync<RequestStatusDto>("Request?id=7", CancellationToken.None);
        Assert.IsTrue(emptyJson.Succeeded);
        Assert.IsNull(emptyJson.Data);

        var emptyStr = await gateway.GetStringAsync("ResultStatuses/Log", CancellationToken.None);
        Assert.IsTrue(emptyStr.Succeeded);
        Assert.AreEqual(string.Empty, emptyStr.Data);

        mode = "error-with-body";
        var errJson = await gateway.GetAsync<RequestStatusDto>("Request", CancellationToken.None);
        Assert.IsFalse(errJson.Succeeded);
        StringAssert.Contains(errJson.Error!, "Bad input payload");

        var errStr = await gateway.GetStringAsync("ResultStatuses/Log", CancellationToken.None);
        Assert.IsFalse(errStr.Succeeded);
        StringAssert.Contains(errStr.Error!, "Bad input payload");

        mode = "error-empty";
        var errEmptyJson = await gateway.GetAsync<RequestStatusDto>("Request", CancellationToken.None);
        Assert.IsFalse(errEmptyJson.Succeeded);
        StringAssert.Contains(errEmptyJson.Error!, "HTTP 403");

        var errEmptyStr = await gateway.GetStringAsync("ResultStatuses/Log", CancellationToken.None);
        Assert.IsFalse(errEmptyStr.Succeeded);
        StringAssert.Contains(errEmptyStr.Error!, "HTTP 403");

        mode = "string-quoted";
        var quotedStr = await gateway.GetStringAsync("ResultStatuses/Log", CancellationToken.None);
        Assert.IsTrue(quotedStr.Succeeded);
        Assert.AreEqual("Line 1\nLine 2", quotedStr.Data);

        mode = "string-null-literal";
        var unquotedStr = await gateway.PostWithoutResponseBodyAsync<object?>("Terraform/plan/1/confirm", null, CancellationToken.None);
        Assert.IsTrue(unquotedStr.Succeeded);
        Assert.AreEqual("Plain unquoted text", unquotedStr.Data);

        var putStr = await gateway.PutStringResponseAsync("DaemonStatus", new DaemonStatusApiModel { ServerName = "srv" }, CancellationToken.None);
        Assert.IsTrue(putStr.Succeeded);
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
                ["DORC_MCP_ENDPOINT_PATH"] = " /api/mcp "
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

        var defaultApiOpts = DorcMcpHostConfiguration.ResolveApiOptions(emptyConfig);
        Assert.AreEqual("dorc-api.manage", defaultApiOpts.Scope);
        var defaultMcpOpts = DorcMcpHostConfiguration.ResolveMcpOptions(emptyConfig);
        Assert.IsTrue(defaultMcpOpts.ReadOnly);
        Assert.IsFalse(defaultMcpOpts.AllowProd);
        Assert.AreEqual("Stdio", defaultMcpOpts.Transport);
        Assert.AreEqual("/mcp", defaultMcpOpts.EndpointPath);

        // Test CreateHostBuilder with both non-empty and empty BaseUrl, OAuth vs Windows credentials, and ReadOnly vs non-ReadOnly
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

        using var hostWithoutUrl = DorcMcpHostConfiguration.CreateHostBuilder(["--DorcApi:BaseUrl="]).Build();
        var gatewayWithoutUrl = hostWithoutUrl.Services.GetRequiredService<IDorcApiGateway>();
        Assert.IsNotNull(gatewayWithoutUrl);

        // Test CreateWebApplication with custom and blank EndpointPath
        await using var webAppCustom = DorcMcpHostConfiguration.CreateWebApplication([
            "--DorcApi:BaseUrl=https://dorc.local",
            "--DorcMcp:Transport=Http",
            "--DorcMcp:EndpointPath=/mcp"
        ]);
        Assert.IsNotNull(webAppCustom.Services.GetRequiredService<IDorcApiGateway>());

        await using var webAppBlankPath = DorcMcpHostConfiguration.CreateWebApplication([
            "--DorcApi:BaseUrl=https://dorc.local",
            "--DorcMcp:Transport=Http",
            "--DorcMcp:EndpointPath=   "
        ]);
        Assert.IsNotNull(webAppBlankPath.Services.GetRequiredService<IDorcApiGateway>());
    }
}
