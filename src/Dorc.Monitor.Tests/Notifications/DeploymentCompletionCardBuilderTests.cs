using System.Text.Json;
using Dorc.ApiModel;
using System.Linq;
using Dorc.Monitor.Notifications.Teams;
using Microsoft.Extensions.Options;

namespace Dorc.Monitor.Tests.Notifications
{
    [TestClass]
    public class DeploymentCompletionCardBuilderTests
    {
        private static readonly DateTimeOffset StartedTime = new(2026, 8, 1, 10, 0, 0, TimeSpan.Zero);

        private static DeploymentCompletionCardBuilder CreateBuilder(string dorcUiBaseUrl = "")
        {
            return new DeploymentCompletionCardBuilder(
                Options.Create(new TeamsBotOptions { DorcUiBaseUrl = dorcUiBaseUrl }));
        }

        private static DeploymentRequestApiModel CreateRequest()
        {
            return new DeploymentRequestApiModel
            {
                Id = 42,
                UserName = "jane.doe@example.com",
                Project = "TestProject",
                EnvironmentName = "TestEnv",
                BuildNumber = "1.2.3"
            };
        }

        private static Dictionary<string, string> GetFacts(JsonElement card)
        {
            var factSet = card.GetProperty("body")[1];
            return factSet.GetProperty("facts")
                .EnumerateArray()
                .ToDictionary(
                    f => f.GetProperty("title").GetString()!,
                    f => f.GetProperty("value").GetString()!);
        }

        [TestMethod]
        public void Build_ProducesAdaptiveCard14WithRequestFacts()
        {
            var builder = CreateBuilder();

            var json = builder.Build(CreateRequest(), "Completed", StartedTime, StartedTime.AddMinutes(2).AddSeconds(5));

            using var doc = JsonDocument.Parse(json);
            var card = doc.RootElement;

            Assert.AreEqual("AdaptiveCard", card.GetProperty("type").GetString());
            Assert.AreEqual("1.4", card.GetProperty("version").GetString());

            var facts = GetFacts(card);
            Assert.AreEqual("42", facts["Request ID"]);
            Assert.AreEqual("jane.doe@example.com", facts["Requester"]);
            Assert.AreEqual("TestProject", facts["Project"]);
            Assert.AreEqual("TestEnv", facts["Environment"]);
            Assert.AreEqual("1.2.3", facts["Build"]);
            Assert.AreEqual("Completed", facts["Status"]);
            Assert.AreEqual("2m 5s", facts["Duration"]);
        }

        [TestMethod]
        public void Build_DurationUnderAMinute_IsFormattedInSeconds()
        {
            var builder = CreateBuilder();

            var json = builder.Build(CreateRequest(), "Failed", StartedTime, StartedTime.AddSeconds(42));

            using var doc = JsonDocument.Parse(json);
            Assert.AreEqual("42s", GetFacts(doc.RootElement)["Duration"]);
        }

        [TestMethod]
        public void Build_WithUiBaseUrl_AddsDeepLinkActionWithoutDoubleSlash()
        {
            var builder = CreateBuilder("https://dorc.example.com/");

            var json = builder.Build(CreateRequest(), "Completed", StartedTime, StartedTime.AddSeconds(1));

            using var doc = JsonDocument.Parse(json);
            var action = doc.RootElement.GetProperty("actions")[0];
            Assert.AreEqual("Action.OpenUrl", action.GetProperty("type").GetString());
            Assert.AreEqual("https://dorc.example.com/monitor-result/42", action.GetProperty("url").GetString());
        }

        [TestMethod]
        public void Build_WithoutUiBaseUrl_HasNoActions()
        {
            var builder = CreateBuilder();

            var json = builder.Build(CreateRequest(), "Completed", StartedTime, StartedTime.AddSeconds(1));

            using var doc = JsonDocument.Parse(json);
            Assert.IsFalse(doc.RootElement.TryGetProperty("actions", out _));
        }

