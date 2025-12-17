using System.Text.RegularExpressions;
using Dorc.ApiModel;
using FluentAssertions.Execution;
using Reqnroll;
using RestSharp;
using Tests.Acceptance.Support;

namespace Tests.Acceptance.StepDefinitions
{
    [Binding]
    public class RefDataEnvironmentsDetailsSteps
    {
        private readonly ScenarioContext _scenarioContext;

        private ApiResult<TemplateApiModel<EnvironmentApiModel>> projectApiResult = default!;
        private ApiResult<EnvironmentContentApiModel> environmentApiResult = default!;
        private ApiResult<EnvironmentComponentsDto<DatabaseApiModel>> databasesApiResult = default!;
        private ApiResult<EnvironmentComponentsDto<ServerApiModel>> serversApiResult = default!;
        private ApiResult<ApiBoolResult> componentsActionApiResult = default!;

        public RefDataEnvironmentsDetailsSteps(ScenarioContext scenarioContext)
        {
            _scenarioContext = scenarioContext;
        }

        [Given(@"I have created GET request to RefDataEnvironmentsDetails with query '(.*)'='(.*)'")]
        public void GivenIHaveCreatedGETRequestToRefDataEnvironmentsDetailsWithQuery(string idParameterName, string id)
        {
            var urlQueryParameters = new Dictionary<string, string>
            {
                { idParameterName, id },
                { "includeRead", "true" }
            };

            using (var caller = new ApiCaller())
            {
                projectApiResult = caller.Call<TemplateApiModel<EnvironmentApiModel>>(
                                Endpoints.RefDataEnvironmentsDetails,
                                Method.Get,
                                queryParameters: urlQueryParameters);
            }
        }

        [Then(@"The result should be json with project equals '(.*)' and list of environments whose names should contain '(.*)'")]
        public void ThenTheResultShouldBeJsonWithProjectEqualsAndListOfEnvironmentsWhoseNamesShouldContain(string projectName, string environmentName)
        {
            using (new AssertionScope())
            {
                Assert.IsNotNull(projectApiResult, "Api request failed!");
                Assert.IsTrue(projectApiResult.IsModelValid, projectApiResult.Message);

                var model = projectApiResult.Model as TemplateApiModel<EnvironmentApiModel>;

                Assert.IsNotNull(model, "Model is not valid!");
                Assert.AreEqual(projectName, model.Project.ProjectName);
                var rgx = new Regex($"^{environmentName}*");
                var result = model.Items.All(i => rgx.IsMatch(i.EnvironmentName));
                Assert.IsTrue(result);
            }
        }

        [Given(@"I have Created GET request to RefDataEnvironmentsDetails with query '(.*)'='(.*)'")]
        public void GivenIHaveCreatedGETRequestToRefDataEnvironmentsDetailsWithQuery(string idParameterName, int id)
        {
            var segments = new List<string> { id.ToString() };

            using (var caller = new ApiCaller())
            {
                environmentApiResult = caller.Call<EnvironmentContentApiModel>(
                                Endpoints.RefDataEnvironmentsDetails,
                                Method.Get,
                                segments: segments);
            }
        }

        [Then(@"The result should be json with environment '(.*)' and contain '(.*)' and '(.*)'")]
        public void ThenTheResultShouldBeJsonWithEnvironmentAndContainAnd(
            string environmentName,
            int databaseCount,
            int serverCount)
        {
            Assert.IsNotNull(environmentApiResult, "Api request failed!");
            Assert.IsTrue(environmentApiResult.IsModelValid, environmentApiResult.Message);
            var model = environmentApiResult.Model as EnvironmentContentApiModel;
            Assert.IsNotNull(model, "Model is not valid!");
            Assert.AreEqual(environmentName, model.EnvironmentName);
            Assert.AreEqual(databaseCount, model.DbServers.Count());
            Assert.AreEqual(serverCount, model.AppServers.Count());
        }

        [Given(@"I have created Get request on RefDataEnvironmentsDetails with query '(.*)'='(.*)' and '(.*)'='(.*)'")]
        public void GivenIHaveCreatedGetRequestOnRefDataEnvironmentsDetailsWithQueryAnd(
            string idParameterName,
            int id,
            string typeParameterName,
            int typeCount)
        {
            Dictionary<string, string> urlQueryParameters = new Dictionary<string, string>
            {
                { idParameterName, id.ToString() },
                { typeParameterName, typeCount.ToString() }
            };

            using (var caller = new ApiCaller())
            {
                switch (typeCount)
                {
                    case 0:
                        {
                            databasesApiResult = caller.Call<EnvironmentComponentsDto<DatabaseApiModel>>(
                                Endpoints.RefDataEnvironmentsDetails,
                                Method.Get,
                                queryParameters: urlQueryParameters);
                            break;
                        }
                    case 1:
                        {
                            serversApiResult = caller.Call<EnvironmentComponentsDto<ServerApiModel>>(
                                Endpoints.RefDataEnvironmentsDetails,
                                Method.Get,
                                queryParameters: urlQueryParameters);
                            break;
                        }
                }
            }
        }

