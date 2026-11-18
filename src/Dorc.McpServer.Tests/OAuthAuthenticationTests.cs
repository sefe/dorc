using System.Net;
using System.Text;
using Microsoft.AspNetCore.Http;

namespace Dorc.McpServer.Tests;

[TestClass]
public sealed class OAuthAuthenticationTests
{
    [TestMethod]
    public async Task OAuthTokenAcquirer_And_Handlers_CoverAllFlows()
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

        // Invalid discovery (missing token_endpoint or mismatched token_endpoint origin)
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

        using (var mismatchedOriginHandler = new StubHttpMessageHandler((req, _) =>
        {
            if (req.RequestUri!.AbsolutePath.EndsWith("/ApiConfig", StringComparison.Ordinal))
            {
                return Task.FromResult(StubHttpMessageHandler.CreateJsonResponse("{\"OAuthAuthority\":\"https://idp.local:8443\"}"));
            }

            return Task.FromResult(StubHttpMessageHandler.CreateJsonResponse("{\"token_endpoint\":\"https://evil.local:8443/connect/token\"}"));
        }))
        using (var mismatchedOriginClient = new HttpClient(mismatchedOriginHandler))
        using (var mismatchedOriginAcquirer = new OAuthTokenAcquirer(mismatchedOriginClient, new DorcApiOptions { BaseUrl = "https://dorc.local", ClientId = "c", ClientSecret = "s" }))
        {
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
                mismatchedOriginAcquirer.GetAccessTokenAsync(false, CancellationToken.None));
        }

        using (var mismatchedSchemeHandler = new StubHttpMessageHandler((req, _) =>
        {
            if (req.RequestUri!.AbsolutePath.EndsWith("/ApiConfig", StringComparison.Ordinal))
            {
                return Task.FromResult(StubHttpMessageHandler.CreateJsonResponse("{\"OAuthAuthority\":\"https://idp.local\"}"));
            }

            return Task.FromResult(StubHttpMessageHandler.CreateJsonResponse("{\"token_endpoint\":\"http://idp.local/connect/token\"}"));
        }))
        using (var mismatchedSchemeClient = new HttpClient(mismatchedSchemeHandler))
        using (var mismatchedSchemeAcquirer = new OAuthTokenAcquirer(mismatchedSchemeClient, new DorcApiOptions { BaseUrl = "https://dorc.local", ClientId = "c", ClientSecret = "s" }))
        {
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
                mismatchedSchemeAcquirer.GetAccessTokenAsync(false, CancellationToken.None));
        }

        using (var mismatchedPortHandler = new StubHttpMessageHandler((req, _) =>
        {
            if (req.RequestUri!.AbsolutePath.EndsWith("/ApiConfig", StringComparison.Ordinal))
            {
                return Task.FromResult(StubHttpMessageHandler.CreateJsonResponse("{\"OAuthAuthority\":\"https://idp.local:8443\"}"));
            }

            return Task.FromResult(StubHttpMessageHandler.CreateJsonResponse("{\"token_endpoint\":\"https://idp.local:9443/connect/token\"}"));
        }))
        using (var mismatchedPortClient = new HttpClient(mismatchedPortHandler))
        using (var mismatchedPortAcquirer = new OAuthTokenAcquirer(mismatchedPortClient, new DorcApiOptions { BaseUrl = "https://dorc.local", ClientId = "c", ClientSecret = "s" }))
        {
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
                mismatchedPortAcquirer.GetAccessTokenAsync(false, CancellationToken.None));
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
        using var innerApiHandler = new StubHttpMessageHandler((_, _) =>
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
}
