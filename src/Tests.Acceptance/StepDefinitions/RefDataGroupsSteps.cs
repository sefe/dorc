using Dorc.ApiModel;
using Reqnroll;
using RestSharp;
using Tests.Acceptance.Support;

namespace Tests.Acceptance.StepDefinitions
{
    [Binding]
    public class RefDataGroupsSteps
    {
        private ApiResult<List<GroupApiModel>> groupsListApiResult = default!;
        private ApiResult<GroupApiModel> groupApiResult = default!;

        [Given(@"I have created GET request to RefDataGroupsFeature")]
        public void GivenIHaveCreatedGETRequestToRefDataGroupsFeature()
        {
            using (var caller = new ApiCaller())
            {
                groupsListApiResult = caller.Call<List<GroupApiModel>>(Endpoints.RefDataGroups, Method.Get);
            }
        }

        [Then(@"The result should be list of groups")]
        public void ThenTheResultShouldBeListOfGroups()
        {
            Assert.IsNotNull(groupsListApiResult, "Api request failed!");
            Assert.IsTrue(groupsListApiResult.IsModelValid, groupsListApiResult.Message);
            var model = groupsListApiResult.Model as List<GroupApiModel>;
            Assert.IsNotNull(model, $"Model not valid, raw data: {groupsListApiResult.RawJson}");
            Assert.AreNotEqual(0, model.Count);
        }

        [Given(@"I have created GET request to RefDataGroupsFeature with query parameter '(.*)' and value '(.*)'")]
        public void GivenIHaveCreatedGETRequestToRefDataGroupsFeatureWithQueryParameterAndValue(
            string groupNameParameterName,
            string groupName)
        {
            var segments = new List<string> { groupName };

            using (var caller = new ApiCaller())
            {
                groupApiResult = caller.Call<GroupApiModel>(
                                Endpoints.RefDataGroups,
                                Method.Get,
                                segments: segments);
            }
        }

        [Then(@"The result should be group with id '(.*)' and name '(.*)'")]
        public void ThenTheResultShouldBeGroupWithIdAndName(
            int groupId,
            string groupName)
        {
            Assert.IsNotNull(groupApiResult, "Api request failed!");
            Assert.IsTrue(groupApiResult.IsModelValid, groupApiResult.Message);
            var model = groupApiResult.Model as GroupApiModel;
            Assert.IsNotNull(model, $"Model not valid, raw data: {groupApiResult.RawJson}");
            Assert.AreEqual(groupId, model.GroupId);
            Assert.AreEqual(groupName, model.GroupName);
        }
    }
}
