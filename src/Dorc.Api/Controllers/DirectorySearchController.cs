using System.Net;
using System.Runtime.Versioning;
using System.Text.RegularExpressions;
using Dorc.Api.Services;
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

        private readonly IDirectorySearchService _searchService;
        internal readonly string _domainName;

        public DirectorySearchController(IDirectorySearchService searchService, IConfigurationSettings configurationSettingsEngine)
        {
            _searchService = searchService;
            _domainName = configurationSettingsEngine.GetConfigurationDomainName();
        }

        /// <summary>
        /// Search for users in the Active Directory using the specified search criteria.
        /// </summary>
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

            var output = _searchService.FindUsers(userSearchCriteria, _domainName);
            return Ok(output);
        }

        /// <summary>
        /// Search for groups in Active Directory using the specified search criteria.
        /// </summary>
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

            var output = _searchService.FindGroups(groupSearchCriteria, _domainName);
            return Ok(output);
        }

        /// <summary>
        /// Determines whether the specified user is a member of the specified group.
        /// </summary>
        [HttpGet]
        [Route("isuseringroup")]
        [SwaggerResponse(StatusCodes.Status200OK, Type = typeof(ApiBoolResult))]
        public IActionResult IsUserInGroup(string groupName, string account)
        {
            if (string.IsNullOrEmpty(groupName) || string.IsNullOrEmpty(account))
                return StatusCode(StatusCodes.Status400BadRequest);

            var result = new ApiBoolResult { Result = _searchService.IsUserInGroup(groupName, account, _domainName) };
            return StatusCode(StatusCodes.Status200OK, result);
        }
    }
}
