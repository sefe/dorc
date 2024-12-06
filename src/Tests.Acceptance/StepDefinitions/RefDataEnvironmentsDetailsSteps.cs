﻿using System.Text.RegularExpressions;
using Dorc.ApiModel;
using FluentAssertions.Execution;
using RestSharp;
using Tests.Acceptance.Support;

namespace Tests.Acceptance.StepDefinitions
{
    [Binding]
    public class RefDataEnvironmentsDetailsSteps
    {
        private ApiResult<TemplateApiModel<EnvironmentApiModel>> projectApiResult = default!;
        private ApiResult<EnvironmentContentApiModel> environmentApiResult = default!;
        private ApiResult<EnvironmentComponentsDto<DatabaseApiModel>> databasesApiResult = default!;
        private ApiResult<EnvironmentComponentsDto<ServerApiModel>> serversApiResult = default!;
        private ApiResult<ApiBoolResult> componentsActionApiResult = default!;

        [Ignore("The endpoint requested in this scenario exists only in the previous version of DOrc.")]
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

        [Ignore("The endpoint requested in this scenario exists only in the previous version of DOrc.")]
        [Then(@"The result should be json with project equals '(.*)' and list of environments whose names should contain '(.*)'")]
        public void ThenTheResultShouldBeJsonWithProjectEqualsAndListOfEnvironmentsWhoseNamesShouldContain(string projectName, string environmentName)
        {
            using (new AssertionScope())
            {
                Assert.IsNotNull(projectApiResult, "Api request failed!");
                Assert.AreEqual(true, projectApiResult.IsModelValid, projectApiResult.Message);

                var model = projectApiResult.Model as TemplateApiModel<EnvironmentApiModel>;

                Assert.IsNotNull(model, "Model is not valid!");
                Assert.AreEqual(projectName, model.Project.ProjectName);
                var rgx = new Regex($"^{environmentName}*");
                var result = model.Items.All(i => rgx.IsMatch(i.EnvironmentName));
                Assert.AreEqual(true, result);
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
                        Assert.AreEqual(count, model.Result.Count);
                        break;
                    }
                case 1:
                    {
                        Assert.IsNotNull(serversApiResult, "Api request failed!");
                        Assert.IsTrue(serversApiResult.IsModelValid, serversApiResult.Message);
                        var model = serversApiResult.Model as EnvironmentComponentsDto<ServerApiModel>;
                        Assert.IsNotNull(model, "Model is not valid!");
                        Assert.AreEqual(count, model.Result.Count);
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
    }
}
