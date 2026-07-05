using Dorc.Api.Controllers;
using Dorc.Api.Interfaces;
using Dorc.ApiModel;
using Dorc.Core.AzureStorageAccount;
using Dorc.Core.Interfaces;
using Dorc.PersistentData;
using Dorc.PersistentData.Model;
using Dorc.PersistentData.Sources.Interfaces;
using Dorc.Terraform.Catalog;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using System.Security.Claims;

namespace Dorc.Api.Tests.Controllers
{
    [TestClass]
    public class TerraformControllerTests
    {
        private IRequestsPersistentSource _requests = null!;
        private ISecurityPrivilegesChecker _security = null!;
        private IClaimsPrincipalReader _claimsReader = null!;
        private IAzureStorageAccountWorker _storage = null!;
        private ITemplateCatalog _catalog = null!;
        private IProjectsPersistentSource _projects = null!;
        private IManageProjectsPersistentSource _manageProjects = null!;
        private IParameterValidator _parameterValidator = null!;
        private IRequestService _requestService = null!;
        private TerraformController _controller = null!;

        private const int DeploymentResultId = 42;
        private const int RequestId = 1001;
        private const string EnvName = "TEVO DV 11";
        private const string ProjectName = "TEVO";

        [TestInitialize]
        public void Setup()
        {
            _requests = Substitute.For<IRequestsPersistentSource>();
            _security = Substitute.For<ISecurityPrivilegesChecker>();
            _claimsReader = Substitute.For<IClaimsPrincipalReader>();
            _storage = Substitute.For<IAzureStorageAccountWorker>();
            _catalog = Substitute.For<ITemplateCatalog>();
            _projects = Substitute.For<IProjectsPersistentSource>();
            _manageProjects = Substitute.For<IManageProjectsPersistentSource>();
            _parameterValidator = Substitute.For<IParameterValidator>();
            _requestService = Substitute.For<IRequestService>();

            _controller = new TerraformController(
                NullLogger<TerraformController>.Instance,
                _requests,
                _security,
                _claimsReader,
                _storage,
                _catalog,
                _projects,
                _manageProjects,
                _parameterValidator,
                _requestService)
            {
                ControllerContext = new ControllerContext
                {
                    HttpContext = new DefaultHttpContext()
                }
            };
            _controller.HttpContext.User = new ClaimsPrincipal(
                new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, "TestUser") }, "TestAuth"));
        }

        private void GivenStandardDeploymentResult(string status = "WaitingConfirmation")
        {
            _requests.GetDeploymentResults(DeploymentResultId).Returns(new DeploymentResultApiModel
            {
                Id = DeploymentResultId,
                RequestId = RequestId,
                Status = status
            });
            _requests.GetRequest(RequestId).Returns(new DeploymentRequestApiModel
            {
                Id = RequestId,
                EnvironmentName = EnvName,
                Project = ProjectName
            });
        }

        // ---------- InstantiateTemplate ----------

        private const int ProjectId = 7;
        private const string TemplateName = "testmod";
        private const string TemplateVersion = "1.0.0";

        private static TerraformTemplateManifest Manifest(params TerraformTemplateParameter[] parameters)
            => new(
                Name: TemplateName,
                Version: TemplateVersion,
                Source: new TerraformTemplateSource("git", "https://example/repo", "v1.0.0"),
                Parameters: parameters,
                Outputs: Array.Empty<TerraformTemplateOutput>(),
                Description: null,
                Tags: Array.Empty<string>(),
                Category: null,
                RequiredProviders: new Dictionary<string, string>(),
                RequiredTerraformVersion: ">= 1.5.0",
                Owner: null,
                Deprecated: false,
                DeprecationReason: null);

        private void GivenTemplateAndProject(TerraformTemplateManifest manifest)
        {
            _catalog.GetAsync(TemplateName, TemplateVersion, Arg.Any<CancellationToken>())
                .Returns(manifest);
            _projects.GetProject(ProjectId).Returns(new ProjectApiModel
            {
                ProjectId = ProjectId,
                ProjectName = ProjectName
            });
            _security.IsProjectOwnerOrAdmin(Arg.Any<ClaimsPrincipal>(), ProjectName).Returns(true);
        }

        [TestMethod]
        public async Task InstantiateTemplate_UnknownTemplate_Returns404()
        {
            _catalog.GetAsync(TemplateName, TemplateVersion, Arg.Any<CancellationToken>())
                .Returns((TerraformTemplateManifest?)null);

            var result = await _controller.InstantiateTemplate(
                TemplateName,
                TemplateVersion,
                new TerraformTemplateInstantiateRequestApiModel { ProjectId = ProjectId },
                CancellationToken.None);

            Assert.IsInstanceOfType(result, typeof(NotFoundObjectResult));
            _manageProjects.DidNotReceiveWithAnyArgs()
                .CreateComponent(default!, default, default, default!);
        }

        [TestMethod]
        public async Task InstantiateTemplate_DeployRequestedButCannotModifyEnvironment_Returns403()
        {
            GivenTemplateAndProject(Manifest());
            _security.CanModifyEnvironment(Arg.Any<ClaimsPrincipal>(), EnvName).Returns(false);

            var result = await _controller.InstantiateTemplate(
                TemplateName,
                TemplateVersion,
                new TerraformTemplateInstantiateRequestApiModel
                {
                    ProjectId = ProjectId,
                    EnvironmentName = EnvName,
                    Parameters = new Dictionary<string, string>()
                },
                CancellationToken.None);

            Assert.IsInstanceOfType(result, typeof(ObjectResult), "Deploy-path RBAC failure returns StatusCode(403, message).");
            var objectResult = (ObjectResult)result;
            Assert.AreEqual(StatusCodes.Status403Forbidden, objectResult.StatusCode);
            _requestService.DidNotReceiveWithAnyArgs().CreateRequest(default!, default!);
        }

        [TestMethod]
        public async Task InstantiateTemplate_ParameterValidationFails_Returns400WithoutSensitiveValue()
        {
            // Uses the real ParameterValidator (not the mock) so the test
            // proves the sensitive value is redacted end-to-end: manifest
            // Sensitive flag -> validator message -> 400 response body.
            const string rawSecret = "hunter2-not-a-number";
            var manifest = Manifest(new TerraformTemplateParameter(
                Name: "admin_password",
                Type: TerraformParameterType.Number,
                Required: true,
                Description: null,
                Default: null,
                AllowedValues: null,
                Pattern: null,
                Min: null,
                Max: null,
                Sensitive: true));
            GivenTemplateAndProject(manifest);
            _security.CanModifyEnvironment(Arg.Any<ClaimsPrincipal>(), EnvName).Returns(true);

            var controller = new TerraformController(
                NullLogger<TerraformController>.Instance,
                _requests,
                _security,
                _claimsReader,
                _storage,
                _catalog,
                _projects,
                _manageProjects,
                new ParameterValidator(),
                _requestService)
            {
                ControllerContext = new ControllerContext
                {
                    HttpContext = new DefaultHttpContext()
                }
            };
            controller.HttpContext.User = _controller.HttpContext.User;

            var result = await controller.InstantiateTemplate(
                TemplateName,
                TemplateVersion,
                new TerraformTemplateInstantiateRequestApiModel
                {
                    ProjectId = ProjectId,
                    EnvironmentName = EnvName,
                    Parameters = new Dictionary<string, string> { ["admin_password"] = rawSecret }
                },
                CancellationToken.None);

            Assert.IsInstanceOfType(result, typeof(BadRequestObjectResult), "Validation failure must produce 400.");
            var badRequest = (BadRequestObjectResult)result;
            Assert.IsInstanceOfType(badRequest.Value, typeof(string), "400 body is the validation message string.");
            var body = (string)badRequest.Value!;
            Assert.IsFalse(body.Contains(rawSecret),
                "Sensitive parameter's raw value must not appear in the 400 response body.");
            StringAssert.Contains(body, "[REDACTED]",
                "Sensitive parameter's value is replaced with the redaction marker.");
            _requestService.DidNotReceiveWithAnyArgs().CreateRequest(default!, default!);
        }

        [TestMethod]
        public async Task InstantiateTemplate_CreateAndDeployHappyPath_SubmitsRequestAndReturns200()
        {
            const int newRequestId = 123;
            GivenTemplateAndProject(Manifest());
            _security.CanModifyEnvironment(Arg.Any<ClaimsPrincipal>(), EnvName).Returns(true);
            _parameterValidator.Validate(
                    Arg.Any<TerraformTemplateManifest>(),
                    Arg.Any<IReadOnlyDictionary<string, string?>>())
                .Returns(new ParameterValidationResult(true, Array.Empty<ParameterValidationError>()));
            _requestService.CreateRequest(Arg.Any<RequestDto>(), Arg.Any<ClaimsPrincipal>())
                .Returns(new RequestStatusDto { Id = newRequestId, Status = "Pending" });

            var result = await _controller.InstantiateTemplate(
                TemplateName,
                TemplateVersion,
                new TerraformTemplateInstantiateRequestApiModel
                {
                    ProjectId = ProjectId,
                    EnvironmentName = EnvName,
                    Parameters = new Dictionary<string, string>()
                },
                CancellationToken.None);

            Assert.IsInstanceOfType(result, typeof(OkObjectResult), "Happy-path create-and-deploy returns 200.");
            var ok = (OkObjectResult)result;
            _manageProjects.Received(1).CreateComponent(
                Arg.Any<ComponentApiModel>(), ProjectId, Arg.Any<int?>(), Arg.Any<string>());
            _requestService.Received(1).CreateRequest(
                Arg.Is<RequestDto>(dto =>
                    dto.Environment == EnvName
                    && dto.Project == ProjectName
                    && dto.Components.Contains(TemplateName)),
                Arg.Any<ClaimsPrincipal>());

            // The 200 body is an anonymous object { component, requestId, requestStatus }.
            var requestIdProperty = ok.Value!.GetType().GetProperty("requestId");
            if (requestIdProperty is null)
            {
                Assert.Fail("200 body carries a requestId member.");
                return;
            }
            Assert.AreEqual(newRequestId, (int)requestIdProperty.GetValue(ok.Value)!);
        }

        [TestMethod]
        public async Task InstantiateTemplate_CreateOnlyWithoutEnvironment_Returns200AndDoesNotSubmitRequest()
        {
            GivenTemplateAndProject(Manifest());

            var result = await _controller.InstantiateTemplate(
                TemplateName,
                TemplateVersion,
                new TerraformTemplateInstantiateRequestApiModel { ProjectId = ProjectId },
                CancellationToken.None);

            Assert.IsInstanceOfType(result, typeof(OkObjectResult), "Create-only mode returns 200.");
            var ok = (OkObjectResult)result;
            Assert.IsInstanceOfType(ok.Value, typeof(ComponentApiModel), "Create-only 200 body is the created ComponentApiModel.");
            var component = (ComponentApiModel)ok.Value!;
            Assert.AreEqual(TemplateName, component.TerraformTemplateName);
            Assert.AreEqual(TemplateVersion, component.TerraformTemplateVersion);
            _manageProjects.Received(1).CreateComponent(
                Arg.Any<ComponentApiModel>(), ProjectId, Arg.Any<int?>(), Arg.Any<string>());
            _requestService.DidNotReceiveWithAnyArgs().CreateRequest(default!, default!);
        }

        // ---------- View ----------

        [TestMethod]
        public void GetTerraformPlan_EnvOwner_Returns200()
        {
            GivenStandardDeploymentResult();
            _security.IsEnvironmentOwnerOrAdmin(Arg.Any<ClaimsPrincipal>(), EnvName).Returns(true);
            _storage.LoadFileFromBlobs(Arg.Any<string>()).Returns("plan-content");

            var result = _controller.GetTerraformPlan(DeploymentResultId);

            Assert.IsInstanceOfType(result, typeof(OkObjectResult));
        }

        [TestMethod]
        public void GetTerraformPlan_ProjectOwnerOnly_Returns200()
        {
            GivenStandardDeploymentResult();
            _security.IsEnvironmentOwnerOrAdmin(Arg.Any<ClaimsPrincipal>(), EnvName).Returns(false);
            _security.IsProjectOwnerOrAdmin(Arg.Any<ClaimsPrincipal>(), ProjectName).Returns(true);
            _storage.LoadFileFromBlobs(Arg.Any<string>()).Returns("plan-content");

            var result = _controller.GetTerraformPlan(DeploymentResultId);

            Assert.IsInstanceOfType(result, typeof(OkObjectResult));
        }

        [TestMethod]
        public void GetTerraformPlan_NeitherOwnerNorAdmin_Returns403()
        {
            GivenStandardDeploymentResult();
            _security.IsEnvironmentOwnerOrAdmin(Arg.Any<ClaimsPrincipal>(), Arg.Any<string>()).Returns(false);
            _security.IsProjectOwnerOrAdmin(Arg.Any<ClaimsPrincipal>(), Arg.Any<string>()).Returns(false);

            var result = _controller.GetTerraformPlan(DeploymentResultId);

            Assert.IsInstanceOfType(result, typeof(ForbidResult));
        }

        [TestMethod]
        public void GetTerraformPlan_DeploymentResultMissing_Returns404()
        {
            _requests.GetDeploymentResults(DeploymentResultId).Returns((DeploymentResultApiModel?)null);

            var result = _controller.GetTerraformPlan(DeploymentResultId);

            Assert.IsInstanceOfType(result, typeof(NotFoundObjectResult));
        }

        [TestMethod]
        public void GetTerraformPlan_RequestLookupNull_Returns403()
        {
            _requests.GetDeploymentResults(DeploymentResultId).Returns(new DeploymentResultApiModel
            {
                Id = DeploymentResultId,
                RequestId = RequestId
            });
            _requests.GetRequest(RequestId).Returns((DeploymentRequestApiModel?)null);

            var result = _controller.GetTerraformPlan(DeploymentResultId);

            Assert.IsInstanceOfType(result, typeof(ForbidResult));
        }

        // ---------- Confirm ----------

        [TestMethod]
        public void ConfirmTerraformPlan_CanModifyEnvironment_Returns200()
        {
            GivenStandardDeploymentResult();
            _security.CanModifyEnvironment(Arg.Any<ClaimsPrincipal>(), EnvName).Returns(true);

            var result = _controller.ConfirmTerraformPlan(DeploymentResultId);

            Assert.IsInstanceOfType(result, typeof(OkObjectResult));
        }

        [TestMethod]
        public void ConfirmTerraformPlan_CannotModifyEnvironment_Returns403()
        {
            GivenStandardDeploymentResult();
            _security.CanModifyEnvironment(Arg.Any<ClaimsPrincipal>(), EnvName).Returns(false);

            var result = _controller.ConfirmTerraformPlan(DeploymentResultId);

            Assert.IsInstanceOfType(result, typeof(ForbidResult));
        }

        // ---------- Decline ----------

        [TestMethod]
        public void DeclineTerraformPlan_CanModifyEnvironment_Returns200()
        {
            GivenStandardDeploymentResult();
            _security.CanModifyEnvironment(Arg.Any<ClaimsPrincipal>(), EnvName).Returns(true);

            var result = _controller.DeclineTerraformPlan(DeploymentResultId);

            Assert.IsInstanceOfType(result, typeof(OkObjectResult));
        }

        [TestMethod]
        public void DeclineTerraformPlan_CannotModifyEnvironment_Returns403()
        {
            GivenStandardDeploymentResult();
            _security.CanModifyEnvironment(Arg.Any<ClaimsPrincipal>(), EnvName).Returns(false);

            var result = _controller.DeclineTerraformPlan(DeploymentResultId);

            Assert.IsInstanceOfType(result, typeof(ForbidResult));
        }
    }
}
