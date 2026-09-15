using Dorc.ApiModel;
using Dorc.Core;
using Dorc.Core.Interfaces;
using Microsoft.Extensions.Logging.Abstractions;

namespace Dorc.Core.Tests
{
    [TestClass]
    public class FallbackDirectorySearcherTests
    {
        private sealed class StubSearcher : IActiveDirectorySearcher
        {
            public Exception? Throws { get; init; }
            public List<UserElementApiModel> SearchResult { get; init; } = new();
            public UserElementApiModel UserData { get; init; } = new();
            public List<string> Sids { get; init; } = new();
            public string? GroupSid { get; init; }
            public int Calls { get; private set; }

            private T Answer<T>(T value)
            {
                Calls++;
                if (Throws != null) throw Throws;
                return value;
            }

            public List<UserElementApiModel> Search(string objectName) => Answer(SearchResult);
            public UserElementApiModel GetUserData(string name) => Answer(UserData);
            public UserElementApiModel GetUserDataById(string id) => Answer(UserData);
            public List<string> GetSidsForUser(string username) => Answer(Sids);
            public string? GetGroupSidIfUserIsMemberRecursive(string userName, string groupName, string domainName)
                => Answer(GroupSid);
        }

        private static FallbackDirectorySearcher Build(StubSearcher primary, StubSearcher fallback)
            => new(primary, fallback, NullLogger<FallbackDirectorySearcher>.Instance);

        [TestMethod]
        public void PrimarySuccess_DoesNotTouchFallback()
        {
            var primary = new StubSearcher { SearchResult = new List<UserElementApiModel> { new() { Username = "alice" } } };
            var fallback = new StubSearcher();

            var result = Build(primary, fallback).Search("alice");

            Assert.AreEqual(1, result.Count);
            Assert.AreEqual("alice", result[0].Username);
            Assert.AreEqual(0, fallback.Calls);
        }

        [TestMethod]
        public void PrimaryEmptyAnswer_IsARealAnswer_NoFallback()
        {
            // Graph saying "nobody matches" must not be second-guessed via AD, or the two
            // directories could disagree depending on transient Graph health.
            var primary = new StubSearcher();
            var fallback = new StubSearcher { SearchResult = new List<UserElementApiModel> { new() { Username = "ghost" } } };

            var result = Build(primary, fallback).Search("ghost");

            Assert.AreEqual(0, result.Count);
            Assert.AreEqual(0, fallback.Calls);
        }

        [TestMethod]
        public void PrimaryThrows_FallbackAnswers()
        {
            var primary = new StubSearcher { Throws = new InvalidOperationException("graph down") };
            var fallback = new StubSearcher { UserData = new UserElementApiModel { Username = "bob" } };

            var user = Build(primary, fallback).GetUserData("bob");

            Assert.AreEqual("bob", user.Username);
            Assert.AreEqual(1, primary.Calls);
            Assert.AreEqual(1, fallback.Calls);
        }

        [TestMethod]
        public void BothThrow_FallbackExceptionPropagates()
        {
            var primary = new StubSearcher { Throws = new InvalidOperationException("graph down") };
            var fallback = new StubSearcher { Throws = new ArgumentException("no such user") };

            Assert.ThrowsExactly<ArgumentException>(
                () => Build(primary, fallback).GetUserDataById("S-1-5-21-1-2-3-500"));
        }

        [TestMethod]
        public void GroupMembership_NullFromPrimary_IsNotMembership_NoFallback()
        {
            var primary = new StubSearcher { GroupSid = null };
            var fallback = new StubSearcher { GroupSid = "S-1-5-21-1-2-3-513" };

            var sid = Build(primary, fallback).GetGroupSidIfUserIsMemberRecursive("u", "g", "d");

            Assert.IsNull(sid);
            Assert.AreEqual(0, fallback.Calls);
        }

        [TestMethod]
        public void SidsForUser_PrimaryThrows_FallbackUsed()
        {
            var primary = new StubSearcher { Throws = new InvalidOperationException("graph down") };
            var fallback = new StubSearcher { Sids = new List<string> { "S-1-5-21-1-2-3-1104" } };

            var sids = Build(primary, fallback).GetSidsForUser("alice");

            CollectionAssert.AreEqual(new List<string> { "S-1-5-21-1-2-3-1104" }, sids);
        }
    }
}
