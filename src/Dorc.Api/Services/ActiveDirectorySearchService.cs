using System.DirectoryServices;
using System.DirectoryServices.AccountManagement;
using System.Runtime.Versioning;
using System.Text;
using Dorc.ApiModel;

namespace Dorc.Api.Services
{
    [SupportedOSPlatform("windows")]
    public class ActiveDirectorySearchService : IDirectorySearchService, IDisposable
    {
        private const int ADS_UF_ACCOUNTDISABLE = 0x0002;
        private const string DefaultDisplayName = "DEFAULT DISPLAY NAME";
        private const string DefaultLogonName = "DEFAULT LOGON NAME";

        private readonly DirectorySearcher _directorySearcher;
        private bool _disposed;

        public ActiveDirectorySearchService(DirectorySearcher directorySearcher)
        {
            _directorySearcher = directorySearcher;
            _directorySearcher.SearchScope = SearchScope.Subtree;
            _directorySearcher.CacheResults = true;
            _directorySearcher.ClientTimeout = TimeSpan.FromMinutes(1);
            _directorySearcher.ServerTimeLimit = TimeSpan.FromMinutes(1);
            _directorySearcher.Tombstone = false;
        }

        public void Dispose()
        {
            if (_disposed) return;
            // DirectorySearcher does not own the DirectoryEntry it was constructed
            // from (we receive both via DI) — dispose the searcher first, then any
            // backing DirectoryEntry owned by the searcher. The DI container will
            // dispose the service at scope end, which cascades to the underlying
            // unmanaged AD handles.
            _directorySearcher.SearchRoot?.Dispose();
            _directorySearcher.Dispose();
            _disposed = true;
            GC.SuppressFinalize(this);
        }

        public IList<UserSearchResult> FindUsers(string searchCriteria, string domainName)
        {
            const string samAccountNamePropertyName = "SAMAccountName";
            const string userAccountControlPropertyName = "userAccountControl";
            const string displayNamePropertyName = "DisplayName";

            _directorySearcher.PropertiesToLoad.Clear();
            _directorySearcher.PropertiesToLoad.Add(samAccountNamePropertyName);
            _directorySearcher.PropertiesToLoad.Add(userAccountControlPropertyName);
            _directorySearcher.PropertiesToLoad.Add(displayNamePropertyName);

            var escaped = EscapeLdapSearchFilter(searchCriteria);
            _directorySearcher.Filter = string.Format(
                "(&(objectClass=user)(|(cn={0})(sn={0}*)(givenName={0})(DisplayName={0}*)(sAMAccountName={0}*)))", escaped);

            var output = new List<UserSearchResult>();
            using (SearchResultCollection searchResults = _directorySearcher.FindAll())
            {
                foreach (SearchResult searchResult in searchResults)
                {
                    using DirectoryEntry foundUser = searchResult.GetDirectoryEntry();
                    if (foundUser.NativeGuid == null)
                        continue;

                    var userAccountFlags = (int)foundUser.Properties[userAccountControlPropertyName].Value;
                    var isFoundUserAccountDisabled = Convert.ToBoolean(userAccountFlags & ADS_UF_ACCOUNTDISABLE);
                    if (isFoundUserAccountDisabled)
                        continue;

                    var foundUserLogonName = foundUser.Properties.Contains(samAccountNamePropertyName)
                        ? foundUser.Properties[samAccountNamePropertyName][0].ToString()
                        : DefaultLogonName;

                    var foundUserDisplayName = foundUser.Properties.Contains(displayNamePropertyName)
                        ? foundUser.Properties[displayNamePropertyName][0].ToString()
                        : foundUserLogonName.Equals(DefaultLogonName)
                            ? DefaultDisplayName
                            : foundUserLogonName;

                    output.Add(new UserSearchResult
                    {
                        DisplayName = foundUserDisplayName,
                        FullLogonName = $@"{domainName}\{foundUserLogonName}"
                    });
                }
            }

            return output;
        }

        public IList<GroupSearchResult> FindGroups(string searchCriteria, string domainName)
        {
            const string namePropertyName = "Name";

            _directorySearcher.PropertiesToLoad.Clear();
            _directorySearcher.PropertiesToLoad.Add(namePropertyName);

            var escaped = EscapeLdapSearchFilter(searchCriteria);
            _directorySearcher.Filter = string.Format(
                "(&(objectClass=group)(|(cn={0})(DisplayName={0}*)(sAMAccountName={0}*)))", escaped);

            var output = new List<GroupSearchResult>();
            using (SearchResultCollection searchResults = _directorySearcher.FindAll())
            {
                foreach (SearchResult searchResult in searchResults)
                {
                    using DirectoryEntry foundGroup = searchResult.GetDirectoryEntry();
                    if (foundGroup.NativeGuid == null)
                        continue;

                    var foundGroupName = foundGroup.Properties.Contains(namePropertyName)
                        ? foundGroup.Properties[namePropertyName][0].ToString()
                        : DefaultDisplayName;

                    output.Add(new GroupSearchResult
                    {
                        DisplayName = foundGroupName,
                        FullLogonName = $@"{domainName}\{foundGroupName}"
                    });
                }
            }

            return output;
        }

        /// <summary>
        /// Escapes LDAP special characters per RFC 4515 to prevent LDAP injection.
        /// </summary>
        private static string EscapeLdapSearchFilter(string input)
        {
            var sb = new StringBuilder(input.Length);
            foreach (var c in input)
            {
                switch (c)
                {
                    case '\\': sb.Append(@"\5c"); break;
                    case '*': sb.Append(@"\2a"); break;
                    case '(': sb.Append(@"\28"); break;
                    case ')': sb.Append(@"\29"); break;
                    case '\0': sb.Append(@"\00"); break;
                    default: sb.Append(c); break;
                }
            }
            return sb.ToString();
        }

        public bool IsUserInGroup(string groupName, string account, string domainName)
        {
            using var context = new PrincipalContext(ContextType.Domain, domainName);
            using var user = UserPrincipal.FindByIdentity(context, IdentityType.SamAccountName, account);
            using var groupPrincipal = GroupPrincipal.FindByIdentity(context, IdentityType.Name, groupName);
            return user != null && groupPrincipal != null && user.IsMemberOf(groupPrincipal);
        }
    }
}