        [Then(@"the result should be list of '(.*)' with '(.*)' elements")]
        public void ThenTheResultShouldBeListOfWithElements(int typeCount, int count)
        {
            switch (typeCount)
            {
                case 0:
                    {
                        Assert.IsNotNull(databasesApiResult, "Api request failed!");
                        Assert.IsTrue(databasesApiResult.IsModelValid, databasesApiResult.Message);
                        var model = databasesApiResult.Model as EnvironmentComponentsDto<DatabaseApiModel>;
                        Assert.IsNotNull(model, "Model is not valid!");
                        Assert.HasCount(count, model.Result);
                        break;
                    }
                case 1:
                    {
                        Assert.IsNotNull(serversApiResult, "Api request failed!");
                        Assert.IsTrue(serversApiResult.IsModelValid, serversApiResult.Message);
                        var model = serversApiResult.Model as EnvironmentComponentsDto<ServerApiModel>;
                        Assert.IsNotNull(model, "Model is not valid!");
                        Assert.HasCount(count, model.Result);
                        break;
                    }
            }
        }

        [Given(@"I have created PUT request vith query '(.*)'='(.*)' '(.*)'='(.*)' '(.*)'='(.*)' '(.*)'='(.*)'")]
        public void GivenIHaveCreatedPutRequestVithQuery(
            string environmentIdParameterName,
            int environmentId,
            string componentIdParameterName,
            int componentId,
            string actionParameterName,
            string action,
            string componentParameterName,
            string component)
        {
            Dictionary<string, string> query = new Dictionary<string, string>
            {
                { environmentIdParameterName,environmentId.ToString()},
                { componentIdParameterName,componentId.ToString()},
                { actionParameterName,action},
                { componentParameterName,component}
            };

            using (var caller = new ApiCaller())
            {
                componentsActionApiResult = caller.Call<ApiBoolResult>(
                                Endpoints.RefDataEnvironmentsDetails,
                                Method.Put,
                                queryParameters: query);
            }
        }

        [Then(@"The result should be ApiBoolResult with Result '(.*)'")]
        public void ThenTheResultShouldBeApiBoolResultWithResult(string p0)
        {
            bool expected = bool.Parse(p0);
            Assert.IsNotNull(componentsActionApiResult, "Api request failed!");
            Assert.IsTrue(componentsActionApiResult.IsModelValid, componentsActionApiResult.Message);
            var model = componentsActionApiResult.Model as ApiBoolResult;
            Assert.AreEqual(expected, model?.Result);
            Console.WriteLine(model?.Message);
        }

        [Given(@"Environment with name '(.*)' created")]
        public void GivenThereIsEnvironmentWithName(string environmentName)
        {
            var environmentId = new DataAccessor().GetEnvironments(environmentName).FirstOrDefault();
            
            if (environmentId != 0)
            {
                return;
            }

            environmentId = new DataAccessor().CreateEnvironment(environmentName);
            _scenarioContext[$"{environmentName}_id"] = environmentId;
        }

        [When(@"I set parent to '(.*)' environment equals the ID of the environment with name '(.*)'")]
        public void WhenISetParentForEnvironment(string childEnvName, string parentEnvName)
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
                    var segments = new List<string> { "SetParentForEnvironment" };
                    var query = new Dictionary<string, string> { { "parentEnvId", parentEnvironmentId.ToString() }, { "childEnvId", model.EnvironmentId.ToString() } };
                    _scenarioContext["setParentApiResult"] = caller.Call<ApiBoolResult>(
                        Endpoints.RefDataEnvironmentsDetails,
                        Method.Put,
                        segments: segments,
                        queryParameters: query);
                }
            }
        }

        [Then(@"The result should( not)? be Environment '(.*)' with Parent")]
        public void ThenTheResultShouldBeEnvironmentWithParent(string shouldNot, string childEnvironmentName)
        {
            bool expectedResult = string.IsNullOrEmpty(shouldNot);
            try
            {
                var setParentApiResult = _scenarioContext["setParentApiResult"] as ApiResult<ApiBoolResult>;
                Assert.IsNotNull(setParentApiResult, "Api request failed");
                Assert.IsTrue(setParentApiResult!.IsModelValid);

                var boolResult = setParentApiResult.Model as ApiBoolResult;
                Assert.IsNotNull(boolResult, "Model is null");
                Assert.AreEqual(expectedResult, boolResult!.Result);

                using (var caller = new ApiCaller())
                {
                    var getEnvActionApiResult = caller.Call<IEnumerable<EnvironmentApiModel>>(
                                    Endpoints.RefDataEnvironments,
                                    Method.Get,
                                    queryParameters: new Dictionary<string, string> { { "env", childEnvironmentName } });

                    var envList = getEnvActionApiResult.Model as IEnumerable<EnvironmentApiModel>;

                    Assert.IsNotNull(getEnvActionApiResult, "Api request failed!");
                    Assert.IsTrue(getEnvActionApiResult.IsModelValid);
                    Assert.IsNotNull(envList, "Environment list is null");

                    var envModel = envList.FirstOrDefault();
                    Assert.IsNotNull(envModel);
                    if (expectedResult)
                    {
                        Assert.IsNotNull(envModel.ParentId);
                    }
                    else
                    {
                        Assert.IsNull(envModel.ParentId);
                    }
                }
            }
            catch (Exception ex)
            {
                Assert.Fail($"Parent set unsuccessful: {ex.Message}");
            }
            finally
            {
                foreach (var key in _scenarioContext.Keys.Reverse())
                {
                    if (key.EndsWith("_id"))
                    {
                        var idStr = _scenarioContext[key]?.ToString();
                        if (!string.IsNullOrEmpty(idStr))
                            new DataAccessor().DeleteEnvironment(int.Parse(idStr));
                    }
                }
            }
        }
    }
}
