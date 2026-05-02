using Dorc.ApiModel;
using Reqnroll;
using RestSharp;
using Tests.Acceptance.Support;

namespace Tests.Acceptance.StepDefinitions
{
    [Binding]
    public class RefDataProjectBuildsFeatureSteps
    {
        private ApiResult<List<EnvBuildsApiModel>> result = default!;

        private int existingEnvironmentId;
        private int existingEnvironmentComponentStatusId;

        #region Scenario: Get environment builds
        [Given(@"There is at least one record in the EnvironmentComponentStatuses for the environmetn with name '(.*)'")]
        public void GivenThereIsAtLeastOneRecordInTheEnvironmentComponentStatusesForTheEnvironmetnWithName(string environmentName)
        {
            this.existingEnvironmentId = new DataAccessor().GetEnvironments(environmentName).FirstOrDefault();
            if (this.existingEnvironmentId == 0)
            {
                this.existingEnvironmentId = new DataAccessor().CreateEnvironment(environmentName);
                this.existingEnvironmentComponentStatusId = new DataAccessor().CreateEnvironmentComponentStatus(this.existingEnvironmentId);
                return;
            }

            this.existingEnvironmentComponentStatusId = new DataAccessor().GetEnvironmentComponentStatuses(this.existingEnvironmentId).FirstOrDefault();

            if (this.existingEnvironmentComponentStatusId != 0)
            {
                return;
            }

            this.existingEnvironmentComponentStatusId = new DataAccessor().CreateEnvironmentComponentStatus(this.existingEnvironmentId);
        }

        [Given(@"I have created GET request to RefDataProjectBuilds with '(.*)' as id")]
        public void GivenIHaveCreatedGETRequestToRefDataProjectBuildsWithAsId(string environmentBuildId)
        {
            var caller = new ApiCaller();

            try
            {
                var urlQueryParameters = new Dictionary<string, string>
                {
                    { "id", environmentBuildId }
                };

                result = caller.Call<List<EnvBuildsApiModel>>(
                    Endpoints.RefDataProjectBuilds,
                    Method.Get,
                    queryParameters: urlQueryParameters);
            }
            catch (Exception exception)
            {
                new DataAccessor().DeleteEnvironmentComponentStatus(this.existingEnvironmentComponentStatusId);
                new DataAccessor().DeleteEnvironment(this.existingEnvironmentId);
                throw;
            }
            finally
            {
                caller.Dispose();
            }
        }

        [Then(@"The result should contain list of EnvBuildsApiModel")]
        public void ThenTheResultShouldContainListOfEnvBuildsApiModel()
        {
            try
            {
                Assert.IsNotNull(result, "Request failed!");
                Assert.IsTrue(result.IsModelValid, result.Message);
                var model = result.Model as List<EnvBuildsApiModel>;
                Assert.IsNotNull(model, $"Model not valid, raw data: {result.RawJson}");
                Assert.AreNotEqual(0, model.Count);
            }
            catch (Exception)
            {
                throw;
            }
            finally
            {
                new DataAccessor().DeleteEnvironmentComponentStatus(this.existingEnvironmentComponentStatusId);
                new DataAccessor().DeleteEnvironment(this.existingEnvironmentId);
            }
        }
        #endregion
    }
}
