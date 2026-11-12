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

            _controller = new PropertyValuesController(
                _propertyValuesService,
                _propertyValuesPersistentSource,
                _variableResolver,
                _environmentsPersistentSource,
                _variableScopeOptionsResolver,
                _directorySearcherFactory,
                _logger)
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
    }
}
