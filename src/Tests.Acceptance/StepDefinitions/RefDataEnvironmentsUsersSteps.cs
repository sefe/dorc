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
            Assert.AreEqual(userCount, model.Count);
        }
        #endregion
    }
}
