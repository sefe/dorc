using Dorc.ApiModel;
using Dorc.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace Dorc.Core
{
    // Primary/fallback pair over IPrincipalDirectory: every call goes to the primary
    // (Graph) first; the fallback (on-prem AD, Windows-only hosts) is consulted only when
    // the primary THROWS. A successful-but-empty primary answer is a real answer — "no such
    // user" from Graph must not become an AD query, or the two directories could disagree
    // about who exists depending on transient Graph health.
    public class FallbackPrincipalDirectory : IPrincipalDirectory
    {
        private readonly IPrincipalDirectory _primary;
        private readonly IPrincipalDirectory _fallback;
        private readonly ILogger _log;

        public FallbackPrincipalDirectory(
            IPrincipalDirectory primary,
            IPrincipalDirectory fallback,
            ILogger<FallbackPrincipalDirectory> log)
        {
            _primary = primary ?? throw new ArgumentNullException(nameof(primary));
            _fallback = fallback ?? throw new ArgumentNullException(nameof(fallback));
            _log = log;
        }

        public List<DirectoryPrincipalApiModel> Search(string objectName)
            => Invoke(d => d.Search(objectName), nameof(Search));

        public DirectoryPrincipalApiModel FindByName(string name)
            => Invoke(d => d.FindByName(name), nameof(FindByName));

        public DirectoryPrincipalApiModel FindById(string id)
            => Invoke(d => d.FindById(id), nameof(FindById));

        public List<string> GetIdentifiersForUser(string username)
            => Invoke(d => d.GetIdentifiersForUser(username), nameof(GetIdentifiersForUser));

        public string? FindGroupIfMember(string userName, string groupName, string domainName)
            => Invoke(d => d.FindGroupIfMember(userName, groupName, domainName), nameof(FindGroupIfMember));

        private T Invoke<T>(Func<IPrincipalDirectory, T> call, string operation)
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
