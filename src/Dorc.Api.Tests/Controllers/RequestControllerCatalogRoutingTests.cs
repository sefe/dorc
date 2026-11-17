using Dorc.Api.Controllers;
using Dorc.Api.Interfaces;
using Dorc.Api.Model;
using Dorc.ApiModel;
using Dorc.Core.Configuration;
using Dorc.Core.Events;
using Dorc.Core.Interfaces;
using Dorc.OpenSearchData.Sources.Interfaces;
using Dorc.PersistentData;
using Dorc.PersistentData.Sources.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using System.Security.Claims;

namespace Dorc.Api.Tests.Controllers;

[TestClass]
public class RequestControllerCatalogRoutingTests
{
    private const string Project = "project";
    private const string Environment = "environment";
    private IRequestService _service = null!;
    private IProjectsPersistentSource _projects = null!;
    private ISecurityPrivilegesChecker _security = null!;
    private IRequestsPersistentSource _requests = null!;
    private RequestController _controller = null!;

    [TestInitialize]
    public void Setup()
    {
        _service = Substitute.For<IRequestService>();
        _projects = Substitute.For<IProjectsPersistentSource>();
        _security = Substitute.For<ISecurityPrivilegesChecker>();
        _requests = Substitute.For<IRequestsPersistentSource>();
        var environments = Substitute.For<IEnvironmentsPersistentSource>();
        var directoryFactory = Substitute.For<IDirectorySearcherFactory>();
        directoryFactory.GetEntraSearcher().Returns(Substitute.For<IActiveDirectorySearcher>());
        _projects.GetProject(Project).Returns(new ProjectApiModel { ProjectName = Project });
        environments.GetEnvironment(Environment).Returns(new EnvironmentApiModel { EnvironmentName = Environment });
        _security.CanModifyEnvironment(Arg.Any<ClaimsPrincipal>(), Environment).Returns(true);
        _service.CreateRequest(Arg.Any<RequestDto>(), Arg.Any<ClaimsPrincipal>())
            .Returns(new RequestStatusDto { Id = 42, Status = "Pending" });
        _controller = new RequestController(
            _service, _security, NullLogger<RequestController>.Instance,
            Substitute.For<IRequestsManager>(), _requests,
            _projects, Substitute.For<IClaimsPrincipalReader>(), Substitute.For<IDeploymentEventsPublisher>(),
            Substitute.For<IConfigurationSettings>(), environments, directoryFactory,
            Substitute.For<IDeploymentLogService>())
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
    }

    private static ComponentApiModel CatalogComponent(string name) => new()
    {
        ComponentName = name,
        TerraformSourceType = TerraformSourceType.Catalog,
        TerraformTemplateName = "cosmosdb",
        TerraformTemplateVersion = "1.0.0"
    };

    private static RequestDto Request(params string[] components) => new()
    {
        Project = Project,
        Environment = Environment,
        Components = components
    };

    [TestMethod]
    public void Restart_RetiredBackendReturnsConflictWithoutArchivingOrRequeueing()
    {
        _requests.GetRequestForUser(42, Arg.Any<ClaimsPrincipal>()).Returns(new DeploymentRequestApiModel
        {
            Id = 42, EnvironmentName = Environment, Status = "Failed"
        });
        _requests.When(r => r.EnsureRequestBackendNotRetired(42))
            .Do(_ => throw new TerraformBackendRetiredException());
        Assert.IsInstanceOfType<ConflictObjectResult>(_controller.RestartPost(42));
        _requests.DidNotReceive().ArchiveCurrentAttempt(Arg.Any<int>());
        _requests.DidNotReceive().UpdateRequestStatus(Arg.Any<int>(), Arg.Any<DeploymentRequestStatus>(), Arg.Any<string>());
    }

    [TestMethod]
    public void Post_AllCatalogWithoutAnyBuildMetadata_SubmitsVerifiedCatalogRequest()
    {
        _projects.GetComponentsForProject(Project).Returns(new[]
        {
            CatalogComponent("cosmos"), CatalogComponent("storage")
        });
        var request = Request("cosmos", "storage");

        var result = _controller.Post(request);

        Assert.IsInstanceOfType<OkObjectResult>(result);
        Assert.AreEqual(BuildDetails.CatalogSentinel, request.BuildUrl);
        Assert.IsNull(request.BuildText);
        Assert.IsNull(request.BuildNum);
        _service.Received(1).CreateRequest(request, Arg.Any<ClaimsPrincipal>());
    }

