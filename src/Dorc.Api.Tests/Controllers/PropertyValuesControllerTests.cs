using Dorc.Api.Controllers;
using Dorc.Api.Interfaces;
using Dorc.ApiModel;
using Dorc.ApiModel.MonitorRunnerApi;
using Dorc.Core.Interfaces;
using Dorc.Core.VariableResolution;
using Dorc.PersistentData.Sources.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using NSubstitute;
using System.Security.Claims;

namespace Dorc.Api.Tests.Controllers
{
    [TestClass]
    public class PropertyValuesControllerTests
    {
        private IPropertyValuesService _propertyValuesService = null!;
        private IPropertyValuesPersistentSource _propertyValuesPersistentSource = null!;
        private IVariableResolver _variableResolver = null!;
        private IEnvironmentsPersistentSource _environmentsPersistentSource = null!;
        private IVariableScopeOptionsResolver _variableScopeOptionsResolver = null!;
        private IDirectorySearcherFactory _directorySearcherFactory = null!;
        private IActiveDirectorySearcher _directorySearcher = null!;
        private ILogger<PropertyValuesController> _logger = null!;
        private IPropertyResolutionTracer _propertyResolutionTracer = null!;
        private ISecurityPrivilegesChecker _securityPrivilegesChecker = null!;
        private PropertyValuesController _controller = null!;

        [TestInitialize]
        public void Setup()
        {
            _propertyValuesService = Substitute.For<IPropertyValuesService>();
            _propertyValuesPersistentSource = Substitute.For<IPropertyValuesPersistentSource>();
            _variableResolver = Substitute.For<IVariableResolver>();
            _environmentsPersistentSource = Substitute.For<IEnvironmentsPersistentSource>();
            _variableScopeOptionsResolver = Substitute.For<IVariableScopeOptionsResolver>();
            _directorySearcherFactory = Substitute.For<IDirectorySearcherFactory>();
            _directorySearcher = Substitute.For<IActiveDirectorySearcher>();
            _directorySearcherFactory.GetEntraSearcher().Returns(_directorySearcher);
            _logger = Substitute.For<ILogger<PropertyValuesController>>();
            _propertyResolutionTracer = Substitute.For<IPropertyResolutionTracer>();
            _securityPrivilegesChecker = Substitute.For<ISecurityPrivilegesChecker>();

            _controller = new PropertyValuesController(
                _propertyValuesService,
                _propertyValuesPersistentSource,
                _variableResolver,
                _environmentsPersistentSource,
                _variableScopeOptionsResolver,
                _directorySearcherFactory,
                _logger,
                _propertyResolutionTracer,
                _securityPrivilegesChecker)
            {
                ControllerContext = new ControllerContext
                {
                    HttpContext = new DefaultHttpContext
                    {
                        User = new ClaimsPrincipal(new ClaimsIdentity(new[]
                        {
                            new Claim(ClaimTypes.Name, "TestUser")
                        }))
                    }
                }
            };
        }

        [TestMethod]
        public void GetScopeOptions_WhenNormalizedTagCollisionThrowsInvalidOperationException_ReturnsBadRequestWithMessage()
        {
            // Arrange
            const string envName = "Endur DV 10";
            const string collisionMessage =
                "Tags 'A B' and 'A_B' both produce deployment variable 'ServerNames_A_B'. Rename one tag before deployment.";
            var environment = new EnvironmentApiModel
            {
                EnvironmentId = 42,
                EnvironmentName = envName,
                Details = new EnvironmentDetailsApiModel()
            };
            _environmentsPersistentSource.GetEnvironment(envName, Arg.Any<ClaimsPrincipal>())
                .Returns(environment);
            _environmentsPersistentSource.GetEnvironmentOwnerIds(42)
                .Returns(new List<string>());
            _variableScopeOptionsResolver
                .When(r => r.SetPropertyValues(_variableResolver, environment))
                .Do(_ => throw new InvalidOperationException(collisionMessage));

            // Act
            var result = _controller.GetScopeOptions(envName);

            // Assert
            Assert.IsInstanceOfType(result, typeof(BadRequestObjectResult));
            var badRequest = (BadRequestObjectResult)result;
            Assert.AreEqual(StatusCodes.Status400BadRequest, badRequest.StatusCode);
            Assert.AreEqual(collisionMessage, badRequest.Value);
        }

