using Dorc.ApiModel;
using Reqnroll;
using RestSharp;
using Tests.Acceptance.Support;

namespace Tests.Acceptance.StepDefinitions
{
    [Binding]
    public class RefDataEnvironmentsHistorySteps
    {
        private ApiResult<List<EnvironmentHistoryApiModel>> historyApiResult = default!;

        [Given(@"I have created RefDataEnvironments with '(.*)'='(.*)'")]
        public void GivenIHaveCreatedRefDataEnvironmentsWith(string idParameterName, int id)
        {
            var urlQueryParameters = new Dictionary<string, string>
            {
                { idParameterName, id.ToString() }
            };

            using (var caller = new ApiCaller())
            {
                historyApiResult = caller.Call<List<EnvironmentHistoryApiModel>>(
                                Endpoints.RefDataEnvironmentsHistory,
                                Method.Get,
                                queryParameters: urlQueryParameters);
            }
        }

        [Then(@"The result should contain list of history items with environment name '(.*)'")]
        public void ThenTheResultShouldContainListOfHistoryItemsWithEnvironmentName(string environmentName)
        {
            Assert.IsNotNull(historyApiResult, "Api request failed!");
            Assert.IsTrue(historyApiResult.IsModelValid, historyApiResult.Message);
            var model = historyApiResult.Model as List<EnvironmentHistoryApiModel>;
            Assert.IsNotNull(model, "Model not valid");
            var result = model.TrueForAll(h => h.EnvName.Equals(environmentName));
            Assert.IsTrue(result, $"History contains environments differ than {environmentName}");
        }
    }
}
