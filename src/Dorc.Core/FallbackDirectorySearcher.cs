using Dorc.ApiModel;
using Dorc.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace Dorc.Core
{
    // Primary/fallback pair over IActiveDirectorySearcher: every call goes to the primary
    // (Graph) first; the fallback (on-prem AD, Windows-only hosts) is consulted only when
    // the primary THROWS. A successful-but-empty primary answer is a real answer — "no such
    // user" from Graph must not become an AD query, or the two directories could disagree
    // about who exists depending on transient Graph health.
    public class FallbackDirectorySearcher : IActiveDirectorySearcher
    {
        private readonly IActiveDirectorySearcher _primary;
        private readonly IActiveDirectorySearcher _fallback;
        private readonly ILogger _log;

        public FallbackDirectorySearcher(
            IActiveDirectorySearcher primary,
            IActiveDirectorySearcher fallback,
            ILogger<FallbackDirectorySearcher> log)
        {
            _primary = primary ?? throw new ArgumentNullException(nameof(primary));
            _fallback = fallback ?? throw new ArgumentNullException(nameof(fallback));
            _log = log;
        }

        public List<UserElementApiModel> Search(string objectName)
            => Invoke(s => s.Search(objectName), nameof(Search));

        public UserElementApiModel GetUserData(string name)
            => Invoke(s => s.GetUserData(name), nameof(GetUserData));

        public UserElementApiModel GetUserDataById(string id)
            => Invoke(s => s.GetUserDataById(id), nameof(GetUserDataById));

        public List<string> GetSidsForUser(string username)
            => Invoke(s => s.GetSidsForUser(username), nameof(GetSidsForUser));

        public string? GetGroupSidIfUserIsMemberRecursive(string userName, string groupName, string domainName)
            => Invoke(s => s.GetGroupSidIfUserIsMemberRecursive(userName, groupName, domainName),
                nameof(GetGroupSidIfUserIsMemberRecursive));

        private T Invoke<T>(Func<IActiveDirectorySearcher, T> call, string operation)
        {
            try
            {
                return call(_primary);
            }
            catch (Exception primaryEx)
            {
                _log.LogWarning(primaryEx,
                    "{Operation} failed against {Primary}; falling back to {Fallback}.",
                    operation, _primary.GetType().Name, _fallback.GetType().Name);

                return call(_fallback);
            }
        }
    }
}