        [TestMethod]
        public void Build_FallbackTextNamesStatusAndRequestId()
        {
            var builder = CreateBuilder();

            var json = builder.Build(CreateRequest(), "Errored", StartedTime, StartedTime.AddSeconds(1));

            using var doc = JsonDocument.Parse(json);
            var fallback = doc.RootElement.GetProperty("fallbackText").GetString()!;
            StringAssert.Contains(fallback, "Errored");
            StringAssert.Contains(fallback, "42");
        }

        [TestMethod]
        public void Build_NullRequestFields_FallBackToPlaceholders()
        {
            var builder = CreateBuilder();
            var request = new DeploymentRequestApiModel { Id = 7 };

            var json = builder.Build(request, "Completed", StartedTime, StartedTime.AddSeconds(1));

            using var doc = JsonDocument.Parse(json);
            var facts = GetFacts(doc.RootElement);
            Assert.AreEqual("—", facts["Requester"]);
            Assert.AreEqual("—", facts["Project"]);
            Assert.AreEqual("—", facts["Environment"]);
            Assert.AreEqual("—", facts["Build"]);
        }
        // ---- Batch summary card ----

        private static List<DeploymentRequestApiModel> CreateBatch(int count, int firstId = 1)
        {
            return Enumerable.Range(firstId, count)
                .Select(id => new DeploymentRequestApiModel
                {
                    Id = id,
                    UserName = "jane.doe@example.com",
                    Project = "TestProject",
                    EnvironmentName = "TestEnv"
                })
                .ToList();
        }

        [TestMethod]
        public void BuildBatch_ListsEveryRequestWhenFewEnough()
        {
            var json = CreateBuilder().BuildBatch(CreateBatch(3), "Cancelled", StartedTime);

            StringAssert.Contains(json, "3 deployments Cancelled");
            foreach (var id in new[] { 1, 2, 3 })
                StringAssert.Contains(json, $"#{id}");
            Assert.IsFalse(json.Contains("and 0 more"));
        }

        [TestMethod]
        public void BuildBatch_TruncatesLongListAndSaysHowManyRemain()
        {
            var json = CreateBuilder().BuildBatch(CreateBatch(14), "Cancelled", StartedTime);

            StringAssert.Contains(json, "14 deployments Cancelled");
            StringAssert.Contains(json, "#10");      // last listed
            StringAssert.Contains(json, "and 4 more");
            Assert.IsFalse(json.Contains("#11 "), "Requests past the cap should be summarised, not listed.");
        }

        [TestMethod]
        public void BuildBatch_DeepLinksToTheRequestListNotASingleResult()
        {
            var json = CreateBuilder("http://dorc.example.com").BuildBatch(CreateBatch(2), "Cancelled", StartedTime);

            StringAssert.Contains(json, "http://dorc.example.com/monitor-requests");
            Assert.IsFalse(json.Contains("monitor-result"), "A batch card cannot deep-link to one result.");
        }

        [TestMethod]
        public void BuildBatch_WithoutBaseUrl_OmitsActions()
        {
            var json = CreateBuilder().BuildBatch(CreateBatch(2), "Cancelled", StartedTime);

            Assert.IsFalse(json.Contains("actions"));
        }

        [TestMethod]
        public void BuildBatch_CarriesFallbackTextForClientsThatCannotRenderCards()
        {
            var json = CreateBuilder().BuildBatch(CreateBatch(5), "Errored", StartedTime);

            StringAssert.Contains(json, "fallbackText");
            StringAssert.Contains(json, "5 deployments Errored");
        }

        [TestMethod]
        public void BuildBatch_EmptyBatch_Throws()
        {
            Assert.ThrowsExactly<ArgumentException>(() =>
                CreateBuilder().BuildBatch(new List<DeploymentRequestApiModel>(), "Cancelled", StartedTime));
        }

    }
}
