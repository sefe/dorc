using Dorc.ApiModel;
using Dorc.Core;
using Dorc.Core.Interfaces;
using Microsoft.Extensions.Logging.Abstractions;

namespace Dorc.Core.Tests
{
    [TestClass]
    public class FallbackPrincipalDirectoryTests
    {
        private sealed class StubDirectory : IPrincipalDirectory
        {
            public Exception? Throws { get; init; }
            public List<DirectoryPrincipalApiModel> SearchResult { get; init; } = new();
            public DirectoryPrincipalApiModel Principal { get; init; } = new();
            public List<string> Identifiers { get; init; } = new();
            public string? GroupId { get; init; }
            public int Calls { get; private set; }

            private T Answer<T>(T value)
            {
                Calls++;
                if (Throws != null) throw Throws;
                return value;
            }

            public List<DirectoryPrincipalApiModel> Search(string objectName) => Answer(SearchResult);
            public DirectoryPrincipalApiModel FindByName(string name) => Answer(Principal);
            public DirectoryPrincipalApiModel FindById(string id) => Answer(Principal);
            public List<string> GetIdentifiersForUser(string username) => Answer(Identifiers);
            public string? FindGroupIfMember(string userName, string groupName, string domainName)
                => Answer(GroupId);
        }

        private static FallbackPrincipalDirectory Build(StubDirectory primary, StubDirectory fallback)
            => new(primary, fallback, NullLogger<FallbackPrincipalDirectory>.Instance);

        [TestMethod]
        public void PrimarySuccess_DoesNotTouchFallback()
        {
            var primary = new StubDirectory { SearchResult = new List<DirectoryPrincipalApiModel> { new() { Username = "alice" } } };
            var fallback = new StubDirectory();

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
            var primary = new StubDirectory();
            var fallback = new StubDirectory { SearchResult = new List<DirectoryPrincipalApiModel> { new() { Username = "ghost" } } };

            var result = Build(primary, fallback).Search("ghost");

            Assert.AreEqual(0, result.Count);
            Assert.AreEqual(0, fallback.Calls);
        }

        [TestMethod]
        public void PrimaryThrows_FallbackAnswers()
        {
            var primary = new StubDirectory { Throws = new InvalidOperationException("graph down") };
            var fallback = new StubDirectory { Principal = new DirectoryPrincipalApiModel { Username = "bob" } };

            var user = Build(primary, fallback).FindByName("bob");

            Assert.AreEqual("bob", user.Username);
            Assert.AreEqual(1, primary.Calls);
            Assert.AreEqual(1, fallback.Calls);
        }

        [TestMethod]
        public void BothThrow_FallbackExceptionPropagates()
        {
            var primary = new StubDirectory { Throws = new InvalidOperationException("graph down") };
            var fallback = new StubDirectory { Throws = new ArgumentException("no such user") };

            Assert.ThrowsExactly<ArgumentException>(
                () => Build(primary, fallback).FindById("S-1-5-21-1-2-3-500"));
        }

        [TestMethod]
        public void GroupMembership_NullFromPrimary_IsNotMembership_NoFallback()
        {
            var primary = new StubDirectory { GroupId = null };
            var fallback = new StubDirectory { GroupId = "S-1-5-21-1-2-3-513" };

            var groupId = Build(primary, fallback).FindGroupIfMember("u", "g", "d");

            Assert.IsNull(groupId);
            Assert.AreEqual(0, fallback.Calls);
        }

        [TestMethod]
        public void IdentifiersForUser_PrimaryThrows_FallbackUsed()
        {
            var primary = new StubDirectory { Throws = new InvalidOperationException("graph down") };
            var fallback = new StubDirectory { Identifiers = new List<string> { "S-1-5-21-1-2-3-1104" } };

            var identifiers = Build(primary, fallback).GetIdentifiersForUser("alice");

            CollectionAssert.AreEqual(new List<string> { "S-1-5-21-1-2-3-1104" }, identifiers);
        }
    }
}
