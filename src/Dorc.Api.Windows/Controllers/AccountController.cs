using Dorc.Core.Account;
using Dorc.Core.Account.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;
using System.Net;
using System.Runtime.Versioning;

namespace Dorc.Api.Windows.Controllers
{
    [Authorize]
    [ApiController]
    [Route("[controller]")]
    [SupportedOSPlatform("windows")]
    public class AccountController : Controller
    {
        internal const string UserLanIdIsRequiredExplanation = "User LanId is required.";
        internal const string UserLanIdIsNotSpecifiedMessage = "User LanId is not specified.";

        internal const string GroupLanIdIsRequiredExplanation = "Group LanId is required.";
        internal const string GroupLanIdIsNotSpecifiedMessage = "Group LanId is not specified.";

        internal const string AccountTypeIsRequiredExplanation = "Account type is required.";
        internal const string AccountTypeIsNotSpecifiedMessage = "Account type is not specified.";
        internal const string InvalidAccountTypeMessage = "Invalid account type.";

        private readonly IAccountExistenceChecker accountExistenceChecker;

        public AccountController(IAccountExistenceChecker accountExistenceChecker)
        {
            this.accountExistenceChecker = accountExistenceChecker;
        }

        /// <summary>
        /// Determine whether a user exits in Active Directory
        /// </summary>
        /// <param name="userLanId"></param>
        /// <param name="accountType"></param>
        /// <returns></returns>
        [Route("userExists")]
        [HttpGet]
        [SwaggerResponse(StatusCodes.Status200OK, Type = typeof(bool))]
        public IActionResult UserExists([FromQuery] string userLanId, [FromQuery] string accountType)
        {
            #region Input verification
            if (string.IsNullOrEmpty(userLanId))
            {
                var internalServerError = new HttpResponseMessage(HttpStatusCode.InternalServerError)
                {
                    Content = new StringContent(UserLanIdIsRequiredExplanation),
                    ReasonPhrase = UserLanIdIsNotSpecifiedMessage
                };
                return StatusCode(StatusCodes.Status500InternalServerError, internalServerError);
            }

            if (string.IsNullOrEmpty(accountType))
            {
                var internalServerError = new HttpResponseMessage(HttpStatusCode.InternalServerError)
                {
                    Content = new StringContent(AccountTypeIsRequiredExplanation),
                    ReasonPhrase = AccountTypeIsNotSpecifiedMessage
                };
                return StatusCode(StatusCodes.Status500InternalServerError, internalServerError);
            }

            AccountType accountTypeEnum = default;

            try
            {
                accountTypeEnum = (AccountType)Enum.Parse(typeof(AccountType), accountType, true);
            }
            catch (Exception exception)
            {
                var internalServerError = new HttpResponseMessage(HttpStatusCode.InternalServerError)
                {
                    Content = new StringContent(exception.Message),
                    ReasonPhrase = AccountTypeIsNotSpecifiedMessage
                };
                return StatusCode(StatusCodes.Status500InternalServerError, internalServerError);
            }
            #endregion

            return Ok(accountExistenceChecker.UserExists(userLanId, accountTypeEnum));
        }

        /// <summary>
        /// Determine whether a group exits in Active Directory
        /// </summary>
        /// <param name="groupLanId"></param>
        /// <param name="accountType"></param>
        /// <returns></returns>
        [Route("groupExists")]
        [HttpGet]
        [SwaggerResponse(StatusCodes.Status200OK, Type = typeof(bool))]
        public IActionResult GroupExists([FromQuery] string groupLanId, [FromQuery] string accountType)
        {
            #region Input verification
            if (string.IsNullOrEmpty(groupLanId))
            {
                var internalServerError = new HttpResponseMessage(HttpStatusCode.InternalServerError)
                {
                    Content = new StringContent(GroupLanIdIsRequiredExplanation),
                    ReasonPhrase = GroupLanIdIsNotSpecifiedMessage
                };
                return StatusCode(StatusCodes.Status500InternalServerError, internalServerError);
            }

            if (string.IsNullOrEmpty(accountType))
            {
                var internalServerError = new HttpResponseMessage(HttpStatusCode.InternalServerError)
                {
                    Content = new StringContent(AccountTypeIsRequiredExplanation),
                    ReasonPhrase = AccountTypeIsNotSpecifiedMessage
                };
                return StatusCode(StatusCodes.Status500InternalServerError, internalServerError);
            }

            AccountType accountTypeEnum = default;

            try
            {
                accountTypeEnum = (AccountType)Enum.Parse(typeof(AccountType), accountType, true);
            }
            catch (Exception exception)
            {
                var internalServerError = new HttpResponseMessage(HttpStatusCode.InternalServerError)
                {
                    Content = new StringContent(exception.Message),
                    ReasonPhrase = AccountTypeIsNotSpecifiedMessage
                };
                return StatusCode(StatusCodes.Status500InternalServerError, internalServerError);
            }
            #endregion

            return Ok(accountExistenceChecker.GroupExists(groupLanId, accountTypeEnum));
        }
    }
}
