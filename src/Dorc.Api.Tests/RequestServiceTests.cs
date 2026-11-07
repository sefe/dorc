using System.Security.Principal;
using Dorc.Api.Interfaces;
using Dorc.Api.Services;
using Dorc.Api.Tests.Mocks;
using Dorc.ApiModel;
using Dorc.Core.BuildServer;
using Dorc.Core.Interfaces;
using Dorc.PersistentData;
using Dorc.PersistentData.Contexts;
using Dorc.PersistentData.Model;
using Dorc.PersistentData.Sources;
using Dorc.PersistentData.Sources.Interfaces;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Dorc.Terraform.Catalog;
using Dorc.Core;
using System.Security.Claims;

namespace Dorc.Api.Tests
{
    [TestClass]
    public class RequestServiceTests
    {
        [TestMethod]
        [DataRow("dorc-catalog://", false)]
        [DataRow("file:\\\\build", false)]
        [DataRow("file:\\\\build", true)]
        public void CreateRequest_DerivesSensitivityBeforeAnyBuildPath(string buildUrl, bool callerFlag)
        {
            var projects = Substitute.For<IProjectsPersistentSource>();
            projects.GetComponentsForProject("project").Returns(new[]
            {
                new ComponentApiModel
                {
                    ComponentName = "component", TerraformSourceType = TerraformSourceType.Catalog,
                    TerraformTemplateName = "module", TerraformTemplateVersion = "1.0.0"
                }
            });
            var catalog = Substitute.For<ITemplateCatalog>();
            catalog.GetAsync("module", "1.0.0", Arg.Any<CancellationToken>()).Returns(
                new TerraformTemplateManifest("module", "1.0.0",
                    new TerraformTemplateSource("git", "https://example.invalid/module", "v1"),
                    new[] { new TerraformTemplateParameter("password", TerraformParameterType.String,
                        true, null, null, null, null, null, null, true) },
                    Array.Empty<TerraformTemplateOutput>(), null, Array.Empty<string>(), null,
                    new Dictionary<string, string>(), null, null, false, null));
            var properties = Substitute.For<IPropertiesPersistentSource>();
            properties.GetAllProperties().Returns(new[] { new PropertyApiModel { Name = "secureSetting", Secure = true } });
            var request = new RequestDto
            {
                Project = "project", BuildUrl = buildUrl, Components = new List<string> { "component" },
                RequestProperties = new List<RequestProperty>
                {
                    new() { PropertyName = "password", PropertyValue = "manifest-secret", IsSensitive = callerFlag },
                    new() { PropertyName = "secureSetting", PropertyValue = "secure-value" },
                    new() { PropertyName = "callerOnly", PropertyValue = "caller-secret", IsSensitive = true },
                    new() { PropertyName = "ordinary", PropertyValue = "public-value" }
                }
            };
            var build = Substitute.For<IDeployableBuild>();
            build.IsValid(Arg.Any<Dorc.Api.Model.BuildDetails>()).Returns(true);
            string? storedXml = null;
            build.Process(Arg.Any<RequestDto>(), Arg.Any<ClaimsPrincipal>()).Returns(call =>
            {
                var actual = call.Arg<RequestDto>();
                storedXml = new DeploymentRequestDetailSerializer().Serialize(new DeploymentRequestDetail
                {
                    Properties = actual.RequestProperties.Select(p => new PropertyPair(p.PropertyName, p.PropertyValue)
                        { IsSensitive = p.IsSensitive }).ToList()
                });
                return new RequestStatusDto { Id = 1 };
            });
            var factory = Substitute.For<IDeployableBuildFactory>();
            factory.CreateInstance(request).Returns(build);
            var service = new RequestService(Substitute.For<ILogger<RequestService>>(), factory, projects, catalog, properties);

            Assert.AreEqual(1, service.CreateRequest(request, new ClaimsPrincipal()).Id);

            Assert.IsNotNull(storedXml);
            StringAssert.Contains(storedXml, "manifest-secret");
            var emitted = RequestPropertyRedaction.RedactRequestDetailsXml(storedXml);
            foreach (var secret in new[] { "manifest-secret", "secure-value", "caller-secret" })
            {
                Assert.IsFalse(emitted.Contains(secret));
                Assert.IsFalse(RequestPropertyRedaction.FormatForLog(request.RequestProperties).Contains(secret));
            }
            StringAssert.Contains(emitted, "public-value");
        }

