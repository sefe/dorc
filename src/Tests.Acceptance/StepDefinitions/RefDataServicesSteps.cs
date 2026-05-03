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
        private ApiResult<DaemonStatusApiModel> serviceStatusResult = default!;

        [Ignore]
        [Given(@"I have created PUT request to RefDataServices with body contains json with '(.*)' '(.*)' '(.*)'")]
        public void GivenIHaveCreatedPUTRequestToRefDataServicesWithBodyContainsJsonWith(string p0, string p1, string p2)
        {
            DaemonStatusApiModel jsonObject = new DaemonStatusApiModel
            {
                ServerName = p0,
                DaemonName = p1,
                Status = p2
            };
            string json = JsonSerializer.Serialize(jsonObject);

            using (var caller = new ApiCaller())
            {
                serviceStatusResult = caller.Call<DaemonStatusApiModel>(Endpoints.RefDataServices, Method.Put, body: json);
            }
        }

        [Ignore]
        [Then(@"The '(.*)' should be equal DaemonStatusApiModel ServiceStatus")]
        public void ThenTheShouldBeEqualDaemonStatusApiModelServiceStatus(string p0)
        {
            if (serviceStatusResult != null)
            {
                if (serviceStatusResult.IsModelValid)
                {
                    if (serviceStatusResult.Model is DaemonStatusApiModel model)
                    {
                        Assert.AreEqual(p0, model.Status);
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
