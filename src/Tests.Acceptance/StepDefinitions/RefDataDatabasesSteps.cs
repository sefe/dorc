using Dorc.ApiModel;
using Reqnroll;
using RestSharp;
using Tests.Acceptance.Support;

namespace Tests.Acceptance.StepDefinitions
{
    [Binding]
    public class RefDataDatabasesSteps
    {
        private ApiResult<List<DatabaseApiModel>> databasesApiResult = default!;
        private ApiResult<DatabaseApiModel> databaseApiResult = default!;
        private ApiResult<DatabaseApiModel> dbAddApiResult = default!;

        private DataAccessor dataAccessor = new DataAccessor();

        [Given(@"I have created GET request to RefDataDatabases")]
        public void GivenIHaveCreatedGETRequestToRefDataDatabases()
        {
            using (var caller = new ApiCaller())
            {
                databasesApiResult = caller.Call<List<DatabaseApiModel>>(
                                Endpoints.RefDataDatabases,
                                Method.Get);
            }
        }

        [Then(@"The result should be list of databases")]
        public void ThenTheResultShouldBeListOfDatabases()
        {
            Assert.IsNotNull(databasesApiResult, "Api request failed");
            Assert.IsTrue(databasesApiResult.IsModelValid, databasesApiResult.Message);
            var model = databasesApiResult.Model as List<DatabaseApiModel>;
            Assert.IsNotNull(model);
            Assert.AreNotEqual(0, model.Count);
        }

        [Given(@"I have created GET request to RefDataDatabases with query '(.*)'='(.*)'")]
        public void GivenIHaveCreatedGETRequestToRefDataDatabasesWithQuery(string idParameterName, int id)
        {
            var segments = new List<string> { id.ToString() };

            using (var caller = new ApiCaller())
            {
                databaseApiResult = caller.Call<DatabaseApiModel>(
                                Endpoints.RefDataDatabases,
                                Method.Get,
                                segments: segments);
            }
        }

        [Then(@"The result should be database with Name '(.*)' and Id '(.*)'")]
        public void ThenTheResultShouldBeDatabaseWithDatabaseNameAndDatabaseId(string databaseName, int databaseId)
        {
            Assert.IsNotNull(databaseApiResult, "Api request failed!");
            Assert.IsTrue(databaseApiResult.IsModelValid, databaseApiResult.Message);
            var model = databaseApiResult.Model as DatabaseApiModel;
            Assert.IsNotNull(model);
            Assert.AreEqual(databaseName, model.Name);
            Assert.AreEqual(databaseId, model.Id);
        }

        [Given(@"I have created GET request to RefDataDatabases with query parameter '(.*)'='(.*)' '(.*)'='(.*)'")]
        public void GivenIHaveCreatedGETRequestToRefDataDatabasesWithQueryParameter(
            string name,
            string nameValue,
            string server,
            string serverValue)
        {
            var urlQueryParameters = new Dictionary<string, string>
            {
                { name, nameValue },
                { server, serverValue }
            };

            using (var caller = new ApiCaller())
            {
                databasesApiResult = caller.Call<List<DatabaseApiModel>>(
                                Endpoints.RefDataDatabases,
                                Method.Get,
                                queryParameters: urlQueryParameters);
            }
        }

        [Then(@"The result should contain single database with Name '(.*)' and Id '(.*)'")]
        public void ThenTheResultShouldContainSingleDatabaseWithDatabaseNameAndDatabaseId(string databaseName, int databaseId)
        {
            Assert.IsNotNull(databasesApiResult, "Api request failed!");
            Assert.IsTrue(databasesApiResult.IsModelValid, databasesApiResult.Message);
            var model = databasesApiResult.Model as List<DatabaseApiModel>;
            Assert.IsNotNull(model);
            Assert.AreEqual(databaseName, model[0].Name);
            Assert.AreEqual(databaseId, model[0].Id);
        }

        #region Scenario: Create Database entry
        [Given(@"There is no database named '(.*)' on the server named '(.*)'")]
        public void GivenThereIsNoDatabaseNamedOnTheServerNamed(string databaseName, string serverName)
        {
            try
            {
                this.dataAccessor.DeleteDatabase(databaseName);
            }
            catch (Exception exception)
            {
                Assert.Fail($"Scenario preconditions are not met {exception}.");
            }
        }

        [Given(@"I have created POST request to RefDataDatabases with body below")]
        public void GivenIHaveCreatedPOSTRequestToRefDataDatabasesWithBodyBelow(string multilineText)
        {
            using (var caller = new ApiCaller())
            {
                dbAddApiResult = caller.Call<DatabaseApiModel>(
                                Endpoints.RefDataDatabases,
                                Method.Post,
                                body: multilineText);
            }
        }

        [Then(@"The result should be database with id greater than '(.*)' and Name '(.*)'")]
        public void ThenTheResultShouldBeDatabaseWithIdGreaterThanAndName(int id, string name)
        {
            Assert.IsNotNull(dbAddApiResult, "Api request failed!");
            Assert.IsTrue(dbAddApiResult.IsModelValid, dbAddApiResult.Message);
            var model = dbAddApiResult.Model as DatabaseApiModel;
            Assert.IsNotNull(model);
            Assert.AreEqual(name, model.Name);
            Assert.IsTrue(model.Id > 0);
        }
        #endregion

        #region Scenario: Attempt to create duplicate Database entry
        [Given(@"There is a database named '(.*)' on the server named '(.*)'")]
        public void GivenThereIsADatabaseNamedOnTheServerNamed(string databaseName, string serverName)
        {
            try
            {
                // First ensure it doesn't exist, then create it
                this.dataAccessor.DeleteDatabase(databaseName);
                this.dataAccessor.CreateDatabase(databaseName, serverName, "TestType", "OTHER");
            }
            catch (Exception exception)
            {
                Assert.Fail($"Scenario preconditions are not met {exception}.");
            }
        }

        [Then(@"The result should be error '(.*)'")]
        public void ThenTheResultShouldBeError(string expectedError)
        {
            Assert.IsNotNull(dbAddApiResult, "Api request should have been made!");
            Assert.IsFalse(dbAddApiResult.IsModelValid, "Request should have failed!");
            Assert.IsTrue(dbAddApiResult.Message.Contains(expectedError), 
                $"Expected error message '{expectedError}' but got '{dbAddApiResult.Message}'");
        }
        #endregion
    }
}
