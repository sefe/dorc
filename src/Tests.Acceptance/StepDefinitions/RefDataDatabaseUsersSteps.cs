using Dorc.ApiModel;
using RestSharp;
using Tests.Acceptance.Support;

namespace Tests.Acceptance.StepDefinitions
{
    [Binding]
    public class RefDataDatabaseUsersSteps
    {
        private ApiResult<List<UserApiModel>> result = default!;

        [Given(@"I have created GET request to RefDataDatabaseUsers with query '(.*)'='(.*)' and '(.*)'='(.*)'")]
        public void GivenIHaveCreatedGETRequestToRefDataDatabaseUsersWithQuery(
            string userIdParameterName,
            int userId,
            string environmentIdParameterName,
            int environmentId)
        {
            var urlQueryParameters = new Dictionary<string, string>
            {
                { userIdParameterName, userId.ToString() },
                { environmentIdParameterName, environmentId.ToString() }
            };

            using (var caller = new ApiCaller())
            {
                result = caller.Call<List<UserApiModel>>(
                                Endpoints.RefDataDatabaseUsers,
                                Method.Get,
                                queryParameters: urlQueryParameters);
            }
        }

        [Then(@"The result should be list of '(.*)' users")]
        public void ThenTheResultShouldBeListOfUsers(int userCount)
        {
            Assert.IsNotNull(result, "Api request failed!");
            Assert.IsTrue(result.IsModelValid, result.Message);
            var model = result.Model as List<UserApiModel>;
            Assert.IsNotNull(model);
            Assert.AreEqual(userCount, model.Count);
        }
    }
}
