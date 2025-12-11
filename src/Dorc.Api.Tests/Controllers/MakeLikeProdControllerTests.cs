using Dorc.Api.Controllers;
using Dorc.Api.Interfaces;
using Dorc.Api.Model;
using Dorc.ApiModel;
using Dorc.Core;
using Dorc.Core.Interfaces;
using Dorc.Core.VariableResolution;
using Dorc.PersistentData;
using Dorc.PersistentData.Sources.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using NSubstitute;
using System.Security.Claims;

namespace Dorc.Api.Tests.Controllers
{
    [TestClass]
    public class MakeLikeProdControllerTests
    {
        private ILogger<MakeLikeProdController> _logger;
        private IDeployLibrary _deployLibrary;
        private IEnvironmentsPersistentSource _environmentsPersistentSource;
        private ISecurityPrivilegesChecker _securityPrivilegesChecker;
        private IEnvBackups _envBackups;
        private IBundledRequestsPersistentSource _bundledRequestsPersistentSource;
        private IVariableResolver _variableResolver;
        private IBundledRequestVariableLoader _bundledRequestVariableLoader;
        private IProjectsPersistentSource _projectsPersistentSource;
        private IClaimsPrincipalReader _claimsPrincipalReader;
        private MakeLikeProdController _controller;
        private ClaimsPrincipal _user;

        [TestInitialize]
        public void Setup()
        {
            _logger = Substitute.For<ILogger<MakeLikeProdController>>();
            _deployLibrary = Substitute.For<IDeployLibrary>();
            _environmentsPersistentSource = Substitute.For<IEnvironmentsPersistentSource>();
            _securityPrivilegesChecker = Substitute.For<ISecurityPrivilegesChecker>();
            _envBackups = Substitute.For<IEnvBackups>();
            _bundledRequestsPersistentSource = Substitute.For<IBundledRequestsPersistentSource>();
            _variableResolver = Substitute.For<IVariableResolver>();
            _bundledRequestVariableLoader = Substitute.For<IBundledRequestVariableLoader>();
            _projectsPersistentSource = Substitute.For<IProjectsPersistentSource>();
            _claimsPrincipalReader = Substitute.For<IClaimsPrincipalReader>();

            _controller = new MakeLikeProdController(
                _logger,
                _deployLibrary,
                _environmentsPersistentSource,
                _securityPrivilegesChecker,
                _envBackups,
                _bundledRequestsPersistentSource,
                _variableResolver,
                _bundledRequestVariableLoader,
                _projectsPersistentSource,
                _claimsPrincipalReader)
            {
                ControllerContext = new ControllerContext()
                {
                    HttpContext = new DefaultHttpContext()
                }
            };

            _user = new ClaimsPrincipal(new ClaimsIdentity(new Claim[] {
                new Claim(ClaimTypes.Name, "TestUser"),
                new Claim(ClaimTypes.Email, "testuser@example.com")
            }));
            _controller.HttpContext.User = _user;
        }

        [TestMethod]
        public void Put_SetsAllRequestIdsVariable_WithSingleJobRequest()
        {
            // Arrange
            var targetEnv = "TestEnv";
            var bundleName = "TestBundle";
            var mlpRequest = new MakeLikeProdRequest
            {
                TargetEnv = targetEnv,
                DataBackup = "Live Snap",
                BundleName = bundleName,
                BundleProperties = new List<RequestProperty>()
            };

            var jobRequest = new RequestDto
            {
                Project = "TestProject",
                BuildUrl = "http://build.url",
                BuildText = "Build 1.0",
                Components = new List<string> { "Component1" },
                RequestProperties = new List<RequestProperty>()
            };

            var bundledRequests = new List<BundledRequestsApiModel>
            {
                new BundledRequestsApiModel
                {
                    Type = BundledRequestType.JobRequest,
                    Request = System.Text.Json.JsonSerializer.Serialize(jobRequest),
                    Sequence = 1
                }
            };

            _securityPrivilegesChecker.IsEnvironmentOwnerOrAdmin(_user, targetEnv).Returns(true);
            _environmentsPersistentSource.GetEnvironment(targetEnv).Returns(new EnvironmentApiModel { EnvironmentIsProd = false });
            _bundledRequestsPersistentSource.GetRequestsForBundle(bundleName).Returns(bundledRequests);
            _deployLibrary.SubmitRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), 
                Arg.Any<string>(), Arg.Any<List<string>>(), Arg.Any<List<RequestProperty>>(), Arg.Any<ClaimsPrincipal>())
                .Returns(12345);
            _claimsPrincipalReader.GetUserEmail(_user).Returns("testuser@example.com");
            _variableResolver.GetPropertyValue(Arg.Any<string>()).Returns(new VariableValue { Value = "test", Type = typeof(string) });

            // Act
            var result = _controller.Put(mlpRequest);

