using System.Security.Claims;
using System.Security.Principal;
using Dorc.Api.Controllers;
using Dorc.ApiModel;
using Dorc.PersistentData;
using Dorc.PersistentData.Sources;
using Dorc.PersistentData.Sources.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;

namespace Dorc.Api.Tests.Controllers
{
    [TestClass]
    public class ConfigValuesControllerTests
    {
        private IConfigValuesPersistentSource _configValues = null!;
        private IRolePrivilegesChecker _roleChecker = null!;
        private ConfigValuesController _controller = null!;
        private RefDataConfigController _refDataConfigController = null!;

        [TestInitialize]
        public void Setup()
        {
            _configValues = Substitute.For<IConfigValuesPersistentSource>();
            _roleChecker = Substitute.For<IRolePrivilegesChecker>();
            var controllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, "TestUser") }))
                }
            };
            _controller = new ConfigValuesController(_configValues, _roleChecker)
            {
                ControllerContext = controllerContext
            };
            _refDataConfigController = new RefDataConfigController(_configValues, _roleChecker)
            {
                ControllerContext = controllerContext
            };
        }

        [TestMethod]
        public void GetConfigValue_NonAdmin_Forbidden()
        {
            _roleChecker.IsAdmin(Arg.Any<IPrincipal>()).Returns(false);

            var result = _controller.GetConfigValue("DORC_NonProdDeployPassword");

            var objectResult = (ObjectResult)result;
            Assert.AreEqual(StatusCodes.Status403Forbidden, objectResult.StatusCode);
            // The secret store must not even be consulted for a non-admin.
            _configValues.DidNotReceive().GetNonSecureConfigValue(Arg.Any<string>());
        }

        [TestMethod]
        public void GetConfigValue_AdminRequestingSecureKey_Forbidden()
        {
            _roleChecker.IsAdmin(Arg.Any<IPrincipal>()).Returns(true);
            // GetNonSecureConfigValue throws for secure keys; the endpoint must not expose them.
            _configValues.GetNonSecureConfigValue("DORC_NonProdDeployPassword")
                .Returns<string?>(_ => throw new SecureConfigValueRequestedException("DORC_NonProdDeployPassword"));

            var result = _controller.GetConfigValue("DORC_NonProdDeployPassword");

            var objectResult = (ObjectResult)result;
            Assert.AreEqual(StatusCodes.Status403Forbidden, objectResult.StatusCode);
        }

        [TestMethod]
        public void GetConfigValue_AdminRequestingNonSecureKey_ReturnsValue()
        {
            _roleChecker.IsAdmin(Arg.Any<IPrincipal>()).Returns(true);
            _configValues.GetNonSecureConfigValue("SomePublicSetting").Returns("public-value");

            var result = _controller.GetConfigValue("SomePublicSetting");

            var ok = (OkObjectResult)result;
            Assert.AreEqual("public-value", ok.Value);
        }

        [TestMethod]
        public void RefDataConfig_NonAdminCannotCreateOrFlipVisibleToScripts()
        {
            _roleChecker.IsAdmin(Arg.Any<IPrincipal>()).Returns(false);
            var model = new ConfigValueApiModel
            {
                Id = 7,
                Key = "SomeSecret",
                Value = "secret",
                Secure = true,
                VisibleToScripts = true
            };

            Assert.IsInstanceOfType<ForbidResult>(_refDataConfigController.Post(model));
            Assert.IsInstanceOfType<ForbidResult>(_refDataConfigController.Put(7, model));
            Assert.IsInstanceOfType<ForbidResult>(_refDataConfigController.PutScriptVisibility(
                7,
                new ConfigValueScriptVisibilityApiModel { VisibleToScripts = true }));
            _configValues.DidNotReceiveWithAnyArgs().Add(default!);
            _configValues.DidNotReceiveWithAnyArgs().UpdateConfigValue(default!);
            _configValues.DidNotReceiveWithAnyArgs().UpdateConfigValueScriptVisibility(default, default);
        }

        [TestMethod]
        public void RefDataConfig_PutScriptVisibility_AdminUpdatesOnlyVisibilityAndMapsNotFoundOrBadRequest()
        {
            _roleChecker.IsAdmin(Arg.Any<IPrincipal>()).Returns(true);
            var updated = new ConfigValueApiModel
            {
                Id = 7,
                Key = "SomeSecret",
                Value = "enc:secret",
                Secure = true,
                IsForProd = true,
                VisibleToScripts = false
            };
            _configValues.UpdateConfigValueScriptVisibility(7, false).Returns(updated);

            var ok = (OkObjectResult)_refDataConfigController.PutScriptVisibility(
                7,
                new ConfigValueScriptVisibilityApiModel { VisibleToScripts = false });
            Assert.AreSame(updated, ok.Value);

            _configValues.UpdateConfigValueScriptVisibility(999, true)
                .Returns<ConfigValueApiModel>(_ => throw new KeyNotFoundException("missing"));
            Assert.IsInstanceOfType<NotFoundObjectResult>(_refDataConfigController.PutScriptVisibility(
                999,
                new ConfigValueScriptVisibilityApiModel { VisibleToScripts = true }));

            _configValues.UpdateConfigValueScriptVisibility(8, false)
                .Returns<ConfigValueApiModel>(_ => throw new ArgumentException("non-secure"));
            Assert.IsInstanceOfType<BadRequestObjectResult>(_refDataConfigController.PutScriptVisibility(
                8,
                new ConfigValueScriptVisibilityApiModel { VisibleToScripts = false }));
        }
    }
}