        [TestMethod]
        public void CreateRequest_UnavailableCatalog_DoesNotSubmitUnclassifiedProperties()
        {
            var projects = Substitute.For<IProjectsPersistentSource>();
            projects.GetComponentsForProject("project").Returns(new[]
            {
                new ComponentApiModel { ComponentName = "component", TerraformSourceType = TerraformSourceType.Catalog,
                    TerraformTemplateName = "missing", TerraformTemplateVersion = "1.0.0" }
            });
            var factory = Substitute.For<IDeployableBuildFactory>();
            var service = new RequestService(Substitute.For<ILogger<RequestService>>(), factory, projects,
                Substitute.For<ITemplateCatalog>(), Substitute.For<IPropertiesPersistentSource>());
            var request = new RequestDto { Project = "project", BuildUrl = "file:\\\\build",
                Components = new List<string> { "component" },
                RequestProperties = new List<RequestProperty> { new() { PropertyName = "password", PropertyValue = "secret" } } };

            Assert.Throws<InvalidOperationException>(() => service.CreateRequest(request, new ClaimsPrincipal()));
            factory.DidNotReceive().CreateInstance(Arg.Any<RequestDto>());
        }

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
            ILogger<RequestService> mockedLog = Substitute.For<ILogger<RequestService>>();
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
            var test1Service = new RequestService(mockedLog, mockedFactory, mockedProjectsPds,
                Substitute.For<ITemplateCatalog>(), Substitute.For<IPropertiesPersistentSource>());

            var result1 = test1Service.CreateRequest(request, new GenericPrincipal(new GenericIdentity("test-user"), null));
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
            var mockedLog = new MockedLog<RequestService>();
            var mockedFactory = Substitute.For<IDeployableBuildFactory>();
            mockedFactory.CreateInstance(Arg.Any<RequestDto>())
                .Returns(new FileShareBuildStub(null, true));
            var test2Service = new RequestService(mockedLog, mockedFactory, mockedProjectsPds,
                Substitute.For<ITemplateCatalog>(), Substitute.For<IPropertiesPersistentSource>());
            var result2 = test2Service.CreateRequest(request, new GenericPrincipal(new GenericIdentity("test-user"), null));
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
            var mockedLog = new MockedLog<RequestService>();
            var mockedFactory = Substitute.For<IDeployableBuildFactory>();
            mockedFactory.CreateInstance(Arg.Any<RequestDto>())
                .Returns(new FileShareBuildStub(null, false));
            var test3service = new RequestService(mockedLog, mockedFactory, mockedProjectsPds,
                Substitute.For<ITemplateCatalog>(), Substitute.For<IPropertiesPersistentSource>());
            Exception expectedException = null;
            try
            {
                test3service.CreateRequest(request, new GenericPrincipal(new GenericIdentity("test-user"), null));
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
            var mockedFileSystemHelper = Substitute.For<IFileSystemHelper>();
            var mockedLogger = Substitute.For<ILogger<RequestService>>();
            var mockedLoggerFactory = Substitute.For<ILoggerFactory>();
            var mockDeployLibrary = Substitute.For<IDeployLibrary>();
            Func<GitHubDeployableBuild> mockedGitHubDeployableBuildFactory = () => null!;
            IDeployableBuildFactory factory = new DeployableBuildFactory(mockedFileSystemHelper, mockedLoggerFactory, mockedProjectsPds, mockDeployLibrary, mockRequestsPersistentSource, mockedGitHubDeployableBuildFactory);
            var test4Service = new RequestService(mockedLogger, factory, mockedProjectsPds,
                Substitute.For<ITemplateCatalog>(), Substitute.For<IPropertiesPersistentSource>());
            Exception expectedException1 = null;
            try
            {
                test4Service.CreateRequest(request1, new GenericPrincipal(new GenericIdentity("test-user"), null));

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
            var mockedLog = Substitute.For<ILogger<RequestService>>();
            var request = new RequestDto();
            var mockedProjectsPds = Substitute.For<IProjectsPersistentSource>();

            mockedProjectsPds.GetProject(Arg.Any<string>())
                .Returns(new ProjectApiModel
                {
                    ArtefactsUrl = "http://some_url"
                });

            var service = new RequestService(mockedLog, mockedBuildFactory, mockedProjectsPds,
                Substitute.For<ITemplateCatalog>(), Substitute.For<IPropertiesPersistentSource>());
            service.CheckRequest(ref request);
            Assert.IsTrue(request.BuildUrl.Equals("http://some_url"));
        }
    }
}
