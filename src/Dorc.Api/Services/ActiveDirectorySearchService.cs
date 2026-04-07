using System.DirectoryServices;
using System.DirectoryServices.AccountManagement;
using System.Runtime.Versioning;
using Dorc.ApiModel;

namespace Dorc.Api.Services
{
    [SupportedOSPlatform("windows")]
    public class ActiveDirectorySearchService : IDirectorySearchService
    {
        private const int ADS_UF_ACCOUNTDISABLE = 0x0002;
        private const string DefaultDisplayName = "DEFAULT DISPLAY NAME";
        private const string DefaultLogonName = "DEFAULT LOGON NAME";

        private readonly DirectorySearcher _directorySearcher;

        public ActiveDirectorySearchService(DirectorySearcher directorySearcher)
        {
            _directorySearcher = directorySearcher;
            _directorySearcher.SearchScope = SearchScope.Subtree;
            _directorySearcher.CacheResults = true;
            _directorySearcher.ClientTimeout = TimeSpan.FromMinutes(1);
            _directorySearcher.ServerTimeLimit = TimeSpan.FromMinutes(1);
            _directorySearcher.Tombstone = false;
        }

        public IList<UserSearchResult> FindUsers(string searchCriteria, string domainName)
        {
            const string samAccountNamePropertyName = "SAMAccountName";
            const string userAccountControlPropertyName = "userAccountControl";
            const string displayNamePropertyName = "DisplayName";

            _directorySearcher.PropertiesToLoad.Add(samAccountNamePropertyName);
            _directorySearcher.PropertiesToLoad.Add(userAccountControlPropertyName);
            _directorySearcher.PropertiesToLoad.Add(displayNamePropertyName);

            _directorySearcher.Filter = string.Format(
                "(&(objectClass=user)(|(cn={0})(sn={0}*)(givenName={0})(DisplayName={0}*)(sAMAccountName={0}*)))", searchCriteria);

            var output = new List<UserSearchResult>();
            using (SearchResultCollection searchResults = _directorySearcher.FindAll())
            {
                foreach (SearchResult searchResult in searchResults)
                {
                    DirectoryEntry foundUser = searchResult.GetDirectoryEntry();
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

            _directorySearcher.PropertiesToLoad.Add(namePropertyName);

            _directorySearcher.Filter = string.Format(
                "(&(objectClass=group)(|(cn={0})(DisplayName={0}*)(sAMAccountName={0}*)))", searchCriteria);

            var output = new List<GroupSearchResult>();
            using (SearchResultCollection searchResults = _directorySearcher.FindAll())
            {
                foreach (SearchResult searchResult in searchResults)
                {
                    DirectoryEntry foundGroup = searchResult.GetDirectoryEntry();
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

        public bool IsUserInGroup(string groupName, string account, string domainName)
        {
            var context = new PrincipalContext(ContextType.Domain, domainName);
            var user = UserPrincipal.FindByIdentity(context, IdentityType.SamAccountName, account);
            var groupPrincipal = GroupPrincipal.FindByIdentity(context, IdentityType.Name, groupName);
            return groupPrincipal != null && user.IsMemberOf(groupPrincipal);
        }
    }
}