    [TestMethod]
    public void Post_MixedComponentsWithoutBuildMetadata_RejectsBeforeSubmission()
    {
        _projects.GetComponentsForProject(Project).Returns(new[]
        {
            CatalogComponent("cosmos"), new ComponentApiModel { ComponentName = "legacy" }
        });

        var result = _controller.Post(Request("cosmos", "legacy"));

        Assert.IsInstanceOfType<BadRequestObjectResult>(result);
        StringAssert.Contains((string)((BadRequestObjectResult)result).Value!, "build artifact");
        _service.DidNotReceiveWithAnyArgs().CreateRequest(default!, default!);
    }

    [DataTestMethod]
    [DataRow(null, null, "Either BuildText or BuildUrl must be provided.")]
    [DataRow("build", null, "BuildNum must be provided.")]
    public void Post_NonCatalogWithoutRequiredBuildMetadata_RetainsValidation(
        string? buildText, string? buildNum, string expectedMessage)
    {
        _projects.GetComponentsForProject(Project).Returns(new[] { new ComponentApiModel { ComponentName = "legacy" } });
        var request = Request("legacy");
        request.BuildText = buildText!;
        request.BuildNum = buildNum!;

        var result = _controller.Post(request);

        Assert.IsInstanceOfType<BadRequestObjectResult>(result);
        Assert.AreEqual(expectedMessage, ((BadRequestObjectResult)result).Value);
        _service.DidNotReceiveWithAnyArgs().CreateRequest(default!, default!);
    }

    [TestMethod]
    public void Post_LegacyWithBuildMetadataAndNoUrl_PreservesArtifactFallback()
    {
        _projects.GetComponentsForProject(Project).Returns(new[] { new ComponentApiModel { ComponentName = "legacy" } });
        var request = Request("legacy");
        request.BuildText = "build";
        request.BuildNum = "123";

        Assert.IsInstanceOfType<OkObjectResult>(_controller.Post(request));
        Assert.IsNull(request.BuildUrl);
        _service.Received(1).CreateRequest(request, Arg.Any<ClaimsPrincipal>());
    }

    [TestMethod]
    public void Post_ArtifactBackedCatalogWithoutBuildNumber_RetainsValidation()
    {
        _projects.GetComponentsForProject(Project).Returns(new[] { CatalogComponent("cosmos") });
        var request = Request("cosmos");
        request.BuildUrl = "https://example.test/build";

        var result = _controller.Post(request);

        Assert.IsInstanceOfType<BadRequestObjectResult>(result);
        Assert.AreEqual("BuildNum must be provided.", ((BadRequestObjectResult)result).Value);
        _service.DidNotReceiveWithAnyArgs().CreateRequest(default!, default!);
    }

    [TestMethod]
    public void Post_ClientSuppliedSentinel_CannotBypassCatalogVerification()
    {
        _projects.GetComponentsForProject(Project).Returns(new[] { new ComponentApiModel { ComponentName = "legacy" } });
        var request = Request("legacy");
        request.BuildUrl = BuildDetails.CatalogSentinel;

        Assert.IsInstanceOfType<BadRequestObjectResult>(_controller.Post(request));
        _service.DidNotReceiveWithAnyArgs().CreateRequest(default!, default!);
    }

    [TestMethod]
    public void Post_NoEnvironmentAuthority_DoesNotResolveCatalogBindings()
    {
        _projects.GetComponentsForProject(Project).Returns(new[] { CatalogComponent("cosmos") });
        _security.CanModifyEnvironment(Arg.Any<ClaimsPrincipal>(), Environment).Returns(false);

        var result = _controller.Post(Request("cosmos"));

        Assert.AreEqual(StatusCodes.Status403Forbidden, ((ObjectResult)result).StatusCode);
        _projects.Received(1).GetComponentsForProject(Project);
        _service.DidNotReceiveWithAnyArgs().CreateRequest(default!, default!);
    }

