using Dorc.ApiModel;
using RestSharp;
using Tests.Acceptance.Support;

namespace Tests.Acceptance.StepDefinitions
{
    [Binding]
    public class RefDataServersFeatureSteps
    {
        private ApiResult<ServerApiModel> postServerResult = default!;
        private ApiResult<List<ServerApiModel>> getServerResults = default!;
        private ApiResult<ServerApiModel> getServerResult = default!;
        private ApiResult<ApiBoolResult> deleteServerApiResult = default!;

        private int createdServerId = default!;

        [Given(@"I have Created GET request on RefDataServersFeature")]
        public void GivenIHaveCreatedGETRequestOnRefDataServersFeature()
        {
            using (var caller = new ApiCaller())
            {
                getServerResults = caller.Call<List<ServerApiModel>>(
                                Endpoints.RefDataServers,
                                Method.Get,
                                segments: new List<string> { "GetAll" });
            }
        }

        [Then(@"the result should be list of EnvironmentUIComponentApiModel")]
        public void ThenTheResultShouldBeListOfServerApiModel()
        {
            Assert.IsNotNull(getServerResults, "Request failed");
            var model = getServerResults.Model as List<ServerApiModel>;
            Assert.IsNotNull(model, getServerResults.Message);
            Assert.AreNotSame(0, model.Count);
        }

        [Given(@"I have created POST request on RefDataServers with data below in body")]
        public void GivenIHaveCreatedPOSTRequestOnRefDataServersWithDataBelowInBody(string serverModel)
        {
            using (var caller = new ApiCaller())
            {
                postServerResult = caller.Call<ServerApiModel>(
                                Endpoints.RefDataServers,
                                Method.Post,
                                body: serverModel);
            }
        }

        [Then(@"The result should contain  EnvironmentUIComponentApiModel with ServerID greater than '(.*)'")]
        public void ThenTheResultShouldContainServerApiModelWithServerIdGraterThan(int lowestServerId)
        {
            Assert.IsNotNull(postServerResult, "Request failed");
            var model = postServerResult.Model as ServerApiModel;
            Assert.IsNotNull(model, postServerResult.Message);
            Assert.AreNotSame(lowestServerId, model.ServerId);
        }

        [Given(@"I have created GET request on RefDataServers with '(.*)' as '(.*)'")]
        public void GivenIHaveCreatedGETRequestOnRefDataServersWithAs(string serverName, string serverNameParameterName)
        {
            var segments = new List<string>
            {
                serverName
            };

            using (var caller = new ApiCaller())
            {
                getServerResult = caller.Call<ServerApiModel>(
                                Endpoints.RefDataServers,
                                Method.Get,
                                segments: segments);

                var model = getServerResult.Model as ServerApiModel;
                if (model == null)
                {
                    return;
                }
                createdServerId = model.ServerId;
                var urlQueryParameters = new Dictionary<string, string>
                {
                    { "serverId", createdServerId.ToString() }
                };

                deleteServerApiResult = caller.Call<ApiBoolResult>(
                                Endpoints.RefDataServers,
                                Method.Delete,
                                queryParameters: urlQueryParameters);
            }
        }

        [Then(@"the result should be EnvironmentUIComponentApiModel with '(.*)' as ServerName and ServerID must be not equal '(.*)'")]
        public void ThenTheResultShouldBeServerApiModelWithAsServerNameAndServerIdMustBeNotEqual(string serverName, int incorrectServerId)
        {
            Assert.IsNotNull(getServerResult, "Request failed");
            var model = getServerResult.Model as ServerApiModel;
            Assert.IsNotNull(model, getServerResult.Message);
            Assert.AreEqual(serverName, model.Name);
            Assert.AreNotEqual(incorrectServerId, model.ServerId);

            Assert.IsNotNull(deleteServerApiResult, "API request failed");
            Assert.IsTrue(deleteServerApiResult.IsModelValid, deleteServerApiResult.Message);
            var apiBoolResult = deleteServerApiResult.Model as ApiBoolResult;
            Assert.IsNotNull(apiBoolResult, deleteServerApiResult.Message);
            Assert.IsTrue(apiBoolResult.Result);
        }
    }
}