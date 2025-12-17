using Dorc.Core.Account;
using Dorc.Core.Account.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using System.Reflection;
using System.Text;
using Dorc.Api.Controllers;

namespace Dorc.Api.Tests.Controllers
{
    [TestClass]
    public class AccountControllerTests
    {
        public static IEnumerable<object[]> UserExistsData
        {
            get
            {
                return new[]
                {
                    new object[] { "testUserLanId", "Windows", AccountType.Windows, true, null, false },
                    new object[] { "testUserLanId", "Sql", AccountType.Sql, true, null, false },
                    new object[] { "testUserLanId", "SQL", AccountType.Sql, true, null, false },
                    new object[] { "testUserLanId", "Endur", AccountType.Endur, true, null, false },
                    new object[] { string.Empty, "Endur", AccountType.Endur, false, "User LanId is required.", true },
                    new object[] { "testUserLanId", string.Empty, AccountType.Endur, false, "Account type is required.", true },
                    new object[] { "testUserLanId", "invalidAccountType", AccountType.Endur, false, "test exception message", false },
                };
            }
        }

        public static string GetTestName(MethodInfo methodInfo, object[] data)
        {
            StringBuilder testNameBuilder = new StringBuilder(string.Format("{0} for account verification '{1}'. Parameters: ", methodInfo.Name, data[0]));

            for (int i = 1; i < data.Length; i++)
            {
                object testParameter = data[i];
                testNameBuilder.Append(testParameter ?? "null");

                if (i != data.Length - 1)
                {
                    testNameBuilder.Append(", ");
                }
                else
                {
                    testNameBuilder.Append(".");
                }
            }
            return testNameBuilder.ToString();
        }

        [TestMethod]
        [DynamicData(nameof(UserExistsData), DynamicDataDisplayName = nameof(GetTestName))]
        public void TestUserExists(
            string userLanId,
            string accountType,
            AccountType accountTypeEnum,
            bool expectedResult,
            string expectedExceptionMessage,
            bool isExpectedExceptionMessageCustom)
        {
            var accountExistenceCheckerMock = Substitute.For<IAccountExistenceChecker>();
            accountExistenceCheckerMock.UserExists(userLanId, accountTypeEnum).Returns(expectedResult);

            using (var accountController = new AccountController(accountExistenceCheckerMock))
            {
                if (!string.IsNullOrEmpty(expectedExceptionMessage))
                {
                    var exists = accountController.UserExists(userLanId, accountType);
                    var failedObjectResult = exists as ObjectResult;

                    Assert.IsNotNull(failedObjectResult);
                    Assert.AreEqual(StatusCodes.Status500InternalServerError, failedObjectResult.StatusCode);
                    var responseMsg = failedObjectResult.Value as HttpResponseMessage;

                    if (isExpectedExceptionMessageCustom)
                    {
                        Assert.AreEqual(expectedExceptionMessage, responseMsg?.Content.ReadAsStringAsync().Result);
                    }

                    return;
                }

                bool actualResult = (bool)(accountController.UserExists(userLanId, accountType) as OkObjectResult)!.Value!;

                accountExistenceCheckerMock.Received().UserExists(userLanId, accountTypeEnum);

                Assert.AreEqual(expectedResult, actualResult, "The verification against existance of specified user in DOrc is incorrect.");
            }
        }

        public static IEnumerable<object[]> GroupExistsData
        {
            get
            {
                return new[]
                {
                    new object[] { "testGroupLanId", "Windows", AccountType.Windows, true, null, false },
                    new object[] { "testGroupLanId", "Sql", AccountType.Sql, true, null, false },
                    new object[] { "testGroupLanId", "SQL", AccountType.Sql, true, null, false },
                    new object[] { "testGroupLanId", "Endur", AccountType.Endur, true, null, false },
                    new object[] { string.Empty, "Endur", AccountType.Endur, false, "Group LanId is required.", true },
                    new object[] { "testGroupLanId", string.Empty, AccountType.Endur, false, "Account type is required.", true },
                    new object[] { "testGroupLanId", "invalidAccountType", AccountType.Endur, false, "test exception message", false },
                };
            }
        }

        [TestMethod]
        [DynamicData(nameof(GroupExistsData), DynamicDataDisplayName = nameof(GetTestName))]
        public void TestGroupExists(
            string groupLanId,
            string accountType,
            AccountType accountTypeEnum,
            bool expectedResult,
            string expectedExceptionMessage,
            bool isExpectedExceptionMessageCustom)
        {
            var accountExistenceCheckerMock = Substitute.For<IAccountExistenceChecker>();
            accountExistenceCheckerMock.GroupExists(groupLanId, accountTypeEnum).Returns(expectedResult);

            using (var accountController = new AccountController(accountExistenceCheckerMock))
            {
                if (!string.IsNullOrEmpty(expectedExceptionMessage))
                {
                    var groupExists = accountController.GroupExists(groupLanId, accountType);
                    var failedObjectResult = groupExists as ObjectResult;

                    Assert.IsNotNull(failedObjectResult);
                    Assert.AreEqual(StatusCodes.Status500InternalServerError, failedObjectResult.StatusCode);
                    var responseMsg = failedObjectResult.Value as HttpResponseMessage;
                    if (isExpectedExceptionMessageCustom)
                    {
                        Assert.AreEqual(expectedExceptionMessage, responseMsg?.Content.ReadAsStringAsync().Result);
                    }

                    return;
                }

                bool actualResult = (bool)(accountController.GroupExists(groupLanId, accountType) as OkObjectResult)!.Value!;

                accountExistenceCheckerMock.Received().GroupExists(groupLanId, accountTypeEnum);

                Assert.AreEqual(expectedResult, actualResult, "The verification against existence of specified group in DOrc is incorrect.");
            }
        }

    }
}
