using System.Security.Claims;
using Dorc.Api.Controllers;
using Dorc.Api.Interfaces;
using Dorc.Api.Tests.Mocks;
using Dorc.ApiModel;
using Dorc.Core.Interfaces;
using Dorc.PersistentData;
using Dorc.PersistentData.Contexts;
using Dorc.PersistentData.Model;
using Dorc.PersistentData.Sources;
using Dorc.PersistentData.Sources.Interfaces;
using log4net;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;

namespace Dorc.Api.Tests.Controllers
{
    [TestClass]
    public class RequestControllerTests
    {
        [TestMethod]
        public void RestartPost_ShouldCloneRequest_AndReturnNewRequestId()
        {
            // Arrange
            var mockContextFactory = Substitute.For<IDeploymentContextFactory>();
            var mockDeployContext = Substitute.For<IDeploymentContext>();
            var mockClaimsPrincipalReader = Substitute.For<IClaimsPrincipalReader>();
            var mockRequestService = Substitute.For<IRequestService>();
            var mockSecurityService = Substitute.For<ISecurityPrivilegesChecker>();
            var mockLog = Substitute.For<ILog>();
            var mockRequestsManager = Substitute.For<IRequestsManager>();
            var mockProjectsPersistentSource = Substitute.For<IProjectsPersistentSource>();

            int originalRequestId = 1;
            int newRequestId = 2;
            string userName = "test@domain.com";

            // Setup existing request
            var originalRequest = new DeploymentRequest
            {
                Id = originalRequestId,
                RequestDetails = "<xml>test details</xml>",
                UserName = "original@domain.com",
                Status = "Completed",
                Project = "TestProject",
                Environment = "TestEnv",
                BuildNumber = "1.0.0",
                Components = "TestComponent",
                IsProd = false
            };

            var originalApiRequest = new DeploymentRequestApiModel
            {
                Id = originalRequestId,
                RequestDetails = "<xml>test details</xml>",
                UserName = "original@domain.com",
                Status = "Completed",
                Project = "TestProject",
                EnvironmentName = "TestEnv",
                BuildNumber = "1.0.0",
                Components = "TestComponent",
                IsProd = false
            };

            // Setup mocks
            mockContextFactory.GetContext().Returns(mockDeployContext);
            
            var entities = new List<DeploymentRequest> { originalRequest };
            mockDeployContext.DeploymentRequests = DbContextMock.GetQueryableMockDbSet(entities);

            var requestsPersistentSource = new RequestsPersistentSource(mockContextFactory, mockClaimsPrincipalReader);
            
            // Mock the user
            var user = new ClaimsPrincipal();
            mockClaimsPrincipalReader.GetUserFullDomainName(user).Returns(userName);
            
            // Mock security check
            mockSecurityService.CanModifyEnvironment(user, originalApiRequest.EnvironmentName).Returns(true);

            // Create controller
            var controller = new RequestController(
                mockRequestService,
                mockSecurityService,
                mockLog,
                mockRequestsManager,
                requestsPersistentSource,
                mockProjectsPersistentSource,
                mockClaimsPrincipalReader
            );

            // Set user context
            controller.ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = user
                }
            };

            // Mock GetRequestForUser to return the original request
            var mockRequestsPersistentSource = Substitute.For<IRequestsPersistentSource>();
            mockRequestsPersistentSource.GetRequestForUser(originalRequestId, user).Returns(originalApiRequest);
            mockRequestsPersistentSource.CloneRequest(originalRequestId, userName).Returns(newRequestId);

            var controllerWithMockedSource = new RequestController(
                mockRequestService,
                mockSecurityService,
                mockLog,
                mockRequestsManager,
                mockRequestsPersistentSource,
                mockProjectsPersistentSource,
                mockClaimsPrincipalReader
            );

