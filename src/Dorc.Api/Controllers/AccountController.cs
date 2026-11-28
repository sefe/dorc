using Dorc.Core.Account;
using Dorc.Core.Account.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;
using System.Runtime.Versioning;

namespace Dorc.Api.Controllers
{
    [Authorize]
    [ApiController]
    [Route("[controller]")]
    [SupportedOSPlatform("windows")]
    public class AccountController : Controller
    {
        internal const string UserLanIdIsRequiredExplanation = "User LanId is required.";
        internal const string GroupLanIdIsRequiredExplanation = "Group LanId is required.";
        internal const string AccountTypeIsRequiredExplanation = "Account type is required.";
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
            if (string.IsNullOrWhiteSpace(userLanId))
            {
                return BadRequest(UserLanIdIsRequiredExplanation);
            }

            if (string.IsNullOrWhiteSpace(accountType))
            {
                return BadRequest(AccountTypeIsRequiredExplanation);
            }

            if (!Enum.TryParse<AccountType>(accountType, true, out var accountTypeEnum) ||
                !Enum.IsDefined(typeof(AccountType), accountTypeEnum))
            {
                return BadRequest(InvalidAccountTypeMessage);
            }

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
            if (string.IsNullOrWhiteSpace(groupLanId))
            {
                return BadRequest(GroupLanIdIsRequiredExplanation);
            }

            if (string.IsNullOrWhiteSpace(accountType))
            {
                return BadRequest(AccountTypeIsRequiredExplanation);
            }

            if (!Enum.TryParse<AccountType>(accountType, true, out var accountTypeEnum) ||
                !Enum.IsDefined(typeof(AccountType), accountTypeEnum))
            {
                return BadRequest(InvalidAccountTypeMessage);
            }

            return Ok(accountExistenceChecker.GroupExists(groupLanId, accountTypeEnum));
        }
    }
}