        [TestMethod]
        [DataRow(null)]
        [DataRow("")]
        [DataRow("   ")]
        public void GetScopeOptions_WhenDefaultScope_PopulatesGlobalPropertyValues(string? scope)
        {
            _environmentsPersistentSource.GetEnvironment(scope, Arg.Any<ClaimsPrincipal>())
                .Returns((EnvironmentApiModel)null!);
            _variableResolver.LocalProperties().Returns(new Dictionary<string, VariableValue>
            {
                ["ServerNames_STP_Strategies"] = new VariableValue { Value = "STP-SRV01", Type = typeof(string) }
            });

            var result = _controller.GetScopeOptions(scope);

            _variableScopeOptionsResolver.Received(1).SetGlobalPropertyValues(_variableResolver, Arg.Any<ClaimsPrincipal>());
            Assert.IsInstanceOfType(result, typeof(OkObjectResult));
            var okResult = (OkObjectResult)result;
            Assert.IsInstanceOfType(okResult.Value, typeof(List<PropertyValueScopeOptionApiModel>));
            var options = (List<PropertyValueScopeOptionApiModel>)okResult.Value;
            Assert.HasCount(1, options);
            Assert.AreEqual("$ServerNames_STP_Strategies$", options[0].ValueOption);
            Assert.AreEqual("STP-SRV01", options[0].SampleResolvedValue);
        }

        [TestMethod]
        public void GetScopeOptions_WhenNonEmptyMissingEnvironment_DoesNotCallGlobalOrEnvironmentResolver()
        {
            _environmentsPersistentSource.GetEnvironment("UnknownEnv", Arg.Any<ClaimsPrincipal>())
                .Returns((EnvironmentApiModel)null!);
            _variableResolver.LocalProperties().Returns(new Dictionary<string, VariableValue>());

            var result = _controller.GetScopeOptions("UnknownEnv");

            _variableScopeOptionsResolver.DidNotReceiveWithAnyArgs().SetGlobalPropertyValues(default!, default!);
            _variableScopeOptionsResolver.DidNotReceiveWithAnyArgs().SetPropertyValues(default!, default!);
            Assert.IsInstanceOfType(result, typeof(OkObjectResult));
        }

        [TestMethod]
        public void GetScopeOptions_WhenValid_ReturnsOkWithScopeOptions()
        {
            // Arrange
            const string envName = "Endur DV 10";
            var environment = new EnvironmentApiModel
            {
                EnvironmentId = 42,
                EnvironmentName = envName,
                Details = new EnvironmentDetailsApiModel()
            };
            _environmentsPersistentSource.GetEnvironment(envName, Arg.Any<ClaimsPrincipal>())
                .Returns(environment);
            _environmentsPersistentSource.GetEnvironmentOwnerIds(42)
                .Returns(new List<string>());
            _variableResolver.LocalProperties().Returns(new Dictionary<string, VariableValue>
            {
                ["ServerNames_A_B"] = new VariableValue { Value = "GMDV-ENDAPP30", Type = typeof(string) }
            });

            // Act
            var result = _controller.GetScopeOptions(envName);

            // Assert
            Assert.IsInstanceOfType(result, typeof(OkObjectResult));
            var okResult = (OkObjectResult)result;
            var options = okResult.Value as List<PropertyValueScopeOptionApiModel>;
            Assert.IsNotNull(options);
            Assert.AreEqual(1, options.Count);
            Assert.AreEqual("$ServerNames_A_B$", options[0].ValueOption);
            Assert.AreEqual("GMDV-ENDAPP30", options[0].SampleResolvedValue);
        }

