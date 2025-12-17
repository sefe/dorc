using Dorc.ApiModel;
using Reqnroll;
using RestSharp;
using Tests.Acceptance.Support;

namespace Tests.Acceptance.StepDefinitions
{
    [Binding]
    public class RefDataPermissionSteps
    {
        private ApiResult<List<PermissionDto>> permissionsListApiResult = default!;
        private ApiResult<List<UserPermDto>> userPermissionsApiResult = default!;
        private ApiResult<string> addPermissionApiResult = default!;
        private ApiResult<string> deletePermissionApiResult = default!;

        [Given(@"I have created GET request on RefDataPermission")]
        public void GivenIHaveCreatedGETRequestOnRefDataPermission()
        {
            using (var caller = new ApiCaller())
            {
                permissionsListApiResult = caller.Call<List<PermissionDto>>(Endpoints.RefDataPermission, Method.Get);
            }
        }

        [Then(@"The result should be list of permissions")]
        public void ThenTheResultShouldBeListOfPermissions()
        {
            Assert.IsNotNull(permissionsListApiResult, "Request failed!");
            Assert.IsTrue(permissionsListApiResult.IsModelValid, permissionsListApiResult.Message);
            var model = permissionsListApiResult.Model as List<PermissionDto>;
            Assert.IsNotNull(model, $"Model not valid, raw data: {permissionsListApiResult.RawJson}");
            Assert.AreNotEqual(0, model.Count);
        }

        [Given(@"I have created GET request on RefDataUserPermissions with '(.*)' as '(.*)' and '(.*)' as '(.*)' and '(.*)' as '(.*)'")]
        public void GivenIHaveCreatedGETRequestOnRefDataPermissionWithAsAndAs(
            int userId,
            string userIdParameterName,
            int databaseId,
            string databaseIdParameterName,
            int environmentId,
            string environmentIdParameterName)
        {
            var urlQueryParameters = new Dictionary<string, string>
            {
                { userIdParameterName, userId.ToString() },
                { databaseIdParameterName, databaseId.ToString() },
                { environmentIdParameterName, environmentId.ToString() }
            };

            using (var caller = new ApiCaller())
            {
                userPermissionsApiResult = caller.Call<List<UserPermDto>>(
                                Endpoints.RefDataUserPermissions,
                                Method.Get,
                                queryParameters: urlQueryParameters);
            }
        }

        [Then(@"The result should be list with '(.*)' permission which name is '(.*)'")]
        public void ThenTheResultShouldBeListWithPermissionWhichNameIs(
            int userPermissionCount,
            string permissionName)
        {
            Assert.IsNotNull(userPermissionsApiResult, "API Request failed!");
            Assert.IsTrue(userPermissionsApiResult.IsModelValid, userPermissionsApiResult.Message);
            var model = userPermissionsApiResult.Model as List<UserPermDto>;
            Assert.IsNotNull(model, $"Model not valid, raw data: {userPermissionsApiResult.RawJson}");
            Assert.HasCount(userPermissionCount, model);
            var permission = model.First(p => p.Role == permissionName);
            Assert.IsNotNull(permission, $"Permission {permissionName} not found in result collection!");
        }

        [Given(
            @"I have created PUT request to RefDataUserPermissions '(.*)' as '(.*)' and '(.*)' as '(.*)' and '(.*)' as '(.*)' and '(.*)' as '(.*)'")]
        public void GivenIHaveCreatedPutRequestToRefDataPermissionAsAndAsAndAs(
            int userId,
            string userIdParameterName,
            int databaseId,
            string databaseIdParameterName,
            int permissionId,
            string permissionIdParameterName,
            int environmentId,
            string environmentIdParameterName)
        {
            var urlQueryParameters = new Dictionary<string, string>
            {
                { userIdParameterName, userId.ToString() },
                { databaseIdParameterName, databaseId.ToString() },
                { permissionIdParameterName, permissionId.ToString() },
                { environmentIdParameterName, environmentId.ToString() }
            };

            using (var caller = new ApiCaller())
            {
                addPermissionApiResult = caller.Call<string>(
                                Endpoints.RefDataUserPermissions,
                                Method.Put,
                                queryParameters: urlQueryParameters);
            }
        }

        [Then(@"The result should be '(.*)'")]
        public void ThenTheResultShouldBe(string isPermissionCreated)
        {
            Assert.IsNotNull(addPermissionApiResult, "API request failed");
            Assert.IsTrue(addPermissionApiResult.IsModelValid, addPermissionApiResult.Message);
            var model = addPermissionApiResult.Model as string;
            Assert.AreEqual(isPermissionCreated, model);
        }

        [Given(
            @"I have created DELETE request to RefDataUserPermissions '(.*)' as '(.*)' and '(.*)' as '(.*)' and '(.*)' as '(.*)' and '(.*)' as '(.*)'")]
        public void GivenIHaveCreatedDeleteRequestToRefDataPermissionAsAndAsAndAs(
            int userId,
            string userIdParameterName,
            int databaseId,
            string databaseIdParameterName,
            int permissionId,
            string permissionIdParameterName,
            int environmentId,
            string environmentIdParameterName)
        {
            var urlQueryParameters = new Dictionary<string, string>
            {
                { userIdParameterName, userId.ToString() },
                { databaseIdParameterName, databaseId.ToString() },
                { permissionIdParameterName, permissionId.ToString() },
                { environmentIdParameterName, environmentId.ToString() }
            };

            using (var caller = new ApiCaller())
            {
                deletePermissionApiResult = caller.Call<string>(
                                Endpoints.RefDataUserPermissions,
                                Method.Delete,
                                queryParameters: urlQueryParameters);
            }
        }

        [Then(@"The result should be '(.*)' in request")]
        public void ThenTheResultShouldBeInRequest(string isPermissionDeleted)
        {
            Assert.IsNotNull(deletePermissionApiResult, "API request failed");
            Assert.IsTrue(deletePermissionApiResult.IsModelValid, deletePermissionApiResult.Message);
            var model = deletePermissionApiResult.Model as string;
            Assert.AreEqual(isPermissionDeleted, model);
        }
    }
}