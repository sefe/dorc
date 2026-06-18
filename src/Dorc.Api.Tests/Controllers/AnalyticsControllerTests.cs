using Dorc.Api.Controllers;
using Dorc.ApiModel;
using Dorc.PersistentData.Sources.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Dorc.Api.Tests.Controllers
{
    [TestClass]
    public class AnalyticsControllerTests
    {
        private IAnalyticsPersistentSource _analyticsPersistentSource;
        private ILogger<AnalyticsController> _logger;
        private AnalyticsController _controller;

        [TestInitialize]
        public void Setup()
        {
            _analyticsPersistentSource = Substitute.For<IAnalyticsPersistentSource>();
            _logger = Substitute.For<ILogger<AnalyticsController>>();
            _controller = new AnalyticsController(_analyticsPersistentSource, _logger);
        }

        [TestMethod]
        public void GetDeploymentSummary_ReturnsOkWithSummary()
        {
            var summary = new AnalyticsDeploymentSummaryApiModel { TotalDeployments = 42 };
            _analyticsPersistentSource.GetDeploymentSummary().Returns(summary);

            var result = _controller.GetDeploymentSummary();

            Assert.IsInstanceOfType(result, typeof(OkObjectResult));
            var ok = result as OkObjectResult;
            Assert.IsNotNull(ok);
            Assert.AreSame(summary, ok.Value);
        }

        [TestMethod]
        public void GetDeploymentSummary_OnException_Returns500()
        {
            _analyticsPersistentSource.GetDeploymentSummary()
                .Returns(_ => throw new InvalidOperationException("boom"));

            var result = _controller.GetDeploymentSummary();

            Assert.IsInstanceOfType(result, typeof(ObjectResult));
            var status = result as ObjectResult;
            Assert.IsNotNull(status);
            Assert.AreEqual(StatusCodes.Status500InternalServerError, status.StatusCode);
        }

        [TestMethod]
        public void GetDeploymentsMonth_ReturnsOkWithData()
        {
            var data = new List<AnalyticsDeploymentsPerProjectApiModel>
            {
                new() { ProjectName = "A", CountOfDeployments = 5 }
            };
            _analyticsPersistentSource.GetCountDeploymentsPerProjectMonth().Returns(data);

            var result = _controller.GetDeploymentsMonth();

            Assert.IsInstanceOfType(result, typeof(OkObjectResult));
            var ok = result as OkObjectResult;
            Assert.IsNotNull(ok);
            Assert.AreSame(data, ok.Value);
        }

        [TestMethod]
        public void GetDeploymentsMonth_OnException_Returns500()
        {
            _analyticsPersistentSource.GetCountDeploymentsPerProjectMonth()
                .Returns(_ => throw new InvalidOperationException("boom"));

            var result = _controller.GetDeploymentsMonth();

            Assert.IsInstanceOfType(result, typeof(ObjectResult));
            var status = result as ObjectResult;
            Assert.IsNotNull(status);
            Assert.AreEqual(StatusCodes.Status500InternalServerError, status.StatusCode);
        }

        [TestMethod]
        public void GetDuration_OnException_Returns500()
        {
            _analyticsPersistentSource.GetDeploymentDuration()
                .Returns(_ => throw new InvalidOperationException("boom"));

            var result = _controller.GetDuration();

            Assert.IsInstanceOfType(result, typeof(ObjectResult));
            var status2 = result as ObjectResult;
            Assert.IsNotNull(status2);
            Assert.AreEqual(StatusCodes.Status500InternalServerError, status2.StatusCode);
        }

        [TestMethod]
        public void GetMonthlyOutcomes_ReturnsOkWithData()
        {
            var data = new List<AnalyticsMonthlyOutcomeApiModel>
            {
                new() { Year = 2026, Month = 1, IsProd = true, CountOfDeployments = 10, Failed = 1, Cancelled = 0 }
            };
            _analyticsPersistentSource.GetMonthlyOutcomes().Returns(data);

            var result = _controller.GetMonthlyOutcomes();

            Assert.IsInstanceOfType(result, typeof(OkObjectResult));
            Assert.AreSame(data, ((OkObjectResult)result).Value);
        }

        [TestMethod]
        public void GetMonthlyOutcomes_OnException_Returns500()
        {
            _analyticsPersistentSource.GetMonthlyOutcomes()
                .Returns(_ => throw new InvalidOperationException("boom"));

            var result = _controller.GetMonthlyOutcomes();

            Assert.IsInstanceOfType(result, typeof(ObjectResult));
            Assert.AreEqual(StatusCodes.Status500InternalServerError, ((ObjectResult)result).StatusCode);
        }

        [TestMethod]
        public void GetEnvironmentWaitTimes_ReturnsOkWithData()
        {
            var data = new List<AnalyticsEnvironmentWaitApiModel>
            {
                new() { EnvironmentName = "ENV1", MedianWaitMinutes = 5, SampleCount = 10 }
            };
            _analyticsPersistentSource.GetEnvironmentWaitTimes().Returns(data);

            var result = _controller.GetEnvironmentWaitTimes();

            Assert.IsInstanceOfType(result, typeof(OkObjectResult));
            Assert.AreSame(data, ((OkObjectResult)result).Value);
        }

        [TestMethod]
        public void GetEnvironmentWaitTimes_OnException_Returns500()
        {
            _analyticsPersistentSource.GetEnvironmentWaitTimes()
                .Returns(_ => throw new InvalidOperationException("boom"));

            var result = _controller.GetEnvironmentWaitTimes();

            Assert.IsInstanceOfType(result, typeof(ObjectResult));
            Assert.AreEqual(StatusCodes.Status500InternalServerError, ((ObjectResult)result).StatusCode);
        }

        [TestMethod]
        public void GetProjectDurations_ReturnsOkWithData()
        {
            var data = new List<AnalyticsProjectDurationApiModel>
            {
                new() { ProjectName = "P1", MedianDurationMinutes = 4, SampleCount = 100 }
            };
            _analyticsPersistentSource.GetProjectDurations().Returns(data);

            var result = _controller.GetProjectDurations();

            Assert.IsInstanceOfType(result, typeof(OkObjectResult));
            Assert.AreSame(data, ((OkObjectResult)result).Value);
        }

        [TestMethod]
        public void GetProjectDurations_OnException_Returns500()
        {
            _analyticsPersistentSource.GetProjectDurations()
                .Returns(_ => throw new InvalidOperationException("boom"));

            var result = _controller.GetProjectDurations();

            Assert.IsInstanceOfType(result, typeof(ObjectResult));
            Assert.AreEqual(StatusCodes.Status500InternalServerError, ((ObjectResult)result).StatusCode);
        }

        [TestMethod]
        public void GetComponentReliability_ReturnsOkWithData()
        {
            var data = new List<AnalyticsComponentReliabilityApiModel>
            {
                new() { ComponentName = "C1", AttemptCount = 100, FailedCount = 5, RetryAttemptCount = 8 }
            };
            _analyticsPersistentSource.GetComponentReliability().Returns(data);

            var result = _controller.GetComponentReliability();

            Assert.IsInstanceOfType(result, typeof(OkObjectResult));
            Assert.AreSame(data, ((OkObjectResult)result).Value);
        }

        [TestMethod]
        public void GetComponentReliability_OnException_Returns500()
        {
            _analyticsPersistentSource.GetComponentReliability()
                .Returns(_ => throw new InvalidOperationException("boom"));

            var result = _controller.GetComponentReliability();

            Assert.IsInstanceOfType(result, typeof(ObjectResult));
            Assert.AreEqual(StatusCodes.Status500InternalServerError, ((ObjectResult)result).StatusCode);
        }

        [TestMethod]
        public void GetRecoveryTimes_ReturnsOkWithData()
        {
            var data = new List<AnalyticsRecoveryTimeApiModel>
            {
                new() { ProjectName = "P1", MedianRecoveryHours = 2, SampleCount = 6 }
            };
            _analyticsPersistentSource.GetRecoveryTimes().Returns(data);

            var result = _controller.GetRecoveryTimes();

            Assert.IsInstanceOfType(result, typeof(OkObjectResult));
            Assert.AreSame(data, ((OkObjectResult)result).Value);
        }

        [TestMethod]
        public void GetRecoveryTimes_OnException_Returns500()
        {
            _analyticsPersistentSource.GetRecoveryTimes()
                .Returns(_ => throw new InvalidOperationException("boom"));

            var result = _controller.GetRecoveryTimes();

            Assert.IsInstanceOfType(result, typeof(ObjectResult));
            Assert.AreEqual(StatusCodes.Status500InternalServerError, ((ObjectResult)result).StatusCode);
        }
    }
}
