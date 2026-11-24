using System.Security.Claims;
using Dorc.Api.Controllers;
using Dorc.ApiModel;
using Dorc.OpenSearchData.Model;
using Dorc.OpenSearchData.Sources;
using Dorc.OpenSearchData.Sources.Interfaces;
using Dorc.PersistentData.Sources.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using NSubstitute;
using OpenSearch.Client;

namespace Dorc.Api.Tests.Controllers
{
    [TestClass]
    public class ResultStatusesControllerTests
    {
        private IRequestsPersistentSource _requestsPersistentSource = null!;
        private IDeploymentLogService _deploymentLogService = null!;
        private ResultStatusesController _sut = null!;

        [TestInitialize]
        public void Setup()
        {
            _requestsPersistentSource = Substitute.For<IRequestsPersistentSource>();
            _deploymentLogService = Substitute.For<IDeploymentLogService>();
            _sut = new ResultStatusesController(_requestsPersistentSource, _deploymentLogService)
            {
                ControllerContext = new ControllerContext
                {
                    HttpContext = new DefaultHttpContext
                    {
                        User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, "tester") }, "TestAuth"))
                    }
                }
            };
        }

        [TestMethod]
        public void GetComponents_WhenRequestNotFound_ReturnsEmptyList()
        {
            _requestsPersistentSource.GetRequestForUser(100, Arg.Any<ClaimsPrincipal>())
                .Returns((DeploymentRequestApiModel?)null);

            var actionResult = _sut.GetComponents(100) as OkObjectResult;

            Assert.IsNotNull(actionResult);
            var results = actionResult.Value as IEnumerable<DeploymentResultApiModel>;
            Assert.IsNotNull(results);
            Assert.AreEqual(0, results.Count());
        }

        [TestMethod]
        public void GetComponents_WhenRequestExists_EnrichesResultsWithLogs()
        {
            _requestsPersistentSource.GetRequestForUser(100, Arg.Any<ClaimsPrincipal>())
                .Returns(new DeploymentRequestApiModel { Id = 100 });
            var models = new List<DeploymentResultApiModel>
            {
                new() { Id = 10, RequestId = 100, Log = "Persisted log" }
            };
            _requestsPersistentSource.GetDeploymentResultsForRequest(100).Returns(models);

            var actionResult = _sut.GetComponents(100) as OkObjectResult;

            Assert.IsNotNull(actionResult);
            _deploymentLogService.Received(1).EnrichDeploymentResultsWithLogs(models, 3);
            Assert.AreSame(models, actionResult.Value);
        }

        [TestMethod]
        public void GetLog_WhenRequestNotFound_ReturnsNotFound()
        {
            _requestsPersistentSource.GetRequestForUser(100, Arg.Any<ClaimsPrincipal>())
                .Returns((DeploymentRequestApiModel?)null);

            var actionResult = _sut.GetLog(100, 10) as NotFoundObjectResult;

            Assert.IsNotNull(actionResult);
        }

        [TestMethod]
        public void GetLog_WhenResultNotFoundOrDifferentRequest_ReturnsNotFound()
        {
            _requestsPersistentSource.GetRequestForUser(100, Arg.Any<ClaimsPrincipal>())
                .Returns(new DeploymentRequestApiModel { Id = 100 });
            _requestsPersistentSource.GetDeploymentResults(10)
                .Returns((DeploymentResultApiModel?)null);

            Assert.IsInstanceOfType(_sut.GetLog(100, 10), typeof(NotFoundObjectResult));

            _requestsPersistentSource.GetDeploymentResults(10)
                .Returns(new DeploymentResultApiModel { Id = 10, RequestId = 999 });

            Assert.IsInstanceOfType(_sut.GetLog(100, 10), typeof(NotFoundObjectResult));
        }

        [TestMethod]
        public void GetLog_WhenOpenSearchHasLogsAndSqlHasMonitorInterruptedLog_AppendsPersistedLog()
        {
            _requestsPersistentSource.GetRequestForUser(2105132, Arg.Any<ClaimsPrincipal>())
                .Returns(new DeploymentRequestApiModel { Id = 2105132 });
            const string interruptedLine = "[2026-11-24 15:23:10.000000] [ERROR]   The previous component execution was interrupted because the active monitor stopped or lost its lock. Reconcile infrastructure/state and submit a fresh deployment; it will not be replayed automatically.";
            _requestsPersistentSource.GetDeploymentResults(10)
                .Returns(new DeploymentResultApiModel { Id = 10, RequestId = 2105132, Log = interruptedLine });
            _deploymentLogService.GetLogsForSingleResult(2105132, 10)
                .Returns("[2026-11-24 15:19:40.938692] [INFO]   Online and available for deployment");

            var actionResult = _sut.GetLog(2105132, 10) as OkObjectResult;

            Assert.IsNotNull(actionResult);
            var expected = "[2026-11-24 15:19:40.938692] [INFO]   Online and available for deployment"
                + Environment.NewLine
                + interruptedLine;
            Assert.AreEqual(expected, actionResult.Value);
        }

        [TestMethod]
        public void GetLog_WhenOpenSearchEmptyOrUnavailableAndSqlHasPersistedLog_ReturnsPersistedLog()
        {
            _requestsPersistentSource.GetRequestForUser(100, Arg.Any<ClaimsPrincipal>())
                .Returns(new DeploymentRequestApiModel { Id = 100 });
            _requestsPersistentSource.GetDeploymentResults(10)
                .Returns(new DeploymentResultApiModel { Id = 10, RequestId = 100, Log = "Persisted SQL log" });

            _deploymentLogService.GetLogsForSingleResult(100, 10).Returns(string.Empty);
            var emptyResult = _sut.GetLog(100, 10) as OkObjectResult;
            Assert.IsNotNull(emptyResult);
            Assert.AreEqual("Persisted SQL log", emptyResult.Value);

            _deploymentLogService.GetLogsForSingleResult(100, 10).Returns("No logs in the OpenSearch or it is unavailable.");
            var unavailableResult = _sut.GetLog(100, 10) as OkObjectResult;
            Assert.IsNotNull(unavailableResult);
            Assert.AreEqual("Persisted SQL log", unavailableResult.Value);
        }

        [TestMethod]
        public void GetLog_WhenPersistedLogIsEmptyOrAlreadyIncluded_ReturnsOpenSearchLogUnchanged()
        {
            _requestsPersistentSource.GetRequestForUser(100, Arg.Any<ClaimsPrincipal>())
                .Returns(new DeploymentRequestApiModel { Id = 100 });
            _deploymentLogService.GetLogsForSingleResult(100, 10)
                .Returns("Line 1\nPersisted SQL log");

            _requestsPersistentSource.GetDeploymentResults(10)
                .Returns(new DeploymentResultApiModel { Id = 10, RequestId = 100, Log = "Persisted SQL log" });
            var duplicateResult = _sut.GetLog(100, 10) as OkObjectResult;
            Assert.IsNotNull(duplicateResult);
            Assert.AreEqual("Line 1\nPersisted SQL log", duplicateResult.Value);

            _requestsPersistentSource.GetDeploymentResults(10)
                .Returns(new DeploymentResultApiModel { Id = 10, RequestId = 100, Log = "   " });
            var blankResult = _sut.GetLog(100, 10) as OkObjectResult;
            Assert.IsNotNull(blankResult);
            Assert.AreEqual("Line 1\nPersisted SQL log", blankResult.Value);
        }

        [TestMethod]
        public void DeploymentLogService_EnrichDeploymentResultsWithLogs_PreservesAndAppendsPersistedSqlLogs()
        {
            var openSearchClient = Substitute.For<IOpenSearchClient>();
            var logger = Substitute.For<ILogger<DeploymentLogService>>();
            var service = new DeploymentLogService(openSearchClient, logger, "deployment-result-*");

            var searchResponse10 = Substitute.For<ISearchResponse<DeployOpenSearchLogModel>>();
            searchResponse10.IsValid.Returns(true);
            searchResponse10.Documents.Returns(new List<DeployOpenSearchLogModel>
            {
                new(100, 10, "Runner line 1", OpenSearch.Client.LogLevel.Info)
                {
                    timestamp = new DateTime(2026, 11, 24, 15, 19, 40, DateTimeKind.Utc)
                }
            });

            var emptySearchResponse = Substitute.For<ISearchResponse<DeployOpenSearchLogModel>>();
            emptySearchResponse.IsValid.Returns(true);
            emptySearchResponse.Documents.Returns(Array.Empty<DeployOpenSearchLogModel>());

            openSearchClient.Search(Arg.Any<Func<SearchDescriptor<DeployOpenSearchLogModel>, ISearchRequest>>())
                .Returns(searchResponse10, emptySearchResponse);

            var resultWithPersisted = new DeploymentResultApiModel { Id = 10, RequestId = 100, Log = "[ERROR] Interrupted" };
            var resultWithBlankPersisted = new DeploymentResultApiModel { Id = 10, RequestId = 100, Log = "" };
            var resultWithDuplicatePersisted = new DeploymentResultApiModel { Id = 10, RequestId = 100, Log = "Runner line 1" };
            var resultWithNoOpenSearchLogs = new DeploymentResultApiModel { Id = 99, RequestId = 100, Log = "SQL only log" };
            var results = new List<DeploymentResultApiModel> { resultWithPersisted, resultWithBlankPersisted, resultWithDuplicatePersisted, resultWithNoOpenSearchLogs };

            service.EnrichDeploymentResultsWithLogs(results, 3);

            StringAssert.Contains(resultWithPersisted.Log, "Runner line 1");
            StringAssert.EndsWith(resultWithPersisted.Log, Environment.NewLine + "[ERROR] Interrupted");
            StringAssert.Contains(resultWithBlankPersisted.Log, "Runner line 1");
            Assert.IsFalse(resultWithBlankPersisted.Log!.Contains(Environment.NewLine));
            Assert.AreEqual(resultWithBlankPersisted.Log, resultWithDuplicatePersisted.Log);
            Assert.AreEqual("SQL only log", resultWithNoOpenSearchLogs.Log);
        }

        [TestMethod]
        public void DeploymentLogService_EnrichDeploymentResultsWithLogs_WhenOpenSearchThrows_PreservesExistingLogOrSetsFallback()
        {
            var openSearchClient = Substitute.For<IOpenSearchClient>();
            var logger = Substitute.For<ILogger<DeploymentLogService>>();
            var service = new DeploymentLogService(openSearchClient, logger, "deployment-result-*");

            openSearchClient.Search(Arg.Any<Func<SearchDescriptor<DeployOpenSearchLogModel>, ISearchRequest>>())
                .Returns(_ => throw new InvalidOperationException("OpenSearch down"));

            var resultWithPersisted = new DeploymentResultApiModel { Id = 10, RequestId = 100, Log = "Persisted SQL error" };
            var resultWithoutPersisted = new DeploymentResultApiModel { Id = 11, RequestId = 100, Log = null };
            var results = new List<DeploymentResultApiModel> { resultWithPersisted, resultWithoutPersisted };

            service.EnrichDeploymentResultsWithLogs(results, 3);

            Assert.AreEqual("Persisted SQL error", resultWithPersisted.Log);
            Assert.AreEqual("No logs in the OpenSearch or it is unavailable.", resultWithoutPersisted.Log);
        }
    }
}
