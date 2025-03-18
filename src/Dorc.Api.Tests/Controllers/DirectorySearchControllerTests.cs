using Dorc.ApiModel;
using Dorc.Core.Configuration;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.QualityTools.Testing.Fakes;
using System.Collections.Specialized;
using System.DirectoryServices;
using System.DirectoryServices.Fakes;
using System.Reflection;
using System.Text;
using Dorc.Api.Controllers;

namespace Dorc.Api.Tests.Controllers
{
    [TestClass]
    public partial class DirectorySearchControllerTests
    {
        public static IEnumerable<object[]> SearchUsersData
        {
            get
            {
                return new[]
                {
                    new object[] { "te", null, null, null, null, null, null, false, "User search criteria length should be not less then 3 characters. Actual length : 2." },
                    new object[] { "0123456789"
                    + "0123456789"
                    + "0123456789"
                    + "0123456789"
                    + "0123456789"
                    + "0123456789"
                    + "0123456789"
                    + "0123456789"
                    + "0123456789"
                    + "0123456789" + "1", null, null, null, null, null, null, false, "User search criteria length should be not greater then 100 characters. Actual length : 101."},
                    new object[] { "test!", null, null, null, null, null, null, false, "User search criteria contains unacceptable characters. User search criteria should match the RegEx: ^[a-zA-Z0-9-_.' ()&]+$" },
                    new object[] { "test@", null, null, null, null, null, null, false, "User search criteria contains unacceptable characters. User search criteria should match the RegEx: ^[a-zA-Z0-9-_.' ()&]+$" },
                    new object[] { "test#", null, null, null, null, null, null, false, "User search criteria contains unacceptable characters. User search criteria should match the RegEx: ^[a-zA-Z0-9-_.' ()&]+$" },
                    new object[] { "test$", null, null, null, null, null, null, false, "User search criteria contains unacceptable characters. User search criteria should match the RegEx: ^[a-zA-Z0-9-_.' ()&]+$" },
                    new object[] { "test%", null, null, null, null, null, null, false, "User search criteria contains unacceptable characters. User search criteria should match the RegEx: ^[a-zA-Z0-9-_.' ()&]+$" },
                    new object[] { "test^", null, null, null, null, null, null, false, "User search criteria contains unacceptable characters. User search criteria should match the RegEx: ^[a-zA-Z0-9-_.' ()&]+$" },
                    new object[] { "test*", null, null, null, null, null, null, false, "User search criteria contains unacceptable characters. User search criteria should match the RegEx: ^[a-zA-Z0-9-_.' ()&]+$" },
                    new object[] { "test+", null, null, null, null, null, null, false, "User search criteria contains unacceptable characters. User search criteria should match the RegEx: ^[a-zA-Z0-9-_.' ()&]+$" },
                    new object[] { "test*", null, null, null, null, null, null, false, "User search criteria contains unacceptable characters. User search criteria should match the RegEx: ^[a-zA-Z0-9-_.' ()&]+$" },
                    new object[] { "test/", null, null, null, null, null, null, false, "User search criteria contains unacceptable characters. User search criteria should match the RegEx: ^[a-zA-Z0-9-_.' ()&]+$" },
                    new object[] { "test[", null, null, null, null, null, null, false, "User search criteria contains unacceptable characters. User search criteria should match the RegEx: ^[a-zA-Z0-9-_.' ()&]+$" },
                    new object[] { "test]", null, null, null, null, null, null, false, "User search criteria contains unacceptable characters. User search criteria should match the RegEx: ^[a-zA-Z0-9-_.' ()&]+$" },
                    new object[] { "test{", null, null, null, null, null, null, false, "User search criteria contains unacceptable characters. User search criteria should match the RegEx: ^[a-zA-Z0-9-_.' ()&]+$" },
                    new object[] { "test}", null, null, null, null, null, null, false, "User search criteria contains unacceptable characters. User search criteria should match the RegEx: ^[a-zA-Z0-9-_.' ()&]+$" },
                    new object[] { "test|", null, null, null, null, null, null, false, "User search criteria contains unacceptable characters. User search criteria should match the RegEx: ^[a-zA-Z0-9-_.' ()&]+$" },
                    new object[] { "test:", null, null, null, null, null, null, false, "User search criteria contains unacceptable characters. User search criteria should match the RegEx: ^[a-zA-Z0-9-_.' ()&]+$" },
                    new object[] { "test?", null, null, null, null, null, null, false, "User search criteria contains unacceptable characters. User search criteria should match the RegEx: ^[a-zA-Z0-9-_.' ()&]+$" },
                    new object[] { "test<", null, null, null, null, null, null, false, "User search criteria contains unacceptable characters. User search criteria should match the RegEx: ^[a-zA-Z0-9-_.' ()&]+$" },
                    new object[] { "test>", null, null, null, null, null, null, false, "User search criteria contains unacceptable characters. User search criteria should match the RegEx: ^[a-zA-Z0-9-_.' ()&]+$" },
                    new object[] { "test;", null, null, null, null, null, null, false, "User search criteria contains unacceptable characters. User search criteria should match the RegEx: ^[a-zA-Z0-9-_.' ()&]+$" },
                    new object[] { "test,", null, null, null, null, null, null, false, "User search criteria contains unacceptable characters. User search criteria should match the RegEx: ^[a-zA-Z0-9-_.' ()&]+$" },
                    new object[] { "test/", null, null, null, null, null, null, false, "User search criteria contains unacceptable characters. User search criteria should match the RegEx: ^[a-zA-Z0-9-_.' ()&]+$" },
                    new object[] { "test\\", null, null, null, null, null, null, false, "User search criteria contains unacceptable characters. User search criteria should match the RegEx: ^[a-zA-Z0-9-_.' ()&]+$" },
                    new object[] { "test", null, null, null, null, null, null, false, null },
                    new object[] { "test", "testNativeGuid", null, 0x0002, null, null, null, false, null },
                    new object[] { "test", "testNativeGuid", null, 0x1112, null, null, null, false, null },
                    new object[] { "test", "testNativeGuid", "testSAMAccountName", 0x0001, "testDisplayName", "testDisplayName", "DOMAIN\\testSAMAccountName", true, null },
                    new object[] { "abcdefghijklmopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-_.' ()", "testNativeGuid", "testSAMAccountName", 0, "testDisplayName", "testDisplayName", "DOMAIN\\testSAMAccountName", true, null },
                };
            }
        }

