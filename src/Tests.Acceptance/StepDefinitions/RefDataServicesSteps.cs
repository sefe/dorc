using System.Text.Json;
using Dorc.ApiModel;
using Reqnroll;
using RestSharp;
using Tests.Acceptance.Support;

namespace Tests.Acceptance.StepDefinitions
{
    [Binding]
    public class RefDataServicesFeatureSteps
    {
        private ApiResult<ServiceStatusApiModel> serviceStatusResult = default!;

        [Ignore]
        [Given(@"I have created PUT request to RefDataServices with body contains json with '(.*)' '(.*)' '(.*)'")]
        public void GivenIHaveCreatedPUTRequestToRefDataServicesWithBodyContainsJsonWith(string p0, string p1, string p2)
        {
            ServiceStatusApiModel jsonObject = new ServiceStatusApiModel
            {
                ServerName = p0,
                ServiceName = p1,
                ServiceStatus = p2
            };
            string json = JsonSerializer.Serialize(jsonObject);

            using (var caller = new ApiCaller())
            {
                serviceStatusResult = caller.Call<ServiceStatusApiModel>(Endpoints.RefDataServices, Method.Put, body: json);
            }
        }

        [Ignore]
        [Then(@"The '(.*)' should be equal ServiceStatusApiModel ServiceStatus")]
        public void ThenTheShouldBeEqualServiceStatusApiModelServiceStatus(string p0)
        {
            if (serviceStatusResult != null)
            {
                if (serviceStatusResult.IsModelValid)
                {
                    if (serviceStatusResult.Model is ServiceStatusApiModel model)
                    {
                        Assert.AreEqual(p0, model.ServiceStatus);
                    }
                }
                else
                {
                    Assert.Fail($"Model invalid. Api message {serviceStatusResult.Message}");
                }
            }
            else
            {
                Assert.Fail("Request fail");
            }
        }
    }
}
