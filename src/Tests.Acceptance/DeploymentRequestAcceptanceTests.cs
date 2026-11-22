using System.Text.Json;
using Dorc.ApiModel;
using Microsoft.Extensions.Configuration;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RestSharp;
using Tests.Acceptance.Support;

namespace Tests.Acceptance
{
    [TestClass]
    [TestCategory("LiveDvAcceptance")]
    [TestCategory("LiveEnvironment")]
    public class DeploymentRequestAcceptanceTests
    {
        [TestInitialize]
        public void EnsureLiveEnvironmentEnabled()
        {
            var enabled = Environment.GetEnvironmentVariable("DORC_LIVE_DV_ENABLED");
            var configured = new Microsoft.Extensions.Configuration.ConfigurationBuilder()
                .AddJsonFile("appsettings.test.json", optional: true)
                .AddEnvironmentVariables()
                .Build();
            var baseUrl = configured.GetSection("DorcApi")["BaseUrl"]
                ?? configured.GetSection("AppSettings")["BaseAddress"];

            var hasConfiguredLiveUrl = !string.IsNullOrWhiteSpace(baseUrl)
                && !baseUrl.Contains("DEPLOYAPI.ENDPOINT", StringComparison.OrdinalIgnoreCase);

            if (!hasConfiguredLiveUrl &&
                !string.Equals(enabled, "true", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(enabled, "1", StringComparison.OrdinalIgnoreCase))
            {
                Assert.Inconclusive(
                    "Live DV acceptance tests require DORC_LIVE_DV_ENABLED=true (or a configured DorcApi:BaseUrl) and a reachable DOrc DV environment.");
            }
        }

        [TestMethod]
        public void GetEnvironments_ReturnsNonEmptyListFromLiveDvApi()
        {
            using var caller = new DorcApiClient();
            var result = caller.Call<List<EnvironmentApiModel>>(Endpoints.RefDataEnvironments, Method.Get);

            Assert.IsTrue(result.IsModelValid, result.Message);
            if (result.Model is not List<EnvironmentApiModel> model)
            {
                Assert.Fail("Expected a non-null List<EnvironmentApiModel> response.");
                return;
            }
            Assert.IsTrue(model.Count > 0, "Expected at least one environment from live DV API.");
        }

        [TestMethod]
        public void GetProjects_ReturnsNonEmptyListFromLiveDvApi()
        {
            using var caller = new DorcApiClient();
            var result = caller.Call<List<ProjectApiModel>>(Endpoints.RefDataProjects, Method.Get);

            Assert.IsTrue(result.IsModelValid, result.Message);
            if (result.Model is not List<ProjectApiModel> model)
            {
                Assert.Fail("Expected a non-null List<ProjectApiModel> response.");
                return;
            }
            Assert.IsTrue(model.Count > 0, "Expected at least one project from live DV API.");
        }

        [TestMethod]
        public void SubmitDeploymentRequest_WhenProjectAndEnvironmentConfigured_PollsUntilTerminalState()
        {
            var projectName = Environment.GetEnvironmentVariable("DORC_LIVE_DV_PROJECT");
            var environmentName = Environment.GetEnvironmentVariable("DORC_LIVE_DV_ENVIRONMENT");
            var componentName = Environment.GetEnvironmentVariable("DORC_LIVE_DV_COMPONENT");
            var buildNum = Environment.GetEnvironmentVariable("DORC_LIVE_DV_BUILD_NUM") ?? "1.0.0.1";

            if (string.IsNullOrWhiteSpace(projectName) ||
                string.IsNullOrWhiteSpace(environmentName) ||
                string.IsNullOrWhiteSpace(componentName))
            {
                Assert.Inconclusive(
                    "Set DORC_LIVE_DV_PROJECT, DORC_LIVE_DV_ENVIRONMENT, and DORC_LIVE_DV_COMPONENT to run live deployment acceptance.");
                return;
            }

            using var caller = new DorcApiClient();
            var payload = new RequestDto
            {
                Project = projectName,
                Environment = environmentName,
                BuildNum = buildNum,
                Components = new List<string> { componentName }
            };

            var createResult = caller.Call<RequestStatusDto>(
                Endpoints.Request,
                Method.Post,
                body: JsonSerializer.Serialize(payload));

            Assert.IsTrue(createResult.IsModelValid, createResult.Message);
            if (createResult.Model is not RequestStatusDto createdModel)
            {
                Assert.Fail("Expected a non-null RequestStatusDto response.");
                return;
            }
            Assert.IsTrue(createdModel.Id > 0);

            var deadline = DateTime.UtcNow.AddMinutes(3);
            string latestStatus = createdModel.Status;
            while (DateTime.UtcNow < deadline &&
                   !string.Equals(latestStatus, "Completed", StringComparison.OrdinalIgnoreCase) &&
                   !string.Equals(latestStatus, "WaitingConfirmation", StringComparison.OrdinalIgnoreCase) &&
                   !string.Equals(latestStatus, "Failed", StringComparison.OrdinalIgnoreCase) &&
                   !string.Equals(latestStatus, "Errored", StringComparison.OrdinalIgnoreCase) &&
                   !string.Equals(latestStatus, "Cancelled", StringComparison.OrdinalIgnoreCase))
            {
                Thread.Sleep(2000);
                var statusResult = caller.Call<DeploymentRequestApiModel>(
                    Endpoints.RequestStatuses,
                    Method.Get,
                    queryParameters: new Dictionary<string, string>
                    {
                        ["requestId"] = createdModel.Id.ToString()
                    });

                Assert.IsTrue(statusResult.IsModelValid, statusResult.Message);
                var statusModel = statusResult.Model as DeploymentRequestApiModel;
                latestStatus = statusModel?.Status ?? latestStatus;
            }

            Assert.IsTrue(
                string.Equals(latestStatus, "Completed", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(latestStatus, "WaitingConfirmation", StringComparison.OrdinalIgnoreCase),
                $"Expected request {createdModel.Id} to reach Completed or WaitingConfirmation, but got {latestStatus}.");
        }
    }
}
