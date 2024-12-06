using Dorc.ApiModel;
using RestSharp;
using Tests.Acceptance.Support;

namespace Tests.Acceptance.StepDefinitions
{
    [Binding]
    public class RefDataComponentsSteps
    {
        private ApiResult<TemplateApiModel<ComponentApiModel>> componenetsApiResult = default!;

        [Given(@"I have created GET request to RefDataComponents with query '(.*)'='(.*)'")]
        public void GivenIHaveCreatedGETRequestToRefDataComponentsWithQuery(string urlQueryParameterName, string urlQueryParameterValue)
        {
            var urlQueryParameters = new Dictionary<string, string> { { urlQueryParameterName, urlQueryParameterValue } };

            using (var _caller = new ApiCaller())
            {
                componenetsApiResult = new ApiCaller().Call<TemplateApiModel<ComponentApiModel>>(
                               Endpoints.RefDataComponents,
                               Method.Get,
                               queryParameters: urlQueryParameters);
            }
        }

        [Then(@"the result should contain project with id '(.*)' and non empty list of components")]
        public void ThenTheResultShouldContainProjectWithIdAndNonEmptyListOfComponents(int p0)
        {
            Assert.IsNotNull(componenetsApiResult);
            Assert.IsTrue(componenetsApiResult.IsModelValid, componenetsApiResult.Message);
            var model = componenetsApiResult.Model as TemplateApiModel<ComponentApiModel>;
            Assert.IsNotNull(model);
            Assert.AreEqual(p0, model.Project.ProjectId);
            Assert.IsTrue(model.Items.Any());
        }
    }
}
