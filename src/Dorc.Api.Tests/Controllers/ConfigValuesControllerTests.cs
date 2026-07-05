using System.Security.Claims;
using System.Security.Principal;
using Dorc.Api.Controllers;
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

        [TestInitialize]
        public void Setup()
        {
            _configValues = Substitute.For<IConfigValuesPersistentSource>();
            _roleChecker = Substitute.For<IRolePrivilegesChecker>();
            _controller = new ConfigValuesController(_configValues, _roleChecker)
            {
                ControllerContext = new ControllerContext
                {
                    HttpContext = new DefaultHttpContext
                    {
                        User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, "TestUser") }))
                    }
                }
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
    }
}
