using System.Security.Claims;
using Dorc.Api.Controllers;
using Dorc.Api.Interfaces;
using Dorc.Api.Model;
using Dorc.ApiModel;
using Dorc.Core;
using Dorc.Core.Interfaces;
using Dorc.PersistentData;
using Dorc.PersistentData.Model;
using Dorc.PersistentData.Sources.Interfaces;
using Dorc.Terraform.Catalog;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Dorc.Api.Tests.Controllers
{
    [TestClass]
    public class EphemeralEnvironmentsControllerTests
    {
        private ILogger<EphemeralEnvironmentsController> _log = null!;
        private IEnvironmentsPersistentSource _environmentsPersistentSource = null!;
        private IEnvironmentTemplatesPersistentSource _environmentTemplatesPersistentSource = null!;
        private IProjectsPersistentSource _projectsPersistentSource = null!;
        private IRequestsPersistentSource _requestsPersistentSource = null!;
        private ISecurityPrivilegesChecker _securityPrivilegesChecker = null!;
        private IRolePrivilegesChecker _rolePrivilegesChecker = null!;
        private IClaimsPrincipalReader _claimsPrincipalReader = null!;
        private ITemplateCatalog _templateCatalog = null!;
        private IRequestService _requestService = null!;
        private EphemeralEnvironmentsController _controller = null!;
        private ClaimsPrincipal _user = null!;

        [TestInitialize]
        public void Setup()
        {
            _log = Substitute.For<ILogger<EphemeralEnvironmentsController>>();
            _environmentsPersistentSource = Substitute.For<IEnvironmentsPersistentSource>();
            _environmentTemplatesPersistentSource = Substitute.For<IEnvironmentTemplatesPersistentSource>();
            _projectsPersistentSource = Substitute.For<IProjectsPersistentSource>();
            _requestsPersistentSource = Substitute.For<IRequestsPersistentSource>();
            _securityPrivilegesChecker = Substitute.For<ISecurityPrivilegesChecker>();
            _rolePrivilegesChecker = Substitute.For<IRolePrivilegesChecker>();
            _claimsPrincipalReader = Substitute.For<IClaimsPrincipalReader>();
            _templateCatalog = Substitute.For<ITemplateCatalog>();
            _requestService = Substitute.For<IRequestService>();

            _user = new ClaimsPrincipal(new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.Name, "DOMAIN\\tester")
            }));

            _claimsPrincipalReader.GetUserFullDomainName(Arg.Any<System.Security.Principal.IPrincipal>())
                .Returns("DOMAIN\\tester");
            _securityPrivilegesChecker.IsEnvironmentOwnerOrAdmin(Arg.Any<ClaimsPrincipal>(), Arg.Any<string>())
                .Returns(true);

            _controller = new EphemeralEnvironmentsController(
                _log,
                _environmentsPersistentSource,
                _environmentTemplatesPersistentSource,
                _projectsPersistentSource,
                _requestsPersistentSource,
                _securityPrivilegesChecker,
                _claimsPrincipalReader,
                _templateCatalog,
                _requestService,
                _rolePrivilegesChecker)
            {
                ControllerContext = new ControllerContext
                {
                    HttpContext = new DefaultHttpContext { User = _user }
                }
            };
        }

        [TestMethod]
        public void GetEphemeralEnvironments_ReturnsEnvironmentsFromPersistentSource()
        {
            var expected = new[]
            {
                new EnvironmentApiModel { EnvironmentId = 1, EnvironmentName = "eph-pr-1", IsEphemeral = true }
            };
            _environmentsPersistentSource.GetEphemeralEnvironments(10, EphemeralEnvironmentStates.Ready, _user)
                .Returns(expected);

            var result = _controller.GetEphemeralEnvironments(10, EphemeralEnvironmentStates.Ready) as OkObjectResult;

            Assert.IsNotNull(result);
            Assert.AreSame(expected, result.Value);
        }

        [TestMethod]
        public void GetTemplates_ValidationAndSuccess_CoversAllBranches()
        {
            var badResult = _controller.GetTemplates(0) as BadRequestObjectResult;
            Assert.IsNotNull(badResult);

            var templates = new[] { new EnvironmentTemplateApiModel { Id = 1, ProjectId = 5, Name = "Full-Stack" } };
            _environmentTemplatesPersistentSource.GetTemplatesForProject(5).Returns(templates);

            var okResult = _controller.GetTemplates(5) as OkObjectResult;
            Assert.IsNotNull(okResult);
            Assert.AreSame(templates, okResult.Value);
        }

        [TestMethod]
        public void GetTemplate_NotFoundAndSuccess_CoversAllBranches()
        {
            _environmentTemplatesPersistentSource.GetTemplateById(404).Returns((EnvironmentTemplateApiModel?)null);
            Assert.IsInstanceOfType<NotFoundObjectResult>(_controller.GetTemplate(404));

            var template = new EnvironmentTemplateApiModel { Id = 7, Name = "Template7" };
            _environmentTemplatesPersistentSource.GetTemplateById(7).Returns(template);
            var okResult = _controller.GetTemplate(7) as OkObjectResult;
            Assert.IsNotNull(okResult);
            Assert.AreSame(template, okResult.Value);
        }

        [TestMethod]
        public async Task CreateTemplate_ValidationAuthorizationAndSuccess_CoversAllBranches()
        {
            Assert.IsInstanceOfType<BadRequestObjectResult>(await _controller.CreateTemplate(null, CancellationToken.None));
            Assert.IsInstanceOfType<BadRequestObjectResult>(await _controller.CreateTemplate(new EnvironmentTemplateApiModel { ProjectId = 0 }, CancellationToken.None));

            _projectsPersistentSource.GetProject(99).Returns((ProjectApiModel)null!);
            Assert.IsInstanceOfType<NotFoundObjectResult>(await _controller.CreateTemplate(new EnvironmentTemplateApiModel { ProjectId = 99 }, CancellationToken.None));

            var project = new ProjectApiModel { ProjectId = 10, ProjectName = "Traveler" };
            _projectsPersistentSource.GetProject(10).Returns(project);
            _securityPrivilegesChecker.CanModifyProject(_user, "Traveler").Returns(false);
            Assert.IsInstanceOfType<ForbidResult>(await _controller.CreateTemplate(new EnvironmentTemplateApiModel { ProjectId = 10 }, CancellationToken.None));

            _securityPrivilegesChecker.CanModifyProject(_user, "Traveler").Returns(true);

            // BaseEnvironmentId validation & authorization in CreateTemplate
            _environmentsPersistentSource.GetEnvironment(70, _user).Returns((EnvironmentApiModel?)null);
            _environmentsPersistentSource.GetEnvironment(70).Returns((EnvironmentApiModel?)null);
            Assert.IsInstanceOfType<BadRequestObjectResult>(
                await _controller.CreateTemplate(new EnvironmentTemplateApiModel { ProjectId = 10, BaseEnvironmentId = 70 }, CancellationToken.None));

            _environmentsPersistentSource.GetEnvironment(71, _user).Returns(new EnvironmentApiModel { EnvironmentId = 71, EnvironmentName = "ProdEnv", EnvironmentIsProd = true });
            Assert.IsInstanceOfType<BadRequestObjectResult>(
                await _controller.CreateTemplate(new EnvironmentTemplateApiModel { ProjectId = 10, BaseEnvironmentId = 71 }, CancellationToken.None));

            _environmentsPersistentSource.GetEnvironment(79, _user).Returns(new EnvironmentApiModel { EnvironmentId = 79, EnvironmentName = "EphBaseEnv", EnvironmentIsProd = false, IsEphemeral = true });
            Assert.IsInstanceOfType<BadRequestObjectResult>(
                await _controller.CreateTemplate(new EnvironmentTemplateApiModel { ProjectId = 10, BaseEnvironmentId = 79 }, CancellationToken.None));

            _environmentsPersistentSource.GetEnvironment(72, _user).Returns((EnvironmentApiModel?)null);
            _environmentsPersistentSource.GetEnvironment(72).Returns(new EnvironmentApiModel { EnvironmentId = 72, EnvironmentName = "BaseDv", EnvironmentIsProd = false });
            _securityPrivilegesChecker.CanModifyEnvironment(_user, "BaseDv").Returns(false);
            Assert.IsInstanceOfType<ForbidResult>(
                await _controller.CreateTemplate(new EnvironmentTemplateApiModel { ProjectId = 10, BaseEnvironmentId = 72 }, CancellationToken.None));
            _securityPrivilegesChecker.CanModifyEnvironment(_user, "BaseDv").Returns(true);

            // Secure base environment requires CanReadSecrets or IsAdmin
            _environmentsPersistentSource.GetEnvironment(73, _user).Returns(new EnvironmentApiModel
            {
                EnvironmentId = 73,
                EnvironmentName = "SecureBaseDv",
                EnvironmentIsProd = false,
                EnvironmentSecure = true,
                IsOwner = false
            });
            _securityPrivilegesChecker.CanModifyEnvironment(_user, "SecureBaseDv").Returns(true);
            _securityPrivilegesChecker.CanReadSecrets(_user, "SecureBaseDv").Returns(false);
            _rolePrivilegesChecker.IsAdmin(_user).Returns(false);
            Assert.IsInstanceOfType<ForbidResult>(
                await _controller.CreateTemplate(new EnvironmentTemplateApiModel { ProjectId = 10, BaseEnvironmentId = 73 }, CancellationToken.None));

            _securityPrivilegesChecker.CanReadSecrets(_user, "SecureBaseDv").Returns(true);
            _environmentTemplatesPersistentSource.CreateTemplate(Arg.Any<EnvironmentTemplateApiModel>(), Arg.Any<string>())
                .Returns(new EnvironmentTemplateApiModel { Id = 13, ProjectId = 10, Name = "SecureValid", BaseEnvironmentId = 73 });
            Assert.IsInstanceOfType<OkObjectResult>(
                await _controller.CreateTemplate(new EnvironmentTemplateApiModel { ProjectId = 10, Name = "SecureValid", BaseEnvironmentId = 73 }, CancellationToken.None));

            _securityPrivilegesChecker.CanReadSecrets(_user, "SecureBaseDv").Returns(false);
            _rolePrivilegesChecker.IsAdmin(_user).Returns(true);
            Assert.IsInstanceOfType<OkObjectResult>(
                await _controller.CreateTemplate(new EnvironmentTemplateApiModel { ProjectId = 10, Name = "SecureValid", BaseEnvironmentId = 73 }, CancellationToken.None));

            _securityPrivilegesChecker.IsEnvironmentOwnerOrAdmin(_user, "SecureBaseDv").Returns(false);
            _securityPrivilegesChecker.CanReadSecrets(_user, "SecureBaseDv").Returns(true);
            _rolePrivilegesChecker.IsAdmin(_user).Returns(false);
            Assert.IsInstanceOfType<ForbidResult>(
                await _controller.CreateTemplate(new EnvironmentTemplateApiModel { ProjectId = 10, Name = "SecureValid", BaseEnvironmentId = 73 }, CancellationToken.None));
            _rolePrivilegesChecker.IsAdmin(_user).Returns(true);
            Assert.IsInstanceOfType<OkObjectResult>(
                await _controller.CreateTemplate(new EnvironmentTemplateApiModel { ProjectId = 10, Name = "SecureValid", BaseEnvironmentId = 73 }, CancellationToken.None));
            _securityPrivilegesChecker.IsEnvironmentOwnerOrAdmin(_user, "SecureBaseDv").Returns(true);

            // Unknown or disabled component ID in ProvisionComponentIds or ValidationComponentIds rejected with 400 BadRequest
            _projectsPersistentSource.GetComponentsForProject(10).Returns(new[]
            {
                new ComponentApiModel
                {
                    ComponentId = 101,
                    ComponentName = "Postgres",
                    ComponentType = ComponentType.Terraform,
                    TerraformSourceType = TerraformSourceType.Catalog,
                    TerraformTemplateName = "azure-postgresql-flexible-server",
                    TerraformTemplateVersion = "1.0.0",
                    IsEnabled = true
                },
                new ComponentApiModel
                {
                    ComponentId = 102,
                    ComponentName = "DisabledComp",
                    ComponentType = ComponentType.Terraform,
                    IsEnabled = false
                }
            });
            Assert.IsInstanceOfType<BadRequestObjectResult>(
                await _controller.CreateTemplate(new EnvironmentTemplateApiModel
                {
                    ProjectId = 10,
                    Name = "UnknownCompTpl",
                    ProvisionComponentIds = new List<int> { 999 }
                }, CancellationToken.None));
            Assert.IsInstanceOfType<BadRequestObjectResult>(
                await _controller.CreateTemplate(new EnvironmentTemplateApiModel
                {
                    ProjectId = 10,
                    Name = "DisabledCompTpl",
                    ProvisionComponentIds = new List<int> { 102 }
                }, CancellationToken.None));

            // Manifest-declared Sensitive == true parameter in TierParameterOverridesJson rejected with 400 BadRequest
            var sensManifest = BuildManifest(
                "azure-postgresql-flexible-server",
                "database",
                new TerraformTemplateParameter("sku_name", TerraformParameterType.String, false, null, "GP_Standard_D4s_v3", Array.Empty<string>(), null, null, null, Sensitive: false),
                new TerraformTemplateParameter("opaque_cred", TerraformParameterType.String, false, null, null, Array.Empty<string>(), null, null, null, Sensitive: true));
            _templateCatalog.GetAsync("azure-postgresql-flexible-server", "1.0.0", Arg.Any<CancellationToken>())
                .Returns(Task.FromResult<TerraformTemplateManifest?>(sensManifest));
            Assert.IsInstanceOfType<OkObjectResult>(
                await _controller.CreateTemplate(new EnvironmentTemplateApiModel
                {
                    ProjectId = 10,
                    Name = "ManifestNonSensAllowedTpl",
                    ProvisionComponentIds = new List<int> { 101 },
                    TierParameterOverridesJson = "{\"sku_name\":\"B_Standard_B1ms\"}"
                }, CancellationToken.None));
            Assert.IsInstanceOfType<BadRequestObjectResult>(
                await _controller.CreateTemplate(new EnvironmentTemplateApiModel
                {
                    ProjectId = 10,
                    Name = "ManifestSensFlatTpl",
                    ProvisionComponentIds = new List<int> { 101 },
                    TierParameterOverridesJson = "{\"opaque_cred\":\"val\"}"
                }, CancellationToken.None));
            Assert.IsInstanceOfType<BadRequestObjectResult>(
                await _controller.CreateTemplate(new EnvironmentTemplateApiModel
                {
                    ProjectId = 10,
                    Name = "ManifestSensNestedTpl",
                    ValidationComponentIds = new List<int> { 101 },
                    TierParameterOverridesJson = "{\"ephemeral\":{\"opaque_cred\":\"val\"}}"
                }, CancellationToken.None));

            // Also cover requestedIds.Count == 0 with non-empty TierParameterOverridesJson (both valid and sensitive, and null manifest)
            Assert.IsInstanceOfType<OkObjectResult>(
                await _controller.CreateTemplate(new EnvironmentTemplateApiModel
                {
                    ProjectId = 10,
                    Name = "ManifestValidAllProjCompsTpl",
                    TierParameterOverridesJson = "{\"sku_name\":\"B_Standard_B1ms\"}"
                }, CancellationToken.None));
            Assert.IsInstanceOfType<BadRequestObjectResult>(
                await _controller.CreateTemplate(new EnvironmentTemplateApiModel
                {
                    ProjectId = 10,
                    Name = "ManifestSensAllProjCompsTpl",
                    TierParameterOverridesJson = "{\"opaque_cred\":\"val\"}"
                }, CancellationToken.None));
            _templateCatalog.GetAsync("azure-postgresql-flexible-server", "1.0.0", Arg.Any<CancellationToken>())
                .Returns(Task.FromResult<TerraformTemplateManifest?>(null));
            Assert.IsInstanceOfType<BadRequestObjectResult>(
                await _controller.CreateTemplate(new EnvironmentTemplateApiModel
                {
                    ProjectId = 10,
                    Name = "NullManifestSafeOverridesTpl",
                    TierParameterOverridesJson = "{\"sku_name\":\"B_Standard_B1ms\"}"
                }, CancellationToken.None));
            _templateCatalog.GetAsync("azure-postgresql-flexible-server", "1.0.0", Arg.Any<CancellationToken>())
                .Returns(Task.FromResult<TerraformTemplateManifest?>(sensManifest));

            // Reject sensitive parameter names in TierParameterOverridesJson during CreateTemplate (flat, nested, non-object, invalid JSON)
            Assert.IsFalse(EphemeralEnvironmentsController.IsLikelySensitiveParameterName(""));
            Assert.IsFalse(EphemeralEnvironmentsController.IsLikelySensitiveParameterName("   "));
            Assert.IsFalse(EphemeralEnvironmentsController.IsLikelySensitiveParameterName("secret-store:sku_name"));
            Assert.IsTrue(EphemeralEnvironmentsController.IsLikelySensitiveParameterName("secret-store:admin_password"));
            Assert.IsInstanceOfType<BadRequestObjectResult>(
                await _controller.CreateTemplate(new EnvironmentTemplateApiModel
                {
                    ProjectId = 10,
                    Name = "UnknownQualCompTpl",
                    ProvisionComponentIds = new List<int> { 101 },
                    TierParameterOverridesJson = "{\"UnknownComp:sku_name\":\"B_Standard_B1ms\"}"
                }, CancellationToken.None));
            Assert.IsInstanceOfType<BadRequestObjectResult>(
                await _controller.CreateTemplate(new EnvironmentTemplateApiModel
                {
                    ProjectId = 10,
                    Name = "SensTpl",
                    TierParameterOverridesJson = "{\"admin_password\":\"secret\"}"
                }, CancellationToken.None));
            Assert.IsInstanceOfType<BadRequestObjectResult>(
                await _controller.CreateTemplate(new EnvironmentTemplateApiModel
                {
                    ProjectId = 10,
                    Name = "SensNestedTpl",
                    TierParameterOverridesJson = "{\"dv\":{\"admin_password\":\"secret\"}}"
                }, CancellationToken.None));
            Assert.IsInstanceOfType<BadRequestObjectResult>(
                await _controller.CreateTemplate(new EnvironmentTemplateApiModel
                {
                    ProjectId = 10,
                    Name = "BadJsonTpl",
                    TierParameterOverridesJson = "{invalid"
                }, CancellationToken.None));
            Assert.IsInstanceOfType<OkObjectResult>(
                await _controller.CreateTemplate(new EnvironmentTemplateApiModel
                {
                    ProjectId = 10,
                    Name = "SafeNestedTpl",
                    ProvisionComponentIds = new List<int> { 101 },
                    TierParameterOverridesJson = "{\"dv\":{\"max_connections\":\"50\"}}"
                }, CancellationToken.None));
            Assert.IsInstanceOfType<OkObjectResult>(
                await _controller.CreateTemplate(new EnvironmentTemplateApiModel
                {
                    ProjectId = 10,
                    Name = "ArrayJsonTpl",
                    TierParameterOverridesJson = "[]"
                }, CancellationToken.None));
            Assert.IsInstanceOfType<BadRequestObjectResult>(
                await _controller.CreateTemplate(new EnvironmentTemplateApiModel
                {
                    ProjectId = 10,
                    Name = "SensTpl2",
                    TierParameterOverridesJson = "{\"ephemeral\":{\"client_secret\":\"secret\"}}"
                }, CancellationToken.None));
            Assert.IsInstanceOfType<BadRequestObjectResult>(
                await _controller.CreateTemplate(new EnvironmentTemplateApiModel
                {
                    ProjectId = 10,
                    Name = "BrokenJsonTpl",
                    TierParameterOverridesJson = "{broken"
                }, CancellationToken.None));

            _environmentTemplatesPersistentSource
                .When(x => x.CreateTemplate(Arg.Is<EnvironmentTemplateApiModel>(m => m.Name == "invalid"), Arg.Any<string>()))
                .Do(_ => throw new ArgumentException("Invalid template"));
            Assert.IsInstanceOfType<BadRequestObjectResult>(
                await _controller.CreateTemplate(new EnvironmentTemplateApiModel { ProjectId = 10, Name = "invalid" }, CancellationToken.None));

            var created = new EnvironmentTemplateApiModel { Id = 12, ProjectId = 10, Name = "Valid", BaseEnvironmentId = 72 };
            _environmentTemplatesPersistentSource.CreateTemplate(Arg.Is<EnvironmentTemplateApiModel>(m => m.Name == "Valid"), "DOMAIN\\tester")
                .Returns(created);
            var ok = await _controller.CreateTemplate(new EnvironmentTemplateApiModel { ProjectId = 10, Name = "Valid", BaseEnvironmentId = 72 }, CancellationToken.None) as OkObjectResult;
            Assert.IsNotNull(ok);
            Assert.AreSame(created, ok.Value);
        }

        [TestMethod]
        public async Task UpdateTemplate_ValidationAuthorizationAndSuccess_CoversAllBranches()
        {
            Assert.IsInstanceOfType<BadRequestObjectResult>(await _controller.UpdateTemplate(1, null, CancellationToken.None));

            _environmentTemplatesPersistentSource.GetTemplateById(404).Returns((EnvironmentTemplateApiModel?)null);
            Assert.IsInstanceOfType<NotFoundObjectResult>(await _controller.UpdateTemplate(404, new EnvironmentTemplateApiModel(), CancellationToken.None));

            _environmentTemplatesPersistentSource.GetTemplateById(1).Returns(new EnvironmentTemplateApiModel { Id = 1, ProjectId = 99 });
            _projectsPersistentSource.GetProject(99).Returns((ProjectApiModel)null!);
            Assert.IsInstanceOfType<NotFoundObjectResult>(await _controller.UpdateTemplate(1, new EnvironmentTemplateApiModel(), CancellationToken.None));

            var project = new ProjectApiModel { ProjectId = 10, ProjectName = "Traveler" };
            _environmentTemplatesPersistentSource.GetTemplateById(2).Returns(new EnvironmentTemplateApiModel { Id = 2, ProjectId = 10 });
            _projectsPersistentSource.GetProject(10).Returns(project);

            _securityPrivilegesChecker.CanModifyProject(_user, "Traveler").Returns(false);
            Assert.IsInstanceOfType<ForbidResult>(await _controller.UpdateTemplate(2, new EnvironmentTemplateApiModel(), CancellationToken.None));

            _securityPrivilegesChecker.CanModifyProject(_user, "Traveler").Returns(true);

            // BaseEnvironmentId validation & authorization in UpdateTemplate
            _environmentsPersistentSource.GetEnvironment(80, _user).Returns((EnvironmentApiModel?)null);
            _environmentsPersistentSource.GetEnvironment(80).Returns((EnvironmentApiModel?)null);
            Assert.IsInstanceOfType<BadRequestObjectResult>(
                await _controller.UpdateTemplate(2, new EnvironmentTemplateApiModel { BaseEnvironmentId = 80 }, CancellationToken.None));

            _environmentsPersistentSource.GetEnvironment(81, _user).Returns(new EnvironmentApiModel { EnvironmentId = 81, EnvironmentName = "ProdEnv", EnvironmentIsProd = true });
            Assert.IsInstanceOfType<BadRequestObjectResult>(
                await _controller.UpdateTemplate(2, new EnvironmentTemplateApiModel { BaseEnvironmentId = 81 }, CancellationToken.None));

            _environmentsPersistentSource.GetEnvironment(82, _user).Returns((EnvironmentApiModel?)null);
            _environmentsPersistentSource.GetEnvironment(82).Returns(new EnvironmentApiModel { EnvironmentId = 82, EnvironmentName = "BaseDv", EnvironmentIsProd = false });
            _securityPrivilegesChecker.CanModifyEnvironment(_user, "BaseDv").Returns(false);
            Assert.IsInstanceOfType<ForbidResult>(
                await _controller.UpdateTemplate(2, new EnvironmentTemplateApiModel { BaseEnvironmentId = 82 }, CancellationToken.None));
            _securityPrivilegesChecker.CanModifyEnvironment(_user, "BaseDv").Returns(true);

            // Reject sensitive parameter names in TierParameterOverridesJson during UpdateTemplate
            Assert.IsInstanceOfType<BadRequestObjectResult>(
                await _controller.UpdateTemplate(2, new EnvironmentTemplateApiModel
                {
                    Name = "SensUpdate",
                    TierParameterOverridesJson = "{\"connection_string\":\"secret\"}"
                }, CancellationToken.None));

            _environmentTemplatesPersistentSource
                .When(x => x.UpdateTemplate(Arg.Is<EnvironmentTemplateApiModel>(m => m.Name == "bad"), Arg.Any<string>()))
                .Do(_ => throw new ArgumentException("Bad update"));
            Assert.IsInstanceOfType<BadRequestObjectResult>(
                await _controller.UpdateTemplate(2, new EnvironmentTemplateApiModel { Name = "bad", BaseEnvironmentId = 82 }, CancellationToken.None));

            var updated = new EnvironmentTemplateApiModel { Id = 2, ProjectId = 10, Name = "Updated" };
            _environmentTemplatesPersistentSource
                .UpdateTemplate(Arg.Is<EnvironmentTemplateApiModel>(m => m.Name == "Updated"), "DOMAIN\\tester")
                .Returns(updated);
            var ok = await _controller.UpdateTemplate(2, new EnvironmentTemplateApiModel { Name = "Updated" }, CancellationToken.None) as OkObjectResult;
            Assert.IsNotNull(ok);
            Assert.AreSame(updated, ok.Value);
        }

        [TestMethod]
        public void DeleteTemplate_ValidationAuthorizationAndSuccess_CoversAllBranches()
        {
            _environmentTemplatesPersistentSource.GetTemplateById(404).Returns((EnvironmentTemplateApiModel?)null);
            Assert.IsInstanceOfType<NotFoundObjectResult>(_controller.DeleteTemplate(404));

            _environmentTemplatesPersistentSource.GetTemplateById(1).Returns(new EnvironmentTemplateApiModel { Id = 1, ProjectId = 99 });
            _projectsPersistentSource.GetProject(99).Returns((ProjectApiModel)null!);
            Assert.IsInstanceOfType<NotFoundObjectResult>(_controller.DeleteTemplate(1));

            var project = new ProjectApiModel { ProjectId = 10, ProjectName = "Traveler" };
            _environmentTemplatesPersistentSource.GetTemplateById(2).Returns(new EnvironmentTemplateApiModel { Id = 2, ProjectId = 10 });
            _projectsPersistentSource.GetProject(10).Returns(project);

            _securityPrivilegesChecker.CanModifyProject(_user, "Traveler").Returns(false);
            Assert.IsInstanceOfType<ForbidResult>(_controller.DeleteTemplate(2));

            _securityPrivilegesChecker.CanModifyProject(_user, "Traveler").Returns(true);
            _environmentTemplatesPersistentSource.DeleteTemplate(2).Returns(
                _ => throw new InvalidOperationException("Referenced by active ephemeral environment"),
                _ => true);
            Assert.IsInstanceOfType<ConflictObjectResult>(_controller.DeleteTemplate(2));

            var ok = _controller.DeleteTemplate(2) as OkObjectResult;
            Assert.IsNotNull(ok);
            Assert.AreEqual(true, ok.Value);
        }

        [TestMethod]
        public async Task PreviewTierParameters_CoversAllBranchesAndSharedServiceDetection()
        {
            Assert.IsInstanceOfType<BadRequestObjectResult>(await _controller.PreviewTierParameters(null, CancellationToken.None));
            Assert.IsInstanceOfType<BadRequestObjectResult>(
                await _controller.PreviewTierParameters(new EphemeralEnvironmentTierPreviewRequestApiModel { ProjectId = 0 }, CancellationToken.None));

            _projectsPersistentSource.GetProject(99).Returns((ProjectApiModel)null!);
            Assert.IsInstanceOfType<NotFoundObjectResult>(
                await _controller.PreviewTierParameters(new EphemeralEnvironmentTierPreviewRequestApiModel { ProjectId = 99 }, CancellationToken.None));

            var project = new ProjectApiModel
            {
                ProjectId = 10,
                ProjectName = "Traveler",
                BusinessVertical = "trading",
                ApplicationCode = "trv"
            };
            _projectsPersistentSource.GetProject(10).Returns(project);

            _environmentTemplatesPersistentSource.GetTemplateById(404).Returns((EnvironmentTemplateApiModel?)null);
            Assert.IsInstanceOfType<NotFoundObjectResult>(
                await _controller.PreviewTierParameters(
                    new EphemeralEnvironmentTierPreviewRequestApiModel { ProjectId = 10, TemplateId = 404 },
                    CancellationToken.None));

            // Cross-project template rejected
            _environmentTemplatesPersistentSource.GetTemplateById(405).Returns(new EnvironmentTemplateApiModel { Id = 405, ProjectId = 999 });
            Assert.IsInstanceOfType<BadRequestObjectResult>(
                await _controller.PreviewTierParameters(
                    new EphemeralEnvironmentTierPreviewRequestApiModel { ProjectId = 10, TemplateId = 405 },
                    CancellationToken.None));

            // Blocked production tier and unknown tier
            Assert.IsInstanceOfType<BadRequestObjectResult>(
                await _controller.PreviewTierParameters(
                    new EphemeralEnvironmentTierPreviewRequestApiModel { ProjectId = 10, EnvironmentTier = "prod" },
                    CancellationToken.None));
            Assert.IsInstanceOfType<BadRequestObjectResult>(
                await _controller.PreviewTierParameters(
                    new EphemeralEnvironmentTierPreviewRequestApiModel { ProjectId = 10, EnvironmentTier = "not-a-valid-tier" },
                    CancellationToken.None));

            // Invalid TierParameterOverridesJson formats (invalid JSON, non-object root, nested array)
            _environmentTemplatesPersistentSource.GetTemplateById(406).Returns(new EnvironmentTemplateApiModel
            {
                Id = 406, ProjectId = 10, TierParameterOverridesJson = "{broken"
            });
            Assert.IsInstanceOfType<BadRequestObjectResult>(
                await _controller.PreviewTierParameters(
                    new EphemeralEnvironmentTierPreviewRequestApiModel { ProjectId = 10, TemplateId = 406 },
                    CancellationToken.None));
            _environmentTemplatesPersistentSource.GetTemplateById(406).Returns(new EnvironmentTemplateApiModel
            {
                Id = 406, ProjectId = 10, TierParameterOverridesJson = "[1,2,3]"
            });
            Assert.IsInstanceOfType<BadRequestObjectResult>(
                await _controller.PreviewTierParameters(
                    new EphemeralEnvironmentTierPreviewRequestApiModel { ProjectId = 10, TemplateId = 406 },
                    CancellationToken.None));
            _environmentTemplatesPersistentSource.GetTemplateById(406).Returns(new EnvironmentTemplateApiModel
            {
                Id = 406, ProjectId = 10, TierParameterOverridesJson = "{\"bad_array\":[1,2]}"
            });
            Assert.IsInstanceOfType<BadRequestObjectResult>(
                await _controller.PreviewTierParameters(
                    new EphemeralEnvironmentTierPreviewRequestApiModel { ProjectId = 10, TemplateId = 406 },
                    CancellationToken.None));

            var template = new EnvironmentTemplateApiModel
            {
                Id = 3,
                ProjectId = 10,
                BaseEnvironmentId = 20,
                DefaultTier = "ephemeral",
                ProvisionComponentIds = new List<int> { 101, 102, 103, 104, 105, 999 },
                TierParameterOverridesJson = "{\"Postgres:custom_key\":\"from-flat-qualified\",\"custom_key\":\"from-flat\",\"null_key\":null,\"num_key\":42,\"bool_true\":true,\"bool_false\":false,\"ephemeral\":{\"custom_key\":\"from-tier\",\"termination_protection\":\"true\"},\"qa\":{\"custom_key\":\"from-qa\"}}"
            };
            _environmentTemplatesPersistentSource.GetTemplateById(3).Returns(template);

            // Sensitive override in stored template rejected in PreviewTierParameters
            var savedTplOverrides = template.TierParameterOverridesJson;
            template.TierParameterOverridesJson = "{\"admin_password\":\"secret\"}";
            Assert.IsInstanceOfType<BadRequestObjectResult>(
                await _controller.PreviewTierParameters(
                    new EphemeralEnvironmentTierPreviewRequestApiModel
                    {
                        ProjectId = 10,
                        TemplateId = 3
                    },
                    CancellationToken.None));
            template.TierParameterOverridesJson = savedTplOverrides;

            // BaseEnvironmentId authorization in PreviewTierParameters
            _environmentsPersistentSource.GetEnvironment(20, _user).Returns((EnvironmentApiModel?)null);
            _environmentsPersistentSource.GetEnvironment(20).Returns((EnvironmentApiModel?)null);
            Assert.IsInstanceOfType<BadRequestObjectResult>(
                await _controller.PreviewTierParameters(
                    new EphemeralEnvironmentTierPreviewRequestApiModel { ProjectId = 10, TemplateId = 3 },
                    CancellationToken.None));

            _environmentsPersistentSource.GetEnvironment(20, _user).Returns(new EnvironmentApiModel
            {
                EnvironmentId = 20,
                EnvironmentName = "BaseDv20",
                EnvironmentIsProd = false
            });
            _securityPrivilegesChecker.CanModifyEnvironment(_user, "BaseDv20").Returns(false);
            Assert.IsInstanceOfType<ForbidResult>(
                await _controller.PreviewTierParameters(
                    new EphemeralEnvironmentTierPreviewRequestApiModel { ProjectId = 10, TemplateId = 3 },
                    CancellationToken.None));
            _securityPrivilegesChecker.CanModifyEnvironment(_user, "BaseDv20").Returns(true);
            _requestsPersistentSource.GetCatalogEnvironmentInputs(101, 20).Returns(new CatalogEnvironmentInputs
            {
                ComponentId = 101,
                EnvironmentId = 20,
                InputsJson = "{\"custom_key\":\"from-base\",\"base_only\":\"inherited\",\"location\":\"westeurope\",\"dorc_environment\":\"base-env\",\"target_env_label\":\"base-env-label\",\"sku_name\":\"GP_Standard_D4s_v3\",\"null_base\":null}"
            });
            _requestsPersistentSource.GetCatalogEnvironmentInputs(102, 20).Returns(new CatalogEnvironmentInputs
            {
                ComponentId = 102,
                EnvironmentId = 20,
                InputsJson = ""
            });
            _requestsPersistentSource.GetCatalogEnvironmentInputs(103, 20).Returns(new CatalogEnvironmentInputs
            {
                ComponentId = 103,
                EnvironmentId = 20,
                InputsJson = "null"
            });

            var components = new List<ComponentApiModel>
            {
                new()
                {
                    ComponentId = 101,
                    ComponentName = "Postgres",
                    ComponentType = ComponentType.Terraform,
                    TerraformSourceType = TerraformSourceType.Catalog,
                    TerraformTemplateName = "azure-postgresql-flexible-server",
                    TerraformTemplateVersion = "1.0.0",
                    IsEnabled = true
                },
                new()
                {
                    ComponentId = 102,
                    ComponentName = "AppServiceCreateRgFalse",
                    ComponentType = ComponentType.Terraform,
                    TerraformSourceType = TerraformSourceType.Catalog,
                    TerraformTemplateName = "azure-rg-shared",
                    TerraformTemplateVersion = "1.0.0",
                    IsEnabled = true
                },
                new()
                {
                    ComponentId = 103,
                    ComponentName = "AppServiceByName",
                    ComponentType = ComponentType.Terraform,
                    TerraformSourceType = TerraformSourceType.Catalog,
                    TerraformTemplateName = "azure-named-shared",
                    TerraformTemplateVersion = "1.0.0",
                    IsEnabled = true
                },
                new()
                {
                    ComponentId = 104,
                    ComponentName = "MissingManifest",
                    ComponentType = ComponentType.Terraform,
                    TerraformSourceType = TerraformSourceType.Catalog,
                    TerraformTemplateName = "missing-template",
                    TerraformTemplateVersion = "1.0.0",
                    IsEnabled = true
                },
                new()
                {
                    ComponentId = 105,
                    ComponentName = "PowerShellComp",
                    ComponentType = ComponentType.PowerShell,
                    IsEnabled = true
                },
                new()
                {
                    ComponentId = null,
                    ComponentName = "NoIdComp",
                    ComponentType = ComponentType.Terraform,
                    TerraformSourceType = TerraformSourceType.Catalog,
                    TerraformTemplateName = "azure-postgresql-flexible-server"
                }
            };
            _projectsPersistentSource.GetComponentsForProject(10).Returns(components);

            var pgManifest = BuildManifest(
                "azure-postgresql-flexible-server",
                "database",
                new TerraformTemplateParameter("create_service", TerraformParameterType.Bool, false, null, "true", Array.Empty<string>(), null, null, null),
                new TerraformTemplateParameter("sku_name", TerraformParameterType.String, false, null, "GP_Standard_D4s_v3", Array.Empty<string>(), null, null, null),
                new TerraformTemplateParameter("location", TerraformParameterType.String, false, null, "uksouth", Array.Empty<string>(), null, null, null, Suggest: "location"),
                new TerraformTemplateParameter("dorc_environment", TerraformParameterType.String, false, null, null, Array.Empty<string>(), null, null, null, Suggest: "dorc_environment"),
                new TerraformTemplateParameter("target_env_label", TerraformParameterType.String, false, null, null, Array.Empty<string>(), null, null, null, Suggest: "dorc_environment"),
                new TerraformTemplateParameter("custom_key", TerraformParameterType.String, false, null, "default-custom", Array.Empty<string>(), null, null, null),
                new TerraformTemplateParameter("base_only", TerraformParameterType.String, false, null, null, Array.Empty<string>(), null, null, null),
                new TerraformTemplateParameter("admin_password", TerraformParameterType.String, false, null, "s3cr3t", Array.Empty<string>(), null, null, null, Sensitive: true),
                new TerraformTemplateParameter("termination_protection", TerraformParameterType.Bool, false, null, "true", Array.Empty<string>(), null, null, null),
                new TerraformTemplateParameter("service_termination_protection", TerraformParameterType.Bool, false, null, "true", Array.Empty<string>(), null, null, null));
            var rgSharedManifest = BuildManifest(
                "azure-rg-shared",
                "compute",
                new TerraformTemplateParameter("create_resource_group", TerraformParameterType.Bool, false, null, "false", Array.Empty<string>(), null, null, null));
            var namedSharedManifest = BuildManifest(
                "azure-named-shared",
                "compute",
                new TerraformTemplateParameter("create_resource_group", TerraformParameterType.Bool, false, null, "true", Array.Empty<string>(), null, null, null),
                new TerraformTemplateParameter("service_name", TerraformParameterType.String, false, null, "traveler-unstable-dev", Array.Empty<string>(), null, null, null));

            _templateCatalog.GetAsync("azure-postgresql-flexible-server", "1.0.0", Arg.Any<CancellationToken>())
                .Returns(Task.FromResult<TerraformTemplateManifest?>(pgManifest));
            _templateCatalog.GetAsync("azure-rg-shared", "1.0.0", Arg.Any<CancellationToken>())
                .Returns(Task.FromResult<TerraformTemplateManifest?>(rgSharedManifest));
            _templateCatalog.GetAsync("azure-named-shared", "1.0.0", Arg.Any<CancellationToken>())
                .Returns(Task.FromResult<TerraformTemplateManifest?>(namedSharedManifest));
            _templateCatalog.GetAsync("missing-template", "1.0.0", Arg.Any<CancellationToken>())
                .Returns(Task.FromResult<TerraformTemplateManifest?>(null));

            // When a catalog component's manifest is null and overrides are present, PreviewTierParameters rejects with 400 BadRequest
            Assert.IsInstanceOfType<BadRequestObjectResult>(await _controller.PreviewTierParameters(
                new EphemeralEnvironmentTierPreviewRequestApiModel
                {
                    ProjectId = 10,
                    TemplateId = 3,
                    ParameterOverrides = new Dictionary<string, string> { ["custom_key"] = "from-request" }
                },
                CancellationToken.None));

            // When a catalog component's manifest is null and NO overrides are present, it is skipped
            _environmentTemplatesPersistentSource.GetTemplateById(31).Returns(new EnvironmentTemplateApiModel
            {
                Id = 31,
                ProjectId = 10,
                ProvisionComponentIds = new List<int> { 101, 104 }
            });
            var missingManifestNoOverridesOk = await _controller.PreviewTierParameters(
                new EphemeralEnvironmentTierPreviewRequestApiModel
                {
                    ProjectId = 10,
                    TemplateId = 31
                },
                CancellationToken.None) as OkObjectResult;
            Assert.IsNotNull(missingManifestNoOverridesOk);

            // Remove missing-manifest component 104 from template 3 for override preview tests, and test manifest-marked sensitive parameter rejection on stored template overrides while allowing one-off request overrides (redacted)
            template.ProvisionComponentIds = new List<int> { 101, 102, 103, 105, 999 };
            var pgWithOpaqueSensitive = BuildManifest(
                "azure-postgresql-flexible-server",
                "database",
                new TerraformTemplateParameter("opaque_cred", TerraformParameterType.String, false, null, "v", Array.Empty<string>(), null, null, null, Sensitive: true));
            _templateCatalog.GetAsync("azure-postgresql-flexible-server", "1.0.0", Arg.Any<CancellationToken>())
                .Returns(Task.FromResult<TerraformTemplateManifest?>(pgWithOpaqueSensitive));
            var savedTpl3Overrides = template.TierParameterOverridesJson;
            template.TierParameterOverridesJson = "{\"opaque_cred\":\"bad\"}";
            Assert.IsInstanceOfType<BadRequestObjectResult>(await _controller.PreviewTierParameters(
                new EphemeralEnvironmentTierPreviewRequestApiModel
                {
                    ProjectId = 10,
                    TemplateId = 3
                },
                CancellationToken.None));
            template.TierParameterOverridesJson = savedTpl3Overrides;
            _templateCatalog.GetAsync("azure-postgresql-flexible-server", "1.0.0", Arg.Any<CancellationToken>())
                .Returns(Task.FromResult<TerraformTemplateManifest?>(pgManifest));
            _templateCatalog.GetAsync("missing-template", "1.0.0", Arg.Any<CancellationToken>())
                .Returns(Task.FromResult<TerraformTemplateManifest?>(rgSharedManifest));

            var okResult = await _controller.PreviewTierParameters(
                new EphemeralEnvironmentTierPreviewRequestApiModel
                {
                    ProjectId = 10,
                    TemplateId = 3,
                    EnvironmentName = "",
                    EnvironmentTier = "",
                    ParameterOverrides = new Dictionary<string, string>
                    {
                        ["custom_key"] = "from-request",
                        ["Postgres:location"] = "uksouth",
                        ["create_service"] = "true",
                        ["service_termination_protection"] = "true"
                    }
                },
                CancellationToken.None) as OkObjectResult;

            Assert.IsInstanceOfType<BadRequestObjectResult>(
                await _controller.PreviewTierParameters(
                    new EphemeralEnvironmentTierPreviewRequestApiModel
                    {
                        ProjectId = 10,
                        TemplateId = 3,
                        ParameterOverrides = new Dictionary<string, string> { ["unknown_param"] = "val" }
                    },
                    CancellationToken.None));

            // Invalid parameter value in PreviewTierParameters (e.g. non-boolean for Bool parameter) returns 400 BadRequest
            Assert.IsInstanceOfType<BadRequestObjectResult>(
                await _controller.PreviewTierParameters(
                    new EphemeralEnvironmentTierPreviewRequestApiModel
                    {
                        ProjectId = 10,
                        TemplateId = 3,
                        ParameterOverrides = new Dictionary<string, string> { ["create_service"] = "not-a-boolean" }
                    },
                    CancellationToken.None));

            Assert.IsNotNull(okResult);
            var previewResponse = okResult.Value as EphemeralEnvironmentTierPreviewResponseApiModel;
            Assert.IsNotNull(previewResponse);
            Assert.AreEqual("ephemeral", previewResponse.EnvironmentTier);
            Assert.IsTrue(previewResponse.IsEphemeral);
            Assert.AreEqual(3, previewResponse.ComponentPreviews.Count);

            var pgPreview = previewResponse.ComponentPreviews[0];
            Assert.IsFalse(pgPreview.IsSharedService);
            Assert.AreEqual("false", pgPreview.ResolvedParameters["termination_protection"]);
            Assert.AreEqual("false", pgPreview.ResolvedParameters["service_termination_protection"]);
            Assert.AreEqual("from-request", pgPreview.ResolvedParameters["custom_key"]);
            Assert.AreEqual("inherited", pgPreview.ResolvedParameters["base_only"]);
            Assert.AreEqual("***REDACTED***", pgPreview.ResolvedParameters["admin_password"]);
            Assert.IsFalse(pgPreview.ResolvedParameters.ContainsKey("num_key"));
            Assert.AreEqual("false", pgPreview.ForcedInvariants["termination_protection"]);
            Assert.AreEqual("false", pgPreview.ForcedInvariants["service_termination_protection"]);

            Assert.IsFalse(previewResponse.ComponentPreviews[1].IsSharedService);
            Assert.IsTrue(previewResponse.ComponentPreviews[2].IsSharedService);

            // Also test create_service == "false" branch, explicit BaseEnvironmentId on request, flat-only template overrides, and template without DefaultTier / null overrides
            _environmentTemplatesPersistentSource.GetTemplateById(30).Returns(new EnvironmentTemplateApiModel
            {
                Id = 30,
                ProjectId = 10,
                BaseEnvironmentId = 20,
                ValidationComponentIds = new List<int> { 101 },
                TierParameterOverridesJson = "{\"flat_only\":\"val\"}"
            });
            var flatTplOk = await _controller.PreviewTierParameters(
                new EphemeralEnvironmentTierPreviewRequestApiModel
                {
                    ProjectId = 10,
                    TemplateId = 30,
                    BaseEnvironmentId = 20,
                    EnvironmentName = "eph-flat"
                },
                CancellationToken.None) as OkObjectResult;
            Assert.IsNotNull(flatTplOk);

            // Clearing BaseEnvironmentId explicitly via BaseEnvironmentId = 0 overrides template.BaseEnvironmentId
            var clearedBaseOk = await _controller.PreviewTierParameters(
                new EphemeralEnvironmentTierPreviewRequestApiModel
                {
                    ProjectId = 10,
                    TemplateId = 30,
                    BaseEnvironmentId = 0,
                    EnvironmentName = "eph-no-base"
                },
                CancellationToken.None) as OkObjectResult;
            Assert.IsNotNull(clearedBaseOk);

            // LoadBaseEnvironmentCatalogInputs with IPropertyValuesPersistentSource resolves global + ancestor + base environment non-secure properties
            var propValuesSource = Substitute.For<IPropertyValuesPersistentSource>();
            propValuesSource.GetGlobalProperties(null).Returns(new[]
            {
                new PropertyValueDto { Property = new PropertyApiModel { Name = "location", Secure = false }, Value = "global-region" },
                new PropertyValueDto { Property = new PropertyApiModel { Name = "secure_global", Secure = true }, Value = "sec" },
                new PropertyValueDto { Property = new PropertyApiModel { Name = "", Secure = false }, Value = "empty" },
                new PropertyValueDto { Property = new PropertyApiModel { Name = "db_password", Secure = false }, Value = "sec-name" },
                new PropertyValueDto { Property = null, Value = "null-prop" }
            });
            _environmentsPersistentSource.GetEnvironment(21, _user).Returns((EnvironmentApiModel)null!);
            _environmentsPersistentSource.GetEnvironment(21).Returns(new EnvironmentApiModel
            {
                EnvironmentId = 21,
                EnvironmentName = "ChildBaseEnv",
                ParentId = 22
            });
            _environmentsPersistentSource.GetEnvironment(22, _user).Returns(new EnvironmentApiModel
            {
                EnvironmentId = 22,
                EnvironmentName = "RootBaseEnv",
                ParentId = 23
            });
            _environmentsPersistentSource.GetEnvironment(23, _user).Returns(new EnvironmentApiModel
            {
                EnvironmentId = 23,
                EnvironmentName = "",
                ParentId = 24
            });
            _environmentsPersistentSource.GetEnvironment(24, _user).Returns((EnvironmentApiModel)null!);
            _environmentsPersistentSource.GetEnvironment(24).Returns((EnvironmentApiModel)null!);
            propValuesSource.GetEnvironmentProperties("RootBaseEnv", null).Returns(new[]
            {
                new PropertyValueDto { Property = new PropertyApiModel { Name = "location", Secure = false }, Value = "root-region" },
                new PropertyValueDto { Property = new PropertyApiModel { Name = "RegionPrefix", Secure = false }, Value = "north" },
                new PropertyValueDto { Property = new PropertyApiModel { Name = "NestedToken", Secure = false }, Value = "$$Unresolved$$" },
                new PropertyValueDto { Property = new PropertyApiModel { Name = "EmptyToken", Secure = false }, Value = "" }
            });
            propValuesSource.GetEnvironmentProperties("ChildBaseEnv", null).Returns(new[]
            {
                new PropertyValueDto { Property = new PropertyApiModel { Name = "location", Secure = false }, Value = "$$RegionPrefix$$europe" },
                new PropertyValueDto { Property = new PropertyApiModel { Name = "custom_key", Secure = false }, Value = "$$RuntimeExpr$$$$NestedToken$$$$EmptyToken$$" },
                new PropertyValueDto { Property = new PropertyApiModel { Name = "base_only", Secure = false }, Value = null! },
                new PropertyValueDto { Property = new PropertyApiModel { Name = "secure_env", Secure = true }, Value = "sec" },
                new PropertyValueDto { Property = new PropertyApiModel { Name = "", Secure = false }, Value = "empty" },
                new PropertyValueDto { Property = new PropertyApiModel { Name = "api_token", Secure = false }, Value = "sec-name" },
                new PropertyValueDto { Property = null, Value = "null-prop" }
            });
            _securityPrivilegesChecker.CanModifyEnvironment(_user, "ChildBaseEnv").Returns(true);
            var controllerWithProps = new EphemeralEnvironmentsController(
                _log,
                _environmentsPersistentSource,
                _environmentTemplatesPersistentSource,
                _projectsPersistentSource,
                _requestsPersistentSource,
                _securityPrivilegesChecker,
                _claimsPrincipalReader,
                _templateCatalog,
                _requestService,
                _rolePrivilegesChecker,
                propertyValuesPersistentSource: propValuesSource)
            {
                ControllerContext = new ControllerContext
                {
                    HttpContext = new DefaultHttpContext { User = _user }
                }
            };
            var inheritedPropsOk = await controllerWithProps.PreviewTierParameters(
                new EphemeralEnvironmentTierPreviewRequestApiModel
                {
                    ProjectId = 10,
                    BaseEnvironmentId = 21,
                    EnvironmentName = "eph-inherited-props"
                },
                CancellationToken.None) as OkObjectResult;
            Assert.IsNotNull(inheritedPropsOk);
            var inheritedPreview = (EphemeralEnvironmentTierPreviewResponseApiModel)inheritedPropsOk.Value!;
            Assert.AreEqual("northeurope", inheritedPreview.ComponentPreviews[0].ResolvedParameters["location"]);
            _securityPrivilegesChecker.CanModifyProject(_user, "Traveler").Returns(true);
            _environmentsPersistentSource.CreateEnvironment(Arg.Is<EnvironmentApiModel>(e => e.EnvironmentName == "eph-inherited-prov"), _user)
                .Returns(new EnvironmentApiModel { EnvironmentId = 999, EnvironmentName = "eph-inherited-prov", IsEphemeral = true });
            _requestService.CreateRequest(Arg.Any<RequestDto>(), _user).Returns(new RequestStatusDto { Id = 999 });
            var inheritedProvOk = await controllerWithProps.Provision(
                new EphemeralEnvironmentProvisionRequestApiModel
                {
                    ProjectId = 10,
                    BaseEnvironmentId = 21,
                    EnvironmentName = "eph-inherited-prov"
                },
                CancellationToken.None) as ObjectResult;
            Assert.IsNotNull(inheritedProvOk);

            var noTemplateOk = await _controller.PreviewTierParameters(
                new EphemeralEnvironmentTierPreviewRequestApiModel
                {
                    ProjectId = 10,
                    EnvironmentName = "eph-custom",
                    EnvironmentTier = null!,
                    ParameterOverrides = new Dictionary<string, string>
                    {
                        ["create_service"] = "false",
                        ["create_resource_group"] = "true",
                        ["service_name"] = "custom-dedicated-service"
                    }
                },
                CancellationToken.None) as OkObjectResult;
            Assert.IsNotNull(noTemplateOk);
            var noTemplatePreview = (EphemeralEnvironmentTierPreviewResponseApiModel)noTemplateOk.Value!;
            Assert.IsTrue(noTemplatePreview.ComponentPreviews[0].IsSharedService);
            Assert.IsFalse(noTemplatePreview.ComponentPreviews[1].IsSharedService);
            Assert.IsFalse(noTemplatePreview.ComponentPreviews[2].IsSharedService);

            Assert.IsTrue(EphemeralEnvironmentStates.IsValid(EphemeralEnvironmentStates.Provisioning));
            Assert.IsTrue(EphemeralEnvironmentStates.IsValid(EphemeralEnvironmentStates.ProvisionFailed));
            Assert.IsTrue(EphemeralEnvironmentStates.IsValid(EphemeralEnvironmentStates.Ready));
            Assert.IsTrue(EphemeralEnvironmentStates.IsValid(EphemeralEnvironmentStates.Testing));
            Assert.IsTrue(EphemeralEnvironmentStates.IsValid(EphemeralEnvironmentStates.ValidationFailed));
            Assert.IsTrue(EphemeralEnvironmentStates.IsValid(EphemeralEnvironmentStates.TearingDown));
            Assert.IsTrue(EphemeralEnvironmentStates.IsValid(EphemeralEnvironmentStates.TornDown));
            Assert.IsTrue(EphemeralEnvironmentStates.IsValid(EphemeralEnvironmentStates.TeardownFailed));
            Assert.IsFalse(EphemeralEnvironmentStates.IsValid(null!));
            Assert.IsFalse(EphemeralEnvironmentStates.IsValid("Invalid"));
            Assert.HasCount(8, EphemeralEnvironmentStates.All);
        }

        [TestMethod]
        public async Task Provision_ValidationAuthorizationAndFullFlow_CoversAllBranches()
        {
            Assert.IsInstanceOfType<BadRequestObjectResult>(await _controller.Provision(null, CancellationToken.None));
            Assert.IsInstanceOfType<BadRequestObjectResult>(
                await _controller.Provision(new EphemeralEnvironmentProvisionRequestApiModel { ProjectId = 0, EnvironmentName = "eph-1" }, CancellationToken.None));
            Assert.IsInstanceOfType<BadRequestObjectResult>(
                await _controller.Provision(new EphemeralEnvironmentProvisionRequestApiModel { ProjectId = 10, EnvironmentName = " " }, CancellationToken.None));

            _projectsPersistentSource.GetProject(99).Returns((ProjectApiModel)null!);
            Assert.IsInstanceOfType<NotFoundObjectResult>(
                await _controller.Provision(new EphemeralEnvironmentProvisionRequestApiModel { ProjectId = 99, EnvironmentName = "eph-1" }, CancellationToken.None));

            var project = new ProjectApiModel { ProjectId = 10, ProjectName = "Traveler", BusinessVertical = "trading", ApplicationCode = "trv" };
            _projectsPersistentSource.GetProject(10).Returns(project);

            _securityPrivilegesChecker.CanModifyProject(_user, "Traveler").Returns(false);
            Assert.IsInstanceOfType<ForbidResult>(
                await _controller.Provision(new EphemeralEnvironmentProvisionRequestApiModel { ProjectId = 10, EnvironmentName = "eph-1" }, CancellationToken.None));

            _securityPrivilegesChecker.CanModifyProject(_user, "Traveler").Returns(true);

            // Missing or cross-project template
            _environmentTemplatesPersistentSource.GetTemplateById(404).Returns((EnvironmentTemplateApiModel?)null);
            Assert.IsInstanceOfType<NotFoundObjectResult>(
                await _controller.Provision(
                    new EphemeralEnvironmentProvisionRequestApiModel { ProjectId = 10, TemplateId = 404, EnvironmentName = "eph-1" },
                    CancellationToken.None));
            _environmentTemplatesPersistentSource.GetTemplateById(405).Returns(new EnvironmentTemplateApiModel { Id = 405, ProjectId = 999 });
            Assert.IsInstanceOfType<BadRequestObjectResult>(
                await _controller.Provision(
                    new EphemeralEnvironmentProvisionRequestApiModel { ProjectId = 10, TemplateId = 405, EnvironmentName = "eph-1" },
                    CancellationToken.None));

            // Invalid TierParameterOverridesJson in Provision
            _environmentTemplatesPersistentSource.GetTemplateById(406).Returns(new EnvironmentTemplateApiModel
            {
                Id = 406, ProjectId = 10, TierParameterOverridesJson = "{broken"
            });
            Assert.IsInstanceOfType<BadRequestObjectResult>(
                await _controller.Provision(
                    new EphemeralEnvironmentProvisionRequestApiModel { ProjectId = 10, TemplateId = 406, EnvironmentName = "eph-1" },
                    CancellationToken.None));

            // Blocked prod or Trading-vertical ("tr") tier
            foreach (var blockedTier in new[] { "pr", "tr" })
            {
                Assert.IsInstanceOfType<BadRequestObjectResult>(
                    await _controller.Provision(
                        new EphemeralEnvironmentProvisionRequestApiModel { ProjectId = 10, EnvironmentName = "eph-1", EnvironmentTier = blockedTier },
                        CancellationToken.None));
            }

            // Missing, prod, or unauthorized base environment
            _environmentsPersistentSource.GetEnvironment(50, _user).Returns((EnvironmentApiModel?)null);
            _environmentsPersistentSource.GetEnvironment(50).Returns((EnvironmentApiModel?)null);
            Assert.IsInstanceOfType<BadRequestObjectResult>(
                await _controller.Provision(
                    new EphemeralEnvironmentProvisionRequestApiModel { ProjectId = 10, EnvironmentName = "eph-1", BaseEnvironmentId = 50 },
                    CancellationToken.None));

            _environmentsPersistentSource.GetEnvironment(51, _user).Returns(new EnvironmentApiModel { EnvironmentId = 51, EnvironmentIsProd = true });
            Assert.IsInstanceOfType<BadRequestObjectResult>(
                await _controller.Provision(
                    new EphemeralEnvironmentProvisionRequestApiModel { ProjectId = 10, EnvironmentName = "eph-1", BaseEnvironmentId = 51 },
                    CancellationToken.None));

            _environmentsPersistentSource.GetEnvironment(52, _user).Returns((EnvironmentApiModel?)null);
            _environmentsPersistentSource.GetEnvironment(52).Returns(new EnvironmentApiModel { EnvironmentId = 52, EnvironmentName = "UnauthBase", EnvironmentIsProd = false });
            _securityPrivilegesChecker.CanModifyEnvironment(_user, "UnauthBase").Returns(false);
            Assert.IsInstanceOfType<ForbidResult>(
                await _controller.Provision(
                    new EphemeralEnvironmentProvisionRequestApiModel { ProjectId = 10, EnvironmentName = "eph-1", BaseEnvironmentId = 52 },
                    CancellationToken.None));

            // TTL out of bounds
            Assert.IsInstanceOfType<BadRequestObjectResult>(
                await _controller.Provision(
                    new EphemeralEnvironmentProvisionRequestApiModel { ProjectId = 10, EnvironmentName = "eph-1", TtlMinutes = 0 },
                    CancellationToken.None));
            Assert.IsInstanceOfType<BadRequestObjectResult>(
                await _controller.Provision(
                    new EphemeralEnvironmentProvisionRequestApiModel { ProjectId = 10, EnvironmentName = "eph-1", TtlMinutes = 2000 },
                    CancellationToken.None));

            // Duplicate environment (pre-check, normalized slug collision, and concurrent CreateEnvironment race)
            _environmentsPersistentSource.EnvironmentExists("eph-dup").Returns(true);
            Assert.IsInstanceOfType<ConflictObjectResult>(
                await _controller.Provision(
                    new EphemeralEnvironmentProvisionRequestApiModel { ProjectId = 10, EnvironmentName = "eph-dup" },
                    CancellationToken.None));
            _environmentsPersistentSource.GetEphemeralEnvironments(user: null).Returns(new[]
            {
                new EnvironmentApiModel { EnvironmentId = 801, EnvironmentName = "pr123" }
            });
            Assert.IsInstanceOfType<ConflictObjectResult>(
                await _controller.Provision(
                    new EphemeralEnvironmentProvisionRequestApiModel { ProjectId = 10, EnvironmentName = "pr 123" },
                    CancellationToken.None));
            _environmentsPersistentSource.GetEphemeralEnvironments(user: null).Returns(Array.Empty<EnvironmentApiModel>());
            _environmentsPersistentSource.EnvironmentExists("eph-race-1").Returns(false);
            _environmentsPersistentSource
                .When(x => x.CreateEnvironment(Arg.Is<EnvironmentApiModel>(e => e.EnvironmentName == "eph-race-1"), _user))
                .Do(_ => throw new InvalidOperationException("Already exists"));
            Assert.IsInstanceOfType<ConflictObjectResult>(
                await _controller.Provision(
                    new EphemeralEnvironmentProvisionRequestApiModel { ProjectId = 10, EnvironmentName = "eph-race-1", BuildUrl = "https://dev.azure.com/org/proj/_build/results?buildId=1" },
                    CancellationToken.None));
            _environmentsPersistentSource.EnvironmentExists("eph-race-tpl").Returns(false);
            _environmentsPersistentSource
                .When(x => x.CreateEnvironment(Arg.Is<EnvironmentApiModel>(e => e.EnvironmentName == "eph-race-tpl"), _user))
                .Do(_ => throw new InvalidOperationException("Environment template with ID 5 no longer exists."));
            var tplRaceConflict = await _controller.Provision(
                new EphemeralEnvironmentProvisionRequestApiModel { ProjectId = 10, EnvironmentName = "eph-race-tpl", BuildUrl = "https://dev.azure.com/org/proj/_build/results?buildId=1" },
                CancellationToken.None) as ConflictObjectResult;
            Assert.IsNotNull(tplRaceConflict);
            Assert.AreEqual("Environment template with ID 5 no longer exists.", tplRaceConflict.Value);
            _environmentsPersistentSource.EnvironmentExists("eph-race-2").Returns(false);
            _environmentsPersistentSource
                .When(x => x.CreateEnvironment(Arg.Is<EnvironmentApiModel>(e => e.EnvironmentName == "eph-race-2"), _user))
                .Do(_ => throw new Microsoft.EntityFrameworkCore.DbUpdateException("Duplicate key"));
            Assert.IsInstanceOfType<ConflictObjectResult>(
                await _controller.Provision(
                    new EphemeralEnvironmentProvisionRequestApiModel { ProjectId = 10, EnvironmentName = "eph-race-2", BuildUrl = "https://dev.azure.com/org/proj/_build/results?buildId=1" },
                    CancellationToken.None));

            // Full successful provision with template, base environment, catalog component, missing manifest component, non-catalog component, and validation components
            _environmentsPersistentSource.EnvironmentExists("eph-pr-100").Returns(false);
            _environmentsPersistentSource.GetEnvironment(20, _user).Returns(new EnvironmentApiModel { EnvironmentId = 20, EnvironmentName = "Traveler DV 01", EnvironmentIsProd = false });
            _securityPrivilegesChecker.CanModifyEnvironment(_user, "Traveler DV 01").Returns(true);

            var template = new EnvironmentTemplateApiModel
            {
                Id = 5,
                ProjectId = 10,
                DefaultTier = "",
                BaseEnvironmentId = 20,
                DefaultTtlMinutes = 120,
                MaxTtlMinutes = 480,
                ProvisionComponentIds = new List<int> { 1, 2, 3 },
                ValidationComponentIds = new List<int> { 4 },
                TierParameterOverridesJson = "null"
            };
            _environmentTemplatesPersistentSource.GetTemplateById(5).Returns(template);

            var components = new List<ComponentApiModel>
            {
                new()
                {
                    ComponentId = 1,
                    ComponentName = "Postgres",
                    ComponentType = ComponentType.Terraform,
                    TerraformSourceType = TerraformSourceType.Catalog,
                    TerraformTemplateName = "azure-postgresql-flexible-server",
                    TerraformTemplateVersion = "1.0.0",
                    IsEnabled = true
                },
                new()
                {
                    ComponentId = 2,
                    ComponentName = "MissingCatalogManifest",
                    ComponentType = ComponentType.Terraform,
                    TerraformSourceType = TerraformSourceType.Catalog,
                    TerraformTemplateName = "missing",
                    TerraformTemplateVersion = "1.0.0",
                    IsEnabled = true
                },
                new()
                {
                    ComponentId = 3,
                    ComponentName = "GitTerraform",
                    ComponentType = ComponentType.Terraform,
                    TerraformSourceType = TerraformSourceType.Git,
                    IsEnabled = true
                },
                new()
                {
                    ComponentId = 4,
                    ComponentName = "SmokeTest",
                    ComponentType = ComponentType.Terraform,
                    TerraformSourceType = TerraformSourceType.Catalog,
                    TerraformTemplateName = "azure-postgresql-flexible-server",
                    TerraformTemplateVersion = "1.0.0",
                    IsEnabled = true
                },
                new()
                {
                    ComponentId = 5,
                    ComponentName = "DisabledComp",
                    ComponentType = ComponentType.PowerShell,
                    IsEnabled = false
                }
            };
            _projectsPersistentSource.GetComponentsForProject(10).Returns(components);

            var pgManifest = BuildManifest(
                "azure-postgresql-flexible-server",
                "database",
                new TerraformTemplateParameter("sku_name", TerraformParameterType.String, false, null, "GP_Standard_D4s_v3", Array.Empty<string>(), null, null, null));
            _templateCatalog.GetAsync("azure-postgresql-flexible-server", "1.0.0", Arg.Any<CancellationToken>())
                .Returns(Task.FromResult<TerraformTemplateManifest?>(pgManifest));
            _templateCatalog.GetAsync("missing", "1.0.0", Arg.Any<CancellationToken>())
                .Returns(Task.FromResult<TerraformTemplateManifest?>(null));

            // Stored template sensitive override rejection in Provision (both by parameter name heuristic and by manifest Sensitive == true)
            var savedTpl5Overrides = template.TierParameterOverridesJson;
            template.TierParameterOverridesJson = "{\"admin_password\":\"plain\"}";
            Assert.IsInstanceOfType<BadRequestObjectResult>(
                await _controller.Provision(
                    new EphemeralEnvironmentProvisionRequestApiModel
                    {
                        ProjectId = 10,
                        TemplateId = 5,
                        EnvironmentName = "eph-pr-100"
                    },
                    CancellationToken.None));

            var pgManifestWithSensitive = BuildManifest(
                "azure-postgresql-flexible-server",
                "database",
                new TerraformTemplateParameter("sku_name", TerraformParameterType.String, false, null, "GP_Standard_D4s_v3", Array.Empty<string>(), null, null, null),
                new TerraformTemplateParameter("enable_ha", TerraformParameterType.Bool, false, null, "false", Array.Empty<string>(), null, null, null),
                new TerraformTemplateParameter("opaque_cred", TerraformParameterType.String, false, null, "default-secret", Array.Empty<string>(), null, null, null, Sensitive: true));
            _templateCatalog.GetAsync("azure-postgresql-flexible-server", "1.0.0", Arg.Any<CancellationToken>())
                .Returns(Task.FromResult<TerraformTemplateManifest?>(pgManifestWithSensitive));

            template.TierParameterOverridesJson = "{\"opaque_cred\":\"plain\"}";
            Assert.IsInstanceOfType<BadRequestObjectResult>(
                await _controller.Provision(
                    new EphemeralEnvironmentProvisionRequestApiModel
                    {
                        ProjectId = 10,
                        TemplateId = 5,
                        EnvironmentName = "eph-pr-100"
                    },
                    CancellationToken.None));
            template.TierParameterOverridesJson = savedTpl5Overrides;

            // Invalid parameter value in Provision (e.g. non-boolean for Bool parameter) returns 400 BadRequest before creating environment
            _templateCatalog.GetAsync("missing", "1.0.0", Arg.Any<CancellationToken>())
                .Returns(Task.FromResult<TerraformTemplateManifest?>(pgManifestWithSensitive));
            Assert.IsInstanceOfType<BadRequestObjectResult>(
                await _controller.Provision(
                    new EphemeralEnvironmentProvisionRequestApiModel
                    {
                        ProjectId = 10,
                        TemplateId = 5,
                        EnvironmentName = "eph-pr-100",
                        ParameterOverrides = new Dictionary<string, string> { ["enable_ha"] = "not-a-bool" }
                    },
                    CancellationToken.None));
            _templateCatalog.GetAsync("missing", "1.0.0", Arg.Any<CancellationToken>())
                .Returns(Task.FromResult<TerraformTemplateManifest?>(null));

            // Also cover missing manifest when mergedOverrides is non-empty (line 505), and unknown override key when all manifests resolve
            Assert.IsInstanceOfType<BadRequestObjectResult>(
                await _controller.Provision(
                    new EphemeralEnvironmentProvisionRequestApiModel
                    {
                        ProjectId = 10,
                        TemplateId = 5,
                        EnvironmentName = "eph-pr-100",
                        ParameterOverrides = new Dictionary<string, string> { ["sku_name"] = "B_Standard_B1ms" }
                    },
                    CancellationToken.None));
            _templateCatalog.GetAsync("missing", "1.0.0", Arg.Any<CancellationToken>())
                .Returns(Task.FromResult<TerraformTemplateManifest?>(pgManifest));
            Assert.IsInstanceOfType<BadRequestObjectResult>(
                await _controller.Provision(
                    new EphemeralEnvironmentProvisionRequestApiModel
                    {
                        ProjectId = 10,
                        TemplateId = 5,
                        EnvironmentName = "eph-pr-100",
                        ParameterOverrides = new Dictionary<string, string> { ["unknown_param"] = "val" }
                    },
                    CancellationToken.None));
            // Also verify that when inactiveValidationComponents has a component with a null manifest, it is skipped cleanly
            components[3].TerraformTemplateName = "missing-val";
            _templateCatalog.GetAsync("missing-val", "1.0.0", Arg.Any<CancellationToken>())
                .Returns(Task.FromResult<TerraformTemplateManifest?>(null));
            Assert.IsInstanceOfType<BadRequestObjectResult>(
                await _controller.Provision(
                    new EphemeralEnvironmentProvisionRequestApiModel
                    {
                        ProjectId = 10,
                        TemplateId = 5,
                        EnvironmentName = "eph-pr-100",
                        ParameterOverrides = new Dictionary<string, string> { ["unknown_param"] = "val" }
                    },
                    CancellationToken.None));
            components[3].TerraformTemplateName = "azure-postgresql-flexible-server";
            _templateCatalog.GetAsync("missing", "1.0.0", Arg.Any<CancellationToken>())
                .Returns(Task.FromResult<TerraformTemplateManifest?>(null));

            _environmentsPersistentSource.CreateEnvironment(Arg.Any<EnvironmentApiModel>(), _user)
                .Returns(ci =>
                {
                    var env = ci.Arg<EnvironmentApiModel>();
                    env.EnvironmentId = 500;
                    return env;
                });

            RequestDto? capturedProvisionRequest = null;
            _requestService.CreateRequest(Arg.Do<RequestDto>(r => capturedProvisionRequest = r), _user)
                .Returns(new RequestStatusDto { Id = 1001, Status = "Pending" });

            // Also cover dorcPropertyOnlyKeys branch in Provision when base environment has a non-sensitive DOrc property
            var provPropSource = Substitute.For<IPropertyValuesPersistentSource>();
            provPropSource.GetGlobalProperties(null).Returns(new[]
            {
                new PropertyValueDto { Property = new PropertyApiModel { Name = "sku_name", Secure = false }, Value = "GP_Standard_D2s_v3" }
            });
            var ctrlWithProvProps = new EphemeralEnvironmentsController(
                _log,
                _environmentsPersistentSource,
                _environmentTemplatesPersistentSource,
                _projectsPersistentSource,
                _requestsPersistentSource,
                _securityPrivilegesChecker,
                _claimsPrincipalReader,
                _templateCatalog,
                _requestService,
                _rolePrivilegesChecker,
                provPropSource);
            ctrlWithProvProps.ControllerContext = _controller.ControllerContext;

            var okResult = await ctrlWithProvProps.Provision(
                new EphemeralEnvironmentProvisionRequestApiModel
                {
                    ProjectId = 10,
                    TemplateId = 5,
                    EnvironmentName = "eph-pr-100",
                    RunValidationComponents = true,
                    BuildNum = "20260417.5",
                    BuildUrl = "https://dev.azure.com/org/proj/_build/results?buildId=5"
                },
                CancellationToken.None) as OkObjectResult;

            Assert.IsNotNull(okResult);
            var provisionResponse = (EphemeralEnvironmentProvisionResponseApiModel)okResult.Value!;
            Assert.AreEqual(500, provisionResponse.Environment.EnvironmentId);
            Assert.AreEqual(20, provisionResponse.Environment.ParentId);
            CollectionAssert.AreEqual(new[] { 1001 }, provisionResponse.ProvisionRequestIds);
            Assert.IsNotNull(capturedProvisionRequest);
            Assert.AreEqual("20260417.5", capturedProvisionRequest!.BuildNum);
            Assert.AreEqual("https://dev.azure.com/org/proj/_build/results?buildId=5", capturedProvisionRequest.BuildUrl);
            Assert.AreEqual(
                "4",
                capturedProvisionRequest.RequestProperties.Single(p => p.PropertyName == TerraformDestroyOperationConstants.DeferredValidationComponentsPropertyName).PropertyValue);
            Assert.IsFalse(capturedProvisionRequest.RequestProperties.Any(p => p.PropertyName.Contains("opaque_cred")));
            Assert.IsTrue(provisionResponse.SeededComponentInputs.ContainsKey("Postgres"));
            Assert.AreEqual("***REDACTED***", provisionResponse.SeededComponentInputs["Postgres"]["opaque_cred"]);

            // Also verify one-off sensitive ParameterOverrides are accepted in Provision and forwarded as component-scoped IsSensitive RequestProperty
            _environmentsPersistentSource.EnvironmentExists("eph-pr-100-sens").Returns(false);
            _templateCatalog.GetAsync("missing", "1.0.0", Arg.Any<CancellationToken>())
                .Returns(Task.FromResult<TerraformTemplateManifest?>(pgManifestWithSensitive));
            var sensOverrideOk = await ctrlWithProvProps.Provision(
                new EphemeralEnvironmentProvisionRequestApiModel
                {
                    ProjectId = 10,
                    TemplateId = 5,
                    EnvironmentName = "eph-pr-100-sens",
                    ParameterOverrides = new Dictionary<string, string> { ["opaque_cred"] = "one-off-secret" },
                    BuildNum = "20260417.5"
                },
                CancellationToken.None) as OkObjectResult;
            Assert.IsNotNull(sensOverrideOk);
            var sensProp = capturedProvisionRequest.RequestProperties.Single(p => p.PropertyName == "Postgres:opaque_cred");
            Assert.AreEqual("one-off-secret", sensProp.PropertyValue);
            Assert.IsTrue(sensProp.IsSensitive);
            _templateCatalog.GetAsync("missing", "1.0.0", Arg.Any<CancellationToken>())
                .Returns(Task.FromResult<TerraformTemplateManifest?>(null));

            // Also cover Provision rejecting template with unknown/disabled ProvisionComponentIds (400 BadRequest), no base env, RunValidationComponents with empty resolved validation components, and SubmitComponentRequest failure
            _environmentsPersistentSource.EnvironmentExists("eph-empty").Returns(false);
            var invalidCompTemplate = new EnvironmentTemplateApiModel
            {
                Id = 6,
                ProjectId = 10,
                DefaultTier = "qa",
                DefaultTtlMinutes = 60,
                MaxTtlMinutes = 120,
                ProvisionComponentIds = new List<int> { 999 },
                ValidationComponentIds = new List<int> { 998 }
            };
            _environmentTemplatesPersistentSource.GetTemplateById(6).Returns(invalidCompTemplate);

            var invalidCompResult = await _controller.Provision(
                new EphemeralEnvironmentProvisionRequestApiModel
                {
                    ProjectId = 10,
                    TemplateId = 6,
                    EnvironmentName = "eph-empty",
                    RunValidationComponents = true
                },
                CancellationToken.None) as BadRequestObjectResult;
            Assert.IsNotNull(invalidCompResult);

            // Cover invalid ValidationComponentIds when ProvisionComponentIds are valid (line 474), and non-catalog validation components requiring BuildNum or BuildUrl (lines 483-486)
            var invalidValCompTemplate = new EnvironmentTemplateApiModel
            {
                Id = 61,
                ProjectId = 10,
                DefaultTier = "qa",
                DefaultTtlMinutes = 60,
                MaxTtlMinutes = 120,
                ProvisionComponentIds = new List<int> { 1 },
                ValidationComponentIds = new List<int> { 998 }
            };
            _environmentTemplatesPersistentSource.GetTemplateById(61).Returns(invalidValCompTemplate);
            Assert.IsInstanceOfType<BadRequestObjectResult>(await _controller.Provision(
                new EphemeralEnvironmentProvisionRequestApiModel
                {
                    ProjectId = 10,
                    TemplateId = 61,
                    EnvironmentName = "eph-empty",
                    RunValidationComponents = true
                },
                CancellationToken.None));

            var nonCatalogValTemplate = new EnvironmentTemplateApiModel
            {
                Id = 62,
                ProjectId = 10,
                DefaultTier = "qa",
                DefaultTtlMinutes = 60,
                MaxTtlMinutes = 120,
                ProvisionComponentIds = new List<int> { 1 },
                ValidationComponentIds = new List<int> { 3 }
            };
            _environmentTemplatesPersistentSource.GetTemplateById(62).Returns(nonCatalogValTemplate);
            Assert.IsInstanceOfType<BadRequestObjectResult>(await _controller.Provision(
                new EphemeralEnvironmentProvisionRequestApiModel
                {
                    ProjectId = 10,
                    TemplateId = 62,
                    EnvironmentName = "eph-empty",
                    RunValidationComponents = true
                },
                CancellationToken.None));
            // Reject caller-supplied BuildUrl starting with reserved catalog scheme
            Assert.IsInstanceOfType<BadRequestObjectResult>(await _controller.Provision(
                new EphemeralEnvironmentProvisionRequestApiModel
                {
                    ProjectId = 10,
                    TemplateId = 62,
                    EnvironmentName = "eph-empty",
                    RunValidationComponents = true,
                    BuildUrl = "dorc-catalog://bypass"
                },
                CancellationToken.None));
            Assert.IsInstanceOfType<OkObjectResult>(await _controller.Provision(
                new EphemeralEnvironmentProvisionRequestApiModel
                {
                    ProjectId = 10,
                    TemplateId = 62,
                    EnvironmentName = "eph-empty",
                    RunValidationComponents = true,
                    BuildUrl = "https://dev.azure.com/org/proj/_build/results?buildId=9",
                    BuildText = "CI-Pipeline"
                },
                CancellationToken.None));
            // Also cover vstfs BuildUrl with and without project.ArtefactsUrl, and GitHub SourceControlType setting VstsUrl
            project.ArtefactsUrl = "https://dev.azure.com/org/proj";
            Assert.IsInstanceOfType<OkObjectResult>(await _controller.Provision(
                new EphemeralEnvironmentProvisionRequestApiModel
                {
                    ProjectId = 10,
                    TemplateId = 62,
                    EnvironmentName = "eph-empty",
                    RunValidationComponents = true,
                    BuildUrl = "vstfs:///Build/Build/9",
                    BuildText = "CI-Pipeline"
                },
                CancellationToken.None));
            project.ArtefactsUrl = "";
            project.SourceControlType = SourceControlType.GitHub;
            Assert.IsInstanceOfType<OkObjectResult>(await _controller.Provision(
                new EphemeralEnvironmentProvisionRequestApiModel
                {
                    ProjectId = 10,
                    TemplateId = 62,
                    EnvironmentName = "eph-empty",
                    RunValidationComponents = true,
                    BuildUrl = "https://github.com/org/repo/actions/runs/99"
                },
                CancellationToken.None));
            project.SourceControlType = SourceControlType.AzureDevOps;
            Assert.IsInstanceOfType<OkObjectResult>(await _controller.Provision(
                new EphemeralEnvironmentProvisionRequestApiModel
                {
                    ProjectId = 10,
                    TemplateId = 62,
                    EnvironmentName = "eph-empty",
                    RunValidationComponents = true,
                    BuildNum = "20260401.1"
                },
                CancellationToken.None));
            Assert.IsInstanceOfType<OkObjectResult>(await _controller.Provision(
                new EphemeralEnvironmentProvisionRequestApiModel
                {
                    ProjectId = 10,
                    TemplateId = 62,
                    EnvironmentName = "eph-empty",
                    RunValidationComponents = true,
                    BuildUrl = "file:///drops/val"
                },
                CancellationToken.None));

            _environmentsPersistentSource.TryTransitionEphemeralState(
                    Arg.Any<string>(),
                    Arg.Any<IReadOnlyCollection<string>>(),
                    Arg.Any<string>(),
                    Arg.Any<int?>())
                .Returns(true);

            // Cover template with empty ProvisionComponentIds and non-empty ValidationComponentIds (submits validation directly and transitions to Testing)
            var valOnlyTemplate = new EnvironmentTemplateApiModel
            {
                Id = 63,
                ProjectId = 10,
                DefaultTier = "qa",
                DefaultTtlMinutes = 60,
                MaxTtlMinutes = 120,
                ProvisionComponentIds = new List<int>(),
                ValidationComponentIds = new List<int> { 1 }
            };
            _environmentTemplatesPersistentSource.GetTemplateById(63).Returns(valOnlyTemplate);
            var valOnlyOk = await _controller.Provision(
                new EphemeralEnvironmentProvisionRequestApiModel
                {
                    ProjectId = 10,
                    TemplateId = 63,
                    EnvironmentName = "eph-empty",
                    RunValidationComponents = true
                },
                CancellationToken.None) as OkObjectResult;
            Assert.IsNotNull(valOnlyOk);
            Assert.AreEqual(1, ((EphemeralEnvironmentProvisionResponseApiModel)valOnlyOk.Value!).ValidationRequestIds.Count);

            _environmentsPersistentSource.TryTransitionEphemeralState(
                    "eph-empty",
                    Arg.Any<IReadOnlyCollection<string>>(),
                    EphemeralEnvironmentStates.Testing,
                    500)
                .Returns(false);
            Assert.IsInstanceOfType<ConflictObjectResult>(await _controller.Provision(
                new EphemeralEnvironmentProvisionRequestApiModel
                {
                    ProjectId = 10,
                    TemplateId = 63,
                    EnvironmentName = "eph-empty",
                    RunValidationComponents = true
                },
                CancellationToken.None));
            _environmentsPersistentSource.TryTransitionEphemeralState(
                    "eph-empty",
                    Arg.Any<IReadOnlyCollection<string>>(),
                    EphemeralEnvironmentStates.Testing,
                    500)
                .Returns(true);

            // Cover ResolveDefaultBuildText for GitHub projects (with and without ArtefactsSubPaths) and AzureDevOps fallback
            Assert.AreEqual(
                string.Empty,
                EphemeralEnvironmentsController.ResolveDefaultBuildText(
                    new ProjectApiModel { SourceControlType = SourceControlType.GitHub, ArtefactsSubPaths = "ci.yml;cd.yml" },
                    "dorc-catalog://bundled",
                    "100"));
            Assert.AreEqual(
                "ci.yml",
                EphemeralEnvironmentsController.ResolveDefaultBuildText(
                    new ProjectApiModel { SourceControlType = SourceControlType.GitHub, ArtefactsSubPaths = "ci.yml;cd.yml" },
                    "https://github.com/org/repo/actions/runs/1",
                    "100"));
            Assert.AreEqual(
                "100",
                EphemeralEnvironmentsController.ResolveDefaultBuildText(
                    new ProjectApiModel { SourceControlType = SourceControlType.GitHub, ArtefactsSubPaths = " ; " },
                    "https://github.com/org/repo/actions/runs/1",
                    "100"));
            Assert.AreEqual(
                "100",
                EphemeralEnvironmentsController.ResolveDefaultBuildText(
                    new ProjectApiModel { SourceControlType = SourceControlType.GitHub, ArtefactsSubPaths = "" },
                    "https://github.com/org/repo/actions/runs/1",
                    "100"));
            Assert.AreEqual(
                "100",
                EphemeralEnvironmentsController.ResolveDefaultBuildText(
                    new ProjectApiModel { SourceControlType = SourceControlType.AzureDevOps },
                    "https://dev.azure.com/org/proj/_build/results?buildId=9",
                    "100"));

            // Also cover template with empty ValidationComponentIds when RunValidationComponents is true, and SubmitComponentRequest throwing when status is null or Id <= 0
            var noValTemplate = new EnvironmentTemplateApiModel
            {
                Id = 7,
                ProjectId = 10,
                DefaultTier = "dv",
                DefaultTtlMinutes = 60,
                MaxTtlMinutes = 120,
                ProvisionComponentIds = new List<int> { 4 },
                ValidationComponentIds = new List<int>()
            };
            _environmentTemplatesPersistentSource.GetTemplateById(7).Returns(noValTemplate);
            _environmentsPersistentSource.EnvironmentExists("eph-no-val").Returns(false);
            var noValOk = await _controller.Provision(
                new EphemeralEnvironmentProvisionRequestApiModel
                {
                    ProjectId = 10,
                    TemplateId = 7,
                    EnvironmentName = "eph-no-val",
                    RunValidationComponents = true
                },
                CancellationToken.None) as OkObjectResult;
            Assert.IsNotNull(noValOk);

            // Also cover Provision when TemplateId is null and RunValidationComponents is true, and when project has 0 enabled components (transitions directly to Ready)
            _environmentsPersistentSource.EnvironmentExists("eph-no-tpl").Returns(false);
            var noTplOk = await _controller.Provision(
                new EphemeralEnvironmentProvisionRequestApiModel
                {
                    ProjectId = 10,
                    TemplateId = null,
                    EnvironmentName = "eph-no-tpl",
                    RunValidationComponents = true
                },
                CancellationToken.None) as OkObjectResult;
            Assert.IsNotNull(noTplOk);

            _projectsPersistentSource.GetComponentsForProject(10).Returns(Array.Empty<ComponentApiModel>());
            _environmentsPersistentSource.EnvironmentExists("eph-zero-comps").Returns(false);
            var zeroCompsOk = await _controller.Provision(
                new EphemeralEnvironmentProvisionRequestApiModel
                {
                    ProjectId = 10,
                    TemplateId = null,
                    EnvironmentName = "eph-zero-comps"
                },
                CancellationToken.None) as OkObjectResult;
            Assert.IsNotNull(zeroCompsOk);
            _environmentsPersistentSource.Received().TryTransitionEphemeralState(
                "eph-zero-comps",
                Arg.Is<IReadOnlyCollection<string>>(s => s.Contains(EphemeralEnvironmentStates.Provisioning)),
                EphemeralEnvironmentStates.Ready,
                500);
            _environmentsPersistentSource.TryTransitionEphemeralState(
                    "eph-zero-comps",
                    Arg.Any<IReadOnlyCollection<string>>(),
                    EphemeralEnvironmentStates.Ready,
                    500)
                .Returns(false);
            Assert.IsInstanceOfType<ConflictObjectResult>(await _controller.Provision(
                new EphemeralEnvironmentProvisionRequestApiModel
                {
                    ProjectId = 10,
                    TemplateId = null,
                    EnvironmentName = "eph-zero-comps"
                },
                CancellationToken.None));
            _environmentsPersistentSource.TryTransitionEphemeralState(
                    "eph-zero-comps",
                    Arg.Any<IReadOnlyCollection<string>>(),
                    EphemeralEnvironmentStates.Ready,
                    500)
                .Returns(true);
            _projectsPersistentSource.GetComponentsForProject(10).Returns(components);

            _environmentsPersistentSource.EnvironmentExists("eph-fail-submit").Returns(false);
            _requestService.CreateRequest(Arg.Any<RequestDto>(), _user).Returns((RequestStatusDto)null!);
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => _controller.Provision(
                new EphemeralEnvironmentProvisionRequestApiModel
                {
                    ProjectId = 10,
                    TemplateId = 7,
                    EnvironmentName = "eph-fail-submit",
                    RunValidationComponents = false
                },
                CancellationToken.None));
            _environmentsPersistentSource.Received().DeleteEnvironmentInternal(500, true);

            _requestService.CreateRequest(Arg.Any<RequestDto>(), _user).Returns(new RequestStatusDto { Id = 0, Status = "Rejected" });
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => _controller.Provision(
                new EphemeralEnvironmentProvisionRequestApiModel
                {
                    ProjectId = 10,
                    TemplateId = 7,
                    EnvironmentName = "eph-fail-submit"
                },
                CancellationToken.None));

            // When exception occurs AFTER SubmitComponentRequest succeeded (requestSubmitted == true), Provision transitions to ProvisionFailed instead of deleting live metadata
            _requestService.CreateRequest(Arg.Any<RequestDto>(), _user).Returns(new RequestStatusDto { Id = 1002, Status = "Pending" });
            _environmentsPersistentSource.EnvironmentExists("eph-post-submit-throw").Returns(false);
            _log.When(l => l.Log(
                    LogLevel.Information,
                    Arg.Any<EventId>(),
                    Arg.Any<Arg.AnyType>(),
                    Arg.Any<Exception?>(),
                    Arg.Any<Func<Arg.AnyType, Exception?, string>>()))
                .Do(_ => throw new InvalidOperationException("Post-submit failure"));
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => _controller.Provision(
                new EphemeralEnvironmentProvisionRequestApiModel
                {
                    ProjectId = 10,
                    TemplateId = 7,
                    EnvironmentName = "eph-post-submit-throw"
                },
                CancellationToken.None));
            _environmentsPersistentSource.Received(1).TryTransitionEphemeralState(
                "eph-post-submit-throw",
                Arg.Is<IReadOnlyCollection<string>>(states =>
                    states.Contains(EphemeralEnvironmentStates.Provisioning) &&
                    states.Contains(EphemeralEnvironmentStates.Testing)),
                EphemeralEnvironmentStates.ProvisionFailed,
                500);
        }

        [TestMethod]
        public void Teardown_ValidationCancellationDestroyAndNoComponents_CoversAllBranches()
        {
            _environmentsPersistentSource.GetEnvironment("missing").Returns((EnvironmentApiModel)null!);
            Assert.IsInstanceOfType<NotFoundObjectResult>(_controller.Teardown("missing"));

            _environmentsPersistentSource.GetEnvironment("non-eph").Returns(new EnvironmentApiModel
            {
                EnvironmentId = 1,
                EnvironmentName = "non-eph",
                IsEphemeral = false,
                EnvironmentIsProd = false
            });
            Assert.IsInstanceOfType<BadRequestObjectResult>(_controller.Teardown("non-eph"));

            _environmentsPersistentSource.GetEnvironment("prod-eph").Returns(new EnvironmentApiModel
            {
                EnvironmentId = 2,
                EnvironmentName = "prod-eph",
                IsEphemeral = true,
                EnvironmentIsProd = true
            });
            Assert.IsInstanceOfType<BadRequestObjectResult>(_controller.Teardown("prod-eph"));

            var envWithTemplate = new EnvironmentApiModel
            {
                EnvironmentId = 10,
                EnvironmentName = "eph-pr-10",
                IsEphemeral = true,
                EnvironmentIsProd = false,
                TemplateId = 3
            };
            _environmentsPersistentSource.GetEnvironment("eph-pr-10").Returns(envWithTemplate);
            _securityPrivilegesChecker.CanModifyEnvironment(_user, "eph-pr-10").Returns(false);
            Assert.IsInstanceOfType<ForbidResult>(_controller.Teardown("eph-pr-10"));

            _securityPrivilegesChecker.CanModifyEnvironment(_user, "eph-pr-10").Returns(true);

            // Conflict when TryClaimEphemeralEnvironmentForTeardown returns false (already TearingDown with active destroy request or TornDown)
            string? dummyPrevState;
            _environmentsPersistentSource.TryClaimEphemeralEnvironmentForTeardown(10, Arg.Is(false), Arg.Any<DateTime>(), out dummyPrevState, Arg.Any<bool>())
                .Returns(false);
            Assert.IsInstanceOfType<ConflictObjectResult>(_controller.Teardown("eph-pr-10"));

            envWithTemplate.EphemeralState = EphemeralEnvironmentStates.TornDown;
            Assert.IsInstanceOfType<ConflictObjectResult>(_controller.Teardown("eph-pr-10"));
            envWithTemplate.EphemeralState = EphemeralEnvironmentStates.TearingDown;
            Assert.IsInstanceOfType<ConflictObjectResult>(_controller.Teardown("eph-pr-10"));
            envWithTemplate.EphemeralState = EphemeralEnvironmentStates.Ready;

            _environmentsPersistentSource.TryClaimEphemeralEnvironmentForTeardown(10, Arg.Is(false), Arg.Any<DateTime>(), out dummyPrevState, Arg.Any<bool>())
                .Returns(true);
            _requestsPersistentSource.GetProvisionedComponentIdsForEnvironment(10).Returns(new[] { 1, 2 });

            var serializer = new Dorc.Core.DeploymentRequestDetailSerializer();
            var applyDetails = serializer.Serialize(new DeploymentRequestDetail
            {
                EnvironmentName = "eph-pr-10",
                Components = new List<string> { "CompA" }
            });
            var destroyDetails = serializer.Serialize(new DeploymentRequestDetail
            {
                EnvironmentName = "eph-pr-10",
                Components = new List<string> { "CompA" },
                Properties = new List<PropertyPair>
                {
                    new(TerraformDestroyOperationConstants.PropertyName, TerraformDestroyOperationConstants.DestroyValue)
                }
            });
            var incompleteXmlDetails = "<PropertyName>TerraformOperation</PropertyName>";

            _requestsPersistentSource.GetRequestsWithStatus(
                DeploymentRequestStatus.Pending,
                DeploymentRequestStatus.Running,
                DeploymentRequestStatus.Requesting,
                DeploymentRequestStatus.WaitingConfirmation,
                false).Returns(new[]
            {
                new DeploymentRequestApiModel { Id = 1, EnvironmentName = "other-env", Status = "Pending", RequestDetails = applyDetails },
                new DeploymentRequestApiModel { Id = 2, EnvironmentName = "eph-pr-10", Status = "Running", RequestDetails = destroyDetails },
                new DeploymentRequestApiModel { Id = 3, EnvironmentName = "eph-pr-10", Status = "Running", RequestDetails = applyDetails },
                new DeploymentRequestApiModel { Id = 4, EnvironmentName = "eph-pr-10", Status = "Requesting", RequestDetails = incompleteXmlDetails },
                new DeploymentRequestApiModel { Id = 5, EnvironmentName = "eph-pr-10", Status = "WaitingConfirmation", RequestDetails = "" }
            });
            _requestsPersistentSource.GetRequestsWithStatus(
                DeploymentRequestStatus.Confirmed,
                DeploymentRequestStatus.Paused,
                false).Returns(new[]
            {
                new DeploymentRequestApiModel { Id = 6, EnvironmentName = "eph-pr-10", Status = "Confirmed", RequestDetails = applyDetails },
                new DeploymentRequestApiModel { Id = 7, EnvironmentName = "eph-pr-10", Status = "Paused", RequestDetails = applyDetails }
            });

            // When EphemeralState is TearingDown and there IS an active destroy request (Id = 2), Teardown returns Conflict
            envWithTemplate.EphemeralState = EphemeralEnvironmentStates.TearingDown;
            Assert.IsInstanceOfType<ConflictObjectResult>(_controller.Teardown("eph-pr-10"));
            envWithTemplate.EphemeralState = EphemeralEnvironmentStates.Ready;

            _environmentTemplatesPersistentSource.GetTemplateById(3).Returns(new EnvironmentTemplateApiModel
            {
                Id = 3,
                ProjectId = 10,
                ProvisionComponentIds = new List<int> { 1, 2 }
            });
            _projectsPersistentSource.GetProject(10).Returns(new ProjectApiModel { ProjectId = 10, ProjectName = "Traveler" });
            _projectsPersistentSource.GetComponentsForProject(10).Returns(new[]
            {
                new ComponentApiModel
                {
                    ComponentId = 1,
                    ComponentName = "Postgres",
                    ComponentType = ComponentType.Terraform,
                    TerraformSourceType = TerraformSourceType.Catalog,
                    TerraformTemplateName = "azure-postgresql-flexible-server",
                    IsEnabled = true
                },
                new ComponentApiModel
                {
                    ComponentId = 2,
                    ComponentName = "Redis",
                    ComponentType = ComponentType.Terraform,
                    TerraformSourceType = TerraformSourceType.Catalog,
                    TerraformTemplateName = "azure-redis",
                    IsEnabled = true
                },
                new ComponentApiModel
                {
                    ComponentId = 99,
                    ComponentName = "ParentGroup",
                    ComponentType = ComponentType.Terraform,
                    IsEnabled = true,
                    Children = new List<ComponentApiModel> { new() { ComponentId = 2, ComponentName = "Redis", ComponentType = ComponentType.Terraform, IsEnabled = true } }
                }
            });

            RequestDto? capturedDestroyDto = null;
            _requestService.CreateRequest(Arg.Do<RequestDto>(r => capturedDestroyDto = r), _user)
                .Returns(new RequestStatusDto { Id = 2001, Status = "Pending" });

            var okWithDeleteProp = _controller.Teardown(
                "eph-pr-10",
                new EphemeralEnvironmentTeardownRequestApiModel { Reason = "PR merged", ForceDeleteMetadataOnNoComponents = true }) as OkObjectResult;

            Assert.IsNotNull(okWithDeleteProp);
            var teardownResponse = (EphemeralEnvironmentTeardownResponseApiModel)okWithDeleteProp.Value!;
            Assert.AreEqual(2001, teardownResponse.DestroyRequestId);
            CollectionAssert.AreEqual(new[] { 3, 4, 5, 6, 7 }, teardownResponse.CancelledRequestIds);
            Assert.IsNotNull(capturedDestroyDto);
            CollectionAssert.AreEqual(new[] { "Redis", "Postgres" }, capturedDestroyDto!.Components.ToArray());
            Assert.AreEqual(
                "true",
                capturedDestroyDto.RequestProperties.Single(p => p.PropertyName == TerraformDestroyOperationConstants.DeleteEnvironmentOnSuccessPropertyName).PropertyValue);

            // Also cover Teardown with RetainMetadataAfterTeardown = true and with null request
            var okRetainMetadata = _controller.Teardown(
                "eph-pr-10",
                new EphemeralEnvironmentTeardownRequestApiModel { RetainMetadataAfterTeardown = true }) as OkObjectResult;
            Assert.IsNotNull(okRetainMetadata);
            Assert.AreEqual(
                "false",
                capturedDestroyDto.RequestProperties.Single(p => p.PropertyName == TerraformDestroyOperationConstants.DeleteEnvironmentOnSuccessPropertyName).PropertyValue);

            var okWithoutDeleteProp = _controller.Teardown("eph-pr-10", null) as OkObjectResult;
            Assert.IsNotNull(okWithoutDeleteProp);

            // When provisioned component IDs cannot be resolved in any project, Teardown transitions to TeardownFailed and returns 409 Conflict
            _requestsPersistentSource.GetProvisionedComponentIdsForEnvironment(10).Returns(new[] { 9999 });
            Assert.IsInstanceOfType<ConflictObjectResult>(_controller.Teardown("eph-pr-10", null));
            _environmentsPersistentSource.Received(1).UpdateEphemeralState("eph-pr-10", EphemeralEnvironmentStates.TeardownFailed);
            _requestsPersistentSource.GetProvisionedComponentIdsForEnvironment(10).Returns(Array.Empty<int>());
            Assert.IsInstanceOfType<OkObjectResult>(_controller.Teardown("eph-pr-10", null));
            envWithTemplate.EphemeralState = EphemeralEnvironmentStates.Provisioning;
            Assert.IsInstanceOfType<OkObjectResult>(_controller.Teardown("eph-pr-10", null));
            envWithTemplate.EphemeralState = EphemeralEnvironmentStates.ProvisionFailed;
            Assert.IsInstanceOfType<OkObjectResult>(_controller.Teardown("eph-pr-10", null));
            envWithTemplate.EphemeralState = EphemeralEnvironmentStates.Ready;
            envWithTemplate.ExpiresAtUtc = DateTime.UnixEpoch.AddSeconds(1);
            Assert.IsInstanceOfType<OkObjectResult>(_controller.Teardown("eph-pr-10", null));
            envWithTemplate.ExpiresAtUtc = null;
            _requestsPersistentSource.GetProvisionedComponentIdsForEnvironment(10).Returns(new[] { 1, 2 });

            // When CreateRequest throws during Teardown after claiming TearingDown, environment transitions to TeardownFailed
            _requestService.When(r => r.CreateRequest(Arg.Is<RequestDto>(d => d.Environment == "eph-pr-10"), _user))
                .Do(_ => throw new InvalidOperationException("Destroy submission failed"));
            Assert.Throws<InvalidOperationException>(() => _controller.Teardown("eph-pr-10", null));
            _environmentsPersistentSource.Received(2).UpdateEphemeralState("eph-pr-10", EphemeralEnvironmentStates.TeardownFailed);

            // Now cover Teardown when TemplateId is null, project has no Terraform components, and default metadata deletion vs RetainMetadataAfterTeardown = true
            var envWithoutTemplate = new EnvironmentApiModel
            {
                EnvironmentId = 11,
                EnvironmentName = "eph-no-tf",
                IsEphemeral = true,
                EnvironmentIsProd = false,
                TemplateId = null
            };
            _environmentsPersistentSource.GetEnvironment("eph-no-tf").Returns(envWithoutTemplate);
            _environmentsPersistentSource.TryClaimEphemeralEnvironmentForTeardown(11, Arg.Is(false), Arg.Any<DateTime>(), out dummyPrevState, Arg.Any<bool>())
                .Returns(true);
            _securityPrivilegesChecker.CanModifyEnvironment(_user, "eph-no-tf").Returns(true);
            _environmentsPersistentSource.GetMappedProjects("eph-no-tf").Returns(Array.Empty<ProjectApiModel>());
            _environmentsPersistentSource.DeleteEnvironmentInternal(11).Returns(true);

            var noTfDeleteOk = _controller.Teardown(
                "eph-no-tf",
                new EphemeralEnvironmentTeardownRequestApiModel { ForceDeleteMetadataOnNoComponents = true }) as OkObjectResult;

            // Also cover Teardown when TemplateId is null and GetMappedProjects returns a non-null project (with durable component discovery + null ComponentId + non-catalog component backfill + build-identity batch splitting)
            _projectsPersistentSource.GetComponentsForProject(10).Returns(new[]
            {
                new ComponentApiModel { ComponentId = 1, ComponentName = "Postgres", ComponentType = ComponentType.Terraform, TerraformSourceType = TerraformSourceType.Catalog, TerraformTemplateName = "azure-postgresql-flexible-server", IsEnabled = true },
                new ComponentApiModel { ComponentId = 2, ComponentName = "Redis", ComponentType = ComponentType.Terraform, TerraformSourceType = TerraformSourceType.AzureArtifact, IsEnabled = true },
                new ComponentApiModel { ComponentId = 3, ComponentName = "DurableDisabledTf", ComponentType = ComponentType.Terraform, TerraformSourceType = TerraformSourceType.AzureArtifact, IsEnabled = false },
                new ComponentApiModel { ComponentId = 4, ComponentName = "UnprovisionedDisabledTf", ComponentType = ComponentType.Terraform, IsEnabled = false },
                new ComponentApiModel { ComponentId = null, ComponentName = "NoIdTf", ComponentType = ComponentType.Terraform, TerraformSourceType = TerraformSourceType.AzureArtifact, IsEnabled = true },
                new ComponentApiModel { ComponentId = 5, ComponentName = "PsComp", ComponentType = ComponentType.PowerShell, IsEnabled = true }
            });
            _projectsPersistentSource.GetComponentsForProject(20).Returns(new[]
            {
                new ComponentApiModel { ComponentId = 2, ComponentName = "Redis", ComponentType = ComponentType.Terraform, TerraformSourceType = TerraformSourceType.AzureArtifact, IsEnabled = true },
                new ComponentApiModel { ComponentId = 201, ComponentName = "ExtraTf", ComponentType = ComponentType.Terraform, TerraformSourceType = TerraformSourceType.AzureArtifact, IsEnabled = true },
                new ComponentApiModel { ComponentId = 201, ComponentName = "ExtraTf", ComponentType = ComponentType.Terraform, TerraformSourceType = TerraformSourceType.AzureArtifact, IsEnabled = true }
            });
            _requestsPersistentSource.GetLatestAppliedRequestForEnvironment("eph-no-tf", null, 2).Returns(new DeploymentRequestApiModel
            {
                Project = "Traveler",
                BuildUri = "vstfs:///Build/Build/10",
                DropLocation = @"\\drop\10",
                BuildNumber = "20260401.10"
            });
            _requestsPersistentSource.GetLatestAppliedRequestForEnvironment("eph-no-tf", "Traveler", 2).Returns(new DeploymentRequestApiModel
            {
                Project = "Traveler",
                BuildUri = "vstfs:///Build/Build/10",
                DropLocation = @"\\drop\10",
                BuildNumber = "20260401.10"
            });
            _requestsPersistentSource.GetProvisionedProjectsForComponent(11, 1).Returns(new[] { "Traveler" });
            _requestsPersistentSource.GetProvisionedProjectsForComponent(11, 2).Returns(new[] { "Traveler", "Companion" });
            _requestsPersistentSource.GetLatestAppliedRequestForEnvironment("eph-no-tf", "Traveler", 3).Returns(new DeploymentRequestApiModel
            {
                Project = "Traveler",
                BuildUri = "vstfs:///Build/Build/20",
                DropLocation = @"\\drop\20",
                BuildNumber = "20260401.20"
            });
            _requestsPersistentSource.GetProvisionedProjectsForComponent(11, 201).Returns(new[] { "Companion" });
            _requestsPersistentSource.GetLatestAppliedRequestForEnvironment("eph-no-tf", "Companion", 201).Returns(new DeploymentRequestApiModel
            {
                Project = "Companion",
                BuildUri = "vstfs:///Build/Build/77",
                DropLocation = @"\\drop\77",
                BuildNumber = "20260401.7"
            });
            _requestsPersistentSource.GetLatestAppliedRequestForEnvironment("eph-no-tf", "Companion", 2).Returns(new DeploymentRequestApiModel
            {
                Project = "Companion",
                BuildUri = "vstfs:///Build/Build/77",
                DropLocation = @"\\drop\77",
                BuildNumber = "20260401.7"
            });
            _requestsPersistentSource.GetProvisionedComponentIdsForEnvironment(11)
                .Returns(new[] { 1, 2, 3, 201, 2, 301 }, (IReadOnlyList<int>)null!);
            _requestsPersistentSource.GetProvisionedComponentDefinitionsForEnvironment(11)
                .Returns(new (ProjectApiModel, ComponentApiModel)[]
                {
                    (new ProjectApiModel { ProjectId = 10, ProjectName = "Traveler" }, new ComponentApiModel { ComponentId = null, ComponentName = "NoIdComp", ComponentType = ComponentType.Terraform }),
                    (new ProjectApiModel { ProjectId = 10, ProjectName = "Traveler" }, new ComponentApiModel { ComponentId = 1, ComponentName = "DupComp", ComponentType = ComponentType.Terraform }),
                    (new ProjectApiModel { ProjectId = 20, ProjectName = "Companion" }, new ComponentApiModel { ComponentId = 301, ComponentName = "PinnedComp301", ComponentType = ComponentType.Terraform })
                });
            _environmentsPersistentSource.GetMappedProjects("eph-no-tf").Returns(new[]
            {
                new ProjectApiModel { ProjectId = 10, ProjectName = "Traveler" },
                new ProjectApiModel { ProjectId = 20, ProjectName = "Companion" }
            });
            var noTplWithProjOk = _controller.Teardown("eph-no-tf", null) as OkObjectResult;
            Assert.IsNotNull(noTplWithProjOk);
            Assert.AreEqual(3, ((EphemeralEnvironmentTeardownResponseApiModel)noTplWithProjOk.Value!).DestroyRequestIds.Count);
            var noTplWithNullDurableOk = _controller.Teardown("eph-no-tf", null) as OkObjectResult;
            Assert.IsNotNull(noTplWithNullDurableOk);

            // When GetProvisionedProjectsForComponent includes a project that no longer owns the component (even alongside a still-mapped project), fail closed to TeardownFailed (409 Conflict)
            _requestsPersistentSource.GetProvisionedComponentIdsForEnvironment(11).Returns(new[] { 201 });
            _requestsPersistentSource.GetProvisionedProjectsForComponent(11, 201).Returns(new[] { "Companion", "RemovedProject" });
            Assert.IsInstanceOfType<ConflictObjectResult>(_controller.Teardown("eph-no-tf", null));
            _requestsPersistentSource.GetProvisionedComponentIdsForEnvironment(11).Returns((IReadOnlyList<int>)null!);
            _environmentsPersistentSource.GetMappedProjects("eph-no-tf").Returns(Array.Empty<ProjectApiModel>());
            Assert.IsNotNull(noTfDeleteOk);
            var noTfDeleteResponse = (EphemeralEnvironmentTeardownResponseApiModel)noTfDeleteOk.Value!;
            Assert.AreEqual(EphemeralEnvironmentStates.TornDown, noTfDeleteResponse.EphemeralState);
            Assert.IsTrue(noTfDeleteResponse.EnvironmentDeleted);

            // Templateless unprovisioned environment with mapped project does not queue destroy requests for all leaf components
            _environmentsPersistentSource.GetEnvironment("eph-unprov-no-tpl").Returns(new EnvironmentApiModel
            {
                EnvironmentId = 112,
                EnvironmentName = "eph-unprov-no-tpl",
                IsEphemeral = true,
                TemplateId = null,
                EphemeralState = EphemeralEnvironmentStates.Provisioning
            });
            _securityPrivilegesChecker.CanModifyEnvironment(_user, "eph-unprov-no-tpl").Returns(true);
            _environmentsPersistentSource.TryClaimEphemeralEnvironmentForTeardown(112, Arg.Is(false), Arg.Any<DateTime>(), out dummyPrevState, Arg.Any<bool>()).Returns(true);
            _environmentsPersistentSource.GetMappedProjects("eph-unprov-no-tpl").Returns(new[]
            {
                new ProjectApiModel { ProjectId = 10, ProjectName = "Traveler" }
            });
            var unprovNoTplOk = _controller.Teardown("eph-unprov-no-tpl", new EphemeralEnvironmentTeardownRequestApiModel()) as OkObjectResult;
            Assert.IsNotNull(unprovNoTplOk);
            Assert.AreEqual(0, ((EphemeralEnvironmentTeardownResponseApiModel)unprovNoTplOk.Value!).DestroyRequestIds.Count);

            // When RetainMetadataAfterTeardown = true (or DeleteEnvironmentInternal returns false), metadata is not deleted
            var noTfKeepOk = _controller.Teardown(
                "eph-no-tf",
                new EphemeralEnvironmentTeardownRequestApiModel { RetainMetadataAfterTeardown = true }) as OkObjectResult;
            Assert.IsNotNull(noTfKeepOk);
            var noTfKeepResponse = (EphemeralEnvironmentTeardownResponseApiModel)noTfKeepOk.Value!;
            Assert.IsFalse(noTfKeepResponse.EnvironmentDeleted);

            _environmentsPersistentSource.DeleteEnvironmentInternal(11).Returns(false);
            _requestsPersistentSource.GetRequestsWithStatus(
                DeploymentRequestStatus.Pending,
                DeploymentRequestStatus.Running,
                DeploymentRequestStatus.Requesting,
                DeploymentRequestStatus.WaitingConfirmation,
                false).Returns((IEnumerable<DeploymentRequestApiModel>)null!);
            _requestsPersistentSource.GetRequestsWithStatus(
                DeploymentRequestStatus.Confirmed,
                DeploymentRequestStatus.Paused,
                false).Returns((IEnumerable<DeploymentRequestApiModel>)null!);
            _requestsPersistentSource.GetRequestsWithStatus(
                DeploymentRequestStatus.Cancelling,
                false).Returns((IEnumerable<DeploymentRequestApiModel>)null!);
            var noTfDelFalseOk = _controller.Teardown("eph-no-tf", null) as OkObjectResult;
            Assert.IsNotNull(noTfDelFalseOk);
            Assert.IsFalse(((EphemeralEnvironmentTeardownResponseApiModel)noTfDelFalseOk.Value!).EnvironmentDeleted);

            // When an in-flight request is still Cancelling and there are no Terraform components, Teardown defers metadata deletion/retention and stays in TearingDown
            _requestsPersistentSource.GetRequestsWithStatus(
                DeploymentRequestStatus.Cancelling,
                false).Returns(new[]
            {
                new DeploymentRequestApiModel { Id = 888, EnvironmentName = "eph-no-tf", Status = DeploymentRequestStatus.Cancelling.ToString(), RequestDetails = "<DeploymentRequestDetail />" }
            });
            var noTfCancellingOk = _controller.Teardown("eph-no-tf", null) as OkObjectResult;
            Assert.IsNotNull(noTfCancellingOk);
            var noTfCancellingResp = (EphemeralEnvironmentTeardownResponseApiModel)noTfCancellingOk.Value!;
            Assert.AreEqual(EphemeralEnvironmentStates.TearingDown, noTfCancellingResp.EphemeralState);
            Assert.IsFalse(noTfCancellingResp.EnvironmentDeleted);
            var noTfCancellingRetainOk = _controller.Teardown(
                "eph-no-tf",
                new EphemeralEnvironmentTeardownRequestApiModel { RetainMetadataAfterTeardown = true }) as OkObjectResult;
            Assert.IsNotNull(noTfCancellingRetainOk);
            _environmentsPersistentSource.Received(1).UpdateEphemeralState(11, EphemeralEnvironmentStates.TearingDown, DateTime.UnixEpoch);
            _requestsPersistentSource.GetRequestsWithStatus(
                DeploymentRequestStatus.Cancelling,
                false).Returns((IEnumerable<DeploymentRequestApiModel>)null!);

            // Also cover SubmitComponentRequest fallback when a pinned component was hard-deleted from [deploy].[Component] and RequestService/DeployLibrary throws "Cannot find deployment component"
            _requestsPersistentSource.GetProvisionedComponentIdsForEnvironment(11).Returns(new[] { 301 });
            _requestsPersistentSource.GetProvisionedProjectsForComponent(11, 301).Returns(new[] { "Companion" });
            _requestsPersistentSource.GetProvisionedComponentDefinitionsForEnvironment(11)
                .Returns(new (ProjectApiModel, ComponentApiModel)[]
                {
                    (new ProjectApiModel { ProjectId = 20, ProjectName = "Companion" }, new ComponentApiModel
                    {
                        ComponentId = 301,
                        ComponentName = "DeletedPinnedComp",
                        ComponentType = ComponentType.Terraform,
                        TerraformSourceType = TerraformSourceType.Catalog,
                        TerraformTemplateName = "azure-postgresql-flexible-server",
                        TerraformTemplateVersion = "1.0.0"
                    })
                });
            _requestService
                .When(r => r.CreateRequest(Arg.Is<RequestDto>(dto => dto.Components.Contains("DeletedPinnedComp")), _user))
                .Do(ci =>
                {
                    var dto = ci.Arg<RequestDto>();
                    dto.RequestProperties.Add(new RequestProperty { PropertyName = "secret_token", PropertyValue = "s3cr3t", IsSensitive = true });
                    throw new InvalidOperationException("Cannot find deployment component called 'DeletedPinnedComp'");
                });
            _requestsPersistentSource.SubmitRequest(Arg.Any<Dorc.PersistentData.Model.DeploymentRequest>()).Returns(4004);
            var hardDeletedTeardownOk = _controller.Teardown("eph-no-tf", new EphemeralEnvironmentTeardownRequestApiModel
            {
                Reason = "cleanup"
            }) as OkObjectResult;
            Assert.IsNotNull(hardDeletedTeardownOk);
            CollectionAssert.AreEqual(new[] { 4004 }, ((EphemeralEnvironmentTeardownResponseApiModel)hardDeletedTeardownOk.Value!).DestroyRequestIds);
        }

        [TestMethod]
        public void ExtendTtl_ValidationAuthorizationAndSuccess_CoversAllBranches()
        {
            _environmentsPersistentSource.GetEnvironment("missing").Returns((EnvironmentApiModel)null!);
            Assert.IsInstanceOfType<NotFoundObjectResult>(_controller.ExtendTtl("missing", new EphemeralEnvironmentExtendTtlRequestApiModel { AdditionalMinutes = 30 }));

            _environmentsPersistentSource.GetEnvironment("non-eph").Returns(new EnvironmentApiModel { EnvironmentName = "non-eph", IsEphemeral = false });
            Assert.IsInstanceOfType<BadRequestObjectResult>(_controller.ExtendTtl("non-eph", new EphemeralEnvironmentExtendTtlRequestApiModel { AdditionalMinutes = 30 }));

            _environmentsPersistentSource.GetEnvironment("prod-eph").Returns(new EnvironmentApiModel { EnvironmentName = "prod-eph", IsEphemeral = true, EnvironmentIsProd = true });
            Assert.IsInstanceOfType<BadRequestObjectResult>(_controller.ExtendTtl("prod-eph", new EphemeralEnvironmentExtendTtlRequestApiModel { AdditionalMinutes = 30 }));

            var env = new EnvironmentApiModel
            {
                EnvironmentId = 15,
                EnvironmentName = "eph-ttl",
                IsEphemeral = true,
                EnvironmentIsProd = false,
                TemplateId = 8,
                ExpiresAtUtc = DateTime.UtcNow.AddMinutes(30)
            };
            _environmentsPersistentSource.GetEnvironment("eph-ttl").Returns(env);

            _securityPrivilegesChecker.CanModifyEnvironment(_user, "eph-ttl").Returns(false);
            Assert.IsInstanceOfType<ForbidResult>(_controller.ExtendTtl("eph-ttl", new EphemeralEnvironmentExtendTtlRequestApiModel { AdditionalMinutes = 30 }));

            _securityPrivilegesChecker.CanModifyEnvironment(_user, "eph-ttl").Returns(true);

            // Missing expiry parameters
            Assert.IsInstanceOfType<BadRequestObjectResult>(_controller.ExtendTtl("eph-ttl", null));
            Assert.IsInstanceOfType<BadRequestObjectResult>(_controller.ExtendTtl("eph-ttl", new EphemeralEnvironmentExtendTtlRequestApiModel { AdditionalMinutes = 0 }));

            // Explicit NewExpiresAtUtc with template MaxTtlMinutes
            _environmentTemplatesPersistentSource.GetTemplateById(8).Returns(new EnvironmentTemplateApiModel { Id = 8, MaxTtlMinutes = 360 });
            var explicitTarget = DateTime.UtcNow.AddMinutes(90);
            _environmentsPersistentSource.ExtendEphemeralTtl("eph-ttl", explicitTarget, 360, _user, 15, null).Returns(env);
            var okExplicit = _controller.ExtendTtl("eph-ttl", new EphemeralEnvironmentExtendTtlRequestApiModel { NewExpiresAtUtc = explicitTarget }) as OkObjectResult;
            Assert.IsNotNull(okExplicit);

            // AdditionalMinutes with future ExpiresAtUtc
            _environmentsPersistentSource.ExtendEphemeralTtl("eph-ttl", Arg.Any<DateTime>(), 360, _user, 15, 60).Returns(env);
            var okAdditionalFuture = _controller.ExtendTtl("eph-ttl", new EphemeralEnvironmentExtendTtlRequestApiModel { AdditionalMinutes = 60 }) as OkObjectResult;
            Assert.IsNotNull(okAdditionalFuture);

            // AdditionalMinutes with past/null ExpiresAtUtc and null TemplateId
            env.ExpiresAtUtc = DateTime.UtcNow.AddMinutes(-10);
            env.TemplateId = null;
            _environmentsPersistentSource.ExtendEphemeralTtl("eph-ttl", Arg.Any<DateTime>(), 1440, _user, 15, 60).Returns(env);
            var okAdditionalPast = _controller.ExtendTtl("eph-ttl", new EphemeralEnvironmentExtendTtlRequestApiModel { AdditionalMinutes = 60 }) as OkObjectResult;
            Assert.IsNotNull(okAdditionalPast);

            env.ExpiresAtUtc = null;
            var okAdditionalNull = _controller.ExtendTtl("eph-ttl", new EphemeralEnvironmentExtendTtlRequestApiModel { AdditionalMinutes = 60 }) as OkObjectResult;
            Assert.IsNotNull(okAdditionalNull);

            // Returns Conflict when environment was replaced/deleted before ExtendEphemeralTtl completed
            _environmentsPersistentSource.ExtendEphemeralTtl("eph-ttl", Arg.Any<DateTime>(), 1440, _user, 15, 60).Returns((EnvironmentApiModel?)null);
            Assert.IsInstanceOfType<ConflictObjectResult>(_controller.ExtendTtl("eph-ttl", new EphemeralEnvironmentExtendTtlRequestApiModel { AdditionalMinutes = 60 }));

            // Exception handling (ArgumentException and InvalidOperationException)
            _environmentsPersistentSource
                .When(x => x.ExtendEphemeralTtl("eph-ttl", Arg.Any<DateTime>(), Arg.Any<int>(), _user, 15, 60))
                .Do(_ => throw new InvalidOperationException("Cannot extend TTL"));
            Assert.IsInstanceOfType<BadRequestObjectResult>(_controller.ExtendTtl("eph-ttl", new EphemeralEnvironmentExtendTtlRequestApiModel { AdditionalMinutes = 60 }));
        }

        private static TerraformTemplateManifest BuildManifest(
            string name,
            string category,
            params TerraformTemplateParameter[] parameters)
        {
            return new TerraformTemplateManifest(
                Name: name,
                Version: "1.0.0",
                Source: new TerraformTemplateSource("git", "https://github.com/org/modules.git", "v1.0.0"),
                Parameters: parameters,
                Outputs: Array.Empty<TerraformTemplateOutput>(),
                Description: name,
                Tags: Array.Empty<string>(),
                Category: category,
                RequiredProviders: new Dictionary<string, string>(),
                RequiredTerraformVersion: ">= 1.5.0",
                Owner: "platform",
                Deprecated: false,
                DeprecationReason: null);
        }
    }
}
