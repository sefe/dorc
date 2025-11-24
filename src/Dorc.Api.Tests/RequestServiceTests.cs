using System.Security.Principal;
using Dorc.Api.Deployment;
using Dorc.Api.Exceptions;
using Dorc.Api.Interfaces;
using Dorc.Api.Tests.Mocks;
using Dorc.ApiModel;
using Dorc.Core.Interfaces;
using Dorc.PersistentData;
using Dorc.PersistentData.Contexts;
using Dorc.PersistentData.Model;
using Dorc.PersistentData.Sources;
using Dorc.PersistentData.Sources.Interfaces;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Dorc.Api.Tests
{
    [TestClass]
    public class RequestServiceTests
    {
        [TestMethod]
        public void GetRequestStatusTest()
        {
            var mockContextFactory = Substitute.For<IDeploymentContextFactory>();
            var mockDeployContext = Substitute.For<IDeploymentContext>();
            var mockClaimsPrincipalReader = Substitute.For<IClaimsPrincipalReader>();

            int id = 1;

            mockContextFactory
                .GetContext()
                .Returns(mockDeployContext);

            List<DeploymentRequest> entities =
            [
                new DeploymentRequest
                {
                    Id = id,
                    Status = "Running"
                },
            ];

            mockDeployContext.DeploymentRequests = DbContextMock.GetQueryableMockDbSet(entities);

            try
            {
                IRequestsPersistentSource testService = new RequestsPersistentSource(mockContextFactory, mockClaimsPrincipalReader);
                var result = testService.GetRequestStatus(id);
                Assert.AreEqual(id, result.Id);
                Assert.AreEqual("Running", result.Status);
            }
            catch (Exception e)
            {
                Assert.Fail(e.Message);
            }
        }

        [TestMethod]
        public void CreateRequestTest1()
        {
            var mockEnvPs = Substitute.For<IEnvironmentsPersistentSource>();
            var mockedFactory = Substitute.For<IDeployableBuildFactory>();
            ILogger<Requests> mockedLog = Substitute.For<ILogger<Requests>>();
            var mockedProjectsPds = Substitute.For<IProjectsPersistentSource>();
            // build valid, request created
            RequestDto request = new RequestDto
            {
                Project = "someProject",
                Components = new List<string> { "someComponent" },
                Environment = "someEnvironment",
                BuildUrl = "file:\\\\some_path",
                BuildText = "buildText",
                BuildNum = "buildNUm",
                RequestProperties = new List<RequestProperty>(),
                Pinned = false
            };
            mockedFactory.CreateInstance(Arg.Any<RequestDto>())
                .Returns(new FileShareBuildStub(new RequestStatusDto { Id = 1 }, true));
            var test1Service = new Requests(mockedLog, mockedFactory, mockedProjectsPds);

            var result1 = test1Service.CreateRequest(request, new WindowsPrincipal(WindowsIdentity.GetCurrent()));
            Assert.AreEqual(1, result1.Id);
        }

        [TestMethod]
        public void CreateRequestTest2()
        {
            var mockEnvPs = Substitute.For<IEnvironmentsPersistentSource>();
            // build valid, request was not created
            RequestDto request = new RequestDto
            {
                Project = "someProject",
                Components = new List<string> { "someComponent" },
                Environment = "someEnvironment",
                BuildUrl = "file:\\\\some_path",
                BuildText = "buildText",
                BuildNum = "buildNUm",
                RequestProperties = new List<RequestProperty>(),
                Pinned = false
            };
            var mockedProjectsPds = Substitute.For<IProjectsPersistentSource>();
            var mockedLog = new MockedLog<Requests>();
            var mockedFactory = Substitute.For<IDeployableBuildFactory>();
            mockedFactory.CreateInstance(Arg.Any<RequestDto>())
                .Returns(new FileShareBuildStub(null, true));
            var test2Service = new Requests(mockedLog, mockedFactory, mockedProjectsPds);
            var result2 = test2Service.CreateRequest(request, new WindowsPrincipal(WindowsIdentity.GetCurrent()));
            Assert.IsNull(result2);
        }

        [TestMethod]
        public void CreateRequestTest3()
        {
            var mockEnvPs = Substitute.For<IEnvironmentsPersistentSource>();
            //build not valid
            RequestDto request = new RequestDto
            {
                Project = "someProject",
                Components = new List<string> { "someComponent" },
                Environment = "someEnvironment",
                BuildUrl = "file:\\\\some_path",
                BuildText = "buildText",
                BuildNum = "buildNUm",
                RequestProperties = new List<RequestProperty>(),
                Pinned = false
            };
            var mockedProjectsPds = Substitute.For<IProjectsPersistentSource>();
            var mockedLog = new MockedLog<Requests>();
            var mockedFactory = Substitute.For<IDeployableBuildFactory>();
            mockedFactory.CreateInstance(Arg.Any<RequestDto>())
                .Returns(new FileShareBuildStub(null, false));
            var test3service = new Requests(mockedLog, mockedFactory, mockedProjectsPds);
            Exception expectedException = null;
            try
            {
                var result3 = test3service.CreateRequest(request, new WindowsPrincipal(WindowsIdentity.GetCurrent()));
            }
            catch (Exception e)
            {
                expectedException = e;
            }
            Assert.IsNotNull(expectedException);
            Assert.IsTrue(expectedException is WrongBuildTypeException);
        }

        [TestMethod]
        public void CreateRequestTest4()
        {
            var mockRequestsPersistentSource = Substitute.For<IRequestsPersistentSource>();
            var mockEnvPs = Substitute.For<IEnvironmentsPersistentSource>();
            // wrong build type
            var request1 = new RequestDto
            {
                Project = "someProject",
                Components = new List<string> { "someComponent" },
                Environment = "someEnvironment",
                BuildUrl = "ftp:\\\\some_path",
                BuildText = "buildText",
                BuildNum = "buildNUm",
                RequestProperties = new List<RequestProperty>(),
                Pinned = false
            };
            var mockedProjectsPds = Substitute.For<IProjectsPersistentSource>();
            var mockedFileOperations = Substitute.For<IFileOperations>();
            var mockedLogger = Substitute.For<ILogger<Requests>>();
            var mockedLoggerFactory = Substitute.For<ILoggerFactory>();
            var mockDeployLibrary = Substitute.For<IDeployLibrary>();
            IDeployableBuildFactory factory = new Dorc.Api.Build.DeployableBuildFactory(mockedFileOperations, mockedLoggerFactory, mockedProjectsPds, mockDeployLibrary, mockRequestsPersistentSource);
            var test4Service = new Requests(mockedLogger, factory, mockedProjectsPds);
            Exception expectedException1 = null;
            try
            {
                var result4 = test4Service.CreateRequest(request1, new WindowsPrincipal(WindowsIdentity.GetCurrent()));

            }
            catch (Exception e)
            {
                expectedException1 = e;
            }
            Assert.IsNotNull(expectedException1);
            Assert.IsTrue(expectedException1 is WrongBuildTypeException);
        }

        [TestMethod]
        public void CheckRequestTest()
        {
            var mockEnvPs = Substitute.For<IEnvironmentsPersistentSource>();
            var mockedBuildFactory = Substitute.For<IDeployableBuildFactory>();
            var mockedLog = Substitute.For<ILogger<Requests>>();
            var request = new RequestDto();
            var mockedProjectsPds = Substitute.For<IProjectsPersistentSource>();

            mockedProjectsPds.GetProject(Arg.Any<string>())
                .Returns(new ProjectApiModel
                {
                    ArtefactsUrl = "http://some_url"
                });

            var service = new Requests(mockedLog, mockedBuildFactory, mockedProjectsPds);
            service.CheckRequest(ref request);
            Assert.IsTrue(request.BuildUrl.Equals("http://some_url"));
        }
    }
}
