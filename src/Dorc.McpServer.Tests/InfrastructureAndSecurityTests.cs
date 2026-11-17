using System.Net;
using System.Text;
using System.Text.Json;
using Dorc.ApiModel;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

[assembly: Parallelize(Scope = ExecutionScope.MethodLevel)]

namespace Dorc.McpServer.Tests;

internal sealed class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _responder;

    public StubHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder)
    {
        _responder = responder;
    }

    public List<HttpRequestMessage> SentRequests { get; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        SentRequests.Add(request);
        return await _responder(request, cancellationToken).ConfigureAwait(false);
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

        var mcpOptions = new DorcMcpOptions { ReadOnly = false, AllowProd = true };
        Assert.IsFalse(mcpOptions.ReadOnly);
        Assert.IsTrue(mcpOptions.AllowProd);

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
        StringAssert.Contains(await nonProdGuard.CheckEnvironmentMutationAllowedAsync("op", "LIVE-01", gateway, CancellationToken.None), "EnvironmentIsProd=true");

        // Environment has EnvironmentIsProd = false -> allowed
        gateway.GetAsync<List<EnvironmentApiModel>>("RefDataEnvironments?env=DEV-01", Arg.Any<CancellationToken>())
            .Returns(McpToolOutcome.Success(new List<EnvironmentApiModel> { new() { EnvironmentName = "DEV-01", EnvironmentIsProd = false } }));
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
    }

    [TestMethod]
    public async Task OAuthTokenAcquirer_And_BearerHandler_CoverAllFlows()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() => new OAuthTokenAcquirer(null!, new DorcApiOptions()));
        Assert.ThrowsExactly<ArgumentNullException>(() => new OAuthTokenAcquirer(new HttpClient(), null!));
        Assert.ThrowsExactly<ArgumentNullException>(() => new BearerTokenRefreshingHandler(null!));

        // Missing BaseUrl
        using (var acquirerNoUrl = new OAuthTokenAcquirer(new HttpClient(), new DorcApiOptions()))
        {
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
                acquirerNoUrl.GetAccessTokenAsync(false, CancellationToken.None));
        }

        // Missing ClientId / ClientSecret
        using (var acquirerNoClient = new OAuthTokenAcquirer(new HttpClient(), new DorcApiOptions { BaseUrl = "https://dorc.local", ClientId = "id", ClientSecret = "" }))
        {
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
                acquirerNoClient.GetAccessTokenAsync(false, CancellationToken.None));
        }

        using (var acquirerNoClientId = new OAuthTokenAcquirer(new HttpClient(), new DorcApiOptions { BaseUrl = "https://dorc.local", ClientId = "", ClientSecret = "sec" }))
        {
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
                acquirerNoClientId.GetAccessTokenAsync(false, CancellationToken.None));
        }

        // Invalid /Metadata (missing Authority)
        using (var badMetaHandler = new StubHttpMessageHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json")
            })))
        using (var badMetaAcquirer = new OAuthTokenAcquirer(new HttpClient(badMetaHandler), new DorcApiOptions { BaseUrl = "https://dorc.local", ClientId = "c", ClientSecret = "s" }))
        {
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
                badMetaAcquirer.GetAccessTokenAsync(false, CancellationToken.None));
        }

        // Invalid discovery (missing token_endpoint)
        using (var badDiscHandler = new StubHttpMessageHandler((req, _) =>
        {
            if (req.RequestUri!.AbsolutePath.EndsWith("/Metadata", StringComparison.Ordinal))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"Authority\":\"https://idp.local\"}", Encoding.UTF8, "application/json")
                });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json")
            });
        }))
        using (var badDiscAcquirer = new OAuthTokenAcquirer(new HttpClient(badDiscHandler), new DorcApiOptions { BaseUrl = "https://dorc.local", ClientId = "c", ClientSecret = "s" }))
        {
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
                badDiscAcquirer.GetAccessTokenAsync(false, CancellationToken.None));
        }

        // Invalid token response (missing access_token)
        using (var badTokenHandler = new StubHttpMessageHandler((req, _) =>
        {
            if (req.RequestUri!.AbsolutePath.EndsWith("/Metadata", StringComparison.Ordinal))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"Authority\":\"https://idp.local\"}", Encoding.UTF8, "application/json")
                });
            }

            if (req.RequestUri.AbsolutePath.Contains("openid-configuration", StringComparison.Ordinal))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"token_endpoint\":\"https://idp.local/connect/token\"}", Encoding.UTF8, "application/json")
                });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json")
            });
        }))
        using (var badTokenAcquirer = new OAuthTokenAcquirer(new HttpClient(badTokenHandler), new DorcApiOptions { BaseUrl = "https://dorc.local", ClientId = "c", ClientSecret = "s", Scope = "" }))
        {
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
                badTokenAcquirer.GetAccessTokenAsync(false, CancellationToken.None));
        }

        // Full happy path + caching + expiry + forceRefresh + default expires_in fallback
        var tokenCallCount = 0;
        var fakeTime = new FakeTimeProvider();
        using var oauthHandler = new StubHttpMessageHandler((req, _) =>
        {
            if (req.RequestUri!.AbsolutePath.EndsWith("/Metadata", StringComparison.Ordinal))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"Authority\":\"https://idp.local\"}", Encoding.UTF8, "application/json")
                });
            }

            if (req.RequestUri.AbsolutePath.Contains("openid-configuration", StringComparison.Ordinal))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"token_endpoint\":\"https://idp.local/connect/token\"}", Encoding.UTF8, "application/json")
                });
            }

            tokenCallCount++;
            var body = tokenCallCount == 1
                ? "{\"access_token\":\"tok-1\",\"expires_in\":120}"
                : "{\"access_token\":\"tok-2\",\"expires_in\":\"not-a-number\"}";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            });
        });

        using var acquirer = new OAuthTokenAcquirer(
            new HttpClient(oauthHandler),
            new DorcApiOptions { BaseUrl = "https://dorc.local", ClientId = "cid", ClientSecret = "csec", Scope = "dorc-api.manage" },
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
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized));
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"Id\":1,\"Status\":\"Ok\"}", Encoding.UTF8, "application/json")
            });
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
                "json-ok" => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"Id\":7,\"Status\":\"Complete\"}", Encoding.UTF8, "application/json")
                }),
                "empty-ok" => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent)
                {
                    Content = new StringContent(string.Empty)
                }),
                "error-with-body" => Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest)
                {
                    Content = new StringContent("Bad input payload")
                }),
                "error-empty" => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden)
                {
                    ReasonPhrase = "Forbidden",
                    Content = new StringContent("   ")
                }),
                "string-quoted" => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("\"Line 1\\nLine 2\"", Encoding.UTF8, "application/json")
                }),
                "string-null-literal" => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("Plain unquoted text", Encoding.UTF8, "text/plain")
                }),
                _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK))
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
    public void DorcMcpHostConfiguration_ResolvesOptionsAndBuildsHost()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() => DorcMcpHostConfiguration.ResolveApiOptions(null!));
        Assert.ThrowsExactly<ArgumentNullException>(() => DorcMcpHostConfiguration.ResolveMcpOptions(null!));

        var configFull = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DORC_API_BASE_URL"] = " https://dorc.env.local/ ",
                ["DORC_API_CLIENT_ID"] = " env-client ",
                ["DORC_API_CLIENT_SECRET"] = " env-secret ",
                ["DORC_API_SCOPE"] = " env-scope ",
                ["DORC_MCP_READ_ONLY"] = "false",
                ["DORC_MCP_ALLOW_PROD"] = "true"
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

        var configEmpty = new ConfigurationBuilder().AddInMemoryCollection([]).Build();
        var defaultApiOpts = DorcMcpHostConfiguration.ResolveApiOptions(configEmpty);
        Assert.AreEqual("dorc-api.manage", defaultApiOpts.Scope);
        var defaultMcpOpts = DorcMcpHostConfiguration.ResolveMcpOptions(configEmpty);
        Assert.IsTrue(defaultMcpOpts.ReadOnly);
        Assert.IsFalse(defaultMcpOpts.AllowProd);

        // Test CreateHostBuilder with both non-empty and empty BaseUrl
        using var hostWithUrl = DorcMcpHostConfiguration.CreateHostBuilder(["--DorcApi:BaseUrl=https://dorc.local"]).Build();
        var gatewayWithUrl = hostWithUrl.Services.GetRequiredService<IDorcApiGateway>();
        Assert.IsNotNull(gatewayWithUrl);

        using var hostWithoutUrl = DorcMcpHostConfiguration.CreateHostBuilder(["--DorcApi:BaseUrl="]).Build();
        var gatewayWithoutUrl = hostWithoutUrl.Services.GetRequiredService<IDorcApiGateway>();
        Assert.IsNotNull(gatewayWithoutUrl);
    }
}
