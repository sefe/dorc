using Dorc.ApiModel;
using Reqnroll;
using RestSharp;
using Tests.Acceptance.Support;

namespace Tests.Acceptance.StepDefinitions
{
    [Binding]
    public class RefDataEnvironmentsUsersSteps
    {
        private ApiResult<List<UserApiModel>> endurUsersApiResult = default!;
        private ApiResult<List<UserApiModel>> environmentUsersApiResult = default!;

        private int environmentUserCount;

        #region Scenario: Get Endur users for environment id
        [Given(@"I have created GET request to RefDataEnvironmentsUsers with query parameters '(.*)'='(.*)' and '(.*)'='(.*)'")]
        public void GivenIHaveCreatedGETRequestToRefDataEnvironmentsUsersWithQueryParametersAnd(
           string idParameterName,
           int environmentId,
           string typeParameterName,
           string environmentType)
        {
            var urlQueryParameters = new Dictionary<string, string>
            {
                { idParameterName, environmentId.ToString() },
                { typeParameterName, environmentType }
            };

            using (var caller = new ApiCaller())
            {
                endurUsersApiResult = caller.Call<List<UserApiModel>>(
                                    Endpoints.RefDataEnvironmentsUsers,
                                    Method.Get,
                                    queryParameters: urlQueryParameters);
            }
        }


        [Then(@"The result should be list of endur users with amount '(.*)'")]
        public void ThenTheResultShouldBeListOfEndurUsersWithAmount(int userCount)
        {
            Assert.IsNotNull(endurUsersApiResult, "Api request failed");
            Assert.IsTrue(endurUsersApiResult.IsModelValid, endurUsersApiResult.Message);
            var model = endurUsersApiResult.Model as List<UserApiModel>;
            Assert.IsNotNull(model, $"Model not valid, raw data: {endurUsersApiResult.RawJson}");
            Assert.HasCount(userCount, model);
        }
        #endregion

        #region Scenario: Get users for Environment
        [Given(@"The number of users for the environment with id '(.*)' is known")]
        public void GivenTheNumberOfUsersForTheEnvironmentWithIdIsKnown(int environmentId)
        {
            this.environmentUserCount = new DataAccessor().GetEnvironmentUserCount(environmentId);
        }

        [Given(@"I have created GET request to RefDataEnvironmentsUsers with query '(.*)'='(.*)'")]
        public void GivenIHaveCreatedGETRequestToRefDataEnvironmentsUsersWithQuery(string userIdParameterName, int environmentId)
        {
            var segments = new List<string> { environmentId.ToString() };

            using (var caller = new ApiCaller())
            {
                environmentUsersApiResult = caller.Call<List<UserApiModel>>(
                                Endpoints.RefDataEnvironmentsUsers,
                                Method.Get,
                                segments: segments);
            }
        }

        [Then(@"The result should be list of environment users with known amount")]
        public void ThenTheResultShouldBeListOfEnvironmentUsersWithKnownAmount()
        {
            Assert.IsNotNull(environmentUsersApiResult, "Api request failed");
            Assert.IsTrue(environmentUsersApiResult.IsModelValid, environmentUsersApiResult.Message);
            var model = environmentUsersApiResult.Model as List<UserApiModel>;
            Assert.IsNotNull(model, $"Model not valid, raw data: {environmentUsersApiResult.RawJson}");
            Assert.HasCount(this.environmentUserCount, model);
        }
        #endregion
    }
}