    [TestMethod]
    public void Post_ConcurrentMissingComponentAndNullCatalogBinding_HaveSameOpaqueRejection()
    {
        var incomplete = CatalogComponent("cosmos");
        incomplete.TerraformTemplateVersion = null;
        _projects.GetComponentsForProject(Project).Returns(new[] { incomplete });
        var missingBinding = (BadRequestObjectResult)_controller.Post(Request("cosmos"));
        _projects.GetComponentsForProject(Project).Returns(new[] { CatalogComponent("cosmos") }, Array.Empty<ComponentApiModel>());

        var missingComponent = (BadRequestObjectResult)_controller.Post(Request("cosmos"));

        Assert.AreEqual(missingBinding.Value, missingComponent.Value);
        _service.DidNotReceiveWithAnyArgs().CreateRequest(default!, default!);
    }

    [TestMethod]
    public void Post_ReservedLifecycleProperty_ReturnsBadRequestBeforeSubmission()
    {
        _projects.GetComponentsForProject(Project).Returns(new[] { CatalogComponent("cosmos") });
        foreach (var reserved in TerraformDestroyOperationConstants.ReservedLifecycleProperties)
        {
            var request = Request("cosmos");
            request.RequestProperties = new List<RequestProperty>
            {
                new() { PropertyName = "RegularProp", PropertyValue = "ok" },
                null!,
                new() { PropertyName = reserved, PropertyValue = "injected" }
            };

            var result = _controller.Post(request);

            Assert.IsInstanceOfType<BadRequestObjectResult>(result);
            StringAssert.Contains((string)((BadRequestObjectResult)result).Value!, reserved);
        }
        _service.DidNotReceiveWithAnyArgs().CreateRequest(default!, default!);
    }

    [DataTestMethod]
    [DataRow(EphemeralEnvironmentStates.TearingDown)]
    [DataRow(EphemeralEnvironmentStates.TornDown)]
    public void Post_EphemeralEnvironmentInTearingDownOrTornDown_ReturnsConflict(string ephemeralState)
    {
        var environments = Substitute.For<IEnvironmentsPersistentSource>();
        environments.GetEnvironment(Environment).Returns(new EnvironmentApiModel
        {
            EnvironmentName = Environment,
            IsEphemeral = true,
            EphemeralState = ephemeralState
        });
        var directoryFactory = Substitute.For<IDirectorySearcherFactory>();
        directoryFactory.GetEntraSearcher().Returns(Substitute.For<IActiveDirectorySearcher>());
        var controller = new RequestController(
            _service, _security, NullLogger<RequestController>.Instance,
            Substitute.For<IRequestsManager>(), _requests,
            _projects, Substitute.For<IClaimsPrincipalReader>(), Substitute.For<IDeploymentEventsPublisher>(),
            Substitute.For<IConfigurationSettings>(), environments, directoryFactory,
            Substitute.For<IDeploymentLogService>())
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };

        var result = controller.Post(Request("cosmos"));

        Assert.IsInstanceOfType<ConflictObjectResult>(result);
        Assert.AreEqual(
            "Cannot submit deployments to an ephemeral environment that is tearing down or torn down.",
            ((ConflictObjectResult)result).Value);
        _service.DidNotReceiveWithAnyArgs().CreateRequest(default!, default!);

        // Also cover RestartPost and ResumePut when ephemeral environment is TearingDown/TornDown vs Ready
        var configSettings = Substitute.For<IConfigurationSettings>();
        configSettings.GetPauseDeploymentEnabled().Returns(true);
        var resumeController = new RequestController(
            _service, _security, NullLogger<RequestController>.Instance,
            Substitute.For<IRequestsManager>(), _requests,
            _projects, Substitute.For<IClaimsPrincipalReader>(), Substitute.For<IDeploymentEventsPublisher>(),
            configSettings, environments, directoryFactory,
            Substitute.For<IDeploymentLogService>())
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
        _requests.GetRequestForUser(501, Arg.Any<ClaimsPrincipal>()).Returns(new DeploymentRequestApiModel
        {
            Id = 501,
            EnvironmentName = Environment,
            Status = DeploymentRequestStatus.Paused.ToString(),
            RequestDetails = "<DeploymentRequestDetail><Properties><PropertyPair><Name>EphemeralEnvironmentId</Name><Value>42</Value></PropertyPair></Properties></DeploymentRequestDetail>"
        });
        Assert.IsInstanceOfType<ConflictObjectResult>(resumeController.RestartPost(501));
        Assert.IsInstanceOfType<ConflictObjectResult>(resumeController.ResumePut(501));

