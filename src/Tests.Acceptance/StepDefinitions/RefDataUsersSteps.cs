using Dorc.ApiModel;
using Reqnroll;
using RestSharp;
using Tests.Acceptance.Support;

namespace Tests.Acceptance.StepDefinitions
{
    [Binding]
    public class RefDataUsersSteps
    {
        private ApiResult<List<UserApiModel>> _usersAndGroupsListResult = default!;
        private ApiResult<List<UserApiModel>> _usersListResult = default!;
        private ApiResult<UserApiModel> _addUserResult = default!;

        [Given(@"I have created GET request on RefDataUsers endpoint")]
        public void GivenIHaveCreatedGETRequestOnRefDataUsersEndpoint()
        {
            using (var caller = new ApiCaller())
            {
                _usersAndGroupsListResult = caller.Call<List<UserApiModel>>(Endpoints.RefDataUsers, Method.Get);
            }
        }

        [Then(@"the result should be list of users and groups")]
        public void ThenTheResultShouldBeListOfUsersAndGroups()
        {
            Assert.IsNotNull(_usersAndGroupsListResult);
            Assert.IsTrue(_usersAndGroupsListResult.IsModelValid);
            if (_usersAndGroupsListResult.Model is List<UserApiModel> users)
                Assert.AreNotEqual(0, users.Count);
        }

        [Given(@"I have created GET request on RefDataUsers endpoint with '(.*)' as type")]
        public void GivenIHaveCreatedGETRequestOnRefDataUsersEndpointWithAsType(int p0)
        {
            var segments = new Dictionary<string, string>
            {
                { "type", p0.ToString() }
            };

            using (var caller = new ApiCaller())
            {
                _usersListResult = caller.Call<List<UserApiModel>>(Endpoints.RefDataUsers, Method.Get);
            }
        }

        [Then(@"the result should be list of users only")]
        public void ThenTheResultShouldBeListOfUsersOnly()
        {
            Assert.IsNotNull(_usersListResult);
            Assert.IsTrue(_usersListResult.IsModelValid);
            if (_usersListResult.Model is List<UserApiModel> users)
                Assert.AreNotEqual(0, users.Count);
        }

        [Then(@"The result should be list of groups only")]
        public void ThenTheResultShouldBeListOfGroupsOnly()
        {
            Assert.IsNotNull(_usersListResult);
            Assert.IsTrue(_usersListResult.IsModelValid);
            if (_usersListResult.Model is List<UserApiModel> users)
                Assert.AreNotEqual(0, users.Count);
        }

        [Given(@"I have created POST request on RefDataUsers endpoint with following data")]
        public void GivenIHaveCreatedPOSTRequestOnRefDataUsersEndpointWithAsDisplayNameAsLoginTypeAsLanIdTypeAsLanIdAsTeam(string multilineText)
        {
            using (var caller = new ApiCaller())
            {
                _addUserResult = caller.Call<UserApiModel>(Endpoints.RefDataUsers, Method.Post, body: multilineText);
            }
        }

        [Then(@"the result should be new user")]
        public void ThenTheResultShouldBeNewUser()
        {
            if (_addUserResult.IsModelValid)
            {
                if (_addUserResult.Model is UserApiModel user)
                {
                    Assert.AreNotEqual(0, user.Id);
                }
                else
                {
                    Assert.Fail();
                }
            }
            else
            {
                Assert.Fail();
            }

        }

        [Given(@"I have created GET request on RefDataUsers endpoint with following '(.*)'")]
        public void GivenIHaveCreatedGETRequestOnRefDataUsersEndpointWithFollowing(string p0)
        {
            Dictionary<string, string> segments = new Dictionary<string, string>
            {
                {"userName",p0}
            };

            using (var caller = new ApiCaller())
            {
                _usersListResult = caller.Call<List<UserApiModel>>(Endpoints.RefDataUsers, Method.Get);
            }
        }

        [Then(@"the result should be the user with LanId equals '(.*)'")]
        public void ThenTheResultShouldBeTheUserWithLanIdEquals(string p0)
        {
            if (_usersListResult.IsModelValid)
            {
                if (_usersListResult.Model is UserApiModel user)
                {
                    Assert.AreEqual(p0, user.LanId);
                }
            }
            else
            {
                Assert.Fail("Model not valid");
            }
        }
    }
}