using System.Net;
using System.Net.Http.Json;
using Dorc.ApiModel;
using Dorc.PersistentData.Model;

namespace Dorc.Api.Tests.Controllers;

[TestClass]
[TestCategory("Database")]
[DoNotParallelize]
public class RequestStatusesControllerTests
{
    private static DorcApiFixture _fixture = null!;

    [ClassInitialize]
    public static void Initialize(TestContext _) => _fixture = new DorcApiFixture("dorc_api_req_life");

    [ClassCleanup]
    public static void Cleanup() => _fixture?.Dispose();

    [TestMethod]
    public async Task RequestPauseResumeCancelRestart_AndSecretRedaction_WorkEndToEnd()
    {
        using var adminClient = _fixture.CreateClientForUser("admin-oid", "admin.user@sefe.eu", "Admin");

        var envName = "ReqLifecycleEnv_" + Guid.NewGuid().ToString("N")[..8];
        var projName = "ReqLifecycleProj_" + Guid.NewGuid().ToString("N")[..8];

        await adminClient.PostAsJsonAsync("/RefDataEnvironments", new EnvironmentApiModel
        {
            EnvironmentName = envName,
            Details = new EnvironmentDetailsApiModel { EnvironmentOwner = "admin.user@sefe.eu" }
        }, _fixture.JsonOptions);

        int requestId;
        int resultId;
        using (var context = _fixture.ContextFactory.GetContext())
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

        var statusGetResp = await adminClient.GetFromJsonAsync<DeploymentRequestApiModel>(
            $"/RequestStatuses?requestId={requestId}", _fixture.JsonOptions);
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
            }, _fixture.JsonOptions);
        Assert.AreEqual(HttpStatusCode.OK, statusListResp.StatusCode);
        var statusList = await statusListResp.Content.ReadFromJsonAsync<GetRequestStatusesListResponseDto>(_fixture.JsonOptions);
        Assert.IsNotNull(statusList);
        var listedReq = statusList.Items.Single(i => i.Id == requestId);
        Assert.IsFalse(listedReq.RequestDetails.Contains("SuperSecretCleartext123!", StringComparison.Ordinal));

        var pauseResp = await adminClient.PutAsync($"/Request/pause?requestId={requestId}", null);
        Assert.AreEqual(HttpStatusCode.OK, pauseResp.StatusCode);
        var pausedDto = await pauseResp.Content.ReadFromJsonAsync<RequestStatusDto>(_fixture.JsonOptions);
        Assert.AreEqual(DeploymentRequestStatus.Paused.ToString(), pausedDto!.Status);

        var resumeResp = await adminClient.PutAsync($"/Request/resume?requestId={requestId}", null);
        Assert.AreEqual(HttpStatusCode.OK, resumeResp.StatusCode);
        var resumedDto = await resumeResp.Content.ReadFromJsonAsync<RequestStatusDto>(_fixture.JsonOptions);
        Assert.AreEqual(DeploymentRequestStatus.Pending.ToString(), resumedDto!.Status);

        var cancelResp = await adminClient.PutAsync($"/Request/cancel?requestId={requestId}", null);
        Assert.AreEqual(HttpStatusCode.OK, cancelResp.StatusCode);
        var cancelledDto = await cancelResp.Content.ReadFromJsonAsync<RequestStatusDto>(_fixture.JsonOptions);
        Assert.AreEqual(DeploymentRequestStatus.Cancelled.ToString(), cancelledDto!.Status);

        var restartResp = await adminClient.PostAsync($"/Request/restart?requestId={requestId}", null);
        Assert.AreEqual(HttpStatusCode.OK, restartResp.StatusCode);
        var restartedDto = await restartResp.Content.ReadFromJsonAsync<RequestStatusDto>(_fixture.JsonOptions);
        Assert.AreEqual(DeploymentRequestStatus.Restarting.ToString(), restartedDto!.Status);

        var attemptsResp = await adminClient.GetFromJsonAsync<List<DeploymentRequestAttemptApiModel>>(
            $"/Request/{requestId}/attempts", _fixture.JsonOptions);
        Assert.IsNotNull(attemptsResp);
        Assert.AreEqual(1, attemptsResp.Count);

        var resultsResp = await adminClient.GetFromJsonAsync<List<DeploymentResultApiModel>>(
            $"/ResultStatuses?requestId={requestId}", _fixture.JsonOptions);
        Assert.IsNotNull(resultsResp);
        Assert.AreEqual(1, resultsResp.Count);
        Assert.AreEqual(resultId, resultsResp[0].Id);
    }

    [TestMethod]
    public async Task DeploymentsHub_QueryAccessToken_EnforcesJwtSchemeAndApiScopeInCombinedAuthMode()
    {
        Assert.ThrowsExactly<ArgumentException>(() => Dorc.Api.Security.ContextExtensions.GetAuthenticationScheme(null));

        var hubContextWithQueryToken = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        hubContextWithQueryToken.Request.Path = "/hubs/deployments";
        hubContextWithQueryToken.Request.QueryString = new Microsoft.AspNetCore.Http.QueryString("?access_token=test-jwt");
        Assert.AreEqual(
            Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerDefaults.AuthenticationScheme,
            Dorc.Api.Security.ContextExtensions.GetAuthenticationScheme(hubContextWithQueryToken));

        using var rawClient = _fixture.HostFactory.CreateClient();

        // 1. Hub negotiate with query ?access_token=... and valid scope succeeds in OAuth+WinAuth mode
        using var validHubReq = new HttpRequestMessage(HttpMethod.Post, "/hubs/deployments/negotiate?negotiateVersion=1&access_token=valid-token");
        validHubReq.Headers.Add("X-Test-User-Id", "admin-oid");
        validHubReq.Headers.Add("X-Test-User-Email", "admin.user@sefe.eu");
        using var validHubResp = await rawClient.SendAsync(validHubReq);
        Assert.AreEqual(HttpStatusCode.OK, validHubResp.StatusCode);

        // 2. Hub negotiate with query ?access_token=... but missing scope claim is rejected with 403 Forbidden
        using var missingScopeHubReq = new HttpRequestMessage(HttpMethod.Post, "/hubs/deployments/negotiate?negotiateVersion=1&access_token=no-scope-token");
        missingScopeHubReq.Headers.Add("X-Test-User-Id", "admin-oid");
        missingScopeHubReq.Headers.Add("X-Test-User-Email", "admin.user@sefe.eu");
        missingScopeHubReq.Headers.Add("X-Test-Scope", "none");
        using var missingScopeHubResp = await rawClient.SendAsync(missingScopeHubReq);
        Assert.AreEqual(HttpStatusCode.Forbidden, missingScopeHubResp.StatusCode);

        // 3. Windows Negotiate principal without scope claim is permitted in OAuth+WinAuth mode
        using var winClient = _fixture.CreateClientForUser("win-sid", "win.user@sefe.eu", "Admin", "Negotiate");
        using var winHubResp = await winClient.PostAsync("/hubs/deployments/negotiate?negotiateVersion=1", null);
        Assert.AreEqual(HttpStatusCode.OK, winHubResp.StatusCode);
    }
}
