using Dorc.Api.Controllers;
using Dorc.ApiModel;
using Dorc.Core.AzureStorageAccount;
using Dorc.Core.Configuration;
using Dorc.Core.Interfaces;
using Dorc.PersistentData;
using Dorc.PersistentData.Sources.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using NSubstitute;
using System.Security.Claims;

namespace Dorc.Api.Tests.Controllers
{
    /// <summary>
    /// The defect these tests exist for was not an absent authorization check. It was a
    /// check that was present, was called, and always returned true - so a suite that only
    /// exercised the allow path would have stayed green throughout. Every refuse path is
    /// therefore covered, and the assertions check that refused calls leave the downstream
    /// collaborators untouched rather than only that the status code is 403.
    /// </summary>
    [TestClass]
    public class TerraformControllerTests
    {
        private const int ResultId = 42;
        private const int RequestId = 7;
        private const string Environment = "PROD ENV 01";

        private ILogger<TerraformController> _logger = null!;
        private IRequestsPersistentSource _requestsPersistentSource = null!;
        private ISecurityPrivilegesChecker _privilegesChecker = null!;
        private IClaimsPrincipalReader _claimsPrincipalReader = null!;
        private IAzureStorageAccountWorker _storage = null!;
        private IConfigurationSettings _configurationSettings = null!;
        private TerraformController _controller = null!;

        [TestInitialize]
        public void Setup()
        {
            _logger = Substitute.For<ILogger<TerraformController>>();
            _requestsPersistentSource = Substitute.For<IRequestsPersistentSource>();
            _privilegesChecker = Substitute.For<ISecurityPrivilegesChecker>();
            _claimsPrincipalReader = Substitute.For<IClaimsPrincipalReader>();
            _storage = Substitute.For<IAzureStorageAccountWorker>();
            _configurationSettings = Substitute.For<IConfigurationSettings>();

            _controller = new TerraformController(
                _logger,
                _requestsPersistentSource,
                _privilegesChecker,
                _claimsPrincipalReader,
                _storage,
                _configurationSettings)
            {
                ControllerContext = new ControllerContext
                {
                    HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity()) }
                }
            };

