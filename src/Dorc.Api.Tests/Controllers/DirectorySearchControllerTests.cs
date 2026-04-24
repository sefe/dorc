using Dorc.Api.Controllers;
using Dorc.Api.Services;
using Dorc.ApiModel;
using Dorc.Core.Configuration;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using NSubstitute;

namespace Dorc.Api.Tests.Controllers
{
    [TestClass]
    public class DirectorySearchControllerTests
    {
        private IDirectorySearchService _searchService = null!;
        private IConfigurationSettings _configSettings = null!;

        [TestInitialize]
        public void Setup()
        {
            _searchService = Substitute.For<IDirectorySearchService>();
            _configSettings = Substitute.For<IConfigurationSettings>();
            _configSettings.GetConfigurationDomainName().Returns("DOMAIN");
        }

        private DirectorySearchController CreateController()
        {
            return new DirectorySearchController(_searchService, _configSettings);
        }

        #region SearchUsers validation tests

        [TestMethod]
        [DataRow("te", "User search criteria length should be not less then 3 characters. Actual length : 2.")]
        [DataRow("a", "User search criteria length should be not less then 3 characters. Actual length : 1.")]
        public void SearchUsers_TooShort_Returns500(string criteria, string expectedMessage)
        {
            using var controller = CreateController();
            var result = controller.SearchUsers(criteria) as ObjectResult;

            Assert.IsNotNull(result);
            Assert.AreEqual(StatusCodes.Status500InternalServerError, result!.StatusCode);
            var response = result.Value as HttpResponseMessage;
            Assert.AreEqual(expectedMessage, response?.Content.ReadAsStringAsync().Result);
        }

        [TestMethod]
        public void SearchUsers_TooLong_Returns500()
        {
            var criteria = new string('a', 101);
            using var controller = CreateController();
            var result = controller.SearchUsers(criteria) as ObjectResult;

            Assert.IsNotNull(result);
            Assert.AreEqual(StatusCodes.Status500InternalServerError, result!.StatusCode);
            var response = result.Value as HttpResponseMessage;
            Assert.IsTrue(response?.Content.ReadAsStringAsync().Result.Contains("not greater then 100"));
        }

        [TestMethod]
        [DataRow("test!")]
        [DataRow("test@")]
        [DataRow("test#")]
        [DataRow("test$")]
        [DataRow("test%")]
        [DataRow("test^")]
        [DataRow("test*")]
        [DataRow("test+")]
        [DataRow("test/")]
        [DataRow("test[")]
        [DataRow("test]")]
        [DataRow("test{")]
        [DataRow("test}")]
        [DataRow("test|")]
        [DataRow("test:")]
        [DataRow("test?")]
        [DataRow("test<")]
        [DataRow("test>")]
        [DataRow("test;")]
        [DataRow("test,")]
        [DataRow("test\\")]
        public void SearchUsers_InvalidCharacters_Returns500(string criteria)
        {
            using var controller = CreateController();
            var result = controller.SearchUsers(criteria) as ObjectResult;

            Assert.IsNotNull(result);
            Assert.AreEqual(StatusCodes.Status500InternalServerError, result!.StatusCode);
            var response = result.Value as HttpResponseMessage;
            Assert.IsTrue(response?.Content.ReadAsStringAsync().Result.Contains("unacceptable characters"));
        }

        [TestMethod]
        public void SearchUsers_ValidCriteria_ReturnsOkWithResults()
        {
            var expectedUsers = new List<UserSearchResult>
            {
                new() { DisplayName = "Test User", FullLogonName = @"DOMAIN\testuser" }
            };
            _searchService.FindUsers("test", "DOMAIN").Returns(expectedUsers);

            using var controller = CreateController();
            var result = controller.SearchUsers("test") as OkObjectResult;

            Assert.IsNotNull(result);
            var users = result!.Value as IList<UserSearchResult>;
            Assert.IsNotNull(users);
            Assert.AreEqual(1, users!.Count);
            Assert.AreEqual("Test User", users[0].DisplayName);
            Assert.AreEqual(@"DOMAIN\testuser", users[0].FullLogonName);
        }

        [TestMethod]
        public void SearchUsers_ValidCriteria_NoResults_ReturnsEmptyList()
        {
            _searchService.FindUsers("test", "DOMAIN").Returns(new List<UserSearchResult>());

            using var controller = CreateController();
            var result = controller.SearchUsers("test") as OkObjectResult;

            Assert.IsNotNull(result);
            var users = result!.Value as IList<UserSearchResult>;
            Assert.IsNotNull(users);
            Assert.AreEqual(0, users!.Count);
        }

