using Dorc.ApiModel;
using Dorc.Core;
using NSubstitute;
using RestSharp;
using Tools.RequestCLI;

namespace Tools.RequestCLI.Tests;

[TestClass]
public class RequestMonitorTests
{
    [TestMethod]
    public void MonitorRequest_PollsUntilCompleted_ReturnsZeroAndRaisesStatusChangedEvents()
    {
        var api = Substitute.For<IDorcApiClient>();
        api.Call<RequestStatusDto>(Endpoints.Request, Method.Get, Arg.Any<Dictionary<string, string>>(), Arg.Any<string>())
            .Returns(
                new ApiResult<RequestStatusDto> { IsModelValid = true, Value = new RequestStatusDto { Id = 42, Status = "Pending" } },
                new ApiResult<RequestStatusDto> { IsModelValid = true, Value = new RequestStatusDto { Id = 42, Status = "Running" } },
                new ApiResult<RequestStatusDto> { IsModelValid = true, Value = new RequestStatusDto { Id = 42, Status = "Running" } },
                new ApiResult<RequestStatusDto> { IsModelValid = true, Value = new RequestStatusDto { Id = 42, Status = "Completed" } });

        var monitor = new RequestMonitor(api, pollIntervalMilliseconds: 0);
        var events = new List<string>();
        monitor.OnRequestStatusChanged += msg => events.Add(msg);

        var exitCode = monitor.MonitorRequest(42);

        Assert.AreEqual(0, exitCode);
        CollectionAssert.AreEqual(
            new[]
            {
                "Request 42 changed status to Running",
                "Request 42 changed status to Completed"
            },
            events);
    }

    [TestMethod]
    [DataRow("Failed")]
    [DataRow("Errored")]
    [DataRow("Cancelled")]
    public void MonitorRequest_WhenTerminalFailureStatusReached_ReturnsNonZeroExitCode(string terminalFailureStatus)
    {
        var api = Substitute.For<IDorcApiClient>();
        api.Call<RequestStatusDto>(Endpoints.Request, Method.Get, Arg.Any<Dictionary<string, string>>(), Arg.Any<string>())
            .Returns(
                new ApiResult<RequestStatusDto> { IsModelValid = true, Value = new RequestStatusDto { Id = 99, Status = "Running" } },
                new ApiResult<RequestStatusDto> { IsModelValid = true, Value = new RequestStatusDto { Id = 99, Status = terminalFailureStatus } });

        var monitor = new RequestMonitor(api, pollIntervalMilliseconds: 0);
        var exitCode = monitor.MonitorRequest(99);

        Assert.AreEqual(2, exitCode);
    }

    [TestMethod]
    public void MonitorRequest_WhenApiCallFails_ExitsWithNonZeroCode()
    {
        var api = Substitute.For<IDorcApiClient>();
        api.Call<RequestStatusDto>(Endpoints.Request, Method.Get, Arg.Any<Dictionary<string, string>>(), Arg.Any<string>())
            .Returns(new ApiResult<RequestStatusDto> { IsModelValid = false, ErrorMessage = "500 Server Error" });

        var monitor = new RequestMonitor(api, pollIntervalMilliseconds: 0);
        var exitCode = monitor.MonitorRequest(77);

        Assert.AreEqual(2, exitCode);
    }
}
