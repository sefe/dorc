using System.DirectoryServices;
using System.DirectoryServices.AccountManagement;
using System.Net;
using System.Runtime.Versioning;
using System.Text.RegularExpressions;
using Dorc.ApiModel;
using Dorc.Core.Configuration;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace Dorc.Api.Controllers
{
    [Authorize]
    [ApiController]
    [Route("[controller]")]
    [SupportedOSPlatform("windows")]
    public class DirectorySearchController : Controller
    {
        internal const int ClinetTimeoutInMinutes = 1;
        internal const int ServerTimeoutInMinutes = 1;

        internal const string DefaultDisplayName = "DEFAULT DISPLAY NAME";
        internal const string DefaultLogonName = "DEFAULT LOGON NAME";

        internal const int UserSearchCriteriaMinimalLength = 3;
        internal const int UserSearchCriteriaMaximumLength = 100;
        internal const int GroupSearchCriteriaMinimalLength = 3;
        internal const int GroupSearchCriteriaMaximumLength = 100;

        internal const string UserSearchCriteriaRegExPattern = @"^[a-zA-Z0-9-_.' ()&]+$";
        internal const string GroupSearchCriteriaRegExPattern = @"^[a-zA-Z0-9-_.' ()&]+$";

        internal const string IncorrectUserSearchCriteriaLengthMessage = "Incorrect user search criteria length.";
        internal const string TooLongUserSearchCriteriaLengthErrorExplanationFormat = "User search criteria length should be not greater then {0} characters. Actual length : {1}.";
        internal const string TooShortUserSearchCriteriaLengthErrorExplanationFormat = "User search criteria length should be not less then {0} characters. Actual length : {1}.";
        internal const string IncorrectCharactersInUserSearchCriteriaMessage = "Incorrect characters in user search criteria.";
        internal const string IncorrectCharactersInUserSearchCriteriaExplanationFormat = "User search criteria contains unacceptable characters. User search criteria should match the RegEx: {0}";

        internal const string IncorrectGroupSearchCriteriaLengthMessage = "Incorrect group search criteria length.";
        internal const string TooLongGroupSearchCriteriaLengthErrorExplanationFormat = "Group search criteria length should be not greater then {0} characters. Actual length : {1}.";
        internal const string TooShortGroupSearchCriteriaLengthErrorExplanationFormat = "Group search criteria length should be not less then {0} characters. Actual length : {1}.";
        internal const string IncorrectCharactersInGroupSearchCriteriaMessage = "Incorrect characters in group search criteria.";
        internal const string IncorrectCharactersInGroupSearchCriteriaExplanationFormat = "Group search criteria contains unacceptable characters. Group search criteria should match the RegEx: {0}";

        internal readonly string _domainName = null!;

        private const int ADS_UF_ACCOUNTDISABLE = 0x0002;

        private DirectorySearcher directorySearcher { get; set; } = null!;

        // Default parameterless constructor is intentionally hidden
        // in order to make the controller instantiation possible
        // only with certain services instances.
        private DirectorySearchController() { }

        public DirectorySearchController(DirectorySearcher directorySearcher, IConfigurationSettings configurationSettingsEngine)
        {
            this.directorySearcher = directorySearcher;
            this.directorySearcher.SearchScope = SearchScope.Subtree;
            this.directorySearcher.CacheResults = true;
            this.directorySearcher.ClientTimeout = TimeSpan.FromMinutes(ClinetTimeoutInMinutes);
            this.directorySearcher.ServerTimeLimit = TimeSpan.FromMinutes(ServerTimeoutInMinutes);
            this.directorySearcher.Tombstone = false;

            _domainName = configurationSettingsEngine.GetConfigurationDomainName();
        }

        /// <summary>
        /// Search for users in the Active Directory using the specified search criteria.
        /// </summary>
        /// <param name="userSearchCriteria"></param>
        /// <returns></returns>
        [HttpGet]
        [Route("users")]
        [SwaggerResponse(StatusCodes.Status200OK, Type = typeof(IList<UserSearchResult>))]
        public IActionResult SearchUsers([FromQuery] string userSearchCriteria)
        {
            #region Input verification
            if (userSearchCriteria.Length > UserSearchCriteriaMaximumLength)
            {
                var internalServerError = new HttpResponseMessage(HttpStatusCode.InternalServerError)
                {
                    Content = new StringContent(string.Format(
                        TooLongUserSearchCriteriaLengthErrorExplanationFormat,
                        UserSearchCriteriaMaximumLength,
                        userSearchCriteria.Length)),
                    ReasonPhrase = IncorrectUserSearchCriteriaLengthMessage
                };
                return StatusCode(StatusCodes.Status500InternalServerError, internalServerError);
            }

            if (userSearchCriteria.Length < UserSearchCriteriaMinimalLength)
            {
                var internalServerError = new HttpResponseMessage(HttpStatusCode.InternalServerError)
                {
                    Content = new StringContent(string.Format(
                       TooShortUserSearchCriteriaLengthErrorExplanationFormat,
                       UserSearchCriteriaMinimalLength,
                       userSearchCriteria.Length)),
                    ReasonPhrase = IncorrectUserSearchCriteriaLengthMessage
                };
                return StatusCode(StatusCodes.Status500InternalServerError, internalServerError);
            }

            if (!Regex.IsMatch(userSearchCriteria, UserSearchCriteriaRegExPattern))
            {
                var internalServerError = new HttpResponseMessage(HttpStatusCode.InternalServerError)
                {
                    Content = new StringContent(string.Format(
                        IncorrectCharactersInUserSearchCriteriaExplanationFormat,
                        UserSearchCriteriaRegExPattern)),
                    ReasonPhrase = IncorrectCharactersInUserSearchCriteriaMessage
                };
                return StatusCode(StatusCodes.Status500InternalServerError, internalServerError);
            }
            #endregion

            const string samAccountNamePropertyName = "SAMAccountName";
            const string userAccountControlPropertyName = "userAccountControl";
            const string displayNamePropertyName = "DisplayName";

            directorySearcher.PropertiesToLoad.Add(samAccountNamePropertyName);
            directorySearcher.PropertiesToLoad.Add(userAccountControlPropertyName);
            directorySearcher.PropertiesToLoad.Add(displayNamePropertyName);

            directorySearcher.Filter = string.Format(
            "(&(objectClass=user)(|(cn={0})(sn={0}*)(givenName={0})(DisplayName={0}*)(sAMAccountName={0}*)))", userSearchCriteria);

            List<UserSearchResult> output = [];
            using (SearchResultCollection searchResults = directorySearcher.FindAll())
            {
                foreach (SearchResult searchResult in searchResults)
                {
                    DirectoryEntry foundUser = searchResult.GetDirectoryEntry();
                    if (foundUser.NativeGuid == null)
                    {
                        // The object is not bound to the Active Directory Domain Services.
                        // For more details see https://learn.microsoft.com/en-us/windows/win32/ad/binding-to-active-directory-domain-services.
                        continue;
                    }

                    var userAccountFlagsValue = foundUser.Properties[userAccountControlPropertyName].Value;
                    if (userAccountFlagsValue is null) continue;
                    var userAccountFlags = (int)userAccountFlagsValue;
                    var isFoundUserAccountDisabled = Convert.ToBoolean(userAccountFlags & ADS_UF_ACCOUNTDISABLE);
                    if (isFoundUserAccountDisabled)
                    {
                        continue;
                    }

                    var foundUserLogonName = foundUser.Properties.Contains(samAccountNamePropertyName)
                        ? foundUser.Properties[samAccountNamePropertyName][0].ToString() ?? DefaultLogonName
                        : DefaultLogonName;

                    var foundUserDisplayName = foundUser.Properties.Contains(displayNamePropertyName)
                        ? foundUser.Properties[displayNamePropertyName][0].ToString() ?? DefaultDisplayName
                        : foundUserLogonName.Equals(DefaultLogonName)
                            ? DefaultDisplayName
                            : foundUserLogonName;

                    output.Add(new UserSearchResult
                    {
                        DisplayName = foundUserDisplayName,
                        FullLogonName = $@"{_domainName}\{foundUserLogonName}"
                    });
                }
            }

            return Ok(output);
        }

        /// <summary>
        /// Search for groups in Active Directory using the specified search criteria.
        /// </summary>
        /// <param name="groupSearchCriteria"></param>
        /// <returns></returns>
        [HttpGet]
        [Route("groups")]
        [SwaggerResponse(StatusCodes.Status200OK, Type = typeof(IList<GroupSearchResult>))]
        public IActionResult SearchGroups([FromQuery] string groupSearchCriteria)
        {
            #region Input verification
            if (groupSearchCriteria.Length > GroupSearchCriteriaMaximumLength)
            {
                var internalServerError = new HttpResponseMessage(HttpStatusCode.InternalServerError)
                {
                    Content = new StringContent(string.Format(
                        TooLongGroupSearchCriteriaLengthErrorExplanationFormat,
                        GroupSearchCriteriaMaximumLength,
                        groupSearchCriteria.Length)),
                    ReasonPhrase = IncorrectGroupSearchCriteriaLengthMessage
                };
                return StatusCode(StatusCodes.Status500InternalServerError, internalServerError);
            }

            if (groupSearchCriteria.Length < GroupSearchCriteriaMinimalLength)
            {
                var internalServerError = new HttpResponseMessage(HttpStatusCode.InternalServerError)
                {
                    Content = new StringContent(string.Format(
                       TooShortGroupSearchCriteriaLengthErrorExplanationFormat,
                       GroupSearchCriteriaMinimalLength,
                       groupSearchCriteria.Length)),
                    ReasonPhrase = IncorrectGroupSearchCriteriaLengthMessage
                };
                return StatusCode(StatusCodes.Status500InternalServerError, internalServerError);
            }

            if (!Regex.IsMatch(groupSearchCriteria, GroupSearchCriteriaRegExPattern))
            {
                var internalServerError = new HttpResponseMessage(HttpStatusCode.InternalServerError)
                {
                    Content = new StringContent(string.Format(
                        IncorrectCharactersInGroupSearchCriteriaExplanationFormat,
                        GroupSearchCriteriaRegExPattern)),
                    ReasonPhrase = IncorrectCharactersInGroupSearchCriteriaMessage
                };
                return StatusCode(StatusCodes.Status500InternalServerError, internalServerError);
            }
            #endregion

            const string namePropertyName = "Name";

            directorySearcher.PropertiesToLoad.Add(namePropertyName);

            directorySearcher.Filter = string.Format(
            "(&(objectClass=group)(|(cn={0})(DisplayName={0}*)(sAMAccountName={0}*)))", groupSearchCriteria);

            List<GroupSearchResult> output = [];
            using (SearchResultCollection searchResults = directorySearcher.FindAll())
            {
                foreach (SearchResult searchResult in searchResults)
                {
                    DirectoryEntry foundGroup = searchResult.GetDirectoryEntry();
                    if (foundGroup.NativeGuid == null)
                    {
                        // The object is not bound to the Active Directory Domain Services.
                        // For more details see https://learn.microsoft.com/en-us/windows/win32/ad/binding-to-active-directory-domain-services.
                        continue;
                    }

                    var foundGroupName = foundGroup.Properties.Contains(namePropertyName)
                        ? foundGroup.Properties[namePropertyName][0].ToString() ?? DefaultDisplayName
                        : DefaultDisplayName;

                    output.Add(new GroupSearchResult
                    {
                        DisplayName = foundGroupName,
                        FullLogonName = $@"{_domainName}\{foundGroupName}"
                    });
                }
            }
            return Ok(output);
        }

        /// <summary>
        /// Determines whether the specified user is a member of the specified group.
        /// </summary>
        /// <param name="groupName"></param>
        /// <param name="account"></param>
        /// <returns></returns>
        [HttpGet]
        [Route("isuseringroup")]
        [SwaggerResponse(StatusCodes.Status200OK, Type = typeof(ApiBoolResult))]
        public IActionResult IsUserInGroup(string groupName, string account)
        {
            var result = new ApiBoolResult { Result = false };
            if (string.IsNullOrEmpty(groupName) || string.IsNullOrEmpty(account)) return StatusCode(StatusCodes.Status400BadRequest);
            var context = new PrincipalContext(ContextType.Domain, _domainName);
            var user = UserPrincipal.FindByIdentity(context, IdentityType.SamAccountName, account);
            var groupPrincipal = GroupPrincipal.FindByIdentity(context, IdentityType.Name, groupName);
            result.Result = groupPrincipal != null && user.IsMemberOf(groupPrincipal);
            return StatusCode(StatusCodes.Status200OK, result);
        }
    }
}
