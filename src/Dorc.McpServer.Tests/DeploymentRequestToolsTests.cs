using Dorc.ApiModel;
using Dorc.McpServer.Tools;
using NSubstitute;

namespace Dorc.McpServer.Tests;

[TestClass]
public sealed class DeploymentRequestToolsTests
{
    [TestMethod]
    public async Task DeploymentRequestTools_CoversAllMethodsAndBranches()
    {
        var gateway = Substitute.For<IDorcApiGateway>();
        var allowGuard = new ProductionEnvironmentGuard(new DorcMcpOptions { ReadOnly = false, AllowProd = true });
        var readOnlyGuard = new ProductionEnvironmentGuard(new DorcMcpOptions { ReadOnly = true, AllowProd = false });

        Assert.ThrowsExactly<ArgumentNullException>(() => new DeploymentRequestTools(null!, allowGuard));
        Assert.ThrowsExactly<ArgumentNullException>(() => new DeploymentRequestTools(gateway, null!));

        var tools = new DeploymentRequestTools(gateway, allowGuard);
        var roTools = new DeploymentRequestTools(gateway, readOnlyGuard);

        // SearchDeploymentRequestsAsync with and without filters, plus RequestDetails XML redaction
        var searchXmlWithSecret = "<DeploymentRequestDetail><Properties><PropertyPair><Name>DatabasePassword</Name><Value>unflagged-secret</Value></PropertyPair></Properties></DeploymentRequestDetail>";
        gateway.PutAsync<PagedDataOperators, GetRequestStatusesListResponseDto>(
                "RequestStatuses?page=1&limit=100",
                Arg.Any<PagedDataOperators>(),
                Arg.Any<CancellationToken>())
            .Returns(McpToolOutcome.Success(new GetRequestStatusesListResponseDto
            {
                TotalItems = 2,
                Items =
                [
                    new DeploymentRequestApiModel { Id = 1, RequestDetails = searchXmlWithSecret },
                    new DeploymentRequestApiModel { Id = 2, RequestDetails = string.Empty }
                ]
            }));

        var search1 = await tools.SearchDeploymentRequestsAsync(0, 200, "ProjA", "DEV", "Failed", "alice");
        Assert.IsTrue(search1.Succeeded);
        Assert.IsFalse(search1.Data!.Items[0].RequestDetails.Contains("unflagged-secret", StringComparison.Ordinal));
        StringAssert.Contains(search1.Data.Items[0].RequestDetails, SecretRedactor.RedactedMarker);

        gateway.PutAsync<PagedDataOperators, GetRequestStatusesListResponseDto>(
                "RequestStatuses?page=2&limit=10",
                Arg.Is<PagedDataOperators>(op => op.Filters.Count == 0),
                Arg.Any<CancellationToken>())
            .Returns(McpToolOutcome.Success(new GetRequestStatusesListResponseDto { TotalItems = 0, Items = null! }));

        var search2 = await tools.SearchDeploymentRequestsAsync(2, 10);
        Assert.IsTrue(search2.Succeeded);

        gateway.PutAsync<PagedDataOperators, GetRequestStatusesListResponseDto>(
                "RequestStatuses?page=3&limit=10",
                Arg.Any<PagedDataOperators>(),
                Arg.Any<CancellationToken>())
            .Returns(McpToolOutcome.Failure<GetRequestStatusesListResponseDto>("err", 500));
        Assert.IsFalse((await tools.SearchDeploymentRequestsAsync(3, 10)).Succeeded);

        gateway.PutAsync<PagedDataOperators, GetRequestStatusesListResponseDto>(
                "RequestStatuses?page=4&limit=10",
                Arg.Any<PagedDataOperators>(),
                Arg.Any<CancellationToken>())
            .Returns(new McpToolOutcome<GetRequestStatusesListResponseDto>(true, null, null, 200));
        Assert.IsTrue((await tools.SearchDeploymentRequestsAsync(4, 10)).Succeeded);

        // GetDeploymentRequestAsync: success with XML redaction, success with empty XML, and failure
        var xmlWithSecret = "<DeploymentRequestDetail><Properties><PropertyPair><Name>SecretKey</Name><Value>my-secret</Value><IsSensitive>true</IsSensitive></PropertyPair></Properties></DeploymentRequestDetail>";
        gateway.GetAsync<DeploymentRequestApiModel>("RequestStatuses?requestId=10", Arg.Any<CancellationToken>())
            .Returns(McpToolOutcome.Success(new DeploymentRequestApiModel { Id = 10, RequestDetails = xmlWithSecret }));
        var req10 = await tools.GetDeploymentRequestAsync(10);
        Assert.IsTrue(req10.Succeeded);
        StringAssert.Contains(req10.Data!.RequestDetails, SecretRedactor.RedactedMarker);

        gateway.GetAsync<DeploymentRequestApiModel>("RequestStatuses?requestId=11", Arg.Any<CancellationToken>())
            .Returns(McpToolOutcome.Success(new DeploymentRequestApiModel { Id = 11, RequestDetails = string.Empty }));
        var req11 = await tools.GetDeploymentRequestAsync(11);
        Assert.IsTrue(req11.Succeeded);

        gateway.GetAsync<DeploymentRequestApiModel>("RequestStatuses?requestId=12", Arg.Any<CancellationToken>())
            .Returns(McpToolOutcome.Failure<DeploymentRequestApiModel>("err", 404));
        var req12 = await tools.GetDeploymentRequestAsync(12);
        Assert.IsFalse(req12.Succeeded);

        // Status, ComponentResults, Attempts
        gateway.GetAsync<RequestStatusDto>("Request?id=10", Arg.Any<CancellationToken>())
            .Returns(McpToolOutcome.Success(new RequestStatusDto { Id = 10, Status = "Complete" }));
        Assert.IsTrue((await tools.GetDeploymentRequestStatusAsync(10)).Succeeded);

        gateway.GetAsync<List<DeploymentResultApiModel>>("ResultStatuses?requestId=10", Arg.Any<CancellationToken>())
            .Returns(McpToolOutcome.Success(new List<DeploymentResultApiModel>()));
        Assert.IsTrue((await tools.GetDeploymentComponentResultsAsync(10)).Succeeded);

        gateway.GetAsync<List<DeploymentRequestAttemptApiModel>>("Request/10/attempts", Arg.Any<CancellationToken>())
            .Returns(McpToolOutcome.Success(new List<DeploymentRequestAttemptApiModel>()));
        Assert.IsTrue((await tools.GetDeploymentAttemptsAsync(10)).Succeeded);

        // Component log (success & failure)
        gateway.GetStringAsync("ResultStatuses/Log?requestId=10&resultId=20", Arg.Any<CancellationToken>())
            .Returns(McpToolOutcome.Success("comp-log", 200));
        var compLogOk = await tools.GetDeploymentComponentLogAsync(10, 20);
        Assert.AreEqual("comp-log", compLogOk.Data);

        gateway.GetStringAsync("ResultStatuses/Log?requestId=10&resultId=21", Arg.Any<CancellationToken>())
            .Returns(McpToolOutcome.Failure<string>("missing", 404));
        Assert.IsFalse((await tools.GetDeploymentComponentLogAsync(10, 21)).Succeeded);

        // Attempt log (success & failure)
        gateway.GetStringAsync("Request/10/attempts/log?deploymentResultId=30", Arg.Any<CancellationToken>())
            .Returns(McpToolOutcome.Success("attempt-log", 200));
        Assert.AreEqual("attempt-log", (await tools.GetDeploymentAttemptLogAsync(10, 30)).Data);

        gateway.GetStringAsync("Request/10/attempts/log?deploymentResultId=31", Arg.Any<CancellationToken>())
            .Returns(McpToolOutcome.Failure<string>("missing", 404));
        Assert.IsFalse((await tools.GetDeploymentAttemptLogAsync(10, 31)).Succeeded);

        // Request log: visibility check fails (with error and with null error), or succeeds using Data.Log directly (with and without HttpStatusCode)
        gateway.GetAsync<DeploymentRequestApiModel>("RequestStatuses?requestId=90", Arg.Any<CancellationToken>())
            .Returns(McpToolOutcome.Failure<DeploymentRequestApiModel>("forbidden", 403));
        Assert.IsFalse((await tools.GetDeploymentRequestLogAsync(90)).Succeeded);

        gateway.GetAsync<DeploymentRequestApiModel>("RequestStatuses?requestId=91", Arg.Any<CancellationToken>())
            .Returns(new McpToolOutcome<DeploymentRequestApiModel>(true, null, null, 200));
        StringAssert.Contains((await tools.GetDeploymentRequestLogAsync(91)).Error!, "not found");

        gateway.GetAsync<DeploymentRequestApiModel>("RequestStatuses?requestId=92", Arg.Any<CancellationToken>())
            .Returns(new McpToolOutcome<DeploymentRequestApiModel>(true, new DeploymentRequestApiModel { Id = 92, Log = "req-log-null-status" }, null, null));
        Assert.AreEqual("req-log-null-status", (await tools.GetDeploymentRequestLogAsync(92)).Data);

        gateway.GetAsync<DeploymentRequestApiModel>("RequestStatuses?requestId=93", Arg.Any<CancellationToken>())
            .Returns(McpToolOutcome.Success(new DeploymentRequestApiModel { Id = 93, Log = "req-log-ok" }, 200));
        Assert.AreEqual("req-log-ok", (await tools.GetDeploymentRequestLogAsync(93)).Data);

        // Mutations: blocked by guard vs allowed
        Assert.IsFalse((await roTools.CreateDeploymentRequestAsync("P", "DEV", ["C1"])).Succeeded);
        Assert.IsFalse((await roTools.RestartDeploymentRequestAsync(10)).Succeeded);
        Assert.IsFalse((await roTools.CancelDeploymentRequestAsync(10)).Succeeded);
        Assert.IsFalse((await roTools.PauseDeploymentRequestAsync(10)).Succeeded);
        Assert.IsFalse((await roTools.ResumeDeploymentRequestAsync(10)).Succeeded);

        gateway.PostAsync<RequestDto, RequestStatusDto>("Request", Arg.Any<RequestDto>(), Arg.Any<CancellationToken>())
            .Returns(McpToolOutcome.Success(new RequestStatusDto { Id = 55, Status = "Pending" }));
        Assert.IsTrue((await tools.CreateDeploymentRequestAsync("P", "DEV", null!, "url", "num", "txt", true, [])).Succeeded);
        Assert.IsTrue((await tools.CreateDeploymentRequestAsync("P", "DEV", ["C1"], null, null, null, false, null)).Succeeded);

        gateway.PostAsync<object?, RequestStatusDto>("Request/restart?requestId=10", null, Arg.Any<CancellationToken>())
            .Returns(McpToolOutcome.Success(new RequestStatusDto { Id = 10 }));
        Assert.IsTrue((await tools.RestartDeploymentRequestAsync(10)).Succeeded);

        gateway.PutAsync<object?, RequestStatusDto>("Request/cancel?requestId=10", null, Arg.Any<CancellationToken>())
            .Returns(McpToolOutcome.Success(new RequestStatusDto { Id = 10 }));
        Assert.IsTrue((await tools.CancelDeploymentRequestAsync(10)).Succeeded);

        gateway.PutAsync<object?, RequestStatusDto>("Request/pause?requestId=10", null, Arg.Any<CancellationToken>())
            .Returns(McpToolOutcome.Success(new RequestStatusDto { Id = 10 }));
        Assert.IsTrue((await tools.PauseDeploymentRequestAsync(10)).Succeeded);

        gateway.PutAsync<object?, RequestStatusDto>("Request/resume?requestId=10", null, Arg.Any<CancellationToken>())
            .Returns(McpToolOutcome.Success(new RequestStatusDto { Id = 10 }));
        Assert.IsTrue((await tools.ResumeDeploymentRequestAsync(10)).Succeeded);
    }
}