        [TestMethod]
        public void RefDataScopedAndSearchPropertyValuesControllers_SetUserEditableForAdminAndPowerUser()
        {
            var propSource = Substitute.For<IPropertyValuesPersistentSource>();
            var envSource = Substitute.For<IEnvironmentsPersistentSource>();
            var roleChecker = Substitute.For<Dorc.PersistentData.IRolePrivilegesChecker>();
            var user = new ClaimsPrincipal(new ClaimsIdentity("TestAuth"));

            var scopedController = new RefDataScopedPropertyValuesController(propSource, envSource, roleChecker)
            {
                ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = user } }
            };
            var searchController = new RefDataSearchPropertyValuesController(propSource, roleChecker)
            {
                ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = user } }
            };

            // Validation error branches on Scoped controller
            Assert.AreEqual(StatusCodes.Status400BadRequest, (scopedController.Put(new PagedDataOperators(), "") as ObjectResult)?.StatusCode);
            envSource.EnvironmentExists("MissingEnv").Returns(false);
            Assert.AreEqual(StatusCodes.Status400BadRequest, (scopedController.Put(new PagedDataOperators(), "MissingEnv") as ObjectResult)?.StatusCode);

            envSource.EnvironmentExists("DevEnv").Returns(true);
            var envModel = new EnvironmentApiModel { EnvironmentName = "DevEnv" };
            envSource.GetEnvironment("DevEnv", user).Returns(envModel);

            GetScopedPropertyValuesResponseDto BuildResponse() => new GetScopedPropertyValuesResponseDto
            {
                Items = new List<FlatPropertyValueApiModel>
                {
                    new FlatPropertyValueApiModel { Property = "GlobalProp", PropertyValueScope = null, UserEditable = false },
                    new FlatPropertyValueApiModel { Property = "ScopedProp", PropertyValueScope = "DevEnv", UserEditable = false }
                }
            };

            // 1. Regular user (neither Admin nor PowerUser)
            roleChecker.IsAdmin(user).Returns(false);
            roleChecker.IsPowerUser(user).Returns(false);
            propSource.GetPropertyValuesForScopeByPage(50, 1, Arg.Any<PagedDataOperators>(), envModel, user).Returns(_ => BuildResponse());
            propSource.GetPropertyValuesForSearchValueByPage(50, 1, Arg.Any<PagedDataOperators>(), user).Returns(_ => BuildResponse());

            var regularScoped = (scopedController.Put(new PagedDataOperators(), "DevEnv") as ObjectResult)?.Value as GetScopedPropertyValuesResponseDto;
            var regularSearch = (searchController.Put(new PagedDataOperators()) as ObjectResult)?.Value as GetScopedPropertyValuesResponseDto;
            Assert.IsFalse(regularScoped!.Items[0].UserEditable);
            Assert.IsFalse(regularScoped.Items[1].UserEditable);
            Assert.IsFalse(regularSearch!.Items[0].UserEditable);
            Assert.IsFalse(regularSearch.Items[1].UserEditable);

            // 2. PowerUser (Global editable, Scoped not editable unless granted by SP)
            roleChecker.IsAdmin(user).Returns(false);
            roleChecker.IsPowerUser(user).Returns(true);
            var powerScoped = (scopedController.Put(new PagedDataOperators(), "DevEnv") as ObjectResult)?.Value as GetScopedPropertyValuesResponseDto;
            var powerSearch = (searchController.Put(new PagedDataOperators()) as ObjectResult)?.Value as GetScopedPropertyValuesResponseDto;
            Assert.IsTrue(powerScoped!.Items[0].UserEditable);
            Assert.IsFalse(powerScoped.Items[1].UserEditable);
            Assert.IsTrue(powerSearch!.Items[0].UserEditable);
            Assert.IsFalse(powerSearch.Items[1].UserEditable);

            // 3. Admin (All editable)
            roleChecker.IsAdmin(user).Returns(true);
            roleChecker.IsPowerUser(user).Returns(false);
            var adminScoped = (scopedController.Put(new PagedDataOperators(), "DevEnv") as ObjectResult)?.Value as GetScopedPropertyValuesResponseDto;
            var adminSearch = (searchController.Put(new PagedDataOperators()) as ObjectResult)?.Value as GetScopedPropertyValuesResponseDto;
            Assert.IsTrue(adminScoped!.Items[0].UserEditable);
            Assert.IsTrue(adminScoped.Items[1].UserEditable);
            Assert.IsTrue(adminSearch!.Items[0].UserEditable);
            Assert.IsTrue(adminSearch.Items[1].UserEditable);
        }

        [TestMethod]
        public void PropertiesController_AllowsPostAndImportForEnvWriters_RestrictsPutAndDeleteToPropertyModifiers()
        {
            var propService = Substitute.For<IPropertiesService>();
            var securityChecker = Substitute.For<ISecurityPrivilegesChecker>();
            var roleChecker = Substitute.For<Dorc.PersistentData.IRolePrivilegesChecker>();
            var propValuesSource = Substitute.For<IPropertyValuesPersistentSource>();
            var envSource = Substitute.For<IEnvironmentsPersistentSource>();
            var user = new ClaimsPrincipal(new ClaimsIdentity("TestAuth"));

            var controller = new PropertiesController(propService, securityChecker, roleChecker, propValuesSource, envSource)
            {
                ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = user } }
            };

            var props = new List<PropertyApiModel> { new PropertyApiModel { Name = "MySecret", Secure = true } };
            var importRows = new List<PropertyImportCsvDto> { new PropertyImportCsvDto { PropertyName = "MySecret", Value = "val", IsSecure = true } };
            IDictionary<string, PropertyApiModel> updateDict = new Dictionary<string, PropertyApiModel>
            {
                ["MySecret"] = new PropertyApiModel { Name = "MySecret", Secure = true }
            };
            var deleteNames = new List<string> { "MySecret" };
            var okResponses = new List<Response> { new Response { Status = "success" } };

            propService.PostProperties(props, user).Returns(okResponses);
            propService.ImportProperties(importRows, user).Returns(okResponses);
            propService.PutProperties(updateDict, user).Returns(okResponses);
            propService.DeleteProperties(deleteNames, user).Returns(okResponses);

            // 1. User without CanModifyProperty and without any editable environment (or null list) -> 403 on Post, Import, Put, Delete
            securityChecker.CanModifyProperty(user).Returns(false);
            roleChecker.IsAdmin(user).Returns(false);
            envSource.GetEnvironments(user).Returns((IEnumerable<EnvironmentApiModel>)null!);
            Assert.AreEqual(StatusCodes.Status403Forbidden, (controller.PostProperties(props) as ObjectResult)?.StatusCode);

            envSource.GetEnvironments(user).Returns(new List<EnvironmentApiModel>
            {
                new EnvironmentApiModel { EnvironmentName = "ReadOnlyEnv", UserEditable = false }
            });

            Assert.AreEqual(StatusCodes.Status403Forbidden, (controller.PostProperties(props) as ObjectResult)?.StatusCode);
            Assert.AreEqual(StatusCodes.Status403Forbidden, (controller.ImportProperties(importRows) as ObjectResult)?.StatusCode);
            Assert.AreEqual(StatusCodes.Status403Forbidden, (controller.PutProperty(updateDict) as ObjectResult)?.StatusCode);
            Assert.AreEqual(StatusCodes.Status403Forbidden, (controller.DeleteProperties(deleteNames) as ObjectResult)?.StatusCode);

            // 2. User without CanModifyProperty but WITH an editable environment (Env Write / Env Owner) -> 200 on Post & Import, 403 on Put & Delete
            envSource.GetEnvironments(user).Returns(new List<EnvironmentApiModel>
            {
                new EnvironmentApiModel { EnvironmentName = "ReadOnlyEnv", UserEditable = false },
                new EnvironmentApiModel { EnvironmentName = "WritableEnv", UserEditable = true }
            });

            Assert.AreEqual(StatusCodes.Status200OK, (controller.PostProperties(props) as ObjectResult)?.StatusCode);
            Assert.AreEqual(StatusCodes.Status200OK, (controller.ImportProperties(importRows) as ObjectResult)?.StatusCode);
            Assert.AreEqual(StatusCodes.Status403Forbidden, (controller.PutProperty(updateDict) as ObjectResult)?.StatusCode);
            Assert.AreEqual(StatusCodes.Status403Forbidden, (controller.DeleteProperties(deleteNames) as ObjectResult)?.StatusCode);

            // 3. PowerUser (CanModifyProperty == true, IsAdmin == false) -> 200 on Post, Import, Put; 403 on Delete when property has values, 200 when no values
            securityChecker.CanModifyProperty(user).Returns(true);
            Assert.AreEqual(StatusCodes.Status200OK, (controller.PostProperties(props) as ObjectResult)?.StatusCode);
            Assert.AreEqual(StatusCodes.Status200OK, (controller.ImportProperties(importRows) as ObjectResult)?.StatusCode);
            Assert.AreEqual(StatusCodes.Status200OK, (controller.PutProperty(updateDict) as ObjectResult)?.StatusCode);

            propValuesSource.GetPropertyValuesByName("MySecret").Returns(new[] { new PropertyValueDto { Id = 1 } });
            Assert.AreEqual(StatusCodes.Status403Forbidden, (controller.DeleteProperties(deleteNames) as ObjectResult)?.StatusCode);

            propValuesSource.GetPropertyValuesByName("MySecret").Returns(Array.Empty<PropertyValueDto>());
            Assert.AreEqual(StatusCodes.Status200OK, (controller.DeleteProperties(deleteNames) as ObjectResult)?.StatusCode);

            propValuesSource.GetPropertyValuesByName("MySecret").Returns((PropertyValueDto[])null!);
            Assert.AreEqual(StatusCodes.Status200OK, (controller.DeleteProperties(deleteNames) as ObjectResult)?.StatusCode);

            // 4. Admin (IsAdmin == true) -> 200 on Delete even when property has values
            roleChecker.IsAdmin(user).Returns(true);
            propValuesSource.GetPropertyValuesByName("MySecret").Returns(new[] { new PropertyValueDto { Id = 1 } });
            Assert.AreEqual(StatusCodes.Status200OK, (controller.DeleteProperties(deleteNames) as ObjectResult)?.StatusCode);
        }

        [TestMethod]
        public void ResolvePreview_ValidatesRequestAndEnvironmentAuthorization_AndReturnsTraces()
        {
            // 1. Null request -> 400 BadRequest
            var nullActionResult = _controller.ResolvePreview(null!);
            Assert.IsInstanceOfType(nullActionResult, typeof(BadRequestObjectResult));
            var nullRes = (BadRequestObjectResult)nullActionResult;
            Assert.AreEqual(StatusCodes.Status400BadRequest, nullRes.StatusCode);

            // 2. Non-existent environment -> 400 BadRequest
            _environmentsPersistentSource.EnvironmentExists("MissingEnv").Returns(false);
            var missingEnvReq = new PropertyResolutionPreviewRequestApiModel
            {
                EnvironmentName = "  MissingEnv  ",
                PropertyNames = new List<string> { "PropA" }
            };
            var missingEnvActionResult = _controller.ResolvePreview(missingEnvReq);
            Assert.IsInstanceOfType(missingEnvActionResult, typeof(BadRequestObjectResult));
            var missingEnvRes = (BadRequestObjectResult)missingEnvActionResult;
            Assert.AreEqual(StatusCodes.Status400BadRequest, missingEnvRes.StatusCode);

            // 3. Unauthorized environment (GetEnvironment returns null or existing denied environment) -> 403 Forbidden
            _environmentsPersistentSource.EnvironmentExists("RestrictedEnv").Returns(true);
            _environmentsPersistentSource.GetEnvironment("RestrictedEnv", Arg.Any<ClaimsPrincipal>()).Returns((EnvironmentApiModel)null!);
            var restrictedReq = new PropertyResolutionPreviewRequestApiModel
            {
                EnvironmentName = "RestrictedEnv",
                PropertyNames = new List<string> { "PropA" }
            };
            var restrictedActionResult = _controller.ResolvePreview(restrictedReq);
            Assert.IsInstanceOfType(restrictedActionResult, typeof(StatusCodeResult));
            var restrictedRes = (StatusCodeResult)restrictedActionResult;
            Assert.AreEqual(StatusCodes.Status403Forbidden, restrictedRes.StatusCode);

            var deniedEnvModel = new EnvironmentApiModel
            {
                EnvironmentId = 11,
                EnvironmentName = "DeniedEnv",
                UserEditable = false,
                IsOwner = false
            };
            _environmentsPersistentSource.EnvironmentExists("DeniedEnv").Returns(true);
            _environmentsPersistentSource.GetEnvironment("DeniedEnv", Arg.Any<ClaimsPrincipal>()).Returns(deniedEnvModel);
            _securityPrivilegesChecker.CanModifyEnvironment(Arg.Any<ClaimsPrincipal>(), "DeniedEnv").Returns(false);
            _securityPrivilegesChecker.IsEnvironmentOwnerOrAdmin(Arg.Any<ClaimsPrincipal>(), "DeniedEnv").Returns(false);
            _securityPrivilegesChecker.CanReadSecrets(Arg.Any<ClaimsPrincipal>(), "DeniedEnv").Returns(false);
            var deniedReq = new PropertyResolutionPreviewRequestApiModel
            {
                EnvironmentName = "DeniedEnv",
                PropertyNames = new List<string> { "PropA" }
            };
            var deniedActionResult = _controller.ResolvePreview(deniedReq);
            Assert.IsInstanceOfType(deniedActionResult, typeof(StatusCodeResult));
            Assert.AreEqual(StatusCodes.Status403Forbidden, ((StatusCodeResult)deniedActionResult).StatusCode);

            // 4. Authorized environment variants -> enriches owner emails and returns traces
            var envModel = new EnvironmentApiModel
            {
                EnvironmentId = 10,
                EnvironmentName = "DevEnv",
                UserEditable = true,
                Details = new EnvironmentDetailsApiModel()
            };
            _environmentsPersistentSource.EnvironmentExists("DevEnv").Returns(true);
            _environmentsPersistentSource.GetEnvironment("DevEnv", Arg.Any<ClaimsPrincipal>()).Returns(envModel);
            _environmentsPersistentSource.GetEnvironmentOwnerIds(10).Returns(new List<string>());

            var expectedTraces = new List<PropertyResolutionTraceApiModel>
            {
                new PropertyResolutionTraceApiModel { PropertyName = "PropA", ResolvedValueJson = "\"ValA\"" }
            };
            _propertyResolutionTracer.TraceProperties(Arg.Any<PropertyResolutionPreviewRequestApiModel>(), envModel, Arg.Any<ClaimsPrincipal>())
                .Returns(expectedTraces);

            var validEnvReq = new PropertyResolutionPreviewRequestApiModel
            {
                EnvironmentName = "DevEnv",
                PropertyNames = new List<string> { "PropA" }
            };
            var validEnvActionResult = _controller.ResolvePreview(validEnvReq);
            Assert.IsInstanceOfType(validEnvActionResult, typeof(OkObjectResult));
            var validEnvRes = (OkObjectResult)validEnvActionResult;
            Assert.AreEqual(expectedTraces, validEnvRes.Value);

            envModel.UserEditable = false;
            envModel.IsOwner = true;
            Assert.IsInstanceOfType(_controller.ResolvePreview(validEnvReq), typeof(OkObjectResult));

            envModel.IsOwner = false;
            _securityPrivilegesChecker.CanModifyEnvironment(Arg.Any<ClaimsPrincipal>(), "DevEnv").Returns(true);
            Assert.IsInstanceOfType(_controller.ResolvePreview(validEnvReq), typeof(OkObjectResult));

            _securityPrivilegesChecker.CanModifyEnvironment(Arg.Any<ClaimsPrincipal>(), "DevEnv").Returns(false);
            _securityPrivilegesChecker.IsEnvironmentOwnerOrAdmin(Arg.Any<ClaimsPrincipal>(), "DevEnv").Returns(true);
            Assert.IsInstanceOfType(_controller.ResolvePreview(validEnvReq), typeof(OkObjectResult));

            _securityPrivilegesChecker.IsEnvironmentOwnerOrAdmin(Arg.Any<ClaimsPrincipal>(), "DevEnv").Returns(false);
            _securityPrivilegesChecker.CanReadSecrets(Arg.Any<ClaimsPrincipal>(), "DevEnv").Returns(true);
            Assert.IsInstanceOfType(_controller.ResolvePreview(validEnvReq), typeof(OkObjectResult));

            // 5. Global-only preview (no environmentName) -> passes null environment to tracer
            _propertyResolutionTracer.TraceProperties(Arg.Any<PropertyResolutionPreviewRequestApiModel>(), null, Arg.Any<ClaimsPrincipal>())
                .Returns(expectedTraces);
            var globalReq = new PropertyResolutionPreviewRequestApiModel
            {
                EnvironmentName = "",
                PropertyNames = new List<string> { "PropA" }
            };
            var globalActionResult = _controller.ResolvePreview(globalReq);
            Assert.IsInstanceOfType(globalActionResult, typeof(OkObjectResult));
            var globalRes = (OkObjectResult)globalActionResult;
            Assert.AreEqual(expectedTraces, globalRes.Value);

            // 6. GetScopeOptions with empty/default scope calls SetGlobalPropertyValues
            _variableResolver.LocalProperties().Returns(new Dictionary<string, VariableValue>
            {
                ["ServerNames_STP_Strategies"] = new VariableValue { Value = "srv-01", Type = typeof(object) }
            });
            var defaultScopeResult = _controller.GetScopeOptions("");
            Assert.IsInstanceOfType(defaultScopeResult, typeof(OkObjectResult));
            _variableScopeOptionsResolver.Received(1).SetGlobalPropertyValues(_variableResolver, Arg.Any<ClaimsPrincipal>());
        }

        [TestMethod]
        public void GetScopeOptions_FiltersOutNullEmptyAndSemicolonCompositeKeys()
        {
            const string envName = "Endur DV 02";
            var environment = new EnvironmentApiModel
            {
                EnvironmentId = 42,
                EnvironmentName = envName,
                Details = new EnvironmentDetailsApiModel()
            };
            _environmentsPersistentSource.GetEnvironment(envName, Arg.Any<ClaimsPrincipal>())
                .Returns(environment);
            _environmentsPersistentSource.GetEnvironmentOwnerIds(42)
                .Returns(new List<string>());
            _variableResolver.LocalProperties().Returns(new Dictionary<string, VariableValue>
            {
                ["ServerNames_TagA;TagB"] = new VariableValue { Value = "GMDV-SRV01", Type = typeof(string) },
                ["DatabaseServer_One;Two"] = new VariableValue { Value = "GMDV-DB01", Type = typeof(string) },
                ["   "] = new VariableValue { Value = "BlankKey", Type = typeof(string) },
                ["NullValueKey"] = null!,
                ["EmptyStringValueKey"] = new VariableValue { Value = string.Empty, Type = typeof(string) },
                ["ServerNames_TagA"] = new VariableValue { Value = "GMDV-SRV01", Type = typeof(string) },
                ["NonStringValueKey"] = new VariableValue { Value = 123, Type = typeof(int) }
            });

            var result = _controller.GetScopeOptions(envName);

            Assert.IsInstanceOfType(result, typeof(OkObjectResult));
            var okResult = (OkObjectResult)result;
            Assert.IsInstanceOfType(okResult.Value, typeof(List<PropertyValueScopeOptionApiModel>));
            var options = (List<PropertyValueScopeOptionApiModel>)okResult.Value!;
            Assert.AreEqual(4, options.Count);
            CollectionAssert.AreEquivalent(
                new[]
                {
                    "$ServerNames_TagA;TagB$",
                    "$DatabaseServer_One;Two$",
                    "$ServerNames_TagA$",
                    "$NonStringValueKey$"
                },
                options.Select(o => o.ValueOption).ToArray());
        }

        [TestMethod]
        [DataRow(null)]
        [DataRow("")]
        [DataRow("   ")]
        public void GetWhereUsed_WhenPropertyNameIsNullOrWhitespace_ReturnsBadRequest(string? propertyName)
        {
            var result = _controller.GetWhereUsed(propertyName!);

            Assert.IsInstanceOfType(result, typeof(BadRequestObjectResult));
            var badRequest = (BadRequestObjectResult)result;
            Assert.AreEqual(StatusCodes.Status400BadRequest, badRequest.StatusCode);
        }

        [TestMethod]
        public void GetWhereUsed_MatchesDollarTokensAndIncludesSecureDependentsWithPermissionBasedRedaction()
        {
            var longPrefix = new string('x', 130);
            _securityPrivilegesChecker.CanReadSecrets(Arg.Any<ClaimsPrincipal>(), string.Empty).Returns(false);
            _securityPrivilegesChecker.CanModifyProperty(Arg.Any<ClaimsPrincipal>()).Returns(false);
            _securityPrivilegesChecker.CanReadSecrets(Arg.Any<ClaimsPrincipal>(), "Endur DV 01").Returns(true);
            _securityPrivilegesChecker.CanReadSecrets(Arg.Any<ClaimsPrincipal>(), "Endur DV 02").Returns(false);

            _propertyValuesPersistentSource.GetSecurePropertyValues(true).Returns(new[]
            {
                new PropertyValueDto
                {
                    Property = new PropertyApiModel { Name = "SecretConnection", Secure = true },
                    PropertyValueFilter = null!,
                    Value = "Server=$DbHost$;Password=secret"
                },
                new PropertyValueDto
                {
                    Property = new PropertyApiModel { Name = "SecondDefaultSecret", Secure = true },
                    PropertyValueFilter = "   ",
                    Value = "Host=$DbHost$;Key=abc"
                },
                new PropertyValueDto
                {
                    Property = new PropertyApiModel { Name = "ReadableEnvSecretUrl", Secure = true },
                    PropertyValueFilter = "Endur DV 01",
                    Value = "https://$DbHost$/readable-secret"
                },
                new PropertyValueDto
                {
                    Property = new PropertyApiModel { Name = "EnvSecretUrl", Secure = true },
                    PropertyValueFilter = "Endur DV 02",
                    Value = "https://$DbHost$/secret"
                },
                new PropertyValueDto
                {
                    Property = new PropertyApiModel { Name = "UnrelatedSecret", Secure = true },
                    PropertyValueFilter = "Endur DV 01",
                    Value = "https://$OtherHost$/secret"
                },
                new PropertyValueDto
                {
                    Property = null!,
                    Value = "https://$DbHost$/ignored"
                }
            });

            _propertyValuesPersistentSource.GetGlobalProperties().Returns(new[]
            {
                new PropertyValueDto
                {
                    Property = new PropertyApiModel { Name = "ConnectionString", Secure = false },
                    Value = "Server=$DbHost$;Database=Main"
                },
                new PropertyValueDto
                {
                    Property = new PropertyApiModel { Name = "FallbackDollarWithUnclosedLiteral", Secure = false },
                    Value = "Cost=$50;Server=$DbHost$"
                },
                new PropertyValueDto
                {
                    Property = new PropertyApiModel { Name = "BackupEndpointBraceSyntaxIgnored", Secure = false },
                    Value = "https://${ dbhost }:8443/api"
                },
                new PropertyValueDto
                {
                    Property = new PropertyApiModel { Name = "SecretConnection", Secure = true },
                    Value = "Server=$DbHost$;Password=secret"
                },
                new PropertyValueDto
                {
                    Property = new PropertyApiModel { Name = "EscapedLiteral", Secure = false },
                    Value = "Literal $$DbHost$$ should not match"
                },
                new PropertyValueDto
                {
                    Property = new PropertyApiModel { Name = "AdjacentTokensSeparatedByEscapedDollar", Secure = false },
                    Value = "$Db$$Host$"
                },
                new PropertyValueDto
                {
                    Property = new PropertyApiModel { Name = "UnrelatedProperty", Secure = false },
                    Value = "Server=$DbHostSecondary$"
                },
                new PropertyValueDto
                {
                    Property = new PropertyApiModel { Name = "", Secure = false },
                    Value = "Server=$DbHost$"
                },
                new PropertyValueDto
                {
                    Property = null!,
                    Value = "Server=$DbHost$"
                }
            });

            _propertyValuesPersistentSource.GetPropertyValuesForSearchValueByPage(
                    Arg.Any<int>(),
                    Arg.Any<int>(),
                    Arg.Any<PagedDataOperators>(),
                    Arg.Any<ClaimsPrincipal>())
                .Returns(new GetScopedPropertyValuesResponseDto
                {
                    Items = new List<FlatPropertyValueApiModel>
                    {
                        new FlatPropertyValueApiModel
                        {
                            Property = "ConnectionString",
                            PropertyValueScope = null!,
                            PropertyValue = "Server=$DbHost$;Database=Main",
                            Secure = false
                        },
                        new FlatPropertyValueApiModel
                        {
                            Property = "EnvServiceUrl",
                            PropertyValueScope = "Endur DV 02",
                            PropertyValue = $"{longPrefix}://$DBHOST$/service",
                            Secure = false
                        },
                        new FlatPropertyValueApiModel
                        {
                            Property = "EnvSecretUrl",
                            PropertyValueScope = "Endur DV 02",
                            PropertyValue = string.Empty,
                            Secure = true
                        }
                    }
                });

            var result = _controller.GetWhereUsed("  DbHost  ");

            Assert.IsInstanceOfType(result, typeof(OkObjectResult));
            var okResult = (OkObjectResult)result;
            Assert.IsInstanceOfType(okResult.Value, typeof(List<PropertyUsageReferenceApiModel>));
            var references = (List<PropertyUsageReferenceApiModel>)okResult.Value!;
            Assert.AreEqual(7, references.Count);

            Assert.AreEqual("ConnectionString", references[0].PropertyName);
            Assert.AreEqual("Default", references[0].Scope);
            Assert.AreEqual(string.Empty, references[0].PropertyValueFilter);
            Assert.AreEqual("Server=$DbHost$;Database=Main", references[0].RawValueSnippet);

            Assert.AreEqual("FallbackDollarWithUnclosedLiteral", references[1].PropertyName);
            Assert.AreEqual("Default", references[1].Scope);
            Assert.AreEqual("Cost=$50;Server=$DbHost$", references[1].RawValueSnippet);

            Assert.AreEqual("SecondDefaultSecret", references[2].PropertyName);
            Assert.AreEqual("Default", references[2].Scope);
            Assert.AreEqual(RequestPropertyRedaction.Marker, references[2].RawValueSnippet);

            Assert.AreEqual("SecretConnection", references[3].PropertyName);
            Assert.AreEqual("Default", references[3].Scope);
            Assert.AreEqual(RequestPropertyRedaction.Marker, references[3].RawValueSnippet);

            Assert.AreEqual("ReadableEnvSecretUrl", references[4].PropertyName);
            Assert.AreEqual("Endur DV 01", references[4].Scope);
            Assert.AreEqual("Endur DV 01", references[4].PropertyValueFilter);
            Assert.AreEqual("https://$DbHost$/readable-secret", references[4].RawValueSnippet);

            Assert.AreEqual("EnvSecretUrl", references[5].PropertyName);
            Assert.AreEqual("Endur DV 02", references[5].Scope);
            Assert.AreEqual("Endur DV 02", references[5].PropertyValueFilter);
            Assert.AreEqual(RequestPropertyRedaction.Marker, references[5].RawValueSnippet);

            Assert.AreEqual("EnvServiceUrl", references[6].PropertyName);
            Assert.AreEqual("Endur DV 02", references[6].Scope);
            Assert.AreEqual("Endur DV 02", references[6].PropertyValueFilter);
            Assert.AreEqual(120, references[6].RawValueSnippet.Length);
            StringAssert.EndsWith(references[6].RawValueSnippet, "...");
        }

        [TestMethod]
        public void GetWhereUsed_GatesDefaultScopeSecretSnippetSolelyOnCanReadSecrets()
        {
            _securityPrivilegesChecker.CanReadSecrets(Arg.Any<ClaimsPrincipal>(), string.Empty).Returns(false);
            _securityPrivilegesChecker.CanModifyProperty(Arg.Any<ClaimsPrincipal>()).Returns(true);
            _propertyValuesPersistentSource.GetSecurePropertyValues(true).Returns((PropertyValueDto[])null!);
            _propertyValuesPersistentSource.GetGlobalProperties().Returns(new[]
            {
                new PropertyValueDto
                {
                    Property = new PropertyApiModel { Name = "GlobalSecretConn", Secure = true },
                    Value = "Server=$DbHost$;Password=topsecret"
                }
            });
            _propertyValuesPersistentSource.GetPropertyValuesForSearchValueByPage(
                    Arg.Any<int>(),
                    Arg.Any<int>(),
                    Arg.Any<PagedDataOperators>(),
                    Arg.Any<ClaimsPrincipal>())
                .Returns((GetScopedPropertyValuesResponseDto)null!);

            var result = (OkObjectResult)_controller.GetWhereUsed("DbHost");
            var references = (List<PropertyUsageReferenceApiModel>)result.Value!;
            Assert.AreEqual(1, references.Count);
            Assert.AreEqual("GlobalSecretConn", references[0].PropertyName);
            Assert.AreEqual(RequestPropertyRedaction.Marker, references[0].RawValueSnippet);

            // When CanReadSecrets(User, string.Empty) is true, the default-scope snippet is revealed
            _securityPrivilegesChecker.CanReadSecrets(Arg.Any<ClaimsPrincipal>(), string.Empty).Returns(true);
            var fullPageItems = Enumerable.Range(0, 500)
                .Select(i => new FlatPropertyValueApiModel
                {
                    Property = $"ScopedProp_{i}",
                    PropertyValueScope = "Env1",
                    PropertyValue = i == 0 ? "https://$DbHost$/page" : "static",
                    Secure = false
                })
                .ToList();
            _propertyValuesPersistentSource.GetPropertyValuesForSearchValueByPage(
                    500,
                    1,
                    Arg.Any<PagedDataOperators>(),
                    Arg.Any<ClaimsPrincipal>())
                .Returns(new GetScopedPropertyValuesResponseDto
                {
                    Items = fullPageItems,
                    TotalPages = 2
                });
            _propertyValuesPersistentSource.GetPropertyValuesForSearchValueByPage(
                    500,
                    2,
                    Arg.Any<PagedDataOperators>(),
                    Arg.Any<ClaimsPrincipal>())
                .Returns(new GetScopedPropertyValuesResponseDto
                {
                    Items = fullPageItems,
                    TotalPages = 2
                });

            var pagedResult = (OkObjectResult)_controller.GetWhereUsed("DbHost");
            var pagedRefs = (List<PropertyUsageReferenceApiModel>)pagedResult.Value!;
            Assert.AreEqual(2, pagedRefs.Count);
            Assert.AreEqual("Server=$DbHost$;Password=topsecret", pagedRefs[0].RawValueSnippet);
        }
    }
}
