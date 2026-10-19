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
    private RequestController _controller = null!;

    [TestInitialize]
    public void Setup()
    {
        _service = Substitute.For<IRequestService>();
        _projects = Substitute.For<IProjectsPersistentSource>();
        _security = Substitute.For<ISecurityPrivilegesChecker>();
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
            Substitute.For<IRequestsManager>(), Substitute.For<IRequestsPersistentSource>(),
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
}