            controllerWithMockedSource.ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = user
                }
            };

            // Act
            var result = controllerWithMockedSource.RestartPost(originalRequestId);

            // Assert
            var okResult = result as OkObjectResult;
            Assert.IsNotNull(okResult);
            Assert.AreEqual(200, okResult.StatusCode);

            var responseDto = okResult.Value as RequestStatusDto;
            Assert.IsNotNull(responseDto);
            Assert.AreEqual(newRequestId, responseDto.Id);
            Assert.AreEqual("Pending", responseDto.Status);

            // Verify that CloneRequest was called with correct parameters
            mockRequestsPersistentSource.Received(1).CloneRequest(originalRequestId, userName);
        }

        [TestMethod]
        public void CloneRequest_ShouldCreateNewRequestWithSameDetails()
        {
            // Arrange
            var mockContextFactory = Substitute.For<IDeploymentContextFactory>();
            var mockDeployContext = Substitute.For<IDeploymentContext>();
            var mockClaimsPrincipalReader = Substitute.For<IClaimsPrincipalReader>();

            int originalRequestId = 1;
            string newUserName = "newuser@domain.com";

            var originalRequest = new DeploymentRequest
            {
                Id = originalRequestId,
                RequestDetails = "<xml>test details</xml>",
                UserName = "original@domain.com",
                Status = "Completed",
                Project = "TestProject",
                Environment = "TestEnv",
                BuildNumber = "1.0.0",
                Components = "TestComponent",
                IsProd = false,
                RequestedTime = DateTimeOffset.Now.AddHours(-1),
                StartedTime = DateTimeOffset.Now.AddMinutes(-50),
                CompletedTime = DateTimeOffset.Now.AddMinutes(-30),
                Log = "Original log",
                UncLogPath = "/original/path"
            };

            var entities = new List<DeploymentRequest> { originalRequest };
            
            mockContextFactory.GetContext().Returns(mockDeployContext);
            mockDeployContext.DeploymentRequests = DbContextMock.GetQueryableMockDbSet(entities);
            
            // Mock SaveChanges to simulate new ID assignment
            var newRequest = new DeploymentRequest();
            var mockEntityEntry = Substitute.For<Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry<DeploymentRequest>>();
            mockEntityEntry.Entity.Returns(newRequest);
            mockDeployContext.DeploymentRequests.Add(Arg.Any<DeploymentRequest>()).Returns(mockEntityEntry);
            
            mockDeployContext.SaveChanges().Returns(callInfo =>
            {
                newRequest.Id = 2; // Simulate database assigning new ID
                return 1;
            });

            var requestsPersistentSource = new RequestsPersistentSource(mockContextFactory, mockClaimsPrincipalReader);

            // Act
            var newRequestId = requestsPersistentSource.CloneRequest(originalRequestId, newUserName);

            // Assert
            Assert.AreEqual(2, newRequestId);
            
            // Verify the DeploymentRequests.Add was called with the correct cloned request
            mockDeployContext.DeploymentRequests.Received(1).Add(Arg.Is<DeploymentRequest>(r =>
                r.RequestDetails == originalRequest.RequestDetails &&
                r.UserName == newUserName &&
                r.Status == "Pending" &&
                r.Project == originalRequest.Project &&
                r.Environment == originalRequest.Environment &&
                r.BuildNumber == originalRequest.BuildNumber &&
                r.Components == originalRequest.Components &&
                r.IsProd == originalRequest.IsProd &&
                r.Log == null &&
                r.UncLogPath == null &&
                r.StartedTime == null &&
                r.CompletedTime == null
            ));
        }

        [TestMethod]
        public void CloneRequest_WhenRequestNotFound_ShouldThrowArgumentException()
        {
            // Arrange
            var mockContextFactory = Substitute.For<IDeploymentContextFactory>();
            var mockDeployContext = Substitute.For<IDeploymentContext>();
            var mockClaimsPrincipalReader = Substitute.For<IClaimsPrincipalReader>();

            int nonExistentRequestId = 999;
            string newUserName = "newuser@domain.com";

            var entities = new List<DeploymentRequest>(); // Empty list
            
            mockContextFactory.GetContext().Returns(mockDeployContext);
            mockDeployContext.DeploymentRequests = DbContextMock.GetQueryableMockDbSet(entities);

            var requestsPersistentSource = new RequestsPersistentSource(mockContextFactory, mockClaimsPrincipalReader);

            // Act & Assert
            var exception = Assert.ThrowsException<ArgumentException>(() =>
                requestsPersistentSource.CloneRequest(nonExistentRequestId, newUserName));

            Assert.AreEqual($"Request with ID {nonExistentRequestId} not found.", exception.Message);
        }
    }
}