        environments.GetEnvironment(Environment).Returns(new EnvironmentApiModel
        {
            EnvironmentId = 42,
            EnvironmentName = Environment,
            IsEphemeral = true,
            EphemeralState = EphemeralEnvironmentStates.Ready
        });
        _projects.GetComponentsForProject(Project).Returns(new[] { CatalogComponent("cosmos") });
        _service.CreateRequest(Arg.Any<RequestDto>(), Arg.Any<ClaimsPrincipal>())
            .Returns(new RequestStatusDto { Id = 999, Status = "Pending" });
        var readyReqWithoutProps = Request("cosmos");
        readyReqWithoutProps.RequestProperties = null!;
        Assert.IsInstanceOfType<OkObjectResult>(controller.Post(readyReqWithoutProps));
        Assert.IsTrue(readyReqWithoutProps.RequestProperties.Any(p =>
            p.PropertyName == TerraformDestroyOperationConstants.EphemeralEnvironmentIdPropertyName &&
            p.PropertyValue == "42"));
        var readyReqWithProps = Request("cosmos");
        readyReqWithProps.RequestProperties = new List<RequestProperty> { new() { PropertyName = "Custom", PropertyValue = "1" } };
        Assert.IsInstanceOfType<OkObjectResult>(controller.Post(readyReqWithProps));
        Assert.IsTrue(readyReqWithProps.RequestProperties.Any(p =>
            p.PropertyName == TerraformDestroyOperationConstants.EphemeralEnvironmentIdPropertyName &&
            p.PropertyValue == "42"));
        Assert.IsInstanceOfType<ObjectResult>(resumeController.RestartPost(501));
        Assert.IsInstanceOfType<ObjectResult>(resumeController.ResumePut(501));

        // Null and non-ephemeral environment allowed through ResumePut
        environments.GetEnvironment(Environment).Returns((EnvironmentApiModel?)null);
        Assert.IsInstanceOfType<ObjectResult>(resumeController.ResumePut(501));
        environments.GetEnvironment(Environment).Returns(new EnvironmentApiModel { EnvironmentId = 1, EnvironmentName = Environment, IsEphemeral = false });
        Assert.IsInstanceOfType<ObjectResult>(resumeController.ResumePut(501));

        var readyEnv = new EnvironmentApiModel { EnvironmentId = 42, EnvironmentName = Environment, IsEphemeral = true, EphemeralState = EphemeralEnvironmentStates.Ready };
        var provEnv = new EnvironmentApiModel { EnvironmentId = 42, EnvironmentName = Environment, IsEphemeral = true, EphemeralState = EphemeralEnvironmentStates.Provisioning };
        var testEnv = new EnvironmentApiModel { EnvironmentId = 42, EnvironmentName = Environment, IsEphemeral = true, EphemeralState = EphemeralEnvironmentStates.Testing };

        // Destroy request is always blocked for restart/resume
        environments.GetEnvironment(Environment).Returns(readyEnv);
        _requests.GetRequestForUser(501, Arg.Any<ClaimsPrincipal>()).Returns(new DeploymentRequestApiModel
        {
            Id = 501,
            EnvironmentName = Environment,
            Status = DeploymentRequestStatus.Paused.ToString(),
            RequestDetails = "<DeploymentRequestDetail><Properties><PropertyPair><Name>TerraformOperation</Name><Value>Destroy</Value></PropertyPair></Properties></DeploymentRequestDetail>"
        });
        Assert.IsInstanceOfType<ConflictObjectResult>(resumeController.ResumePut(501));

        // Mismatched EphemeralEnvironmentId is blocked; matching is allowed on Ready
        _requests.GetRequestForUser(501, Arg.Any<ClaimsPrincipal>()).Returns(new DeploymentRequestApiModel
        {
            Id = 501,
            EnvironmentName = Environment,
            Status = DeploymentRequestStatus.Paused.ToString(),
            RequestDetails = "<DeploymentRequestDetail><Properties><PropertyPair><Name>EphemeralEnvironmentId</Name><Value>999</Value></PropertyPair></Properties></DeploymentRequestDetail>"
        });
        Assert.IsInstanceOfType<ConflictObjectResult>(resumeController.ResumePut(501));
        _requests.GetRequestForUser(501, Arg.Any<ClaimsPrincipal>()).Returns(new DeploymentRequestApiModel
        {
            Id = 501,
            EnvironmentName = Environment,
            Status = DeploymentRequestStatus.Paused.ToString(),
            RequestDetails = "<DeploymentRequestDetail><Properties><PropertyPair><Name>EphemeralEnvironmentId</Name><Value>42</Value></PropertyPair></Properties></DeploymentRequestDetail>"
        });
        Assert.IsInstanceOfType<ObjectResult>(resumeController.ResumePut(501));