        [TestMethod]
        public void SearchUsers_AllValidCharacters_ReturnsOk()
        {
            var criteria = "abcdefghijklmopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-_.' ()&";
            _searchService.FindUsers(criteria, "DOMAIN").Returns(new List<UserSearchResult>());

            using var controller = CreateController();
            var result = controller.SearchUsers(criteria);

            Assert.IsInstanceOfType(result, typeof(OkObjectResult));
        }

        #endregion

        #region SearchGroups validation tests

        [TestMethod]
        [DataRow("te", "Group search criteria length should be not less then 3 characters. Actual length : 2.")]
        public void SearchGroups_TooShort_Returns500(string criteria, string expectedMessage)
        {
            using var controller = CreateController();
            var result = controller.SearchGroups(criteria) as ObjectResult;

            Assert.IsNotNull(result);
            Assert.AreEqual(StatusCodes.Status500InternalServerError, result!.StatusCode);
            var response = result.Value as HttpResponseMessage;
            Assert.AreEqual(expectedMessage, response?.Content.ReadAsStringAsync().Result);
        }

        [TestMethod]
        public void SearchGroups_TooLong_Returns500()
        {
            var criteria = new string('a', 101);
            using var controller = CreateController();
            var result = controller.SearchGroups(criteria) as ObjectResult;

            Assert.IsNotNull(result);
            Assert.AreEqual(StatusCodes.Status500InternalServerError, result!.StatusCode);
        }

        [TestMethod]
        [DataRow("test!")]
        [DataRow("test@")]
        [DataRow("test#")]
        [DataRow("test$")]
        [DataRow("test%")]
        [DataRow("test^")]
        [DataRow("test*")]
        [DataRow("test+")]
        [DataRow("test/")]
        [DataRow("test[")]
        [DataRow("test]")]
        [DataRow("test{")]
        [DataRow("test}")]
        [DataRow("test|")]
        [DataRow("test:")]
        [DataRow("test?")]
        [DataRow("test<")]
        [DataRow("test>")]
        [DataRow("test;")]
        [DataRow("test,")]
        [DataRow("test\\")]
        public void SearchGroups_InvalidCharacters_Returns500(string criteria)
        {
            using var controller = CreateController();
            var result = controller.SearchGroups(criteria) as ObjectResult;

            Assert.IsNotNull(result);
            Assert.AreEqual(StatusCodes.Status500InternalServerError, result!.StatusCode);
        }

        [TestMethod]
        public void SearchGroups_ValidCriteria_ReturnsOkWithResults()
        {
            var expectedGroups = new List<GroupSearchResult>
            {
                new() { DisplayName = "Test Group", FullLogonName = @"DOMAIN\TestGroup" }
            };
            _searchService.FindGroups("test", "DOMAIN").Returns(expectedGroups);

            using var controller = CreateController();
            var result = controller.SearchGroups("test") as OkObjectResult;

            Assert.IsNotNull(result);
            var groups = result!.Value as IList<GroupSearchResult>;
            Assert.IsNotNull(groups);
            Assert.AreEqual(1, groups!.Count);
            Assert.AreEqual("Test Group", groups[0].DisplayName);
        }

        [TestMethod]
        public void SearchGroups_AllValidCharacters_ReturnsOk()
        {
            var criteria = "abcdefghijklmopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-_.' ()&";
            _searchService.FindGroups(criteria, "DOMAIN").Returns(new List<GroupSearchResult>());

            using var controller = CreateController();
            var result = controller.SearchGroups(criteria);

            Assert.IsInstanceOfType(result, typeof(OkObjectResult));
        }

        #endregion

        #region IsUserInGroup tests

        [TestMethod]
        public void IsUserInGroup_NullGroupName_Returns400()
        {
            using var controller = CreateController();
            var result = controller.IsUserInGroup(null, "account") as StatusCodeResult;

            Assert.IsNotNull(result);
            Assert.AreEqual(StatusCodes.Status400BadRequest, result!.StatusCode);
        }

        [TestMethod]
        public void IsUserInGroup_NullAccount_Returns400()
        {
            using var controller = CreateController();
            var result = controller.IsUserInGroup("group", null) as StatusCodeResult;

            Assert.IsNotNull(result);
            Assert.AreEqual(StatusCodes.Status400BadRequest, result!.StatusCode);
        }

        [TestMethod]
        public void IsUserInGroup_ValidInput_ReturnsTrueResult()
        {
            _searchService.IsUserInGroup("myGroup", "myUser", "DOMAIN").Returns(true);

            using var controller = CreateController();
            var result = controller.IsUserInGroup("myGroup", "myUser") as ObjectResult;

            Assert.IsNotNull(result);
            Assert.AreEqual(StatusCodes.Status200OK, result!.StatusCode);
            var apiResult = result.Value as ApiBoolResult;
            Assert.IsNotNull(apiResult);
            Assert.IsTrue(apiResult!.Result);
        }

        #endregion
    }
}