            // Default world: a result awaiting confirmation, on a non-production environment,
            // submitted by someone other than the caller. Individual tests narrow from here.
            GivenResult(DeploymentResultStatus.WaitingConfirmation);
            GivenRequest(Environment, isProd: false, submitter: @"CORP\alice");
            GivenCaller(@"CORP\bob");
            _privilegesChecker.CanModifyEnvironment(Arg.Any<ClaimsPrincipal>(), Arg.Any<string>()).Returns(true);
            _requestsPersistentSource
                .UpdateResultStatus(Arg.Any<DeploymentResultApiModel>(), Arg.Any<DeploymentResultStatus>(),
                    Arg.Any<DeploymentResultStatus>())
                .Returns(true);
        }

        // ---------------------------------------------------------------- helpers

        private void GivenResult(DeploymentResultStatus status) =>
            _requestsPersistentSource.GetDeploymentResults(ResultId).Returns(new DeploymentResultApiModel
            {
                Id = ResultId,
                RequestId = RequestId,
                Status = status.ToString()
            });

        private void GivenRequest(string? environment, bool isProd, string? submitter) =>
            _requestsPersistentSource.GetRequest(RequestId).Returns(new DeploymentRequestApiModel
            {
                Id = RequestId,
                EnvironmentName = environment!,
                IsProd = isProd,
                UserName = submitter!
            });

        private void GivenCaller(string fullDomainName) =>
            _claimsPrincipalReader.GetUserFullDomainName(Arg.Any<System.Security.Principal.IPrincipal>())
                .Returns(fullDomainName);

        private void GivenRestartedBy(string restarter, string originalSubmitter)
        {
            // Restart overwrites DeploymentRequest.UserName with the restarter's name, having
            // first archived the pre-restart name onto an attempt.
            GivenRequest(Environment, isProd: true, submitter: restarter);
            _requestsPersistentSource.GetAttemptsForRequest(RequestId).Returns(new[]
            {
                new DeploymentRequestAttemptApiModel { AttemptNumber = 1, UserName = originalSubmitter },
                new DeploymentRequestAttemptApiModel { AttemptNumber = 2, UserName = restarter }
            });
        }

        /// <summary>
        /// DeploymentResultStatus is a class, not an enum, and each static property returns a
        /// NEW instance. It overloads == but overrides neither Equals(object) nor
        /// GetHashCode, so NSubstitute's argument matching falls back to reference equality
        /// and two "Confirmed" values do not match. Match on the value instead.
        /// </summary>
        private static DeploymentResultStatus Status(DeploymentResultStatus expected) =>
            Arg.Is<DeploymentResultStatus>(actual => actual.Value == expected.Value);

        private static int StatusOf(IActionResult result) => result switch
        {
            ObjectResult objectResult => objectResult.StatusCode ?? 0,
            StatusCodeResult statusCodeResult => statusCodeResult.StatusCode,
            _ => 0
        };

        private void AssertNoStateChanged()
        {
            _requestsPersistentSource.DidNotReceiveWithAnyArgs()
                .UpdateResultStatus(default!, default);
            _requestsPersistentSource.DidNotReceiveWithAnyArgs()
                .UpdateResultStatus(default!, default, default);
            _requestsPersistentSource.DidNotReceiveWithAnyArgs()
                .UpdateRequestStatus(default, default);
        }

        // ---------------------------------------------- environment authority (R2, R4)

        [TestMethod]
        public void GetTerraformPlan_WithoutEnvironmentAuthority_Returns403AndDoesNotReadPlan()
        {
            _privilegesChecker.CanModifyEnvironment(Arg.Any<ClaimsPrincipal>(), Environment).Returns(false);

            var response = _controller.GetTerraformPlan(ResultId);

            Assert.AreEqual(StatusCodes.Status403Forbidden, StatusOf(response));
            // Ordering matters: the plan embeds resolved variable values, so a refused caller
            // must not cause it to be read at all.
            _storage.DidNotReceiveWithAnyArgs().LoadFileFromBlobs(default!);
        }

        [TestMethod]
        public void GetTerraformPlan_WithEnvironmentAuthority_ReturnsPlan()
        {
            _storage.LoadFileFromBlobs(Arg.Any<string>()).Returns("plan output");

            var response = _controller.GetTerraformPlan(ResultId);

            Assert.IsInstanceOfType(response, typeof(OkObjectResult));
        }

        [TestMethod]
        public void ConfirmTerraformPlan_WithoutEnvironmentAuthority_Returns403AndDoesNotTransition()
        {
            _privilegesChecker.CanModifyEnvironment(Arg.Any<ClaimsPrincipal>(), Environment).Returns(false);

            var response = _controller.ConfirmTerraformPlan(ResultId);

            Assert.AreEqual(StatusCodes.Status403Forbidden, StatusOf(response));
            AssertNoStateChanged();
        }

        [TestMethod]
        public void DeclineTerraformPlan_WithoutEnvironmentAuthority_Returns403AndDoesNotTransition()
        {
            _privilegesChecker.CanModifyEnvironment(Arg.Any<ClaimsPrincipal>(), Environment).Returns(false);

            var response = _controller.DeclineTerraformPlan(ResultId);

            Assert.AreEqual(StatusCodes.Status403Forbidden, StatusOf(response));
            AssertNoStateChanged();
        }

        /// <summary>
        /// AC-9. A suite that stubbed the checker to false for every argument would pass
        /// against a predicate reading the wrong environment, so assert the checker was
        /// consulted with the environment resolved from the request.
        /// </summary>
        [TestMethod]
        public void ConfirmTerraformPlan_ConsultsThePrivilegesCheckerWithTheRequestsEnvironment()
        {
            _controller.ConfirmTerraformPlan(ResultId);

            _privilegesChecker.Received(1).CanModifyEnvironment(Arg.Any<ClaimsPrincipal>(), Environment);
        }

        // ------------------------------------------------------- fail closed (R1, AC-8)

        [TestMethod]
        public void ConfirmTerraformPlan_WhenRequestCannotBeResolved_Returns403NotAllow()
        {
            _requestsPersistentSource.GetRequest(RequestId).Returns((DeploymentRequestApiModel)null!);

            var response = _controller.ConfirmTerraformPlan(ResultId);

            Assert.AreEqual(StatusCodes.Status403Forbidden, StatusOf(response));
            AssertNoStateChanged();
        }

        /// <summary>
        /// CanModifyEnvironment falls back to an administrator check when the environment
        /// cannot be found, so a blank environment name must be refused by the controller
        /// before the checker is consulted - otherwise every administrator is permitted.
        /// </summary>
        [TestMethod]
        public void ConfirmTerraformPlan_WithBlankEnvironmentName_Returns403EvenWhenCheckerWouldAllow()
        {
            GivenRequest("   ", isProd: true, submitter: @"CORP\alice");
            _privilegesChecker.CanModifyEnvironment(Arg.Any<ClaimsPrincipal>(), Arg.Any<string>()).Returns(true);

            var response = _controller.ConfirmTerraformPlan(ResultId);

            Assert.AreEqual(StatusCodes.Status403Forbidden, StatusOf(response));
            _privilegesChecker.DidNotReceiveWithAnyArgs().CanModifyEnvironment(default!, default!);
            AssertNoStateChanged();
        }

        [TestMethod]
        public void ConfirmTerraformPlan_WhenResolutionThrows_Returns403Not500()
        {
            _requestsPersistentSource.GetRequest(RequestId).Returns(_ => throw new InvalidOperationException("database unavailable"));

            var response = _controller.ConfirmTerraformPlan(ResultId);

            Assert.AreEqual(StatusCodes.Status403Forbidden, StatusOf(response));
            AssertNoStateChanged();
        }

        [TestMethod]
        public void ConfirmTerraformPlan_WhenResultMissing_Returns404()
        {
            _requestsPersistentSource.GetDeploymentResults(ResultId).Returns((DeploymentResultApiModel)null!);

            var response = _controller.ConfirmTerraformPlan(ResultId);

            Assert.IsInstanceOfType(response, typeof(NotFoundObjectResult));
        }

        // ------------------------------------------------ segregation of duties (R3)

        [TestMethod]
        public void ConfirmTerraformPlan_BySubmitter_OnProductionTier_Returns403()
        {
            GivenRequest(Environment, isProd: true, submitter: @"CORP\alice");
            GivenCaller(@"CORP\alice");

            var response = _controller.ConfirmTerraformPlan(ResultId);

            Assert.AreEqual(StatusCodes.Status403Forbidden, StatusOf(response));
            AssertNoStateChanged();
        }

        [TestMethod]
        public void ConfirmTerraformPlan_BySubmitter_OnNonProductionTier_IsAllowed()
        {
            GivenRequest(Environment, isProd: false, submitter: @"CORP\alice");
            GivenCaller(@"CORP\alice");

            var response = _controller.ConfirmTerraformPlan(ResultId);

            Assert.IsInstanceOfType(response, typeof(OkObjectResult));
        }

        /// <summary>
        /// AC-13. The same person submitting under Windows authentication and confirming
        /// under OAuth is recorded under two different strings. Comparing them directly
        /// would report them as different people and permit self-approval - the failure
        /// direction that matters, and one that every other test here would miss.
        /// </summary>
        [TestMethod]
        public void ConfirmTerraformPlan_BySameHumanAcrossAuthenticationSchemes_Returns403()
        {
            GivenRequest(Environment, isProd: true, submitter: @"CORP\jsmith");
            GivenCaller("jsmith@corp.example.com");

            var response = _controller.ConfirmTerraformPlan(ResultId);

            Assert.AreEqual(StatusCodes.Status403Forbidden, StatusOf(response));
            AssertNoStateChanged();
        }

        /// <summary>
        /// AC-11. Restart overwrites the request's user name with the restarter's, so
        /// comparing against it would let the original author approve their own change
        /// once anyone else restarted it.
        /// </summary>
        [TestMethod]
        public void ConfirmTerraformPlan_ByOriginalSubmitterAfterAnotherUserRestarted_Returns403()
        {
            GivenRestartedBy(restarter: @"CORP\bob", originalSubmitter: @"CORP\alice");
            GivenCaller(@"CORP\alice");

            var response = _controller.ConfirmTerraformPlan(ResultId);

            Assert.AreEqual(StatusCodes.Status403Forbidden, StatusOf(response));
            AssertNoStateChanged();
        }

        [TestMethod]
        public void ConfirmTerraformPlan_ByRestarterWhoIsNotTheOriginalSubmitter_IsAllowed()
        {
            GivenRestartedBy(restarter: @"CORP\bob", originalSubmitter: @"CORP\alice");
            GivenCaller(@"CORP\bob");

            var response = _controller.ConfirmTerraformPlan(ResultId);

            Assert.IsInstanceOfType(response, typeof(OkObjectResult));
        }

        [TestMethod]
        public void ConfirmTerraformPlan_WhenSubmitterIdentityCannotBeEstablished_Returns403()
        {
            GivenRequest(Environment, isProd: true, submitter: "   ");
            GivenCaller(@"CORP\bob");

            var response = _controller.ConfirmTerraformPlan(ResultId);

            Assert.AreEqual(StatusCodes.Status403Forbidden, StatusOf(response));
            AssertNoStateChanged();
        }

        /// <summary>
        /// AC-15. The codebase's usual boolean-setting pattern resolves an absent key to
        /// false, which would ship this control silently off. Absent must mean enabled for
        /// production-tier.
        /// </summary>
        [TestMethod]
        public void ConfirmTerraformPlan_WithSeparateApproverSettingAbsent_StillEnforcesOnProductionTier()
        {
            _configurationSettings.GetTerraformSeparateApproverRequired().Returns((bool?)null);
            GivenRequest(Environment, isProd: true, submitter: @"CORP\alice");
            GivenCaller(@"CORP\alice");

            var response = _controller.ConfirmTerraformPlan(ResultId);

            Assert.AreEqual(StatusCodes.Status403Forbidden, StatusOf(response));
        }

        [TestMethod]
        public void ConfirmTerraformPlan_WithSeparateApproverExplicitlyDisabled_AllowsSelfApproval()
        {
            _configurationSettings.GetTerraformSeparateApproverRequired().Returns(false);
            GivenRequest(Environment, isProd: true, submitter: @"CORP\alice");
            GivenCaller(@"CORP\alice");

            var response = _controller.ConfirmTerraformPlan(ResultId);

            Assert.IsInstanceOfType(response, typeof(OkObjectResult));
        }

        [TestMethod]
        public void ConfirmTerraformPlan_WithSeparateApproverExplicitlyEnabled_AppliesToNonProductionToo()
        {
            _configurationSettings.GetTerraformSeparateApproverRequired().Returns(true);
            GivenRequest(Environment, isProd: false, submitter: @"CORP\alice");
            GivenCaller(@"CORP\alice");

            var response = _controller.ConfirmTerraformPlan(ResultId);

            Assert.AreEqual(StatusCodes.Status403Forbidden, StatusOf(response));
        }

        /// <summary>
        /// AC-12. Declining stops a deployment rather than starting one, so there is no
        /// self-approval to prevent. Applying segregation of duties here would stop a
        /// requester cancelling their own pending plan.
        /// </summary>
        [TestMethod]
        public void DeclineTerraformPlan_BySubmitter_OnProductionTier_IsAllowed()
        {
            GivenRequest(Environment, isProd: true, submitter: @"CORP\alice");
            GivenCaller(@"CORP\alice");

            var response = _controller.DeclineTerraformPlan(ResultId);

            Assert.IsInstanceOfType(response, typeof(OkObjectResult));
        }

        // ------------------------------------------------------- guarded transition (R8)

        /// <summary>
        /// AC-14. The status is read before the authorization checks, so it may have moved
        /// since. An unguarded write would push a running result back to Confirmed and
        /// cause a second apply dispatch.
        /// </summary>
        [TestMethod]
        public void ConfirmTerraformPlan_WhenResultLeftWaitingConfirmationConcurrently_Returns409()
        {
            _requestsPersistentSource
                .UpdateResultStatus(Arg.Any<DeploymentResultApiModel>(),
                    Status(DeploymentResultStatus.Confirmed),
                    Status(DeploymentResultStatus.WaitingConfirmation))
                .Returns(false);

            var response = _controller.ConfirmTerraformPlan(ResultId);

            Assert.IsInstanceOfType(response, typeof(ConflictObjectResult));
            _requestsPersistentSource.DidNotReceiveWithAnyArgs().UpdateRequestStatus(default, default);
        }

        [TestMethod]
        public void ConfirmTerraformPlan_TransitionsOnlyFromWaitingConfirmation()
        {
            _controller.ConfirmTerraformPlan(ResultId);

            _requestsPersistentSource.Received(1).UpdateResultStatus(
                Arg.Any<DeploymentResultApiModel>(),
                Status(DeploymentResultStatus.Confirmed),
                Status(DeploymentResultStatus.WaitingConfirmation));
        }

        [TestMethod]
        public void DeclineTerraformPlan_WhenResultLeftWaitingConfirmationConcurrently_Returns409()
        {
            _requestsPersistentSource
                .UpdateResultStatus(Arg.Any<DeploymentResultApiModel>(),
                    Status(DeploymentResultStatus.Cancelled),
                    Status(DeploymentResultStatus.WaitingConfirmation))
                .Returns(false);

            var response = _controller.DeclineTerraformPlan(ResultId);

            Assert.IsInstanceOfType(response, typeof(ConflictObjectResult));
            _requestsPersistentSource.DidNotReceiveWithAnyArgs().UpdateRequestStatus(default, default);
        }

        [TestMethod]
        public void DeclineTerraformPlan_TransitionsOnlyFromWaitingConfirmation()
        {
            _controller.DeclineTerraformPlan(ResultId);

            _requestsPersistentSource.Received(1).UpdateResultStatus(
                Arg.Any<DeploymentResultApiModel>(),
                Status(DeploymentResultStatus.Cancelled),
                Status(DeploymentResultStatus.WaitingConfirmation));
        }

        [TestMethod]
        public void ConfirmTerraformPlan_WhenNotAwaitingConfirmation_ReturnsBadRequest()
        {
            GivenResult(DeploymentResultStatus.Running);

            var response = _controller.ConfirmTerraformPlan(ResultId);

            Assert.IsInstanceOfType(response, typeof(BadRequestObjectResult));
            AssertNoStateChanged();
        }
    }
}