        // Provisioning / Testing allows lifecycle requests with matching EphemeralEnvironmentId and blocks non-lifecycle or un-stamped requests
        environments.GetEnvironment(Environment).Returns(provEnv);
        _requests.GetRequestForUser(501, Arg.Any<ClaimsPrincipal>()).Returns(new DeploymentRequestApiModel
        {
            Id = 501,
            EnvironmentName = Environment,
            Status = DeploymentRequestStatus.Paused.ToString(),
            RequestDetails = null
        });
        Assert.IsInstanceOfType<ConflictObjectResult>(resumeController.ResumePut(501));

        environments.GetEnvironment(Environment).Returns(testEnv);
        _requests.GetRequestForUser(501, Arg.Any<ClaimsPrincipal>()).Returns(new DeploymentRequestApiModel
        {
            Id = 501,
            EnvironmentName = Environment,
            Status = DeploymentRequestStatus.Paused.ToString(),
            RequestDetails = "<DeploymentRequestDetail><Properties><PropertyPair><Name>EphemeralTargetState</Name><Value>Ready</Value></PropertyPair></Properties></DeploymentRequestDetail>"
        });
        Assert.IsInstanceOfType<ConflictObjectResult>(resumeController.ResumePut(501));

        environments.GetEnvironment(Environment).Returns(provEnv);
        _requests.GetRequestForUser(501, Arg.Any<ClaimsPrincipal>()).Returns(new DeploymentRequestApiModel
        {
            Id = 501,
            EnvironmentName = Environment,
            Status = DeploymentRequestStatus.Paused.ToString(),
            RequestDetails = "<DeploymentRequestDetail><Properties><PropertyPair><Name>EphemeralTargetState</Name><Value>Ready</Value></PropertyPair><PropertyPair><Name>EphemeralEnvironmentId</Name><Value>42</Value></PropertyPair></Properties></DeploymentRequestDetail>"
        });
        Assert.IsInstanceOfType<ObjectResult>(resumeController.ResumePut(501));

        // ProvisionFailed / ValidationFailed restores state to Provisioning / Testing when lifecycle request is restarted or resumed, and blocks non-lifecycle requests
        var provFailedEnv = new EnvironmentApiModel { EnvironmentId = 42, EnvironmentName = Environment, IsEphemeral = true, EphemeralState = EphemeralEnvironmentStates.ProvisionFailed };
        var valFailedEnv = new EnvironmentApiModel { EnvironmentId = 42, EnvironmentName = Environment, IsEphemeral = true, EphemeralState = EphemeralEnvironmentStates.ValidationFailed };
        environments.GetEnvironment(Environment).Returns(provFailedEnv);
        Assert.IsInstanceOfType<ObjectResult>(resumeController.RestartPost(501));
        environments.Received(1).TryTransitionEphemeralState(Environment, Arg.Any<IReadOnlyCollection<string>>(), EphemeralEnvironmentStates.Provisioning, 42);
        environments.GetEnvironment(Environment).Returns(valFailedEnv);
        Assert.IsInstanceOfType<ObjectResult>(resumeController.ResumePut(501));
        environments.Received(1).TryTransitionEphemeralState(Environment, Arg.Any<IReadOnlyCollection<string>>(), EphemeralEnvironmentStates.Testing, 42);
        _requests.GetRequestForUser(501, Arg.Any<ClaimsPrincipal>()).Returns(new DeploymentRequestApiModel
        {
            Id = 501,
            EnvironmentName = Environment,
            Status = DeploymentRequestStatus.Paused.ToString(),
            RequestDetails = "<DeploymentRequestDetail><Properties><PropertyPair><Name>EphemeralEnvironmentId</Name><Value>42</Value></PropertyPair></Properties></DeploymentRequestDetail>"
        });
        Assert.IsInstanceOfType<ConflictObjectResult>(resumeController.ResumePut(501));
    }
}
