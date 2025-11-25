using Dorc.ApiModel;
using Reqnroll;
using RestSharp;
using System.Linq;
using Tests.Acceptance.Support;

namespace Tests.Acceptance.StepDefinitions
{
    [Binding]
    public class PropertyValuesSteps
    {
        private List<string> DorcSystemProperties =
        [
            "DORC_CopyEnvBuildTargetWhitelist",
            "DORC_DropDBExePath",
            "DORC_NonProdDeployPassword",
            "DORC_NonProdDeployUsername",
            "DORC_ProdDeployPassword",
            "DORC_ProdDeployUsername",
            "DORC_PropertiesUrl",
            "DORC_RestoreDBExePath",
            "DORC_WebDeployPassword",
            "DORC_WebDeployUsername",
            "DorcApiAccessAccount",
            "DorcApiAccessPassword",
            "DorcApiBaseUrl"
        ];

        private ApiResult<List<PropertyValueDto>> _propertiesApiResult = default!;

        [Given(@"I have created GET request to PropertyValues with query '(.*)'='(.*)'")]
        public void GivenIHaveCreatedGetRequestToPropertyValuesWithQuery(string query1, string queryArgument1)
        {
            var queryParameters = new Dictionary<string, string>
            {
                { query1, queryArgument1 }
            };

            using (var _caller = new ApiCaller())
            {
                _propertiesApiResult = _caller.Call<List<PropertyValueDto>>(
                    Endpoints.PropertyValues,
                    Method.Get,
                    null,
                    queryParameters);
            }
        }

        [Then(@"The result should be non empty list of properties and not contains dorc system properties")]
        public void ThenTheResultShouldBeNonEmptyListOfPropertiesAndNotContainsDorcSystemProperties()
        {
            Assert.IsNotNull(_propertiesApiResult);
            Assert.IsTrue(_propertiesApiResult.IsModelValid, _propertiesApiResult.Message);
            var model = _propertiesApiResult.Model as List<PropertyValueDto>;
            Assert.IsNotNull(model);
            Assert.IsTrue(model.Any());

            var returnedDorcSystemProperties = new List<string>();
            foreach(var property in model)
            {
                if (DorcSystemProperties.Contains(property.Property.Name, StringComparer.OrdinalIgnoreCase))
                    returnedDorcSystemProperties.Add(property.Property.Name);
            }
            Assert.IsTrue(returnedDorcSystemProperties.Count == 0, $"Returned list contains DOrc system properties: {String.Join(", ", returnedDorcSystemProperties)}");
        }
    }
}
