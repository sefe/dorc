using Dorc.ApiModel;
using RestSharp;
using Tests.Acceptance.Support;

namespace Tests.Acceptance.StepDefinitions
{
    [Binding]
    public class RefDataAppServersSteps
    {
        private ApiResult<List<ServerApiModel>> _serversApiResult = default!;

        [Given(@"I have created GET request to RefDataAppServers with query '(.*)'='(.*)'")]
        public void GivenIHaveCreatedGetRequestToRefDataAppServersWithQuery(string query1, int queryArgument1)
        {
            var segments = new List<string>
            {
                queryArgument1.ToString()
            };

            using (var _caller = new ApiCaller())
            {
                _serversApiResult = _caller.Call<List<ServerApiModel>>(
                    Endpoints.RefDataAppServers,
                    Method.Get,
                    segments);
            }
        }

        [Then(@"The result should be non empty list of appservers")]
        public void ThenTheResultShouldBeNonEmptyListOfAppservers()
        {
            Assert.IsNotNull(_serversApiResult);
            Assert.IsTrue(_serversApiResult.IsModelValid, _serversApiResult.Message);
            var model = _serversApiResult.Model as List<ServerApiModel>;
            Assert.IsNotNull(model);
            Assert.IsTrue(model.Any());
        }
    }
}
