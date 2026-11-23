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

        [TestMethod]
        public void ResetAppPassword_EnvironmentUsers_SqlPorts_AndErrorSerialization_BranchesCovered()
        {
            var user = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, "TestUser"), new Claim(ClaimTypes.Role, "Admin") }, "TestAuth"));
            var nonAdminUser = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, "TestUser") }, "TestAuth"));

            // 1. ResetAppPasswordController
            var dbSource = Substitute.For<IDatabasesPersistentSource>();
            var sqlReset = Substitute.For<Dorc.Core.Interfaces.ISqlUserPasswordReset>();
            var configValues = Substitute.For<IConfigValuesPersistentSource>();
            var resetLogger = Substitute.For<ILogger<ResetAppPasswordController>>();
            var securityChecker = Substitute.For<Dorc.Core.Interfaces.ISecurityPrivilegesChecker>();
            var configSettings = Substitute.For<Dorc.Core.Configuration.IConfigurationSettings>();
            var claimsReader = Substitute.For<Dorc.PersistentData.IClaimsPrincipalReader>();
            claimsReader.GetUserLogin(user).Returns("testlogin");

            var resetController = new ResetAppPasswordController(dbSource, sqlReset, configValues, resetLogger, securityChecker, configSettings, claimsReader)
            {
                ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = user } }
            };

            // Self-service Put when db is null
            dbSource.GetApplicationDatabaseForEnvFilter("testlogin", "Endur", "Env1").Returns((DatabaseApiModel?)null);
            var selfResetNullDb = (OkObjectResult)resetController.Put("Endur", "Env1");
            Assert.IsFalse(((ApiBoolResult)selfResetNullDb.Value!).Result);

            // ForUser Put when forbidden vs authorized vs exception
            securityChecker.CanModifyEnvironment(user, "Env1").Returns(false);
            Assert.AreEqual(StatusCodes.Status403Forbidden, ((ObjectResult)resetController.Put("Endur", "Env1", "targetuser")).StatusCode);

            securityChecker.CanModifyEnvironment(user, "Env1").Returns(true);
            dbSource.GetApplicationDatabaseForEnvFilter("targetuser", "Endur", "Env1").Returns(_ => throw new InvalidOperationException("DB lookup failed"));
            var resetError = (ObjectResult)resetController.Put("Endur", "Env1", "targetuser");
            Assert.AreEqual(StatusCodes.Status500InternalServerError, resetError.StatusCode);
            Assert.AreEqual("An error occurred while resetting the application password.", resetError.Value);

            // 2. RefDataEnvironmentsUsersController
            var manageUsers = Substitute.For<IManageUsers>();
            var envSource = Substitute.For<IEnvironmentsPersistentSource>();
            var adSearcher = Substitute.For<IActiveDirectorySearcher>();
            var envMapper = Substitute.For<IEnvironmentMapper>();

            var envUsersController = new RefDataEnvironmentsUsersController(manageUsers, securityChecker, envSource, adSearcher, envMapper)
            {
                ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = user } }
            };

            envSource.GetEnvironmentOwnerId(10).Returns(string.Empty);
            var emptyOwnerResult = (ObjectResult)envUsersController.GetOwner(10);
            Assert.AreEqual(string.Empty, ((EnvironmentOwnerApiModel)emptyOwnerResult.Value!).DisplayName);

            envSource.GetEnvironmentOwnerId(11).Returns("S-1-5-21-123");
            adSearcher.GetUserDataById("S-1-5-21-123").Returns(new UserElementApiModel { DisplayName = "Owner Name" });
            var foundOwnerResult = (ObjectResult)envUsersController.GetOwner(11);
            Assert.AreEqual("Owner Name", ((EnvironmentOwnerApiModel)foundOwnerResult.Value!).DisplayName);

            adSearcher.GetUserDataById("S-1-5-21-123").Returns((UserElementApiModel)null!);
            var nullOwnerResult = (ObjectResult)envUsersController.GetOwner(11);
            Assert.AreEqual(string.Empty, ((EnvironmentOwnerApiModel)nullOwnerResult.Value!).DisplayName);

            Assert.IsInstanceOfType<BadRequestObjectResult>(envUsersController.UpdateOwner(11, null!));
            Assert.IsInstanceOfType<BadRequestObjectResult>(envUsersController.UpdateOwner(11, new EnvironmentOwnerApiModel { DisplayName = " " }));

            envSource.GetEnvironment(11, user).Returns((EnvironmentApiModel)null!);
            Assert.AreEqual(StatusCodes.Status403Forbidden, ((ObjectResult)envUsersController.UpdateOwner(11, new EnvironmentOwnerApiModel { DisplayName = "NewOwner" })).StatusCode);

            envSource.GetEnvironment(11, user).Returns(new EnvironmentApiModel { EnvironmentId = 11, EnvironmentName = "Env1" });
            securityChecker.IsEnvironmentOwnerOrAdmin(user, "Env1").Returns(false);
            Assert.AreEqual(StatusCodes.Status403Forbidden, ((ObjectResult)envUsersController.UpdateOwner(11, new EnvironmentOwnerApiModel { DisplayName = "NewOwner" })).StatusCode);

            securityChecker.IsEnvironmentOwnerOrAdmin(user, "Env1").Returns(true);
            var ownerElement = new UserElementApiModel { Pid = "sid-1", DisplayName = "NewOwner" };
            adSearcher.GetUserData("NewOwner").Returns(ownerElement);
            envSource.SetEnvironmentOwner(user, 11, ownerElement).Returns(true);
            Assert.AreEqual(StatusCodes.Status200OK, ((ObjectResult)envUsersController.UpdateOwner(11, new EnvironmentOwnerApiModel { DisplayName = "NewOwner" })).StatusCode);

            // 3. RefDataSqlPortsController
            var sqlPortsSource = Substitute.For<ISqlPortsPersistentSource>();
            var sqlPortsController = new RefDataSqlPortsController(sqlPortsSource)
            {
                ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = nonAdminUser } }
            };
            var portModel = new SqlPortApiModel { InstanceName = "SQL01", SqlPort = "1433" };
            Assert.Throws<UnauthorizedAccessException>(() => sqlPortsController.Delete(portModel));

            sqlPortsController.ControllerContext.HttpContext.User = user;
            Assert.AreEqual(StatusCodes.Status200OK, ((StatusCodeResult)sqlPortsController.Delete(portModel)).StatusCode);

            sqlPortsSource.When(s => s.DeleteSqlPort(portModel)).Do(_ => throw new ArgumentException("Port not found"));
            var badDelete = (ObjectResult)sqlPortsController.Delete(portModel);
            Assert.AreEqual(StatusCodes.Status400BadRequest, badDelete.StatusCode);
            Assert.AreEqual("Port not found", badDelete.Value);

            // 4. PropertyValuesController error branches
            _propertyValuesService.GetPropertyValues("P1", "E1", Arg.Any<ClaimsPrincipal>())
                .Returns(_ => throw new Dorc.Api.Services.NonEnoughRightsException("No rights"));
            Assert.AreEqual(StatusCodes.Status403Forbidden, ((ObjectResult)_controller.GetPropertyValues("P1", "E1")).StatusCode);

            _propertyValuesService.GetPropertyValues("P2", "E1", Arg.Any<ClaimsPrincipal>())
                .Returns(_ => throw new InvalidOperationException("Unexpected failure"));
            Assert.AreEqual(StatusCodes.Status500InternalServerError, ((ObjectResult)_controller.GetPropertyValues("P2", "E1")).StatusCode);

            _propertyValuesService.GetPropertyValues(null!, "ForbiddenEnv", Arg.Any<ClaimsPrincipal>())
                .Returns(_ => throw new Dorc.Api.Services.NonEnoughRightsException("No export rights"));
            Assert.AreEqual(StatusCodes.Status403Forbidden, ((ObjectResult)_controller.DownloadNonSecurePropertyValues("ForbiddenEnv")).StatusCode);

            _propertyValuesService.GetPropertyValues(null!, "ErrorEnv", Arg.Any<ClaimsPrincipal>())
                .Returns(_ => throw new InvalidOperationException("Export failure"));
            Assert.AreEqual(StatusCodes.Status500InternalServerError, ((ObjectResult)_controller.DownloadNonSecurePropertyValues("ErrorEnv")).StatusCode);

            // 5. RefDataAppServersController RBAC & reboot branches
            var serversSource = Substitute.For<IServersPersistentSource>();
            var roleChecker = Substitute.For<Dorc.PersistentData.IRolePrivilegesChecker>();
            var appServersController = new RefDataAppServersController(
                serversSource,
                securityChecker,
                roleChecker)
            {
                ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = nonAdminUser } }
            };

            serversSource.GetServer("MissingSrv", nonAdminUser).Returns((ServerApiModel)null!);
            roleChecker.IsAdmin(nonAdminUser).Returns(false);
            Assert.IsInstanceOfType<NotFoundObjectResult>(appServersController.PutServerReboot("MissingSrv"));

            serversSource.GetServer("SRV01", nonAdminUser).Returns(new ServerApiModel
            {
                ServerId = 1,
                Name = "SRV01",
                EnvironmentNames = new List<string> { "Env1" }
            });
            serversSource.GetEnvironmentNamesForServerId(1).Returns(new List<string> { "Env1" });
            securityChecker.CanModifyEnvironment(nonAdminUser, "Env1").Returns(false);
            Assert.AreEqual(StatusCodes.Status403Forbidden, ((ObjectResult)appServersController.PutServerReboot("SRV01")).StatusCode);

            // 6. RefDataEnvironmentsHistoryController RBAC & null guards
            var envHistorySource = Substitute.For<IEnvironmentHistoryPersistentSource>();
            var envHistoryController = new RefDataEnvironmentsHistoryController(envHistorySource, securityChecker)
            {
                ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = nonAdminUser } }
            };

            Assert.AreEqual(400, ((Microsoft.AspNetCore.Mvc.Infrastructure.IStatusCodeActionResult)envHistoryController.Put(null!)).StatusCode);
            securityChecker.CanModifyEnvironment(nonAdminUser, "Env1").Returns(false);
            Assert.AreEqual(StatusCodes.Status403Forbidden, ((Microsoft.AspNetCore.Mvc.Infrastructure.IStatusCodeActionResult)envHistoryController.Put(new EnvironmentHistoryApiModel { Id = 1, EnvName = "Env1", Comment = "c" })).StatusCode);

            securityChecker.CanModifyEnvironment(nonAdminUser, "Env1").Returns(true);
            Assert.AreEqual(200, ((Microsoft.AspNetCore.Mvc.Infrastructure.IStatusCodeActionResult)envHistoryController.Put(new EnvironmentHistoryApiModel { Id = 1, EnvName = "Env1", Comment = "c" })).StatusCode);

            Assert.AreEqual(400, ((Microsoft.AspNetCore.Mvc.Infrastructure.IStatusCodeActionResult)envHistoryController.Post(null!)).StatusCode);
            Assert.AreEqual(400, ((Microsoft.AspNetCore.Mvc.Infrastructure.IStatusCodeActionResult)envHistoryController.Post(new UpdateEnvironmentHistoryRequest { EnvName = " " })).StatusCode);
            securityChecker.CanModifyEnvironment(nonAdminUser, "EnvForbidden").Returns(false);
            Assert.AreEqual(StatusCodes.Status403Forbidden, ((Microsoft.AspNetCore.Mvc.Infrastructure.IStatusCodeActionResult)envHistoryController.Post(new UpdateEnvironmentHistoryRequest { EnvName = "EnvForbidden" })).StatusCode);
            Assert.AreEqual(200, ((Microsoft.AspNetCore.Mvc.Infrastructure.IStatusCodeActionResult)envHistoryController.Post(new UpdateEnvironmentHistoryRequest { EnvName = "Env1" })).StatusCode);

            // 7. RequestStatusesController GetLog & Get error sanitization
            var requestsSource = Substitute.For<IRequestsPersistentSource>();
            var requestsStatusSource = Substitute.For<IRequestsStatusPersistentSource>();
            var reqStatusesController = new RequestStatusesController(
                requestsSource,
                requestsStatusSource,
                roleChecker,
                Substitute.For<ILogger<RequestStatusesController>>())
            {
                ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = nonAdminUser } }
            };
            requestsSource.GetRequestForUser(500, nonAdminUser).Returns((DeploymentRequestApiModel)null!);
            Assert.AreEqual(404, ((Microsoft.AspNetCore.Mvc.Infrastructure.IStatusCodeActionResult)reqStatusesController.GetLog(500)).StatusCode);

            requestsSource.GetRequestForUser(501, nonAdminUser).Returns(new DeploymentRequestApiModel { Id = 501 });
            requestsSource.GetRequestLog(501).Returns("LogLine1");
            Assert.AreEqual(StatusCodes.Status200OK, ((ObjectResult)reqStatusesController.GetLog(501)).StatusCode);

            requestsSource.GetRequestForUser(502, nonAdminUser).Returns(_ => throw new InvalidOperationException("DB connection error"));
            var statusErr = (ObjectResult)reqStatusesController.Get(502);
            Assert.AreEqual(StatusCodes.Status500InternalServerError, statusErr.StatusCode);
            Assert.AreEqual("DB connection error", statusErr.Value);

            // 8. RefDataGroupsController, RefDataScopedPropertyValuesController, RefDataUsersController
            var adGroupSource = Substitute.For<IAdGroupPersistentSource>();
            var groupsController = new RefDataGroupsController(adGroupSource);
            adGroupSource.GetAdGroup("MissingGroup").Returns((Dorc.PersistentData.Model.AdGroup)null!);
            Assert.AreEqual(404, ((Microsoft.AspNetCore.Mvc.Infrastructure.IStatusCodeActionResult)groupsController.Get("MissingGroup")).StatusCode);
            adGroupSource.GetAdGroup("FoundGroup").Returns(new Dorc.PersistentData.Model.AdGroup { Id = 5, Name = "FoundGroup" });
            Assert.AreEqual(StatusCodes.Status200OK, ((ObjectResult)groupsController.Get("FoundGroup")).StatusCode);

            var scopedController = new RefDataScopedPropertyValuesController(
                Substitute.For<IPropertyValuesPersistentSource>(),
                envSource,
                roleChecker)
            {
                ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = nonAdminUser } }
            };
            envSource.EnvironmentExists("RestrictedEnv").Returns(true);
            envSource.GetEnvironment("RestrictedEnv", nonAdminUser).Returns((EnvironmentApiModel)null!);
            Assert.AreEqual(StatusCodes.Status403Forbidden, ((ObjectResult)scopedController.Put(new PagedDataOperators(), "RestrictedEnv")).StatusCode);

            var manageUsersForController = Substitute.For<IManageUsers>();
            manageUsersForController.GetUsersList<UserApiModel>().Returns(new List<UserApiModel>
            {
                new UserApiModel { DisplayName = null!, LanId = "DOMAIN\\alice" }
            });
            var usersController = new RefDataUsersController(manageUsersForController);
            Assert.AreEqual("DOMAIN\\alice", usersController.GetByName("domain\\ALICE").LanId);
        }
    }
}
