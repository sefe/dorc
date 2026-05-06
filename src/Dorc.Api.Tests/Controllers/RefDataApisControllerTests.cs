using System.Security.Claims;
using Dorc.Api.Controllers;
using Dorc.Api.Services;
using Dorc.ApiModel;
using Dorc.Core.Interfaces;
using Dorc.PersistentData.Sources.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;

namespace Dorc.Api.Tests.Controllers
{
    [TestClass]
    public class RefDataApisControllerTests
    {
        private IApisPersistentSource _apis;
        private IEnvironmentsPersistentSource _envs;
        private ISecurityPrivilegesChecker _security;
        private IApiEndpointResolver _resolver;
        private RefDataApisController _controller;
        private ClaimsPrincipal _user;

        [TestInitialize]
        public void Setup()
        {
            _apis = Substitute.For<IApisPersistentSource>();
            _envs = Substitute.For<IEnvironmentsPersistentSource>();
            _security = Substitute.For<ISecurityPrivilegesChecker>();
            _resolver = Substitute.For<IApiEndpointResolver>();

            _controller = new RefDataApisController(_apis, _envs, _security, _resolver)
            {
                ControllerContext = new ControllerContext
                {
                    HttpContext = new DefaultHttpContext()
                }
            };
            _user = new ClaimsPrincipal(new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.Name, "TestUser")
            }));
            _controller.HttpContext.User = _user;
        }

        [TestMethod]
        public void GetForEnvironment_HappyPath_ReturnsResolvedApis()
        {
            _envs.GetEnvironment(7, _controller.HttpContext.User)
                .Returns(new EnvironmentApiModel
                {
                    EnvironmentId = 7,
                    EnvironmentName = "ENV1",
                    UserEditable = true
                });
            _apis.GetApisForEnvId(7).Returns(new[]
            {
                new ApiApiModel { Id = 1, Name = "Orders", Endpoint = "https://orders" }
            });

            var result = _controller.GetForEnvironment(7) as OkObjectResult;

            Assert.IsNotNull(result);
            Assert.AreEqual(StatusCodes.Status200OK, result.StatusCode);
            var list = result.Value as List<ApiApiModel>;
            Assert.IsNotNull(list);
            Assert.AreEqual(1, list.Count);
            Assert.IsTrue(list[0].UserEditable);
            _resolver.Received(1).ResolveEndpoints(Arg.Any<IEnumerable<ApiApiModel>>(), "ENV1");
        }

        [TestMethod]
        public void GetForEnvironment_InvalidId_ReturnsBadRequest()
        {
            var result = _controller.GetForEnvironment(0) as BadRequestObjectResult;
            Assert.IsNotNull(result);
            Assert.AreEqual(StatusCodes.Status400BadRequest, result.StatusCode);
        }

        [TestMethod]
        public void Post_UserNotEditable_ReturnsForbidden()
        {
            _envs.GetEnvironment(7, _controller.HttpContext.User)
                .Returns(new EnvironmentApiModel { EnvironmentId = 7, EnvironmentName = "ENV1" });
            _security.CanModifyEnvironment(_controller.HttpContext.User, "ENV1").Returns(false);

            var result = _controller.Post(7, new ApiApiModel { Name = "x", Endpoint = "y" }) as ObjectResult;

            Assert.IsNotNull(result);
            Assert.AreEqual(StatusCodes.Status403Forbidden, result.StatusCode);
        }

        [TestMethod]
        public void Post_HappyPath_ReturnsCreatedModel()
        {
            _envs.GetEnvironment(7, _controller.HttpContext.User)
                .Returns(new EnvironmentApiModel
                {
                    EnvironmentId = 7,
                    EnvironmentName = "ENV1",
                    UserEditable = true
                });
            _security.CanModifyEnvironment(_controller.HttpContext.User, "ENV1").Returns(true);

            var input = new ApiApiModel { Name = "Orders", Endpoint = "https://orders", Type = "REST", AuthType = "None" };
            var created = new ApiApiModel { Id = 11, Name = "Orders", Endpoint = "https://orders" };
            _apis.AddApi(7, input, _controller.HttpContext.User).Returns(created);

            var result = _controller.Post(7, input) as OkObjectResult;
            Assert.IsNotNull(result);
            var returned = result.Value as ApiApiModel;
            Assert.IsNotNull(returned);
            Assert.AreEqual(11, returned.Id);
            Assert.IsTrue(returned.UserEditable);
        }

        [TestMethod]
        public void Post_PersistentSourceThrowsArgument_ReturnsBadRequest()
        {
            _envs.GetEnvironment(7, _controller.HttpContext.User)
                .Returns(new EnvironmentApiModel { EnvironmentId = 7, EnvironmentName = "ENV1", UserEditable = true });
            _security.CanModifyEnvironment(_controller.HttpContext.User, "ENV1").Returns(true);
            _apis.When(x => x.AddApi(Arg.Any<int>(), Arg.Any<ApiApiModel>(), Arg.Any<ClaimsPrincipal>()))
                .Do(_ => throw new ArgumentException("duplicate"));

            var result = _controller.Post(7, new ApiApiModel { Name = "x", Endpoint = "y" }) as BadRequestObjectResult;
            Assert.IsNotNull(result);
            Assert.AreEqual("duplicate", result.Value);
        }

        [TestMethod]
        public void Delete_NotFound_Returns404()
        {
            _apis.GetApi(99).Returns((ApiApiModel?)null);
            var result = _controller.Delete(99) as NotFoundResult;
            Assert.IsNotNull(result);
        }

        [TestMethod]
        public void Delete_HappyPath_ReturnsOk()
        {
            _apis.GetApi(11).Returns(new ApiApiModel { Id = 11, EnvironmentId = 7 });
            _envs.GetEnvironment(7, _controller.HttpContext.User)
                .Returns(new EnvironmentApiModel { EnvironmentId = 7, EnvironmentName = "ENV1" });
            _security.CanModifyEnvironment(_controller.HttpContext.User, "ENV1").Returns(true);
            _apis.DeleteApi(11, _controller.HttpContext.User).Returns(true);

            var result = _controller.Delete(11) as OkObjectResult;
            Assert.IsNotNull(result);
            var bool_ = result.Value as ApiBoolResult;
            Assert.IsNotNull(bool_);
            Assert.IsTrue(bool_.Result);
        }
    }
}
