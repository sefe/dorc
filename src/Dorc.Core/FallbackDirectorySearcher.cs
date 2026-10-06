using Dorc.ApiModel;
using Dorc.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace Dorc.Core
{
    // Primary/fallback pair over IActiveDirectorySearcher: every call goes to the primary
    // (Graph) first; the fallback (on-prem AD, Windows-only hosts) is consulted only when
    // the primary fails for AVAILABILITY reasons. A definitive answer from Graph — empty
    // results, or the ArgumentException the searcher contract uses for "no such entity" and
    // for guard-rejected input — is a real answer and must not become an AD query, or the
    // two directories could disagree about who exists depending on transient Graph health
    // (and input that failed the injection guard would be replayed against LDAP).
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
            catch (ArgumentException)
            {
                // The searcher contract throws ArgumentException for "entity not found" and
                // for input rejected by the validation guard. Both are definitive answers,
                // not availability failures — falling back would let who-exists vary with
                // Graph health and replay guard-rejected input against the LDAP path.
                throw;
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
