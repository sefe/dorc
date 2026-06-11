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

            var status = result as ObjectResult;
            Assert.IsNotNull(status);
            Assert.AreEqual(StatusCodes.Status500InternalServerError, status.StatusCode);
        }
    }
}