        public static string GetTestName(MethodInfo methodInfo, object[] data)
        {
            StringBuilder testNameBuilder = new StringBuilder(string.Format("{0} for search criteria '{1}'. Parameters: ", methodInfo.Name, data[0]));

            int parametersToOutput;
            if (methodInfo.Name == "TestSearchUsers")
            {
                parametersToOutput = 4;
            }
            else
            {
                parametersToOutput = 2;
            }

            for (int i = 1; i <= parametersToOutput; i++)
            {
                object testParameter = data[i];
                testNameBuilder.Append(testParameter ?? "null");

                if (i != parametersToOutput)
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
        [DynamicData(nameof(SearchUsersData), DynamicDataDisplayName = nameof(GetTestName))]
        public void TestSearchUsers(
            string testUserSearchCriteria,
            string testNativeGuid,
            string testSAMAccountName,
            int testUserAccountControlValue,
            string testDisplayName,
            string expectedDisplayName,
            string expectedFullLogonName,
            bool searchResultsExpected,
            string expectedExceptionMessage)
        {
            using (ShimsContext.Create())
            {
                ShimPropertyCollection shimPropertyCollection = new ShimPropertyCollection();
                shimPropertyCollection.ItemGetString = (propertyName) =>
                {
                    switch (propertyName)
                    {
                        case "SAMAccountName":
                            {
                                ShimPropertyValueCollection shimPropertyValueCollection = new ShimPropertyValueCollection();
                                shimPropertyValueCollection.ItemGetInt32 = (i) => testSAMAccountName;
                                return shimPropertyValueCollection;
                            }
                        case "userAccountControl":
                            {
                                ShimPropertyValueCollection shimPropertyValueCollection = new ShimPropertyValueCollection();
                                shimPropertyValueCollection.ValueGet = () => testUserAccountControlValue;
                                return shimPropertyValueCollection;
                            }
                        case "DisplayName":
                            {
                                ShimPropertyValueCollection shimPropertyValueCollection = new ShimPropertyValueCollection();
                                shimPropertyValueCollection.ItemGetInt32 = (i) => testDisplayName;
                                return shimPropertyValueCollection;
                            }
                        default:
                            {
                                return null;
                            }
                    }
                };
                shimPropertyCollection.ContainsString = (propertyName) =>
                {
                    switch (propertyName)
                    {
                        case "SAMAccountName":
                            {
                                return true;
                            }
                        case "userAccountControl":
                            {
                                return true;
                            }
                        case "DisplayName":
                            {
                                return true;
                            }
                        default:
                            {
                                return false;
                            }
                    }
                };
                ShimDirectoryEntry shimDirectoryEntry = new ShimDirectoryEntry();
                shimDirectoryEntry.PropertiesGet = () => shimPropertyCollection;
                shimDirectoryEntry.NativeGuidGet = () => testNativeGuid;

                ShimSearchResult shimSearchResult = new ShimSearchResult();
                shimSearchResult.GetDirectoryEntry = () => shimDirectoryEntry;

                ShimSearchResultCollection shimSearchResultCollection = new ShimSearchResultCollection();
                shimSearchResultCollection.GetEnumerator = () => new List<SearchResult>() { shimSearchResult }.GetEnumerator();

                ShimDirectorySearcher shimDirectorySearcher = new ShimDirectorySearcher
                {
                    PropertiesToLoadGet = () => new StringCollection(),
                    FilterSetString = (s) => { },
                    FindAll = () => shimSearchResultCollection
                };

                IConfigurationRoot configurationRoot = new ConfigurationBuilder().AddJsonFile("appsettings.json").Build();
                IConfigurationSettings configurationSettingsEngine = new ConfigurationSettings(configurationRoot);

                using (var directorySearchController = new DirectorySearchController(shimDirectorySearcher, configurationSettingsEngine))
                {
                    if (!string.IsNullOrEmpty(expectedExceptionMessage))
                    {
                        var failedUsers = directorySearchController.SearchUsers(testUserSearchCriteria);
                        var failedObjectResult = failedUsers as ObjectResult;

                        Assert.IsNotNull(failedObjectResult);
                        Assert.AreEqual(failedObjectResult.StatusCode, StatusCodes.Status500InternalServerError);
                        var responseMsg = failedObjectResult.Value as HttpResponseMessage;

                        Assert.AreEqual(responseMsg?.Content.ReadAsStringAsync().Result, expectedExceptionMessage);
                        return;
                    }

                    var actualUserSearchResults = directorySearchController.SearchUsers(testUserSearchCriteria);
                    var okObjectResult = actualUserSearchResults as OkObjectResult;
                    var users = okObjectResult?.Value as IEnumerable<UserSearchResult>;

                    Assert.IsNotNull(users, "Returned collection is null.");

                    if (!searchResultsExpected)
                    {
                        Assert.IsFalse(users?.Any(), "Returned collection is not empty.");

                        return;
                    }

                    Assert.IsNotNull(actualUserSearchResults, "Returned collection is null.");
                    Assert.IsTrue(users.Any(), "Returned collection is empty.");
                    Assert.AreEqual(expectedDisplayName, users.First().DisplayName, "DisplayName is incorrect.");
                    Assert.AreEqual(expectedFullLogonName, users.First().FullLogonName, "FullLogonName is incorrect.");
                }
            }
        }

        public static IEnumerable<object[]> SearchGroupsData
        {
            get
            {
                return new[]
                {
                    new object[] { "te", null, null, null, null, false, "Group search criteria length should be not less then 3 characters. Actual length : 2." },
                    new object[] { "0123456789"
                    + "0123456789"
                    + "0123456789"
                    + "0123456789"
                    + "0123456789"
                    + "0123456789"
                    + "0123456789"
                    + "0123456789"
                    + "0123456789"
                    + "0123456789" + "1", null, null, null, null, false, "Group search criteria length should be not greater then 100 characters. Actual length : 101."},
                    new object[] { "test!", null, null, null, null, false, "Group search criteria contains unacceptable characters. Group search criteria should match the RegEx: ^[a-zA-Z0-9-_.' ()&]+$" },
                    new object[] { "test@", null, null, null, null, false, "Group search criteria contains unacceptable characters. Group search criteria should match the RegEx: ^[a-zA-Z0-9-_.' ()&]+$" },
                    new object[] { "test#", null, null, null, null, false, "Group search criteria contains unacceptable characters. Group search criteria should match the RegEx: ^[a-zA-Z0-9-_.' ()&]+$" },
                    new object[] { "test$", null, null, null, null, false, "Group search criteria contains unacceptable characters. Group search criteria should match the RegEx: ^[a-zA-Z0-9-_.' ()&]+$" },
                    new object[] { "test%", null, null, null, null, false, "Group search criteria contains unacceptable characters. Group search criteria should match the RegEx: ^[a-zA-Z0-9-_.' ()&]+$" },
                    new object[] { "test^", null, null, null, null, false, "Group search criteria contains unacceptable characters. Group search criteria should match the RegEx: ^[a-zA-Z0-9-_.' ()&]+$" },
                    new object[] { "test*", null, null, null, null, false, "Group search criteria contains unacceptable characters. Group search criteria should match the RegEx: ^[a-zA-Z0-9-_.' ()&]+$" },
                    new object[] { "test+", null, null, null, null, false, "Group search criteria contains unacceptable characters. Group search criteria should match the RegEx: ^[a-zA-Z0-9-_.' ()&]+$" },
                    new object[] { "test*", null, null, null, null, false, "Group search criteria contains unacceptable characters. Group search criteria should match the RegEx: ^[a-zA-Z0-9-_.' ()&]+$" },
                    new object[] { "test/", null, null, null, null, false, "Group search criteria contains unacceptable characters. Group search criteria should match the RegEx: ^[a-zA-Z0-9-_.' ()&]+$" },
                    new object[] { "test[", null, null, null, null, false, "Group search criteria contains unacceptable characters. Group search criteria should match the RegEx: ^[a-zA-Z0-9-_.' ()&]+$" },
                    new object[] { "test]", null, null, null, null, false, "Group search criteria contains unacceptable characters. Group search criteria should match the RegEx: ^[a-zA-Z0-9-_.' ()&]+$" },
                    new object[] { "test{", null, null, null, null, false, "Group search criteria contains unacceptable characters. Group search criteria should match the RegEx: ^[a-zA-Z0-9-_.' ()&]+$" },
                    new object[] { "test}", null, null, null, null, false, "Group search criteria contains unacceptable characters. Group search criteria should match the RegEx: ^[a-zA-Z0-9-_.' ()&]+$" },
                    new object[] { "test|", null, null, null, null, false, "Group search criteria contains unacceptable characters. Group search criteria should match the RegEx: ^[a-zA-Z0-9-_.' ()&]+$" },
                    new object[] { "test:", null, null, null, null, false, "Group search criteria contains unacceptable characters. Group search criteria should match the RegEx: ^[a-zA-Z0-9-_.' ()&]+$" },
                    new object[] { "test?", null, null, null, null, false, "Group search criteria contains unacceptable characters. Group search criteria should match the RegEx: ^[a-zA-Z0-9-_.' ()&]+$" },
                    new object[] { "test<", null, null, null, null, false, "Group search criteria contains unacceptable characters. Group search criteria should match the RegEx: ^[a-zA-Z0-9-_.' ()&]+$" },
                    new object[] { "test>", null, null, null, null, false, "Group search criteria contains unacceptable characters. Group search criteria should match the RegEx: ^[a-zA-Z0-9-_.' ()&]+$" },
                    new object[] { "test;", null, null, null, null, false, "Group search criteria contains unacceptable characters. Group search criteria should match the RegEx: ^[a-zA-Z0-9-_.' ()&]+$" },
                    new object[] { "test,", null, null, null, null, false, "Group search criteria contains unacceptable characters. Group search criteria should match the RegEx: ^[a-zA-Z0-9-_.' ()&]+$" },
                    new object[] { "test/", null, null, null, null, false, "Group search criteria contains unacceptable characters. Group search criteria should match the RegEx: ^[a-zA-Z0-9-_.' ()&]+$" },
                    new object[] { "test\\", null, null, null, null, false, "Group search criteria contains unacceptable characters. Group search criteria should match the RegEx: ^[a-zA-Z0-9-_.' ()&]+$" },
                    new object[] { "test", null, null, null, null, false, null },
                    new object[] { "test", "testNativeGuid", "testName", "testName", "DOMAIN\\testName", true, null },
                    new object[] { "abcdefghijklmopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-_.' ()", "testNativeGuid", "testName", "testName", "DOMAIN\\testName", true, null },
                };
            }
        }

        [TestMethod]
        [DynamicData(nameof(SearchGroupsData), DynamicDataDisplayName = nameof(GetTestName))]
        public void TestSearchGroups(
            string testGroupSearchCriteria,
            string testNativeGuid,
            string testName,
            string expectedDisplayName,
            string expectedFullLogonName,
            bool searchResultsExpected,
            string expectedExceptionMessage)
        {
            using (ShimsContext.Create())
            {
                ShimPropertyCollection shimPropertyCollection = new ShimPropertyCollection();
                shimPropertyCollection.ItemGetString = (propertyName) =>
                {
                    switch (propertyName)
                    {
                        case "Name":
                            {
                                ShimPropertyValueCollection shimPropertyValueCollection = new ShimPropertyValueCollection();
                                shimPropertyValueCollection.ItemGetInt32 = (i) => testName;
                                return shimPropertyValueCollection;
                            }
                        default:
                            {
                                return null;
                            }
                    }
                };
                shimPropertyCollection.ContainsString = (propertyName) =>
                {
                    switch (propertyName)
                    {
                        case "Name":
                            {
                                return true;
                            }
                        default:
                            {
                                return false;
                            }
                    }
                };

                ShimDirectoryEntry shimDirectoryEntry = new ShimDirectoryEntry();
                shimDirectoryEntry.PropertiesGet = () => shimPropertyCollection;
                shimDirectoryEntry.NativeGuidGet = () => testNativeGuid;

                ShimSearchResult shimSearchResult = new ShimSearchResult();
                shimSearchResult.GetDirectoryEntry = () => shimDirectoryEntry;

                ShimSearchResultCollection shimSearchResultCollection = new ShimSearchResultCollection();
                shimSearchResultCollection.GetEnumerator = () => new List<SearchResult>() { shimSearchResult }.GetEnumerator();

                ShimDirectorySearcher shimDirectorySearcher = new ShimDirectorySearcher
                {
                    PropertiesToLoadGet = () => new StringCollection(),
                    FilterSetString = (s) => { },
                    FindAll = () => shimSearchResultCollection
                };

                IConfigurationRoot configurationRoot = new ConfigurationBuilder().AddJsonFile("appsettings.json").Build();
                IConfigurationSettings configurationSettingsEngine = new ConfigurationSettings(configurationRoot);

                using (var directorySearchController = new DirectorySearchController(shimDirectorySearcher, configurationSettingsEngine))
                {
                    if (!string.IsNullOrEmpty(expectedExceptionMessage))
                    {
                        var failedUsers = directorySearchController.SearchGroups(testGroupSearchCriteria);
                        var failedObjectResult = failedUsers as ObjectResult;

                        Assert.IsNotNull(failedObjectResult);
                        Assert.AreEqual(failedObjectResult.StatusCode, StatusCodes.Status500InternalServerError);
                        var responseMsg = failedObjectResult.Value as HttpResponseMessage;

                        Assert.AreEqual(responseMsg?.Content.ReadAsStringAsync().Result, expectedExceptionMessage);

                        return;
                    }

                    var actualGroupSearchResults = directorySearchController.SearchGroups(testGroupSearchCriteria);

                    var okObjectResult = actualGroupSearchResults as OkObjectResult;
                    var groups = okObjectResult?.Value as IEnumerable<GroupSearchResult>;

                    Assert.IsNotNull(actualGroupSearchResults, "Returned collection is null.");

                    if (!searchResultsExpected)
                    {
                        Assert.IsFalse(groups.Any(), "Returned collection is not empty.");

                        return;
                    }

                    Assert.IsNotNull(actualGroupSearchResults, "Returned collection is null.");
                    Assert.IsTrue(groups.Any(), "Returned collection is empty.");
                    Assert.AreEqual(expectedDisplayName, groups.First().DisplayName, "DisplayName is incorrect.");
                    Assert.AreEqual(expectedFullLogonName, groups.First().FullLogonName, "FullLogonName is incorrect.");
                }
            }
        }

    }
}
