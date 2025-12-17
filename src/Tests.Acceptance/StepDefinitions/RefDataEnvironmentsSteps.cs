using Dorc.ApiModel;
using System.Text.Json;
using RestSharp;
using Tests.Acceptance.Support;
using Reqnroll;

namespace Tests.Acceptance.StepDefinitions
{
    [Binding]
    public class RefDataEnvironmentsSteps
    {
        private ApiResult<List<EnvironmentShortApiModel>> _environmentsApiResult = default!;
        private ApiResult<List<EnvironmentApiModel>> envDetailsApiResult = default!;
        private ApiResult<EnvironmentApiModel> addEnvironmentApiResult = default!;

        private int existingEnvironmentId;

        [Given(@"I have Created GET request to  RefDataEnvironments")]
        public void GivenIHaveCreatedGetRequestToRefDataEnvironments()
        {
            using (var caller = new ApiCaller())
            {
                _environmentsApiResult = caller.Call<List<EnvironmentShortApiModel>>(Endpoints.RefDataEnvironments, Method.Get);
            }
        }

        [Then(@"The result should be list of environments")]
        public void ThenTheResultShouldBeListOfEnvironments()
        {
            Assert.IsNotNull(_environmentsApiResult, "Api request failed");
            Assert.IsTrue(_environmentsApiResult.IsModelValid, _environmentsApiResult.Message);
            var model = _environmentsApiResult.Model as List<EnvironmentShortApiModel>;
            Assert.IsNotNull(model, "Model is null");
            Assert.AreNotEqual(0, model.Count);
        }

        #region Scenario: Returns environment by name
        [Given(@"There is environment with name '(.*)'")]
        public void GivenThereIsEnvironmentWithName(string environmentName)
        {
            this.existingEnvironmentId = new DataAccessor().GetEnvironments(environmentName).FirstOrDefault();
            if (this.existingEnvironmentId != 0)
            {
                return;
            }

            this.existingEnvironmentId = new DataAccessor().CreateEnvironment(environmentName);
        }

        [Given(@"I have created GET request to RefDataEnvironments with query '(.*)'='(.*)'")]
        public void GivenIHaveCreatedGetRequestToRefDataEnvironmentsWithQuery(string envQueryParameter, string environmentName)
        {
            var caller = new ApiCaller();
            try
            {
                var urlQueryParameters = new Dictionary<string, string>
                {
                    { envQueryParameter, environmentName }
                };

                envDetailsApiResult = caller.Call<List<EnvironmentApiModel>>(
                    Endpoints.RefDataEnvironments,
                    Method.Get,
                    queryParameters: urlQueryParameters);
            }
            catch (Exception)
            {
                new DataAccessor().DeleteEnvironment(this.existingEnvironmentId);
                throw;
            }
            finally
            {
                caller.Dispose();
            }
        }

        [Then(@"The result should be environment with name '(.*)' and id of the existing environment")]
        public void ThenTheResultShouldBeEnvironmentWithNameAndId(string environmentName)
        {
            try
            {
                Assert.IsNotNull(envDetailsApiResult, "Api request failed!");
                Assert.IsTrue(envDetailsApiResult.IsModelValid, envDetailsApiResult.Message);
                var model = envDetailsApiResult.Model as List<EnvironmentApiModel>;
                Assert.AreEqual(environmentName, model?[0].EnvironmentName);
                Assert.AreEqual(this.existingEnvironmentId, model?[0].EnvironmentId);
            }
            catch (Exception)
            {
                throw;
            }
            finally
            {
                new DataAccessor().DeleteEnvironment(this.existingEnvironmentId);
            }
        }
        #endregion

        [Given(@"I have created POST request with body")]
        public void GivenIHaveCreatedPostRequestWithBody(string multilineText)
        {
            using (var caller = new ApiCaller())
            {
                addEnvironmentApiResult = caller.Call<EnvironmentApiModel>(
                                Endpoints.RefDataEnvironments,
                                Method.Post,
                                body: multilineText);
            }
        }

        [When(@"I edit the '(.*)' environment via PUT and set parentId equals to ID of the environment with name '(.*)'")]
        public void WhenISetEnvironmentAsChildOfAnother(string childEnvName, string parentEnvName)
        {
            var parentEnvironmentId = new DataAccessor().GetEnvironments(parentEnvName).FirstOrDefault();

            using (var caller = new ApiCaller())
            {
                var childEnvApiResponse = caller.Call<IList<EnvironmentApiModel>>(
                    Endpoints.RefDataEnvironments,
                    Method.Get,
                    queryParameters: new Dictionary<string, string>
                    {
                        { "env", childEnvName }
                    });

                if (childEnvApiResponse.IsModelValid)
                {
                    var models = childEnvApiResponse.Model as IList<EnvironmentApiModel>;
                    var model = models?.FirstOrDefault();
                    if (model == null) throw new InvalidOperationException("Model is null");
                    model.ParentId = parentEnvironmentId;

                    addEnvironmentApiResult = caller.Call<EnvironmentApiModel>(
                                    Endpoints.RefDataEnvironments,
                                    Method.Put,
                                    body: JsonSerializer.Serialize(model));
                }
            }
        }

        [Then(@"The result should be Environment with id greater than '(.*)'")]
        public void ThenTheResultShouldBeEnvironmentWithIdGreaterThan(int lowestId)
        {
            Assert.IsNotNull(addEnvironmentApiResult, "Api request failed");
            Assert.IsTrue(addEnvironmentApiResult.IsModelValid, addEnvironmentApiResult.Message);
            var model = addEnvironmentApiResult.Model as EnvironmentApiModel;
            Assert.IsNotNull(model);
            Assert.AreNotEqual(lowestId, model.EnvironmentId);
        }

        [Then(@"The result should be Environment with Parent")]
        public void ThenTheResultShouldBeEnvironmentWithParent()
        {
            EnvironmentApiModel? model = null;
            try
            {
                Assert.IsNotNull(addEnvironmentApiResult, "Api request failed");
                Assert.IsTrue(addEnvironmentApiResult.IsModelValid, addEnvironmentApiResult.Message);
                model = addEnvironmentApiResult.Model as EnvironmentApiModel;
                Assert.IsNotNull(model);
                Assert.IsNotNull(model.ParentEnvironment);
            }
            finally
            {
                new DataAccessor().DeleteEnvironment(this.existingEnvironmentId);
                if (model is not null)
                    new DataAccessor().DeleteEnvironment(model.ParentEnvironment.EnvironmentId);
            }
        }
    }
}