            // Assert
            _variableResolver.Received(1).SetPropertyValue("StartingRequestId", "12345");
            _variableResolver.Received(1).SetPropertyValue("AllRequestIds", "12345");
        }

        [TestMethod]
        public void Put_SetsAllRequestIdsVariable_WithMultipleRequests()
        {
            // Arrange
            var targetEnv = "TestEnv";
            var bundleName = "TestBundle";
            var mlpRequest = new MakeLikeProdRequest
            {
                TargetEnv = targetEnv,
                DataBackup = "Live Snap",
                BundleName = bundleName,
                BundleProperties = new List<RequestProperty>()
            };

            var jobRequest1 = new RequestDto
            {
                Project = "TestProject1",
                BuildUrl = "http://build.url/1",
                BuildText = "Build 1.0",
                Components = new List<string> { "Component1" },
                RequestProperties = new List<RequestProperty>()
            };

            var jobRequest2 = new RequestDto
            {
                Project = "TestProject2",
                BuildUrl = "http://build.url/2",
                BuildText = "Build 2.0",
                Components = new List<string> { "Component2" },
                RequestProperties = new List<RequestProperty>()
            };

            var copyEnvBuildRequest = new CopyEnvBuildRequest
            {
                SourceEnvironmentName = "SourceEnv",
                ProjectName = "TestProject3",
                Components = new List<int> { 100, 101 }
            };

            var bundledRequests = new List<BundledRequestsApiModel>
            {
                new BundledRequestsApiModel
                {
                    Type = BundledRequestType.JobRequest,
                    Request = System.Text.Json.JsonSerializer.Serialize(jobRequest1),
                    Sequence = 1
                },
                new BundledRequestsApiModel
                {
                    Type = BundledRequestType.JobRequest,
                    Request = System.Text.Json.JsonSerializer.Serialize(jobRequest2),
                    Sequence = 2
                },
                new BundledRequestsApiModel
                {
                    Type = BundledRequestType.CopyEnvBuild,
                    Request = System.Text.Json.JsonSerializer.Serialize(copyEnvBuildRequest),
                    Sequence = 3
                }
            };

            _securityPrivilegesChecker.IsEnvironmentOwnerOrAdmin(_user, targetEnv).Returns(true);
            _environmentsPersistentSource.GetEnvironment(targetEnv).Returns(new EnvironmentApiModel { EnvironmentIsProd = false });
            _bundledRequestsPersistentSource.GetRequestsForBundle(bundleName).Returns(bundledRequests);
            
            // First job request returns 12345
            _deployLibrary.SubmitRequest("TestProject1", Arg.Any<string>(), Arg.Any<string>(), 
                Arg.Any<string>(), Arg.Any<List<string>>(), Arg.Any<List<RequestProperty>>(), Arg.Any<ClaimsPrincipal>())
                .Returns(12345);
            
            // Second job request returns 12346
            _deployLibrary.SubmitRequest("TestProject2", Arg.Any<string>(), Arg.Any<string>(), 
                Arg.Any<string>(), Arg.Any<List<string>>(), Arg.Any<List<RequestProperty>>(), Arg.Any<ClaimsPrincipal>())
                .Returns(12346);
            
            // CopyEnvBuild returns two request IDs
            _deployLibrary.CopyEnvBuildWithComponentIds(Arg.Any<string>(), Arg.Any<string>(), 
                Arg.Any<string>(), Arg.Any<int[]>(), Arg.Any<ClaimsPrincipal>())
                .Returns(new List<int> { 12347, 12348 });
            
            _claimsPrincipalReader.GetUserEmail(_user).Returns("testuser@example.com");
            _variableResolver.GetPropertyValue(Arg.Any<string>()).Returns(new VariableValue { Value = "test", Type = typeof(string) });

            // Act
            var result = _controller.Put(mlpRequest);

            // Assert
            _variableResolver.Received(1).SetPropertyValue("StartingRequestId", "12345");
            _variableResolver.Received(1).SetPropertyValue("AllRequestIds", "12345,12346,12347,12348");
        }

        [TestMethod]
        public void Put_DoesNotSetAllRequestIds_WhenNoRequestsCreated()
        {
            // Arrange
            var targetEnv = "TestEnv";
            var bundleName = "TestBundle";
            var mlpRequest = new MakeLikeProdRequest
            {
                TargetEnv = targetEnv,
                DataBackup = "Live Snap",
                BundleName = bundleName,
                BundleProperties = new List<RequestProperty>()
            };

            var bundledRequests = new List<BundledRequestsApiModel>(); // Empty list

            _securityPrivilegesChecker.IsEnvironmentOwnerOrAdmin(_user, targetEnv).Returns(true);
            _environmentsPersistentSource.GetEnvironment(targetEnv).Returns(new EnvironmentApiModel { EnvironmentIsProd = false });
            _bundledRequestsPersistentSource.GetRequestsForBundle(bundleName).Returns(bundledRequests);
            _claimsPrincipalReader.GetUserEmail(_user).Returns("testuser@example.com");

            // Act
            var result = _controller.Put(mlpRequest);

            // Assert
            _variableResolver.DidNotReceive().SetPropertyValue("StartingRequestId", Arg.Any<string>());
            _variableResolver.DidNotReceive().SetPropertyValue("AllRequestIds", Arg.Any<string>());
        }
    }
}
